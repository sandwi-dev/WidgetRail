#pragma once

#include <Windows.h>
#include <d2d1_1.h>
#include <dcomp.h>
#include <d3d11.h>
#include <wrl/client.h>

#include <cstdint>
#include <span>
#include <string>

namespace widgetrail {

/// BeginDraw returns a backing-surface offset in physical pixels. Direct2D
/// drawing uses DIPs after SetDpi, so normalize before composing the offset
/// with the interface-scale transform.
[[nodiscard]] constexpr D2D1_POINT_2F NormalizeCompositionUpdateOffset(
    const POINT physicalPixels, const unsigned int dpi) noexcept {
    const float dipPerPixel = 96.0F /
        static_cast<float>(dpi == 0 ? 96U : dpi);
    return {
        static_cast<float>(physicalPixels.x) * dipPerPixel,
        static_cast<float>(physicalPixels.y) * dipPerPixel,
    };
}

// DirectComposition defines both the requested update rectangle and the
// returned backing-atlas offset in physical surface pixels. Keep that boundary
// in one pixel coordinate space and scale the logical scene exactly once. This
// avoids fractional-DPI target conversion choosing a different raster origin
// for a bounded update than for a full-surface update.
struct CompositionUpdateRasterMapping final {
    RECT requestedPixels{};
    POINT atlasOffsetPixels{};
    float scenePixelsPerDip{1.0F};
    D2D1_POINT_2F requestedLogicalOriginDip{};
    D2D1_POINT_2F sceneTranslationPixels{};
    D2D1_POINT_2F mappedRequestedOriginPixels{};
};

[[nodiscard]] constexpr CompositionUpdateRasterMapping
PlanCompositionUpdateRasterMapping(
    const RECT requestedPixels,
    const POINT atlasOffsetPixels,
    const float physicalPixelsPerDip) noexcept {
    const float scale = physicalPixelsPerDip > 0.0F
        ? physicalPixelsPerDip
        : 1.0F;
    const D2D1_POINT_2F logicalOrigin{
        static_cast<float>(requestedPixels.left) / scale,
        static_cast<float>(requestedPixels.top) / scale,
    };
    const D2D1_POINT_2F translation{
        static_cast<float>(atlasOffsetPixels.x - requestedPixels.left),
        static_cast<float>(atlasOffsetPixels.y - requestedPixels.top),
    };
    return {
        requestedPixels,
        atlasOffsetPixels,
        scale,
        logicalOrigin,
        translation,
        {
            logicalOrigin.x * scale + translation.x,
            logicalOrigin.y * scale + translation.y,
        },
    };
}

// One DirectComposition presentation owner for the overlay HWND. A replacement
// surface is rendered while it is detached from the visual tree; the caller
// attaches it only after EndDraw has completed the full update rectangle.
class OverlayCompositionSurface final {
public:
    enum class ExternalContentEndpoint { Overlay, Pinned };
    enum class ExternalContentCoordinateSpace { ContentLocal, EndpointLocal };
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
        RECT updateArea{};
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
        bool externalPresentationCommitted{};
    };

    struct ExternalContentCommitCounters final {
        std::uint64_t requested{};
        std::uint64_t committed{};
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
    // Adds the fixed chrome endpoint to the existing device. This deliberately
    // creates a second DirectComposition target, not another graphics owner.
    bool InitializeChromeTarget(HWND window, std::wstring& error);
    bool InitializePinnedExternalContentEndpoint(HWND window, std::wstring& error);
    void Reset() noexcept;

    [[nodiscard]] bool available() const noexcept { return device_ != nullptr; }
    [[nodiscard]] bool hasContent() const noexcept;
    [[nodiscard]] bool hasContent(Layer layer) const noexcept;
    [[nodiscard]] unsigned int width() const noexcept;
    [[nodiscard]] unsigned int height() const noexcept;
    [[nodiscard]] unsigned int width(Layer layer) const noexcept;
    [[nodiscard]] unsigned int height(Layer layer) const noexcept;
    [[nodiscard]] PaintCounters paintCounters() const noexcept { return paintCounters_; }
    [[nodiscard]] ExternalContentCommitCounters externalContentCommitCounters(
        ExternalContentEndpoint endpoint) const noexcept;

    /// Ordinary frame commits own content placement only. Guide/tray offsets
    /// belong to the fixed chrome session across repaint and replacement.
    [[nodiscard]] static constexpr bool FrameOwnsVisualOffset(
        const Frame& frame) noexcept {
        return frame.layer == Layer::Content;
    }

    // MediaViewport geometry is emitted in the owning renderer's local space.
    // Overlay media must therefore inherit the content visual's presentation
    // transform; the separately targeted pinned endpoint is already local.
    [[nodiscard]] static constexpr ExternalContentCoordinateSpace
    ExternalContentCoordinates(const ExternalContentEndpoint endpoint) noexcept {
        return endpoint == ExternalContentEndpoint::Overlay
            ? ExternalContentCoordinateSpace::ContentLocal
            : ExternalContentCoordinateSpace::EndpointLocal;
    }

    HRESULT BeginFrame(unsigned int width, unsigned int height, Frame& frame) noexcept;
    HRESULT BeginFrame(
        Layer layer, unsigned int width, unsigned int height,
        float visualOffsetX, float visualOffsetY,
        const RECT* update, Frame& frame) noexcept;
    HRESULT EndFrame(Frame& frame) noexcept;
    HRESULT CommitFrame(
        Frame& frame, bool waitForCompletion, CommitTiming& timing,
        const VisualPresentation* presentation = nullptr) noexcept;
    HRESULT CommitFrames(
        std::span<Frame*> frames, bool waitForCompletion, CommitTiming& timing,
        const VisualPresentation* presentation = nullptr) noexcept;
    HRESULT CommitPresentation(
        const VisualPresentation& presentation, CommitTiming& timing) noexcept;
    // Fixed chrome placement is a separate, typed presentation operation.
    // Content frame replacement and motion cannot mutate these offsets.
    HRESULT CommitChromePresentation(
        const ChromePresentation& presentation, CommitTiming& timing) noexcept;
    HRESULT CommitOpacity(float opacity, CommitTiming& timing) noexcept;
    // Creates the only external content slot under this HWND's existing root.
    // The caller may connect a composition-hosted renderer to the returned
    // visual, but this class remains the sole visual-tree/presentation owner.
    HRESULT CreateExternalContentTarget(IUnknown** target) noexcept;
    HRESULT CreateExternalContentTarget(
        ExternalContentEndpoint endpoint, IUnknown** target) noexcept;
    HRESULT CommitExternalContentPresentation(
        const RECT& bounds, bool visible, CommitTiming& timing) noexcept;
    HRESULT CommitExternalContentPresentation(
        const RECT& bounds, const RECT& clipBounds,
        bool visible, CommitTiming& timing) noexcept;
    HRESULT DetachExternalContentTarget(CommitTiming& timing) noexcept;
    HRESULT CommitExternalContentPresentation(
        ExternalContentEndpoint endpoint, const RECT& bounds,
        bool visible, CommitTiming& timing) noexcept;
    HRESULT CommitExternalContentPresentation(
        ExternalContentEndpoint endpoint, const RECT& bounds,
        const RECT& clipBounds, bool visible, CommitTiming& timing) noexcept;
    HRESULT DetachExternalContentTarget(
        ExternalContentEndpoint endpoint, CommitTiming& timing) noexcept;
    HRESULT ReleasePinnedExternalContentEndpoint(CommitTiming& timing) noexcept;
    void AbandonFrame(Frame& frame) noexcept;

private:
    Microsoft::WRL::ComPtr<ID3D11Device> d3dDevice_;
    Microsoft::WRL::ComPtr<ID2D1Device> d2dDevice_;
    Microsoft::WRL::ComPtr<IDCompositionDesktopDevice> desktopDevice_;
    Microsoft::WRL::ComPtr<IDCompositionDevice2> device_;
    Microsoft::WRL::ComPtr<IDCompositionTarget> target_;
    Microsoft::WRL::ComPtr<IDCompositionTarget> chromeTarget_;
    Microsoft::WRL::ComPtr<IDCompositionTarget> pinnedExternalTarget_;
    struct LayerState final {
        Microsoft::WRL::ComPtr<IDCompositionVisual2> visual;
        Microsoft::WRL::ComPtr<IDCompositionSurface> surface;
        unsigned int width{};
        unsigned int height{};
    };

    Microsoft::WRL::ComPtr<IDCompositionVisual2> rootVisual_;
    Microsoft::WRL::ComPtr<IDCompositionVisual2> externalContentVisual_;
    bool externalContentAttached_{};
    Microsoft::WRL::ComPtr<IDCompositionVisual2> pinnedExternalRootVisual_;
    Microsoft::WRL::ComPtr<IDCompositionVisual2> pinnedExternalContentVisual_;
    bool pinnedExternalContentAttached_{};
    struct ExternalContentPresentationState final {
        bool current{};
        IDCompositionVisual2* visual{};
        IDCompositionVisual2* parent{};
        RECT bounds{};
        RECT clipBounds{};
        bool visible{};
        ExternalContentCommitCounters counters{};
    };
    ExternalContentPresentationState externalContentPresentation_{};
    ExternalContentPresentationState pinnedExternalContentPresentation_{};
    Microsoft::WRL::ComPtr<IDCompositionVisual2> chromeRootVisual_;
    Microsoft::WRL::ComPtr<IDCompositionEffectGroup> effect_;
    LayerState content_;
    LayerState guide_;
    LayerState tray_;
    PaintCounters paintCounters_{};

    HRESULT ApplyPresentation(const VisualPresentation& presentation) noexcept;
    HRESULT ApplyChromePresentation(const ChromePresentation& presentation) noexcept;
    [[nodiscard]] LayerState& StateFor(Layer layer) noexcept;
    [[nodiscard]] const LayerState& StateFor(Layer layer) const noexcept;
    [[nodiscard]] ExternalContentPresentationState& ExternalPresentationFor(
        ExternalContentEndpoint endpoint) noexcept;
    [[nodiscard]] const ExternalContentPresentationState& ExternalPresentationFor(
        ExternalContentEndpoint endpoint) const noexcept;
};

} // namespace widgetrail
