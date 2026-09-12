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
// At most two pool buffers and one retained frame per source: <=192 MiB
// at this aggregate pixel limit, excluding driver metadata and D2D wrappers.
constexpr uint64_t MaximumPixels = 16ULL * 1024ULL * 1024ULL;
constexpr uint64_t MaximumSourcePixels = 4096ULL * 2160ULL;

struct Capture final {
    WindowPreviewSource source;
    GraphicsCaptureItem item{nullptr};
    Direct3D11CaptureFramePool pool{nullptr};
    GraphicsCaptureSession session{nullptr};
    Direct3D11CaptureFrame frame{nullptr};
    winrt::Windows::Graphics::SizeInt32 size{};
    bool failed{};
    std::shared_ptr<std::atomic_bool> ready = std::make_shared<std::atomic_bool>(false);
    winrt::event_token arrived{};
    ~Capture() { Close(); }
    void Pause() noexcept {
        try { if (session) session.Close(); } catch (...) {}
        session = nullptr;
        try { if (frame) frame.Close(); } catch (...) {}
        frame = nullptr;
        try { if (pool) pool.FrameArrived(arrived); } catch (...) {}
        arrived = {};
        try { if (pool) pool.Close(); } catch (...) {}
        ready->store(false);
        pool = nullptr;
    }
    void Close() noexcept { Pause(); item = nullptr; }
};
}

struct WindowPreviewCapture::Impl final {
    Microsoft::WRL::ComPtr<ID3D11Device> device;
    IDirect3DDevice captureDevice{nullptr};
    std::map<std::wstring, std::unique_ptr<Capture>, std::less<>> captures;
};

WindowPreviewCapture::WindowPreviewCapture() : impl_(std::make_unique<Impl>()) {}
WindowPreviewCapture::~WindowPreviewCapture() = default;
void WindowPreviewCapture::Reset() noexcept {
    impl_->captures.clear();
    impl_->captureDevice = nullptr;
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
            impl_->captureDevice = inspectable.as<IDirect3DDevice>();
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
            if (!capture->pool) continue;
            ++active;
            allocated += static_cast<uint64_t>(capture->size.Width) * capture->size.Height;
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
            if (capture->failed || capture->session || active >= MaximumSources) continue;
            try {
                if (!Validate(source)) { capture->failed = true; continue; }
                if (IsIconic(source.window)) continue;
                if (!capture->item) {
                    const auto interop = winrt::get_activation_factory<
                        GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
                    winrt::check_hresult(interop->CreateForWindow(source.window,
                        winrt::guid_of<GraphicsCaptureItem>(), winrt::put_abi(capture->item)));
                }
                if (capture->size.Width <= 0 || capture->size.Height <= 0)
                    capture->size = capture->item.Size();
                const auto size = capture->size;
                if (size.Width <= 0 || size.Height <= 0) continue;
                const auto pixels = static_cast<uint64_t>(size.Width) * size.Height;
                if (pixels > MaximumSourcePixels || allocated + pixels > MaximumPixels) continue;
                capture->pool = Direct3D11CaptureFramePool::CreateFreeThreaded(
                    impl_->captureDevice, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, size);
                capture->arrived = capture->pool.FrameArrived(
                    [ready = capture->ready](auto&&, auto&&) { ready->store(true); });
                capture->session = capture->pool.CreateCaptureSession(capture->item);
                capture->session.IsCursorCaptureEnabled(false);
                capture->session.StartCapture();
                allocated += pixels;
                ++active;
            } catch (...) {
                capture->Close();
                capture->failed = true;
            }
        }
    } catch (...) { Reset(); }
}

std::vector<std::wstring> WindowPreviewCapture::Poll() noexcept {
    std::vector<std::wstring> changed;
    for (auto& [id, capture] : impl_->captures) {
        if (capture->failed || !capture->session) continue;
        try {
            if (!Validate(capture->source)) throw winrt::hresult_error(E_HANDLE);
            if (IsIconic(capture->source.window)) {
                capture->Pause();
                changed.push_back(id);
                continue;
            }
            if (!capture->ready->exchange(false)) continue;
            auto frame = capture->pool.TryGetNextFrame();
            if (!frame) continue;
            // Coalesced callbacks can describe both pool buffers. Consume the
            // newest one so queued frames cannot stall the producer or add lag.
            if (auto newer = capture->pool.TryGetNextFrame()) {
                frame.Close();
                frame = std::move(newer);
            }
            const auto size = frame.ContentSize();
            if (size.Width != capture->size.Width || size.Height != capture->size.Height) {
                frame.Close();
                // ContentSize is authoritative; GraphicsCaptureItem.Size can still
                // describe the original window size. Recreate under the aggregate budget.
                capture->Pause();
                capture->size = size;
                changed.push_back(id);
                continue;
            }
            if (capture->frame) capture->frame.Close();
            capture->frame = std::move(frame);
            changed.push_back(id);
        } catch (...) {
            capture->Close();
            capture->failed = true;
            changed.push_back(id);
        }
    }
    return changed;
}

Microsoft::WRL::ComPtr<ID2D1Bitmap1> WindowPreviewCapture::Bitmap(
    ID2D1RenderTarget* target, std::wstring_view windowId) noexcept {
    Microsoft::WRL::ComPtr<ID2D1Bitmap1> bitmap;
    const auto found = impl_->captures.find(windowId);
    if (!target || found == impl_->captures.end() || !found->second->frame) return bitmap;
    try {
        auto access = found->second->frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
        Microsoft::WRL::ComPtr<IDXGISurface> surface;
        winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&surface)));
        Microsoft::WRL::ComPtr<ID2D1DeviceContext> context;
        winrt::check_hresult(target->QueryInterface(IID_PPV_ARGS(&context)));
        const auto properties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_NONE,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
        winrt::check_hresult(context->CreateBitmapFromDxgiSurface(
            surface.Get(), &properties, &bitmap));
    } catch (...) { bitmap.Reset(); }
    return bitmap;
}

size_t WindowPreviewCapture::activeCount() const noexcept {
    return std::count_if(impl_->captures.begin(), impl_->captures.end(),
        [](const auto& pair) { return pair.second->session != nullptr; });
}

} // namespace widgetrail
