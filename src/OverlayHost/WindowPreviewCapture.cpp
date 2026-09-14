#include "WindowPreviewCapture.h"

#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <dxgi.h>
#include <d3d11_4.h>
#include <algorithm>
#include <atomic>
#include <map>

namespace widgetrail {
namespace {
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
constexpr size_t MaximumSources = 8;
// One WGC buffer plus one owned preview texture per source. Borrowed frames
// return to the pool after the GPU downsample, never during a later UI paint.
// This bounds requested texture storage, excluding driver/runtime overhead.
constexpr uint64_t MaximumBufferBytes = 192ULL * 1024ULL * 1024ULL;
constexpr uint64_t MaximumSourcePixels = 4096ULL * 2160ULL;
constexpr ULONGLONG FirstFrameTimeoutMilliseconds = 3000;

D2D1_SIZE_U PreviewSize(winrt::Windows::Graphics::SizeInt32 size) noexcept {
    const double scale = std::min({1.0, 960.0 / size.Width, 540.0 / size.Height});
    return {std::max(1U, static_cast<UINT>(size.Width * scale)),
            std::max(1U, static_cast<UINT>(size.Height * scale))};
}

uint64_t BufferBytes(winrt::Windows::Graphics::SizeInt32 size) noexcept {
    const auto preview = PreviewSize(size);
    return (static_cast<uint64_t>(size.Width) * size.Height +
        static_cast<uint64_t>(preview.width) * preview.height) * 4;
}

struct Capture final {
    WindowPreviewSource source;
    GraphicsCaptureItem item{nullptr};
    Direct3D11CaptureFramePool pool{nullptr};
    GraphicsCaptureSession session{nullptr};
    Microsoft::WRL::ComPtr<ID3D11Texture2D> preview;
    Microsoft::WRL::ComPtr<ID2D1Bitmap1> previewTarget;
    bool previewReady{};
    uint64_t reserved{};
    winrt::Windows::Graphics::SizeInt32 size{};
    unsigned failures{};
    ULONGLONG retryAt{};
    ULONGLONG startedAt{};
    HRESULT restartError{S_OK};
    std::wstring_view state;
    std::wstring_view reportedState;
    HRESULT stateError{S_OK};
    HRESULT reportedError{S_OK};
    winrt::Windows::Graphics::SizeInt32 stateSize{};
    winrt::Windows::Graphics::SizeInt32 reportedSize{};
    std::shared_ptr<std::atomic_bool> ready = std::make_shared<std::atomic_bool>(false);
    winrt::event_token arrived{};
    ~Capture() { Close(); }
    void SetState(std::wstring_view reason, HRESULT error = S_OK) noexcept {
        state = reason;
        stateError = error;
        stateSize = size;
    }
    void Pause() noexcept {
        try { if (session) session.Close(); } catch (...) {}
        session = nullptr;
        previewReady = false;
        previewTarget.Reset();
        preview.Reset();
        reserved = 0;
        try { if (pool) pool.FrameArrived(arrived); } catch (...) {}
        arrived = {};
        try { if (pool) pool.Close(); } catch (...) {}
        ready->store(false);
        pool = nullptr;
        startedAt = 0;
    }
    void Close() noexcept { Pause(); item = nullptr; }
    void CreatePreview(ID3D11Device* device, ID2D1DeviceContext* context) {
        const auto extent = PreviewSize(size);
        D3D11_TEXTURE2D_DESC description{};
        description.Width = extent.width;
        description.Height = extent.height;
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.SampleDesc.Count = 1;
        description.Usage = D3D11_USAGE_DEFAULT;
        description.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        winrt::check_hresult(device->CreateTexture2D(&description, nullptr, &preview));
        Microsoft::WRL::ComPtr<IDXGISurface> surface;
        winrt::check_hresult(preview.As(&surface));
        const auto properties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED), 96, 96);
        winrt::check_hresult(context->CreateBitmapFromDxgiSurface(surface.Get(), &properties, &previewTarget));
    }
    void CopyPreview(ID2D1DeviceContext* context, const Direct3D11CaptureFrame& frame) {
        auto access = frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
        Microsoft::WRL::ComPtr<IDXGISurface> surface;
        winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&surface)));
        Microsoft::WRL::ComPtr<ID2D1Bitmap1> sourceBitmap;
        const auto properties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_NONE,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED), 96, 96);
        winrt::check_hresult(context->CreateBitmapFromDxgiSurface(surface.Get(), &properties, &sourceBitmap));
        const auto extent = previewTarget->GetSize();
        context->SetTarget(previewTarget.Get());
        context->BeginDraw();
        context->Clear(D2D1::ColorF(0, 0));
        context->DrawBitmap(sourceBitmap.Get(), D2D1::RectF(0, 0, extent.width, extent.height),
            1.0F, D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC);
        const auto result = context->EndDraw();
        context->SetTarget(nullptr);
        winrt::check_hresult(result);
        previewReady = true;
    }
    void Retry(std::wstring_view reason, HRESULT error) noexcept {
        Close();
        SetState(reason, error);
        size = {};
        retryAt = GetTickCount64() + std::min<ULONGLONG>(30000, 500ULL << std::min(failures, 6U));
        failures = std::min(failures + 1, 7U);
        restartError = S_OK;
    }
};
}

struct WindowPreviewCapture::Impl final {
    Microsoft::WRL::ComPtr<ID3D11Device> device;
    IDirect3DDevice captureDevice{nullptr};
    Microsoft::WRL::ComPtr<ID2D1DeviceContext> previewContext;
    std::map<std::wstring, std::unique_ptr<Capture>, std::less<>> captures;
};

WindowPreviewCapture::WindowPreviewCapture() : impl_(std::make_unique<Impl>()) {}
WindowPreviewCapture::~WindowPreviewCapture() = default;
void WindowPreviewCapture::Reset() noexcept {
    impl_->captures.clear();
    impl_->captureDevice = nullptr;
    impl_->previewContext.Reset();
    impl_->device.Reset();
}

bool WindowPreviewCapture::Validate(const WindowPreviewSource& source) noexcept {
    if (!source.window || !source.processId || !source.processCreated ||
        !IsWindow(source.window) || GetAncestor(source.window, GA_ROOT) != source.window)
        return false;
    DWORD processId{};
    GetWindowThreadProcessId(source.window, &processId);
    if (processId != source.processId) return false;
    wchar_t className[256]{};
    if (!GetClassNameW(source.window, className, 256) || source.className != className)
        return false;
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId);
    if (!process) return false;
    FILETIME created{}, exited{}, kernel{}, user{};
    const bool read = GetProcessTimes(process, &created, &exited, &kernel, &user) != FALSE;
    CloseHandle(process);
    const auto time = (static_cast<ULONGLONG>(created.dwHighDateTime) << 32) |
        created.dwLowDateTime;
    DWORD affinity{};
    if (GetWindowDisplayAffinity(source.window, &affinity) && affinity != WDA_NONE)
        return false;
    return read && time == source.processCreated;
}

void WindowPreviewCapture::Reconcile(
    ID3D11Device* device, std::span<const WindowPreviewSource> sources) noexcept {
    if (impl_->device.Get() != device) Reset();
    if (!device || sources.empty()) { Reset(); return; }
    try {
        if (!impl_->captureDevice) {
            if (!GraphicsCaptureSession::IsSupported()) return;
            // The capture runtime and Direct2D share this device. Protect its
            // immediate context before the free-threaded frame pool starts.
            Microsoft::WRL::ComPtr<ID3D11DeviceContext> immediate;
            device->GetImmediateContext(&immediate);
            Microsoft::WRL::ComPtr<ID3D11Multithread> multithread;
            winrt::check_hresult(immediate.As(&multithread));
            multithread->SetMultithreadProtected(TRUE);
            Microsoft::WRL::ComPtr<IDXGIDevice> dxgi;
            winrt::check_hresult(device->QueryInterface(IID_PPV_ARGS(&dxgi)));
            winrt::com_ptr<IInspectable> inspectable;
            winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(
                dxgi.Get(), inspectable.put()));
            Microsoft::WRL::ComPtr<ID2D1Factory1> factory;
            winrt::check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, IID_PPV_ARGS(&factory)));
            Microsoft::WRL::ComPtr<ID2D1Device> previewDevice;
            winrt::check_hresult(factory->CreateDevice(dxgi.Get(), &previewDevice));
            Microsoft::WRL::ComPtr<ID2D1DeviceContext> previewContext;
            winrt::check_hresult(previewDevice->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &previewContext));
            impl_->captureDevice = inspectable.as<IDirect3DDevice>();
            impl_->previewContext = std::move(previewContext);
            impl_->device = device;
        }
        std::erase_if(impl_->captures, [&](const auto& pair) {
            return std::none_of(sources.begin(), sources.end(), [&](const auto& source) {
                return source == pair.second->source;
            });
        });
        uint64_t allocated{};
        size_t active{};
        for (const auto& [id, capture] : impl_->captures) {
            // Bitmap import runs during drawing. Defer capture COM teardown to
            // this timer-owned path, where message pumping is safe.
            if (FAILED(capture->restartError))
                capture->Retry(L"bitmap-import-retry", capture->restartError);
            if (!capture->pool) continue;
            ++active;
            allocated += capture->reserved;
        }
        for (const auto& source : sources) {
            auto found = impl_->captures.find(source.windowId);
            if (found == impl_->captures.end()) {
                if (impl_->captures.size() >= 64) break;
                auto entry = std::make_unique<Capture>();
                entry->source = source;
                found = impl_->captures.emplace(source.windowId, std::move(entry)).first;
            }
            auto& capture = found->second;
            if (capture->session || GetTickCount64() < capture->retryAt) continue;
            if (active >= MaximumSources) { capture->SetState(L"waiting-source-budget"); continue; }
            try {
                if (!Validate(source)) { capture->Retry(L"source-validation-retry", S_FALSE); continue; }
                if (IsIconic(source.window)) { capture->SetState(L"minimized"); continue; }
                if (!capture->item) {
                    const auto interop = winrt::get_activation_factory<
                        GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
                    winrt::check_hresult(interop->CreateForWindow(source.window,
                        winrt::guid_of<GraphicsCaptureItem>(), winrt::put_abi(capture->item)));
                }
                if (capture->size.Width <= 0 || capture->size.Height <= 0)
                    capture->size = capture->item.Size();
                const auto size = capture->size;
                if (size.Width <= 0 || size.Height <= 0) {
                    capture->Retry(L"empty-size-retry", E_PENDING);
                    continue;
                }
                const auto pixels = static_cast<uint64_t>(size.Width) * size.Height;
                const auto bytes = BufferBytes(size);
                if (pixels > MaximumSourcePixels || allocated + bytes > MaximumBufferBytes) {
                    capture->Close();
                    capture->SetState(pixels > MaximumSourcePixels
                        ? L"waiting-source-pixel-budget" : L"waiting-total-byte-budget");
                    // Re-read the current size after a resize even when no pool
                    // fits. A stale oversized capture must not stay stuck forever.
                    capture->size = {};
                    capture->retryAt = GetTickCount64() + 1000;
                    continue;
                }
                capture->ready = std::make_shared<std::atomic_bool>(false);
                capture->CreatePreview(device, impl_->previewContext.Get());
                capture->pool = Direct3D11CaptureFramePool::CreateFreeThreaded(
                    impl_->captureDevice, DirectXPixelFormat::B8G8R8A8UIntNormalized, 1, size);
                capture->arrived = capture->pool.FrameArrived(
                    [ready = capture->ready](auto&&, auto&&) { ready->store(true); });
                capture->session = capture->pool.CreateCaptureSession(capture->item);
                capture->session.IsCursorCaptureEnabled(false);
                capture->session.StartCapture();
                capture->startedAt = GetTickCount64();
                capture->SetState(L"starting");
                capture->reserved = bytes;
                allocated += bytes;
                ++active;
            } catch (const winrt::hresult_error& error) {
                capture->Retry(L"capture-start-retry", error.code());
            } catch (...) {
                capture->Retry(L"capture-start-retry", E_FAIL);
            }
        }
    } catch (...) { Reset(); }
}

std::vector<std::wstring> WindowPreviewCapture::Poll() noexcept {
    std::vector<std::wstring> changed;
    for (auto& [id, capture] : impl_->captures) {
        if (!capture->session) continue;
        try {
            if (!Validate(capture->source)) throw winrt::hresult_error(E_HANDLE);
            if (IsIconic(capture->source.window)) {
                capture->Pause();
                capture->SetState(L"minimized");
                changed.push_back(id);
                continue;
            }
            if (!capture->previewReady && GetTickCount64() - capture->startedAt >= FirstFrameTimeoutMilliseconds) {
                capture->Retry(L"first-frame-timeout-retry", HRESULT_FROM_WIN32(ERROR_TIMEOUT));
                changed.push_back(id);
                continue;
            }
            if (!capture->ready->exchange(false)) continue;
            auto frame = capture->pool.TryGetNextFrame();
            if (!frame) continue;
            struct FrameLease final {
                Direct3D11CaptureFrame& frame;
                ~FrameLease() { try { if (frame) frame.Close(); } catch (...) {} }
            } lease{frame};
            const auto size = frame.ContentSize();
            if (size.Width != capture->size.Width || size.Height != capture->size.Height) {
                frame.Close();
                // ContentSize is authoritative; GraphicsCaptureItem.Size can still
                // describe the original window size. Recreate under the aggregate budget.
                capture->Pause();
                capture->size = size;
                capture->SetState(L"resizing");
                changed.push_back(id);
                continue;
            }
            capture->CopyPreview(impl_->previewContext.Get(), frame);
            frame.Close();
            frame = nullptr;
            capture->failures = 0;
            capture->SetState(L"live");
            changed.push_back(id);
        } catch (const winrt::hresult_error& error) {
            capture->Retry(L"capture-frame-retry", error.code());
            changed.push_back(id);
        } catch (...) {
            capture->Retry(L"capture-frame-retry", E_FAIL);
            changed.push_back(id);
        }
    }
    return changed;
}

Microsoft::WRL::ComPtr<ID2D1Bitmap1> WindowPreviewCapture::Bitmap(
    ID2D1RenderTarget* target, std::wstring_view windowId) noexcept {
    Microsoft::WRL::ComPtr<ID2D1Bitmap1> bitmap;
    const auto found = impl_->captures.find(windowId);
    if (!target || found == impl_->captures.end() || !found->second->previewReady) return bitmap;
    try {
        Microsoft::WRL::ComPtr<IDXGISurface> surface;
        winrt::check_hresult(found->second->preview.As(&surface));
        Microsoft::WRL::ComPtr<ID2D1DeviceContext> context;
        winrt::check_hresult(target->QueryInterface(IID_PPV_ARGS(&context)));
        const auto properties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_NONE,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
        winrt::check_hresult(context->CreateBitmapFromDxgiSurface(
            surface.Get(), &properties, &bitmap));
    } catch (const winrt::hresult_error& error) {
        found->second->restartError = error.code();
        bitmap.Reset();
    } catch (...) {
        found->second->restartError = E_FAIL;
        bitmap.Reset();
    }
    return bitmap;
}

std::vector<std::wstring> WindowPreviewCapture::TakeDiagnostics() {
    std::vector<std::wstring> result;
    for (const auto& [id, capture] : impl_->captures) {
        if (capture->state.empty() || (capture->state == capture->reportedState &&
            capture->stateError == capture->reportedError && capture->stateSize == capture->reportedSize)) continue;
        result.push_back(L"Window preview id=" + id + L" state=" + std::wstring(capture->state) +
            L" size=" + std::to_wstring(capture->stateSize.Width) + L"x" +
            std::to_wstring(capture->stateSize.Height) + L" hresult=" +
            std::to_wstring(static_cast<long>(capture->stateError)) +
            L" buffer-bytes=" + std::to_wstring(capture->reserved) +
            L" total-buffer-bytes=" + std::to_wstring(reservedBytes()));
        capture->reportedState = capture->state;
        capture->reportedError = capture->stateError;
        capture->reportedSize = capture->stateSize;
    }
    return result;
}

size_t WindowPreviewCapture::activeCount() const noexcept {
    return std::count_if(impl_->captures.begin(), impl_->captures.end(),
        [](const auto& pair) { return pair.second->session != nullptr; });
}

std::uint64_t WindowPreviewCapture::reservedBytes() const noexcept {
    std::uint64_t total{};
    for (const auto& [id, capture] : impl_->captures) total += capture->reserved;
    return total;
}

} // namespace widgetrail
