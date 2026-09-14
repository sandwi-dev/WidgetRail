#pragma once

#include <Windows.h>
#include <d2d1_1.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <memory>
#include <span>
#include <string>
#include <vector>

namespace widgetrail {

struct WindowPreviewSource final {
    std::wstring windowId;
    HWND window{};
    DWORD processId{};
    ULONGLONG processCreated{};
    std::wstring className;
    friend bool operator==(const WindowPreviewSource&, const WindowPreviewSource&) = default;
};

// UI-thread owner. Windows capture supplies GPU frames independently; no widget
// callback, image decoding, CPU readback or snapshot participates in frame delivery.
class WindowPreviewCapture final {
public:
    WindowPreviewCapture();
    ~WindowPreviewCapture();
    WindowPreviewCapture(const WindowPreviewCapture&) = delete;
    WindowPreviewCapture& operator=(const WindowPreviewCapture&) = delete;
    void Reset() noexcept;
    void Reconcile(ID3D11Device* device, std::span<const WindowPreviewSource> sources) noexcept;
    [[nodiscard]] std::vector<std::wstring> Poll() noexcept;
    // State changes only; contains opaque IDs and capture metadata, never pixels or titles.
    [[nodiscard]] std::vector<std::wstring> TakeDiagnostics();
    [[nodiscard]] Microsoft::WRL::ComPtr<ID2D1Bitmap1> Bitmap(
        ID2D1RenderTarget* target, std::wstring_view windowId) noexcept;
    [[nodiscard]] size_t activeCount() const noexcept;
    [[nodiscard]] static bool Validate(const WindowPreviewSource& source) noexcept;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace widgetrail
