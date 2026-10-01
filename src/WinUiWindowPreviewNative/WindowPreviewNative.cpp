#include "WindowPreviewNative.h"
#include "WindowPreviewPolicy.h"
#include "WindowPreviewDiagnostics.h"
#include <Windows.h>
#include <d3d11_4.h>
#include <dxgi1_3.h>
#include <cmath>
#include <d2d1_1.h>
#include <dwmapi.h>
#include <wrl/client.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <algorithm>
#include <atomic>
#include <deque>
#include <functional>
#include <future>
#include <map>
#include <memory>
#include <mutex>
#include <thread>
using Microsoft::WRL::ComPtr;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
namespace {
using widgetrail::preview::MaximumActiveBytes;
using widgetrail::preview::MaximumSources;
enum State : uint32_t { Suspended, Starting, Live, Expired, InvalidSource, Budget, Faulted };
int64_t Counter() noexcept { LARGE_INTEGER value{}; QueryPerformanceCounter(&value); return value.QuadPart; }
int64_t Frequency() noexcept { LARGE_INTEGER value{}; QueryPerformanceFrequency(&value); return value.QuadPart; }
struct Wake final { HANDLE event{CreateEventW(nullptr, FALSE, FALSE, nullptr)}; ~Wake() { if (event) CloseHandle(event); } };
bool Identity(const WrailPreviewTarget& source) noexcept {
    const auto window = reinterpret_cast<HWND>(source.window);
    if (!window || !source.processId || !source.processCreated || !IsWindow(window) || GetAncestor(window, GA_ROOT) != window) return false;
    DWORD pid{}; GetWindowThreadProcessId(window, &pid);
    if (pid != source.processId) return false;
    wchar_t name[256]{};
    if (!GetClassNameW(window, name, 256) || wcsncmp(source.className, name, 256) != 0) return false;
    const auto process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!process) return false;
    FILETIME created{}, exited{}, kernel{}, user{};
    const bool read = GetProcessTimes(process, &created, &exited, &kernel, &user) != FALSE;
    CloseHandle(process);
    DWORD affinity{}; DWORD cloaked{};
    return read && ((uint64_t(created.dwHighDateTime) << 32) | created.dwLowDateTime) == source.processCreated &&
        GetWindowDisplayAffinity(window, &affinity) && affinity == WDA_NONE &&
        SUCCEEDED(DwmGetWindowAttribute(window, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))) && !cloaked &&
        IsWindowVisible(window) && !IsIconic(window);
}
std::pair<UINT, UINT> OutputSize(uint32_t width, uint32_t height) {
    const auto size = widgetrail::preview::OutputSize(width, height);
    if (!size) throw winrt::hresult_error(E_INVALIDARG);
    return *size;
}
struct Preview final {
    PreviewCloseDiagnostics* health{};
    uint64_t id{};
    WrailPreviewTarget target;
    WrailPreviewAuthority authority{};
    void* authorityContext{};
    HWND host{};
    uint32_t width{}, height{}, fit{};
    int64_t deadline{}, retryAt{}, startedAt{};
    State state{Suspended}; HRESULT error{S_OK};
    uint64_t frames{}, generation{}, captureBytes{}, outputBytes{};
    winrt::Windows::Graphics::SizeInt32 sourceSize{}, resizeHint{};
    bool clearPending{}, presentPending{}, retired{};
    ComPtr<IDXGISwapChain1> output;
    ComPtr<ID2D1Bitmap1> backBuffer;
    ComPtr<ID3D11RenderTargetView> clearTarget;
    GraphicsCaptureItem item{nullptr};
    Direct3D11CaptureFramePool pool{nullptr};
    GraphicsCaptureSession capture{nullptr};
    winrt::event_token arrived{};
    std::shared_ptr<std::atomic_bool> ready;
    ~Preview() { Stop(); }
    void Stop(State reason = Suspended) noexcept {
        bool observed{};
        auto close = [&](PreviewClosePhase phase, auto&& action) {
            if (health) { health->Begin(phase, reason, id, target, frames, Counter()); observed = true; }
            try { action(); }
            catch (const winrt::hresult_error& failure) { if (health) health->Error(failure.code()); }
            catch (...) { if (health) health->Error(E_FAIL); }
        };
        if (pool && arrived.value) close(PreviewClosePhase::DetachCallback, [&] { pool.FrameArrived(arrived); });
        arrived = {};
        if (capture) close(PreviewClosePhase::CloseSession, [&] { capture.Close(); });
        if (pool) close(PreviewClosePhase::ClosePool, [&] { pool.Close(); });
        capture = nullptr; pool = nullptr; item = nullptr; captureBytes = 0;
        ready.reset(); sourceSize = {}; startedAt = 0;
        if (observed) health->End();
    }
};
}
struct WrailPreviewEngine final {
    std::shared_ptr<Wake> wake{std::make_shared<Wake>()};
    std::atomic_bool stopping{};
    std::mutex mutex;
    std::deque<std::function<void()>> commands;
    std::thread worker;
    std::shared_ptr<PreviewCloseDiagnostics> health{std::make_shared<PreviewCloseDiagnostics>()};
    std::map<uint64_t, std::unique_ptr<Preview>> previews;
    uint64_t nextId{}, surfaceGeneration{};
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> immediate;
    ComPtr<ID2D1DeviceContext> drawing;
    ComPtr<IDXGIFactory2> factory;
    IDirect3DDevice captureDevice{nullptr};
    WrailPreviewEngine() {
        if (!wake->event) throw winrt::hresult_error(HRESULT_FROM_WIN32(GetLastError()));
        worker = std::thread([this] { Run(); });
    }
    ~WrailPreviewEngine() {
        stopping.store(true); SetEvent(wake->event);
        if (worker.joinable()) worker.join();
    }
    HRESULT Invoke(std::function<HRESULT()> callback) {
        auto result = std::make_shared<std::promise<HRESULT>>(); auto future = result->get_future();
        {
            std::lock_guard guard(mutex);
            if (stopping.load()) return RO_E_CLOSED;
            if (commands.size() >= 128) return HRESULT_FROM_WIN32(ERROR_BUSY);
            commands.emplace_back([this, result, callback = std::move(callback)] {
                if (stopping.load()) { result->set_value(RO_E_CLOSED); return; }
                try { result->set_value(callback()); }
                catch (const winrt::hresult_error& error) { result->set_value(error.code()); }
                catch (...) { result->set_value(E_FAIL); }
            });
        }
        SetEvent(wake->event); return future.get();
    }
    uint64_t Bytes() const {
        uint64_t total{}; for (const auto& [id, preview] : previews) total += preview->captureBytes + preview->outputBytes;
        return total;
    }
    void Device() {
        if (device) return;
        ComPtr<ID3D11Device> createdDevice;
        ComPtr<ID3D11DeviceContext> createdImmediate;
        winrt::check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr, 0, D3D11_SDK_VERSION, &createdDevice, nullptr, &createdImmediate));
        ComPtr<ID3D11Multithread> multithread; winrt::check_hresult(createdImmediate.As(&multithread));
        multithread->SetMultithreadProtected(TRUE);
        ComPtr<IDXGIDevice> dxgi; winrt::check_hresult(createdDevice.As(&dxgi));
        ComPtr<IDXGIAdapter> adapter; winrt::check_hresult(dxgi->GetAdapter(&adapter));
        ComPtr<IDXGIFactory2> createdFactory; winrt::check_hresult(adapter->GetParent(IID_PPV_ARGS(&createdFactory)));
        winrt::com_ptr<IInspectable> inspectable;
        winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.Get(), inspectable.put()));
        ComPtr<ID2D1Factory1> d2d; winrt::check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, IID_PPV_ARGS(&d2d)));
        ComPtr<ID2D1Device> paintDevice; winrt::check_hresult(d2d->CreateDevice(dxgi.Get(), &paintDevice));
        ComPtr<ID2D1DeviceContext> createdDrawing; winrt::check_hresult(paintDevice->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &createdDrawing));
        captureDevice = inspectable.as<IDirect3DDevice>();
        drawing = std::move(createdDrawing); factory = std::move(createdFactory); immediate = std::move(createdImmediate); device = std::move(createdDevice);
    }
    void CreateOutput(Preview& preview) {
        Device();
        DXGI_SWAP_CHAIN_DESC1 desc{};
        desc.Width = preview.width; desc.Height = preview.height;
        desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; desc.SampleDesc.Count = 1;
        desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT; desc.BufferCount = 2;
        // WinUI treats swapchains as opaque. Declare that contract explicitly;
        // revoked pixels become a black safety blank until the UI detaches it.
        desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL; desc.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
        winrt::check_hresult(factory->CreateSwapChainForComposition(device.Get(), &desc, nullptr, &preview.output));
        preview.outputBytes = uint64_t(preview.width) * preview.height * 8;
        preview.generation = ++surfaceGeneration;
        BackBuffer(preview);
        Clear(preview);
    }
    void BackBuffer(Preview& preview) {
        ComPtr<IDXGISurface> surface; winrt::check_hresult(preview.output->GetBuffer(0, IID_PPV_ARGS(&surface)));
        auto properties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_IGNORE), 96, 96);
        winrt::check_hresult(drawing->CreateBitmapFromDxgiSurface(surface.Get(), &properties, &preview.backBuffer));
        ComPtr<ID3D11Texture2D> texture; winrt::check_hresult(surface.As(&texture));
        winrt::check_hresult(device->CreateRenderTargetView(texture.Get(), nullptr, &preview.clearTarget));
    }
    bool Present(Preview& preview) {
        auto hr = preview.output->Present(0, DXGI_PRESENT_DO_NOT_WAIT);
        if (hr == DXGI_ERROR_WAS_STILL_DRAWING) return false;
        winrt::check_hresult(hr); return true;
    }
    void Clear(Preview& preview) {
        preview.clearPending = true;
        if (!preview.output || !immediate || !preview.clearTarget) { preview.clearPending = false; return; }
        const float blank[4]{0, 0, 0, 1};
        immediate->ClearRenderTargetView(preview.clearTarget.Get(), blank);
        preview.presentPending = false;
        preview.clearPending = !Present(preview);
    }
    void Suspend(Preview& preview, State reason, HRESULT error = S_OK) {
        const bool wasLive = preview.state == Live || preview.state == Starting;
        preview.Stop(reason); preview.state = reason; preview.error = error;
        if (wasLive || preview.clearPending || preview.presentPending) Clear(preview);
    }
    void Start(Preview& preview) {
        if (!GraphicsCaptureSession::IsSupported()) throw winrt::hresult_error(E_NOTIMPL);
        auto interop = winrt::get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
        winrt::check_hresult(interop->CreateForWindow(reinterpret_cast<HWND>(preview.target.window), winrt::guid_of<GraphicsCaptureItem>(), winrt::put_abi(preview.item)));
        auto size = preview.resizeHint.Width > 0 ? preview.resizeHint : preview.item.Size();
        preview.resizeHint = {};
        if (!widgetrail::preview::SourceFits(size.Width, size.Height, Bytes()))
        { preview.Stop(Budget); preview.state = Budget; return; }
        const auto bytes = uint64_t(size.Width) * size.Height * 4;
        preview.pool = Direct3D11CaptureFramePool::CreateFreeThreaded(captureDevice, DirectXPixelFormat::B8G8R8A8UIntNormalized, 1, size);
        preview.ready = std::make_shared<std::atomic_bool>(false);
        preview.arrived = preview.pool.FrameArrived([signal = wake, ready = preview.ready](auto&&, auto&&) { ready->store(true); SetEvent(signal->event); });
        preview.capture = preview.pool.CreateCaptureSession(preview.item);
        preview.capture.IsCursorCaptureEnabled(false);
        preview.sourceSize = size; preview.captureBytes = bytes; preview.state = Starting; preview.startedAt = Counter();
        preview.capture.StartCapture();
    }
    void Draw(Preview& preview, const Direct3D11CaptureFrame& frame) {
        auto access = frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
        ComPtr<IDXGISurface> surface; winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&surface)));
        ComPtr<ID2D1Bitmap1> bitmap;
        auto properties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_NONE,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED), 96, 96);
        winrt::check_hresult(drawing->CreateBitmapFromDxgiSurface(surface.Get(), &properties, &bitmap));
        const float sw = float(preview.sourceSize.Width), sh = float(preview.sourceSize.Height);
        const float width = float(preview.width), height = float(preview.height);
        D2D1_RECT_F target{0, 0, width, height};
        if (preview.fit != 2) {
            const auto ratio = preview.fit == 1 ? std::max(width / sw, height / sh) : std::min(width / sw, height / sh);
            const float w = sw * ratio, h = sh * ratio;
            target = {(width - w) / 2, (height - h) / 2, (width + w) / 2, (height + h) / 2};
        }
        drawing->SetTarget(preview.backBuffer.Get()); drawing->BeginDraw(); drawing->Clear(D2D1::ColorF(0.f, 0.f, 0.f, 1.f));
        drawing->DrawBitmap(bitmap.Get(), target, 1, D2D1_INTERPOLATION_MODE_LINEAR, D2D1::RectF(0, 0, sw, sh));
        auto hr = drawing->EndDraw(); drawing->SetTarget(nullptr); winrt::check_hresult(hr);
        if (Counter() >= preview.deadline || !preview.authority(preview.authorityContext)) { Clear(preview); preview.state = Expired; return; }
        preview.presentPending = !Present(preview);
        if (!preview.presentPending) { ++preview.frames; preview.state = Live; preview.error = S_OK; }
    }
    void Tick() {
        const auto now = Counter();
        for (auto& [id, value] : previews) {
            auto& preview = *value;
            try {
                // Removal stops callbacks immediately but retains a busy output
                // until its safety blank is presented. A stalled UI may
                // still hold a composition reference to that swapchain.
                if (preview.retired) { if (preview.clearPending) Clear(preview); continue; }
                if (now >= preview.deadline) { Suspend(preview, preview.deadline ? Expired : Suspended); continue; }
                if (!preview.authority(preview.authorityContext)) { Suspend(preview, Expired); continue; }
                if (!IsWindowVisible(preview.host) || IsIconic(preview.host) || !Identity(preview.target)) { Suspend(preview, InvalidSource); continue; }
                if (now < preview.retryAt) continue;
                if (!preview.output) CreateOutput(preview);
                if (preview.presentPending && Present(preview)) {
                    preview.presentPending = false; ++preview.frames; preview.state = Live; preview.error = S_OK;
                }
                if (!preview.capture) Start(preview);
                if (!preview.capture) { preview.retryAt = now + Frequency() / 2; continue; }
                if (preview.state == Starting && now - preview.startedAt > 3 * Frequency()) throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_TIMEOUT));
                if (!preview.ready->exchange(false)) continue;
                auto frame = preview.pool.TryGetNextFrame();
                if (!frame) continue;
                struct CloseFrame { Direct3D11CaptureFrame& frame; ~CloseFrame() { try { if (frame) frame.Close(); } catch (...) {} } } lease{frame};
                const auto size = frame.ContentSize();
                if (size.Width != preview.sourceSize.Width || size.Height != preview.sourceSize.Height) {
                    frame.Close(); frame = nullptr;
                    preview.resizeHint = size;
                    Suspend(preview, Suspended); preview.retryAt = now + Frequency() / 10; continue;
                }
                Draw(preview, frame);
                frame.Close(); frame = nullptr;
                if (Counter() >= preview.deadline) Suspend(preview, Expired);
            } catch (const winrt::hresult_error& error) {
                preview.Stop(Faulted); preview.state = Faulted; preview.error = error.code(); preview.retryAt = now + Frequency();
                try { Clear(preview); } catch (...) {}
                if (!preview.backBuffer || !preview.clearTarget) {
                    preview.backBuffer.Reset(); preview.clearTarget.Reset(); preview.output.Reset(); preview.outputBytes = 0;
                    preview.generation = ++surfaceGeneration;
                }
                if (device && FAILED(device->GetDeviceRemovedReason())) { ResetDevice(); break; }
            } catch (...) {
                preview.Stop(Faulted); preview.state = Faulted; preview.error = E_FAIL;
                try { Clear(preview); } catch (...) { preview.clearPending = true; }
            }
        }
        std::erase_if(previews, [](const auto& entry) { return entry.second->retired && !entry.second->clearPending; });
    }
    void ResetDevice() {
        for (auto& [id, preview] : previews) {
            preview->Stop(Faulted);
            try { Clear(*preview); } catch (...) {}
            preview->backBuffer.Reset(); preview->clearTarget.Reset(); preview->output.Reset(); preview->outputBytes = 0;
            preview->generation = ++surfaceGeneration;
            preview->state = Faulted; preview->error = DXGI_ERROR_DEVICE_REMOVED;
        }
        drawing.Reset(); factory.Reset(); captureDevice = nullptr; immediate.Reset(); device.Reset();
    }
    void Run() noexcept {
        bool initialized{};
        try {
            winrt::init_apartment(winrt::apartment_type::multi_threaded); initialized = true;
            while (!stopping.load()) {
                std::deque<std::function<void()>> pending;
                { std::lock_guard guard(mutex); pending.swap(commands); }
                for (auto& command : pending) command();
                Tick();
                // Independent of frame arrival AND UI dispatch: a frozen source
                // cannot keep expired pixels on the composition surface.
                WaitForSingleObject(wake->event, previews.empty() ? INFINITE : 16);
            }
        } catch (...) { stopping.store(true); }
        std::deque<std::function<void()>> pending;
        { std::lock_guard guard(mutex); stopping.store(true); pending.swap(commands); }
        for (auto& command : pending) command();
        for (auto& [id, preview] : previews) { try { Suspend(*preview, Suspended); } catch (...) {} }
        previews.clear(); ResetDevice();
        if (initialized) winrt::uninit_apartment();
    }

};
struct WrailPreviewHealthReader final {
    std::shared_ptr<PreviewCloseDiagnostics> health;
};
namespace {
template<class F> HRESULT Call(WrailPreviewEngine* engine, uint64_t id, F callback) noexcept {
    if (!engine) return E_INVALIDARG;
    try { return engine->Invoke([=] { auto found = engine->previews.find(id); return found == engine->previews.end() ? E_INVALIDARG : callback(*found->second); }); }
    catch (...) { return E_FAIL; }
}
}
extern "C" {
int32_t __stdcall WrailPreviewCreate(WrailPreviewEngine** result) noexcept {
    if (!result) return E_POINTER; *result = nullptr;
    try { *result = new WrailPreviewEngine(); return S_OK; } catch (...) { return E_FAIL; }
}
void __stdcall WrailPreviewDestroy(WrailPreviewEngine* engine) noexcept { delete engine; }
int32_t __stdcall WrailPreviewAdd(WrailPreviewEngine* engine, const WrailPreviewTarget* target, uint64_t hostWindow,
    uint32_t width, uint32_t height, uint32_t fit, WrailPreviewAuthority authority, void* context, uint64_t* id) noexcept {
    if (!engine || !target || !id || !authority || !context || target->size != sizeof(*target) || target->version != 1 || fit > 2 || !hostWindow || !wmemchr(target->className, 0, 256)) return E_INVALIDARG;
    *id = 0; const auto copied = *target;
    DWORD hostPid{};
    GetWindowThreadProcessId(reinterpret_cast<HWND>(hostWindow), &hostPid);
    if (hostPid != GetCurrentProcessId() || copied.window == hostWindow) return E_ACCESSDENIED;
    try { return engine->Invoke([=] {
        if (engine->previews.size() >= MaximumSources) return HRESULT_FROM_WIN32(ERROR_BUSY);
        if (!Identity(copied)) return E_ACCESSDENIED;
        auto size = OutputSize(width, height);
        if (engine->Bytes() + uint64_t(size.first) * size.second * 8 > MaximumActiveBytes) return E_OUTOFMEMORY;
        auto preview = std::make_unique<Preview>(); preview->target = copied; preview->authority = authority; preview->authorityContext = context; preview->host = reinterpret_cast<HWND>(hostWindow);
        preview->width = size.first; preview->height = size.second; preview->fit = fit;
        engine->CreateOutput(*preview); *id = ++engine->nextId; preview->id = *id; preview->health = engine->health.get();
        engine->previews.emplace(*id, std::move(preview)); return S_OK;
    }); } catch (...) { return E_FAIL; }
}
int32_t __stdcall WrailPreviewRenew(WrailPreviewEngine* engine, uint64_t id, int64_t deadline) noexcept {
    return Call(engine, id, [=](Preview& preview) { const auto now = Counter(); preview.deadline = deadline <= now ? 0 : std::min(deadline, now + 2 * Frequency());
        if (!preview.deadline) engine->Suspend(preview, Suspended); return S_OK; });
}
int32_t __stdcall WrailPreviewResize(WrailPreviewEngine* engine, uint64_t id, uint32_t width, uint32_t height, uint32_t fit) noexcept {
    if (fit > 2) return E_INVALIDARG;
    return Call(engine, id, [=](Preview& preview) {
        auto size = OutputSize(width, height); const bool fitChanged = preview.fit != fit; preview.fit = fit;
        if (preview.width == size.first && preview.height == size.second) {
            if (fitChanged) engine->Suspend(preview, Suspended);
            return S_OK;
        }
        const auto bytes = uint64_t(size.first) * size.second * 8;
        if (engine->Bytes() - preview.outputBytes + bytes > MaximumActiveBytes) return E_OUTOFMEMORY;
        engine->Suspend(preview, Suspended); preview.backBuffer.Reset(); preview.clearTarget.Reset(); preview.width = size.first; preview.height = size.second;
        if (preview.output) {
            winrt::check_hresult(preview.output->ResizeBuffers(2, preview.width, preview.height, DXGI_FORMAT_B8G8R8A8_UNORM, 0));
            engine->BackBuffer(preview);
            preview.outputBytes = bytes; engine->Clear(preview);
        }
        return S_OK;
    });
}
int32_t __stdcall WrailPreviewRemove(WrailPreviewEngine* engine, uint64_t id) noexcept {
    if (!engine) return E_INVALIDARG;
    try { return engine->Invoke([=] { auto found = engine->previews.find(id); if (found == engine->previews.end()) return S_FALSE;
        found->second->retired = true;
        engine->Suspend(*found->second, Suspended);
        if (!found->second->clearPending) engine->previews.erase(found);
        return S_OK; }); } catch (...) { return E_FAIL; }
}
int32_t __stdcall WrailPreviewSwapChain(WrailPreviewEngine* engine, uint64_t id, void** result, uint64_t* generation) noexcept {
    if (!result || !generation) return E_POINTER; *result = nullptr; *generation = 0;
    return Call(engine, id, [=](Preview& preview) { *generation = preview.generation; if (!preview.output) return S_FALSE;
        return preview.output.CopyTo(reinterpret_cast<IDXGISwapChain1**>(result)); });
}
int32_t __stdcall WrailPreviewInspect(WrailPreviewEngine* engine, uint64_t id, WrailPreviewStats* result) noexcept {
    if (!result || result->size != sizeof(*result) || result->version != 1) return E_INVALIDARG;
    if (!engine) return E_INVALIDARG;
    if (id == 0) {
        try { return engine->Invoke([=] { result->totalBytes = engine->Bytes(); result->activeCount = 0;
            for (const auto& [key, other] : engine->previews) if (other->capture) ++result->activeCount; return S_OK; }); }
        catch (...) { return E_FAIL; }
    }
    return Call(engine, id, [=](Preview& preview) { result->state = preview.state; result->error = preview.error;
        result->sourceWidth = preview.sourceSize.Width; result->sourceHeight = preview.sourceSize.Height;
        result->width = preview.width; result->height = preview.height; result->frames = preview.frames; result->surfaceGeneration = preview.generation;
        result->totalBytes = engine->Bytes(); result->activeCount = 0;
        for (const auto& [key, other] : engine->previews) if (other->capture) ++result->activeCount; return S_OK; });
}
int32_t __stdcall WrailPreviewResetDevice(WrailPreviewEngine* engine) noexcept {
    if (!engine) return E_INVALIDARG;
    try { return engine->Invoke([=] { engine->ResetDevice(); return S_OK; }); } catch (...) { return E_FAIL; }
}
int32_t __stdcall WrailPreviewAcquireHealth(WrailPreviewEngine* engine, WrailPreviewHealthReader** result) noexcept {
    if (!result) return E_POINTER;
    *result = nullptr;
    if (!engine) return E_INVALIDARG;
    try { *result = new WrailPreviewHealthReader{engine->health}; return S_OK; }
    catch (...) { return E_OUTOFMEMORY; }
}
void __stdcall WrailPreviewReleaseHealth(WrailPreviewHealthReader* reader) noexcept { delete reader; }
int32_t __stdcall WrailPreviewReadHealth(WrailPreviewHealthReader* reader, WrailPreviewHealth* result) noexcept {
    if (!reader || !result || result->size != sizeof(*result) || result->version != 1) return E_INVALIDARG;
    return reader->health->Read(*result) ? S_OK : E_PENDING;
}
int32_t __stdcall WrailPreviewReadIdentity(uint64_t value, WrailPreviewTarget* target) noexcept {
    if (!target || target->size != sizeof(*target) || target->version != 1) return E_INVALIDARG;
    const auto window = reinterpret_cast<HWND>(value); DWORD pid{};
    if (!IsWindow(window) || GetAncestor(window, GA_ROOT) != window || !GetWindowThreadProcessId(window, &pid)) return E_INVALIDARG;
    const auto process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid); if (!process) return E_ACCESSDENIED;
    FILETIME created{}, exited{}, kernel{}, user{}; const bool read = GetProcessTimes(process, &created, &exited, &kernel, &user) != FALSE; CloseHandle(process);
    if (!read || !GetClassNameW(window, target->className, 256)) return E_FAIL;
    target->window = value; target->processId = pid; target->processCreated = (uint64_t(created.dwHighDateTime) << 32) | created.dwLowDateTime; return S_OK;
}
}
