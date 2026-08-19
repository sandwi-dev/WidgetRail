#pragma once

#include "DeclarativeLayout.h"
#include "DeclarativeMotion.h"
#include "NativeStyle.h"
#include "WidgetBridgeClient.h"

#include <d2d1_1.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <cstdint>
#include <cstddef>
#include <map>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace widgetrail {

class RemoteImageCache;

enum class RenderDiagnosticSeverity {
    Warning,
    Error,
};

struct RenderDiagnostic final {
    RenderDiagnosticSeverity severity{RenderDiagnosticSeverity::Warning};
    std::wstring nodeId;
    std::wstring code;
    std::wstring message;
};

struct RenderHitRegion final {
    std::wstring nodeId;
    declarative::Rect rect;
    bool enabled{};
};

struct RenderAccessibilityRegion final {
    std::wstring nodeId;
    declarative::Rect rect;
};

struct RenderScrollViewport final {
    declarative::ScrollAxis axis{declarative::ScrollAxis::None};
    declarative::Rect rect;
    float offset{};
    float maximumOffset{};
};

/// Exact shared geometry for a Button's optional leading visual, label, and
/// trailing semantic state cue. Empty rectangles represent absent content.
struct ButtonContentPlacement final {
    declarative::Rect leading;
    declarative::Rect text;
    declarative::Rect trailingStateCue;
};

struct DeclarativeRenderTiming final {
    std::uint64_t totalMicroseconds{};
    std::uint64_t preparationMicroseconds{};
    std::uint64_t presentationMicroseconds{};
    std::uint64_t clipSetupMicroseconds{};
    std::uint64_t nodeDrawMicroseconds{};
    std::uint64_t deferredFocusMicroseconds{};
    std::uint64_t finalizationMicroseconds{};
    /// One bounded, render-local convergence summary. It is populated only
    /// when focus following is slow, unusually iterative, or non-convergent;
    /// the existing host slow-frame diagnostic remains the sole log owner.
    std::wstring focusFollowSummary;
    /// One bounded event record for an admitted cursor-collection change.
    /// Stable collection paints and controller repeats leave this empty; the
    /// existing host post-commit diagnostic remains the sole log owner.
    std::wstring collectionAdmissionSummary;
};

struct RenderResult final {
    bool succeeded{};
    /// True only while at least one paint-only node transition requires a
    /// future frame. The renderer never owns a timer or animation thread.
    bool animationActive{};
    /// Captured on every call but emitted only by the existing host diagnostic
    /// when the containing composition frame exceeds its slow threshold.
    DeclarativeRenderTiming timing;
    std::vector<RenderDiagnostic> diagnostics;
    std::vector<RenderHitRegion> hitRegions;
    /// Visible semantic geometry retained for the immutable Windows
    /// accessibility snapshot. Decorative layout nodes are deliberately absent.
    std::vector<RenderAccessibilityRegion> accessibilityRegions;
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
    // Test-only exact geometry seam. Production results intentionally retain
    // only interactive geometry so ordinary paints do not allocate two maps
    // for every decorative and structural node.
    std::map<std::wstring, declarative::Rect, std::less<>> elementRects;
    std::map<std::wstring, declarative::Rect, std::less<>> elementVisibleRects;
    std::map<std::wstring, float, std::less<>> sliderThumbXs;
    std::map<std::wstring, ButtonContentPlacement, std::less<>> buttonContentPlacements;
    std::map<std::wstring, std::uint32_t, std::less<>> textLineCounts;
#endif
    std::map<std::wstring, declarative::Rect, std::less<>> focusRects;
    // Full logical controller geometry includes offscreen descendants of a
    // semantic scroll container. Pointer hit regions remain visible-only.
    std::map<std::wstring, declarative::Rect, std::less<>> navigationRects;
    std::map<std::wstring, bool, std::less<>> navigationEnabled;
    std::set<std::wstring, std::less<>> revealableFocusIds;
    std::map<std::wstring, std::wstring, std::less<>> focusScopes;
    std::map<std::wstring, float, std::less<>> scrollOffsets;
    std::map<std::wstring, RenderScrollViewport, std::less<>> scrollViewports;
    std::optional<declarative::Rect> currentFocusRect;
    // Deferred outlines normally escape ordinary layout clips. A focused
    // scroll descendant additionally carries the effective scroll viewport so
    // its ring cannot paint over adjacent rows or host chrome.
    std::optional<declarative::Rect> currentFocusOutlineClip;
};

struct ContentMeasureResult final {
    bool succeeded{};
    declarative::Size extent{};
    std::vector<RenderDiagnostic> diagnostics;
};

struct ImagePlacement final {
    declarative::Rect destination;
    declarative::Rect source;
};

struct ImageBitmapCacheStats final {
    std::size_t entries{};
    std::size_t bytes{};
    std::uint64_t hits{};
    std::uint64_t creates{};
    std::uint64_t evictions{};
    std::uint64_t resourceInvalidations{};
    std::uint64_t resourceGeneration{};
};

enum class IncrementalPresentationWork {
    NoRaster,
    PaintOnly,
    LocalLayout,
    FullRaster,
};

struct IncrementalPresentationPlan final {
    IncrementalPresentationWork work{IncrementalPresentationWork::PaintOnly};
    declarative::Rect damage;
};

struct FocusedFreeScrollPlan final {
    IncrementalPresentationPlan render;
    std::wstring scrollId;
    declarative::ScrollAxis axis{declarative::ScrollAxis::None};
    declarative::Rect viewport;
    float priorOffset{};
    float offset{};
    float maximumOffset{};
};

struct DeclarativeRenderOptions final {
    float pixelScale{1.0F};
    float rootFontSizePx{16.0F};
    /// Semantic geometry is retained only while a native accessibility client
    /// is active, avoiding per-frame tree allocations during ordinary gameplay.
    bool collectAccessibility{};
    /// Host surface dimensions before detached chrome (such as the controller
    /// guide footer) is removed from the widget content viewport. Responsive
    /// branches describe the surface the widget requested, not an internal
    /// post-chrome layout rectangle.
    std::optional<declarative::Size> responsiveViewport;
    std::optional<NativeColor> surfaceBackground;
    /// Host-owned rounded viewport mask. Widget roots may paint an opaque
    /// full-viewport background, but cannot square off the shell panel's
    /// corners or paint into detached host chrome.
    float surfaceCornerRadiusPx{};
    NativeAccessibilityPolicy accessibility;
    /// Optional deterministic monotonic clock seam. Production supplies the
    /// host tick; native tests can advance exact transition positions.
    std::optional<std::uint64_t> animationTimestampMilliseconds;
    /// Exact node-ID optimistic values owned by the controller slider state.
    /// The immutable widget snapshot remains authoritative after acknowledgement
    /// or timeout.
    std::map<std::wstring, double, std::less<>> sliderValueOverrides;
    /// Exact focused/actionable node held by a physical controller press. The
    /// host owns this transient state; widget snapshots remain immutable.
    std::wstring pressedElementId;
    /// Current bridge widget ID used only to bind lazy opaque artwork demand.
    std::wstring artworkWidgetId;
    /// Right-stick free scroll deliberately retains semantic focus without
    /// allowing that descendant to pull the viewport back until re-entry.
    bool suppressFocusedDescendantFollow{};
};

[[nodiscard]] constexpr bool IsCompactResponsiveSurface(
    const declarative::Size surface) noexcept {
    return surface.width < 960.0F || surface.height < 540.0F;
}

/// Resolves the exact bridge-computed state used for a native paint. Pressed
/// is intentionally layered on focused because controller activation always
/// belongs to the focused actionable element.
[[nodiscard]] WidgetComputedStyle ResolveDeclarativeComputedStyle(
    const WidgetNode& node,
    bool focused,
    bool pressed);

/// Accessibility-safe state presentation. High contrast and reduced
/// transparency retain semantic state cues without fading content below the
/// contrast selected by NativeStyleAdapter.
[[nodiscard]] float DeclarativeStateOpacityFactor(
    bool disabled,
    bool busy,
    const NativeAccessibilityPolicy& accessibility) noexcept;

[[nodiscard]] bool UseAccessibleDeclarativeStateCue(
    const NativeAccessibilityPolicy& accessibility) noexcept;

class DeclarativeRenderer final {
public:
    DeclarativeRenderer(
        ID2D1Factory* d2dFactory,
        IDWriteFactory* writeFactory,
        RemoteImageCache* imageCache) noexcept;

    DeclarativeRenderer(const DeclarativeRenderer&) = delete;
    DeclarativeRenderer& operator=(const DeclarativeRenderer&) = delete;

    [[nodiscard]] RenderResult Render(
        ID2D1RenderTarget* renderTarget,
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId,
        declarative::Rect viewport,
        const DeclarativeRenderOptions& options = {});

    /// Binds one admitted update to the last complete renderer checkpoint.
    /// A missing plan means the caller must conservatively repaint/re-layout
    /// the complete content surface. The next matching Render consumes it.
    [[nodiscard]] std::optional<IncrementalPresentationPlan>
    PlanPresentationUpdate(
        const WidgetSnapshot& snapshot,
        const WidgetPresentationImpact& impact,
        declarative::Rect viewport);

    /// Binds an ordinary host-owned focus move to the complete committed
    /// renderer checkpoint. Scroll ancestors are the bounded damage boundary;
    /// a missing/mismatched checkpoint requires a conservative full repaint.
    [[nodiscard]] std::optional<IncrementalPresentationPlan>
    PlanFocusUpdate(
        const WidgetSnapshot& snapshot,
        std::wstring_view priorFocusedElementId,
        std::wstring_view nextFocusedElementId,
        declarative::Rect viewport);

    /// Unions exact host-owned visual rollbacks into the same committed-layout
    /// focus plan. Missing cache geometry remains a conservative full fallback.
    [[nodiscard]] std::optional<IncrementalPresentationPlan>
    PlanFocusUpdate(
        const WidgetSnapshot& snapshot,
        std::wstring_view priorFocusedElementId,
        std::wstring_view nextFocusedElementId,
        declarative::Rect viewport,
        const std::vector<std::wstring>& additionalPaintNodeIds);

    /// Applies one bounded right-stick delta through the renderer's sole
    /// retained scroll-offset authority and prepares the matching local Taffy
    /// boundary for repaint. A missing result leaves both offset and raster
    /// state unchanged.
    [[nodiscard]] std::optional<FocusedFreeScrollPlan> PlanFocusedFreeScroll(
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId,
        declarative::ScrollAxis axis,
        float deltaDip,
        declarative::Rect viewport,
        std::wstring_view exactScrollId = {});

    void CancelPresentationUpdatePlan() noexcept;

    /// Advances the existing complete renderer checkpoint after a typed
    /// semantic-only admission. Geometry and raster state remain unchanged.
    [[nodiscard]] bool AcceptNoRasterPresentationUpdate(
        const WidgetSnapshot& snapshot,
        const WidgetPresentationImpact& impact,
        declarative::Rect viewport) noexcept;

    /// Performs the one bounded intrinsic host pass used only by an explicit
    /// Content surface axis. It shares native style, DirectWrite leaf
    /// measurement, responsive visibility, and Taffy tree preparation with
    /// final Render, but does not paint or mutate focus/scroll ownership.
    [[nodiscard]] ContentMeasureResult MeasureContent(
        const WidgetSnapshot& snapshot,
        declarative::Size admittedMaximumExtent,
        bool intrinsicWidth,
        const DeclarativeRenderOptions& options = {});

    void DiscardTargetResources() noexcept;

    /// Cumulative bounded GPU-image activity for the existing presentation
    /// diagnostic. Transient BeginDraw context identity is deliberately absent;
    /// resource generation advances only when the underlying D2D device domain
    /// changes (or a non-device render target is replaced).
    [[nodiscard]] ImageBitmapCacheStats GetImageBitmapCacheStats() const noexcept;

    /// Drops host-owned offsets when a widget instance is removed or replaced.
    void ForgetWidgetState(std::wstring_view widgetInstanceId) noexcept;

    // Pure deterministic seam used by the renderer and native tests.
    [[nodiscard]] static ImagePlacement ComputeImagePlacement(
        declarative::Size imageSize,
        declarative::Rect destination,
        NativeImageFit fit,
        NativeObjectPosition position) noexcept;

    [[nodiscard]] static ButtonContentPlacement ComputeButtonContentPlacement(
        declarative::Rect content,
        float leadingSize,
        float measuredTextWidth,
        bool hasLeading,
        bool hasText,
        bool reserveTrailingStateCue,
        NativeTextAlign alignment = NativeTextAlign::Center) noexcept;

private:
    struct PreparedNode;
    struct RenderPass;
    enum class ImagePresentationState {
        Pending,
        Ready,
        Failed,
        TrustedArtworkUnavailable,
    };
    struct TextMeasurementProof final {
        float maximumWidth{};
        float maximumHeight{};
        float measuredWidth{};
        float measuredHeight{};
        float layoutWidth{};
        float layoutHeight{};
        float inkInsetTop{};
        float inkInsetBottom{};
        float baseline{};
        std::uint32_t lineCount{};
        bool valid{};
    };
    struct IncrementalNodeState final {
        declarative::Rect paintBounds;
        declarative::Rect visibleBounds;
        std::wstring safeBoundaryId;
        std::wstring parentId;
        bool scrollBoundary{};
    };
    struct CollectionDiagnosticItemGeometry final {
        float position{};
        float extent{};
        bool valid{};
    };
    struct CollectionDiagnosticObservation final {
        std::vector<std::wstring> itemKeys;
        std::vector<CollectionDiagnosticItemGeometry> itemGeometry;
        bool itemsTruncated{};
        std::wstring anchorKey;
        float anchorPosition{};
        bool hasAnchorPosition{};
        float offset{};
        float reconciliationOffsetBefore{};
        float reconciliationOffsetAfter{};
        bool hasReconciliationOffsets{};
        std::wstring reconciliationMode;
        std::wstring reconciliationKey;
        declarative::Rect focusRect;
        bool hasFocusRect{};
        declarative::Rect viewport;
        bool hasViewport{};
        bool containsFocusedElement{};
        bool containsRequestedFocus{};
    };
    struct IncrementalLayoutCache final {
        std::wstring instanceId;
        long long sequence{};
        std::wstring focusedElementId;
        std::wstring requestedFocusId;
        declarative::Rect viewport;
        declarative::LayoutResult layout;
        DeclarativeRenderOptions options;
        std::map<std::wstring, IncrementalNodeState, std::less<>> nodes;
        std::map<std::wstring, TextMeasurementProof, std::less<>> textMeasurements;
        std::map<std::wstring, CollectionDiagnosticObservation, std::less<>>
            collections;
        std::map<std::wstring, RenderScrollViewport, std::less<>> scrollViewports;
    };
    struct PendingIncrementalPlan final {
        std::wstring instanceId;
        long long baseSequence{};
        long long sequence{};
        IncrementalPresentationWork work{IncrementalPresentationWork::PaintOnly};
        declarative::Rect damage;
        std::vector<std::wstring> layoutBoundaries;
    };
    struct ScrollStateEntry final {
        float offset{};
        std::uint64_t lastAccess{};
        std::wstring anchorKey;
        float anchorPosition{};
        bool hasAnchorPosition{};
    };
    struct BitmapCacheEntry final {
        Microsoft::WRL::ComPtr<ID2D1Bitmap> bitmap;
        std::size_t bytes{};
        std::uint64_t lastUse{};
    };

    [[nodiscard]] Microsoft::WRL::ComPtr<ID2D1Bitmap> GetImageBitmap(
        ID2D1RenderTarget* renderTarget,
        const WidgetNode& node,
        RenderPass& pass,
        std::wstring_view artworkWidgetId,
        ImagePresentationState& presentationState);
    [[nodiscard]] bool EnsureSurfaceClip(
        ID2D1RenderTarget* renderTarget,
        declarative::Rect viewport,
        float radius);
    [[nodiscard]] bool BindBitmapResourceDomain(
        ID2D1RenderTarget* renderTarget) noexcept;
    void ClearBitmapCache(bool resourceInvalidation) noexcept;
    void TrimBitmapCache(std::size_t incomingBytes) noexcept;

    ID2D1Factory* d2dFactory_{};
    IDWriteFactory* writeFactory_{};
    RemoteImageCache* imageCache_{};
    Microsoft::WRL::ComPtr<IUnknown> bitmapResourceDomain_;
    bool bitmapResourceDomainIsDevice_{};
    ID2D1RenderTarget* surfaceClipTarget_{};
    declarative::Rect surfaceClipRect_{};
    float surfaceClipRadius_{};
    Microsoft::WRL::ComPtr<ID2D1Layer> surfaceClipLayer_;
    Microsoft::WRL::ComPtr<ID2D1RoundedRectangleGeometry> surfaceClipGeometry_;
    std::unordered_map<std::wstring, BitmapCacheEntry> bitmaps_;
    std::size_t bitmapBytes_{};
    std::uint64_t bitmapAccessClock_{};
    std::uint64_t bitmapHits_{};
    std::uint64_t bitmapCreates_{};
    std::uint64_t bitmapEvictions_{};
    std::uint64_t bitmapResourceInvalidations_{};
    std::uint64_t bitmapResourceGeneration_{};
    std::unordered_map<std::wstring, ScrollStateEntry> scrollOffsets_;
    std::uint64_t scrollStateAccessClock_{};
    DeclarativeMotionTimeline motionTimeline_;
    std::optional<IncrementalLayoutCache> incrementalLayoutCache_;
    std::optional<PendingIncrementalPlan> pendingIncrementalPlan_;
};

} // namespace widgetrail
