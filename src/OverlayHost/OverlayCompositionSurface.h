#pragma once

#include <Windows.h>
#include <d2d1_1.h>
#include <dcomp.h>
#include <d3d11.h>
#include <wrl/client.h>

#include <cstdint>
#include <string>

namespace gba {

// One DirectComposition presentation owner for the overlay HWND. A replacement
// surface is rendered while it is detached from the visual tree; the caller
// attaches it only after EndDraw has completed the full update rectangle.
class OverlayCompositionSurface final {
public:
    struct Frame final {
        Microsoft::WRL::ComPtr<IDCompositionSurface> surface;
        Microsoft::WRL::ComPtr<ID2D1DeviceContext> target;
        POINT updateOffset{};
        unsigned int width{};
        unsigned int height{};
        bool replacement{};
        bool drawing{};
    };

    struct CommitTiming final {
        std::uint64_t commitMicroseconds{};
        bool waitedForCompletion{};
    };

    bool Initialize(HWND window, ID2D1Factory1* factory, std::wstring& error);
    void Reset() noexcept;

    [[nodiscard]] bool available() const noexcept { return device_ != nullptr; }
    [[nodiscard]] bool hasContent() const noexcept { return surface_ != nullptr; }
    [[nodiscard]] unsigned int width() const noexcept { return width_; }
    [[nodiscard]] unsigned int height() const noexcept { return height_; }

    HRESULT BeginFrame(unsigned int width, unsigned int height, Frame& frame) noexcept;
    HRESULT EndFrame(Frame& frame) noexcept;
    HRESULT CommitFrame(
        Frame& frame, bool waitForCompletion, CommitTiming& timing) noexcept;
    void AbandonFrame(Frame& frame) noexcept;

private:
    Microsoft::WRL::ComPtr<ID3D11Device> d3dDevice_;
    Microsoft::WRL::ComPtr<ID2D1Device> d2dDevice_;
    Microsoft::WRL::ComPtr<IDCompositionDesktopDevice> desktopDevice_;
    Microsoft::WRL::ComPtr<IDCompositionDevice2> device_;
    Microsoft::WRL::ComPtr<IDCompositionTarget> target_;
    Microsoft::WRL::ComPtr<IDCompositionVisual2> visual_;
    Microsoft::WRL::ComPtr<IDCompositionSurface> surface_;
    unsigned int width_{};
    unsigned int height_{};
};

} // namespace gba
