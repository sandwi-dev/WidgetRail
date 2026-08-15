#pragma once

#include <Windows.h>
#include <d2d1_1.h>
#include <dcomp.h>
#include <d3d11.h>
#include <wrl/client.h>

#include <cstdint>
#include <span>
#include <string>

namespace gba {

// One DirectComposition presentation owner for the overlay HWND. A replacement
// surface is rendered while it is detached from the visual tree; the caller
// attaches it only after EndDraw has completed the full update rectangle.
class OverlayCompositionSurface final {
public:
    enum class Layer {
        Content,
        Guide,
        Tray,
    };

    struct Frame final {
        Layer layer{Layer::Content};
        Microsoft::WRL::ComPtr<IDCompositionSurface> surface;
        Microsoft::WRL::ComPtr<ID2D1DeviceContext> target;
        POINT updateOffset{};
        unsigned int width{};
        unsigned int height{};
        float visualOffsetX{};
        float visualOffsetY{};
        bool replacement{};
        bool drawing{};
    };

    struct CommitTiming final {
        std::uint64_t commitMicroseconds{};
        bool waitedForCompletion{};
    };

    struct VisualPresentation final {
        float scaleX{1.0F};
        float scaleY{1.0F};
        float offsetX{};
        float offsetY{};
        float clipWidth{};
        float clipHeight{};
    };

    struct ChromePresentation final {
        float guideOffsetX{};
        float guideOffsetY{};
        float trayOffsetX{};
        float trayOffsetY{};
    };

    struct PaintCounters final {
        std::uint64_t content{};
        std::uint64_t guide{};
        std::uint64_t tray{};
    };

    bool Initialize(HWND window, ID2D1Factory1* factory, std::wstring& error);
    void Reset() noexcept;

    [[nodiscard]] bool available() const noexcept { return device_ != nullptr; }
    [[nodiscard]] bool hasContent() const noexcept;
    [[nodiscard]] bool hasContent(Layer layer) const noexcept;
    [[nodiscard]] unsigned int width() const noexcept;
    [[nodiscard]] unsigned int height() const noexcept;
    [[nodiscard]] unsigned int width(Layer layer) const noexcept;
    [[nodiscard]] unsigned int height(Layer layer) const noexcept;
    [[nodiscard]] PaintCounters paintCounters() const noexcept { return paintCounters_; }

    HRESULT BeginFrame(unsigned int width, unsigned int height, Frame& frame) noexcept;
    HRESULT BeginFrame(
        Layer layer, unsigned int width, unsigned int height,
        float visualOffsetX, float visualOffsetY,
        const RECT* update, Frame& frame) noexcept;
    HRESULT EndFrame(Frame& frame) noexcept;
    HRESULT CommitFrame(
        Frame& frame, bool waitForCompletion, CommitTiming& timing,
        const VisualPresentation* presentation = nullptr,
        const ChromePresentation* chrome = nullptr) noexcept;
    HRESULT CommitFrames(
        std::span<Frame*> frames, bool waitForCompletion, CommitTiming& timing,
        const VisualPresentation* presentation = nullptr,
        const ChromePresentation* chrome = nullptr) noexcept;
    HRESULT CommitPresentation(
        const VisualPresentation& presentation, CommitTiming& timing,
        const ChromePresentation* chrome = nullptr) noexcept;
    HRESULT CommitOpacity(float opacity, CommitTiming& timing) noexcept;
    void AbandonFrame(Frame& frame) noexcept;

private:
    Microsoft::WRL::ComPtr<ID3D11Device> d3dDevice_;
    Microsoft::WRL::ComPtr<ID2D1Device> d2dDevice_;
    Microsoft::WRL::ComPtr<IDCompositionDesktopDevice> desktopDevice_;
    Microsoft::WRL::ComPtr<IDCompositionDevice2> device_;
    Microsoft::WRL::ComPtr<IDCompositionTarget> target_;
    struct LayerState final {
        Microsoft::WRL::ComPtr<IDCompositionVisual2> visual;
        Microsoft::WRL::ComPtr<IDCompositionSurface> surface;
        unsigned int width{};
        unsigned int height{};
    };

    Microsoft::WRL::ComPtr<IDCompositionVisual2> rootVisual_;
    Microsoft::WRL::ComPtr<IDCompositionEffectGroup> effect_;
    LayerState content_;
    LayerState guide_;
    LayerState tray_;
    PaintCounters paintCounters_{};

    HRESULT ApplyPresentation(const VisualPresentation& presentation) noexcept;
    HRESULT ApplyChromePresentation(const ChromePresentation& presentation) noexcept;
    [[nodiscard]] LayerState& StateFor(Layer layer) noexcept;
    [[nodiscard]] const LayerState& StateFor(Layer layer) const noexcept;
};

} // namespace gba
