#pragma once

#include "OverlayPosition.h"

#include <Windows.h>
#include <d2d1_1.h>
#include <dcomp.h>
#include <d3d11.h>
#include <wrl/client.h>

#include <cstdint>
#include <optional>
#include <span>
#include <string>
#include <string_view>

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
        PanelBackground,
        BackgroundBase,
        BackgroundOutgoing,
        BackgroundIncoming,
        Content,
        Guide,
        Tray,
    };
    // Back-to-front within the animated widget panel. Host fill must never
    // occlude artwork extracted from the declarative Content layer.
    static constexpr Layer ContentLayerOrder[] = {
        Layer::PanelBackground, Layer::BackgroundBase,
        Layer::BackgroundOutgoing, Layer::BackgroundIncoming, Layer::Content,
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

    // Names the exact precondition that rejected an external-content commit so
    // a caller can report why the endpoint refused the presentation.
    enum class ExternalCommitRejection {
        None,
        Device,
        Visual,
        Parent,
        Bounds,
        ClipBounds,
        DegenerateClip,
    };

    struct CommitTiming final {
        std::uint64_t commitMicroseconds{};
        bool waitedForCompletion{};
        bool externalPresentationCommitted{};
        ExternalCommitRejection externalCommitRejection{
            ExternalCommitRejection::None};
    };

    struct ExternalContentCommitCounters final {
        std::uint64_t requested{};
        std::uint64_t committed{};
    };

    struct ExternalContentRetirement final {
        HRESULT cleanupResult{S_OK};
        bool surfaceInvalidated{};
        bool surfaceRecovered{};

        [[nodiscard]] bool terminal() const noexcept {
            return SUCCEEDED(cleanupResult) || surfaceInvalidated;
        }
    };

    struct PinnedMediaChromePresentation final {
        RECT bounds{};
        bool visible{};
        bool focused{};
        bool scrubActive{};
        double progress{};
        D2D1_COLOR_F backgroundColor{};
        D2D1_COLOR_F trackColor{};
        D2D1_COLOR_F accentColor{};
        D2D1_COLOR_F focusColor{};
        double rasterScale{1.0};
    };

    struct VisualPresentation final {
        float scaleX{1.0F};
        float scaleY{1.0F};
        float offsetX{};
        float offsetY{};
        float clipWidth{};
        float clipHeight{};
        // Zero commits a static transform. Otherwise the compositor owns the
        // complete cubic motion, including while the UI thread is rendering.
        std::uint64_t durationMilliseconds{};
        float targetScaleX{1.0F};
        float targetScaleY{1.0F};
        float targetOffsetX{};
        float targetOffsetY{};
    };
    struct BackgroundPresentation final {
        bool visible{};
        bool hasIncoming{};
        bool prepared{};
        std::wstring key;
        std::uint64_t generation{};
        float elapsedMilliseconds{};
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
        std::uint64_t panelBackground{};
    };

    bool Initialize(HWND window, ID2D1Factory1* factory, std::wstring& error,
        bool softwareDevice = false);
    // Adds the fixed chrome endpoint to the existing device. This deliberately
    // creates a second DirectComposition target, not another graphics owner.
    bool InitializeChromeTarget(HWND window, std::wstring& error);
    bool InitializePinnedExternalContentEndpoint(HWND window, std::wstring& error);
    void Reset() noexcept;

    [[nodiscard]] ID3D11Device* graphicsDevice() const noexcept { return d3dDevice_.Get(); }
    [[nodiscard]] bool available() const noexcept { return device_ != nullptr; }
    [[nodiscard]] bool hasContent() const noexcept;
    [[nodiscard]] bool hasContent(Layer layer) const noexcept;
    [[nodiscard]] unsigned int width() const noexcept;
    [[nodiscard]] unsigned int height() const noexcept;
    [[nodiscard]] unsigned int width(Layer layer) const noexcept;
    [[nodiscard]] unsigned int height(Layer layer) const noexcept;
    [[nodiscard]] PaintCounters paintCounters() const noexcept { return paintCounters_; }
    // Resource loading must not begin an update on a currently displayed layer.
    HRESULT CreateBitmapResourceContext(ID2D1DeviceContext** context) noexcept;
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
        const VisualPresentation* presentation = nullptr,
        const BackgroundPresentation* background = nullptr,
        bool revealContent = false) noexcept;
    HRESULT CommitPreparedBackground(
        std::wstring_view key, std::uint64_t generation,
        CommitTiming& timing) noexcept;
    HRESULT RetireBackground(CommitTiming& timing) noexcept;
    HRESULT CommitPresentation(
        const VisualPresentation& presentation, CommitTiming& timing) noexcept;
    // Fixed chrome placement is a separate, typed presentation operation.
    // Content frame replacement and motion cannot mutate these offsets.
    HRESULT CommitChromePresentation(
        const ChromePresentation& presentation, CommitTiming& timing) noexcept;
    HRESULT CommitOpacity(float opacity, CommitTiming& timing) noexcept;
    HRESULT SnapContentVisible() noexcept;
    HRESULT CommitShellZoom(float fromScale, bool opening, bool reducedMotion,
        OverlayPosition position = OverlayPosition::Center) noexcept;
    HRESULT SetShellZoomAnchor(float width, float height,
        OverlayPosition position = OverlayPosition::Center) noexcept;
    // Creates the only external content slot under this HWND's existing root.
    // The caller may connect a composition-hosted renderer to the returned
    // visual, but this class remains the sole visual-tree/presentation owner.
    HRESULT CreateExternalContentTarget(IUnknown** target) noexcept;
    HRESULT CreateExternalContentTarget(
        ExternalContentEndpoint endpoint, IUnknown** target) noexcept;
    // Creates an unattached visual whose lifetime belongs to the caller. A
    // retained external-content session can keep its controller rooted here
    // while neither visible endpoint owns presentation.
    HRESULT CreateExternalContentParkingTarget(IUnknown** target) noexcept;
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
    HRESULT StageExternalContentPresentation(
        ExternalContentEndpoint endpoint, const RECT& bounds,
        const RECT& clipBounds, CommitTiming& timing) noexcept;
    HRESULT DetachExternalContentTarget(
        ExternalContentEndpoint endpoint, CommitTiming& timing) noexcept;
    HRESULT ReleasePinnedExternalContentEndpoint(CommitTiming& timing) noexcept;
    // A successful narrow cleanup conclusively retires the selected endpoint.
    // Any uncertain mutation/commit/wait failure invalidates the entire target
    // graph and rebuilds the content/chrome pair before callers may create a
    // later endpoint. Releasing COM target/device ownership is the fail-closed
    // boundary; stale visuals are never made reusable by bookkeeping alone.
    [[nodiscard]] ExternalContentRetirement RetireExternalContentEndpoint(
        ExternalContentEndpoint endpoint, bool releasePinnedEndpoint) noexcept;
    HRESULT CommitPinnedMediaChrome(
        const PinnedMediaChromePresentation& presentation,
        CommitTiming& timing) noexcept;
    void AbandonFrame(Frame& frame) noexcept;

private:
    Microsoft::WRL::ComPtr<ID3D11Device> d3dDevice_;
    Microsoft::WRL::ComPtr<ID2D1Device> d2dDevice_;
    Microsoft::WRL::ComPtr<IDCompositionDesktopDevice> desktopDevice_;
    Microsoft::WRL::ComPtr<IDCompositionDevice2> device_;
    Microsoft::WRL::ComPtr<IDCompositionTarget> target_;
    Microsoft::WRL::ComPtr<IDCompositionTarget> chromeTarget_;
    Microsoft::WRL::ComPtr<IDCompositionTarget> pinnedExternalTarget_;
    // The pinned HWND the endpoint above targets. A DirectComposition
    // target is bound to one window, so a new pinned window requires a
    // rebuilt endpoint while the same window can reuse it.
    HWND pinnedExternalWindow_{};
    HWND contentWindow_{};
    HWND chromeWindow_{};
    Microsoft::WRL::ComPtr<ID2D1Factory1> initializationFactory_;
    struct LayerState final {
        Microsoft::WRL::ComPtr<IDCompositionVisual2> visual;
        Microsoft::WRL::ComPtr<IDCompositionSurface> surface;
        unsigned int width{};
        unsigned int height{};
    };

    Microsoft::WRL::ComPtr<IDCompositionVisual2> rootVisual_;
    Microsoft::WRL::ComPtr<IDCompositionVisual2> presentationVisual_;
    Microsoft::WRL::ComPtr<IDCompositionVisual2> externalContentVisual_;
    bool externalContentAttached_{};
    Microsoft::WRL::ComPtr<IDCompositionVisual2> pinnedExternalRootVisual_;
    Microsoft::WRL::ComPtr<IDCompositionVisual2> pinnedExternalContentVisual_;
    bool pinnedExternalContentAttached_{};
    Microsoft::WRL::ComPtr<IDCompositionVisual2> pinnedMediaChromeVisual_;
    Microsoft::WRL::ComPtr<IDCompositionSurface> pinnedMediaChromeSurface_;
    bool pinnedMediaChromeAttached_{};
    std::optional<PinnedMediaChromePresentation>
        pinnedMediaChromePresentation_;
    unsigned int pinnedMediaChromeWidth_{};
    unsigned int pinnedMediaChromeHeight_{};
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
    LayerState panelBackground_;
    LayerState backgroundBase_;
    LayerState backgroundOutgoing_;
    LayerState backgroundIncoming_;
    LayerState content_;
    LayerState guide_;
    LayerState tray_;
    PaintCounters paintCounters_{};
    BackgroundPresentation backgroundPresentation_{};
    Microsoft::WRL::ComPtr<IDCompositionAnimation> backgroundIncomingAnimation_;
    Microsoft::WRL::ComPtr<IDCompositionMatrixTransform> presentationTransform_;
    Microsoft::WRL::ComPtr<IDCompositionAnimation> contentRevealAnimation_;
    Microsoft::WRL::ComPtr<IDCompositionScaleTransform> shellZoomTransform_;
    Microsoft::WRL::ComPtr<IDCompositionAnimation> shellOpacityAnimation_;

    HRESULT ApplyPresentation(const VisualPresentation& presentation) noexcept;
    HRESULT ApplyBackgroundPresentation(
        const BackgroundPresentation& presentation) noexcept;
    HRESULT ApplyChromePresentation(const ChromePresentation& presentation) noexcept;
    [[nodiscard]] LayerState& StateFor(Layer layer) noexcept;
    [[nodiscard]] const LayerState& StateFor(Layer layer) const noexcept;
    [[nodiscard]] ExternalContentPresentationState& ExternalPresentationFor(
        ExternalContentEndpoint endpoint) noexcept;
    [[nodiscard]] const ExternalContentPresentationState& ExternalPresentationFor(
        ExternalContentEndpoint endpoint) const noexcept;
};

} // namespace widgetrail
