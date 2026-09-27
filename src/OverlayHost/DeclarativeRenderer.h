#pragma once

#include "NativeTextLayout.h"
#include "ControllerPrompt.h"

#include "DeclarativeLayout.h"
#include "CollectionLayoutState.h"
#include "DeclarativeMotion.h"
#include "WidgetTransitions.h"
#include "WidgetCompositionScene.h"
#include "NativeStyle.h"
#include "SurfaceDepth.h"
#include "CompositionPaintCache.h"
#include "RemoteImageCache.h"
#include "WidgetBridgeClient.h"
#include "FocusSurfaceSelection.h"

#include <d2d1_1.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <array>
#include <cstdint>
#include <cstddef>
#include <functional>
#include <map>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace widgetrail {

enum class RenderDiagnosticSeverity {
    Information,
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

// Committed logical navigation authority; no fabricated offscreen rectangles.
struct RenderLogicalCollection final {
    WidgetNode::CollectionLayout policy;
    WidgetComputedStyle containerStyle;
    std::wstring inputScope;
    std::size_t columns{1}, firstColumn{};
    std::int64_t startIndex{};
    std::uint64_t resetGeneration{};
    std::optional<std::uint64_t> firstItemIndex;
    std::vector<std::pair<std::wstring, std::wstring>> itemIdentities; // focus ID, key
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
    // Layout includes its intrinsic text callbacks; these are nested timings.
    std::uint64_t snapshotComparisonMicroseconds{};
    std::uint64_t updatePlanningMicroseconds{};
    std::uint64_t styleResolutionMicroseconds{};
    std::uint64_t textMeasurementMicroseconds{};
    std::uint64_t layoutMicroseconds{};
    std::uint64_t styleCacheHits{};
    std::uint64_t styleCacheMisses{};
    std::uint64_t textLayoutCacheHits{};
    std::uint64_t textLayoutCacheMisses{};
    std::uint64_t compositionPaintHits{}, compositionPaintMisses{}, compositionPaintedBytes{};
    /// One bounded, render-local convergence summary. It is populated only
    /// when focus following is slow, unusually iterative, or non-convergent;
    /// the existing host slow-frame diagnostic remains the sole log owner.
    std::wstring focusFollowSummary;
    /// One bounded event record for an admitted cursor-collection change.
    /// Stable collection paints and controller repeats leave this empty; the
    /// existing host post-commit diagnostic remains the sole log owner.
    std::wstring collectionAdmissionSummary;
    std::size_t preparedNodes{};
    std::size_t deferredViewportItems{};
    std::size_t intrinsicMeasures{};
};

// Opt-in developer data from the same prepared/presented nodes used to paint.
// Deliberately excludes text values, artwork handles and media/window IDs.
struct RenderInspectionNode final {
    std::wstring id, parentId, kind;
    declarative::Rect bounds, visibleBounds;
    NativeRenderStyle layoutStyle, paintStyle;
    bool laidOut{};
};

struct RenderInspection final {
    static constexpr std::size_t maximumNodes = 2048;
    declarative::Rect viewport;
    std::vector<RenderInspectionNode> nodes;
    bool truncated{};
};

enum class ResponsiveSurfaceMode {
    Compact,
    Expanded,
};

/// Exact responsive surface decision used by one successful renderer pass.
/// Host focus persistence consumes this committed geometry authority instead
/// of deriving a second mode from a post-chrome viewport.
struct ResponsiveSurfacePresentation final {
    declarative::Size viewport;
    ResponsiveSurfaceMode mode{ResponsiveSurfaceMode::Compact};
};

/// Exact final declarative geometry for the one host-owned embedded-media
/// pixel plane. Bounds and clip are surface-local DIPs from the same Taffy
/// layout/renderer pass that owns native paint and semantics.
struct RenderWindowPreviewRegion final {
    std::wstring nodeId;
    std::wstring windowId;
    declarative::Rect bounds;
    declarative::Rect clip;
};

struct RenderMediaViewportRegion final {
    std::wstring nodeId;
    std::wstring mediaSessionId;
    declarative::Rect bounds;
    declarative::Rect clip;
};

struct BackgroundSurfaceSettleWake final {
    declarative::Rect damage;
    std::uint64_t deadlineMilliseconds{};
};

struct ComputedCompositorBackground final {
    std::wstring authorityId;
    std::wstring artworkWidgetId;
    std::wstring artworkRuntimeGeneration;
    std::wstring artworkPresentationGeneration;
    std::wstring widgetInstanceId;
    std::wstring nodeId;
    std::wstring focusedElementId;
    std::wstring imageSource;
    std::wstring artworkHandle;
    std::wstring imageFit;
    declarative::Rect bounds;
    NativeRenderStyle style;
    float opacity{1.0F};
    long long snapshotSequence{};
    std::uint64_t resourceGeneration{};
    // Preserve the ancestor/viewport clip independently from image-fit bounds.
    std::optional<declarative::Rect> clipBounds;
    // Source selected by the shared semantic resolver; empty means authored default.
    // This is view-only identity, distinct from current input focus.
    std::wstring selectionSourceId;
    ImageDecodeSize decodeSize{};
};

struct RenderResult final {
    std::shared_ptr<const WidgetCompositionScene> widgetComposition;
    bool playStationControls{};
    bool succeeded{};
    std::uint64_t publicationId{};
    std::shared_ptr<const RenderInspection> inspection;
    /// True only while at least one paint-only node transition requires a
    /// future frame. The renderer never owns a timer or animation thread.
    bool animationActive{};
    /// Coordinated section/modal motion reuses committed layout for each frame.
    std::optional<declarative::Rect> widgetTransitionAnimationDamage;
    /// Exact logical damage for a BackgroundSurface-only animation. Empty
    /// means another declarative animation also needs a frame and the host
    /// must use its conservative full-content wakeup.
    std::optional<declarative::Rect> backgroundSurfaceAnimationDamage;
    /// One exact non-animating wake for a latest focused-background proposal.
    /// Hosts invalidate this damage once at or after the absolute deadline;
    /// static settle time never becomes animation work.
    std::optional<BackgroundSurfaceSettleWake> backgroundSurfaceSettleWake;
    std::optional<ComputedCompositorBackground> compositorBackground;
    /// Captured on every call but emitted only by the existing host diagnostic
    /// when the containing composition frame exceeds its slow threshold.
    DeclarativeRenderTiming timing;
    std::optional<ResponsiveSurfacePresentation> responsiveSurface;
    std::vector<RenderDiagnostic> diagnostics;
    std::vector<RenderHitRegion> hitRegions;
    /// Visible semantic geometry retained for the immutable Windows
    /// accessibility snapshot. Decorative layout nodes are deliberately absent.
    std::vector<RenderAccessibilityRegion> accessibilityRegions;
    std::vector<RenderMediaViewportRegion> mediaViewportRegions;
    std::vector<RenderWindowPreviewRegion> windowPreviewRegions;
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
    std::size_t transitionSurfaceCreates{}, transitionSurfaceReuses{}, transitionRetainedBytes{};
    // Test-only exact geometry seam. Production results intentionally retain
    // only interactive geometry so ordinary paints do not allocate two maps
    // for every decorative and structural node.
    std::map<std::wstring, declarative::Rect, std::less<>> elementRects;
    std::map<std::wstring, declarative::Rect, std::less<>> elementUnroundedRects;
    std::map<std::wstring, declarative::Rect, std::less<>> posterArtworkRects;
    std::map<std::wstring, declarative::Rect, std::less<>> elementVisibleRects;
    std::map<std::wstring, float, std::less<>> sliderThumbXs;
    std::map<std::wstring, ButtonContentPlacement, std::less<>> buttonContentPlacements;
    std::map<std::wstring, declarative::Rect, std::less<>> collectionLoadingRects;
    std::map<std::wstring, declarative::Rect, std::less<>> scrollbarTracks;
    std::map<std::wstring, declarative::Rect, std::less<>> scrollbarThumbs;
    std::map<std::wstring, std::wstring, std::less<>> buttonStateCues;
    std::map<std::wstring, declarative::Rect, std::less<>> contextMenuIndicatorRects;
    std::map<std::wstring, std::uint32_t, std::less<>> textLineCounts;
    std::map<std::wstring, std::wstring, std::less<>> backgroundArtworkHandles;
    std::size_t fullLayoutBuildCount{};
    std::size_t compositionPaintNodeVisits{};
    std::size_t tileClipLayerCreates{};
    std::size_t tileClipGeometryCreates{};
    std::size_t tileClipPushes{};
    std::size_t focusFollowPassCount{};
    bool focusFollowConverged{};
    bool focusFollowNoProgress{};
    bool focusFollowCycle{};
    bool focusFollowBoundHit{};
#endif
    // Visible anchors for explicitly declared non-focusable context menus only.
    std::map<std::wstring, declarative::Rect, std::less<>> contextMenuRects;
    std::map<std::wstring, declarative::Rect, std::less<>> focusRects;
    // Full logical controller geometry includes offscreen descendants of a
    // semantic scroll container. Pointer hit regions remain visible-only.
    std::map<std::wstring, declarative::Rect, std::less<>> navigationRects;
    std::map<std::wstring, bool, std::less<>> navigationEnabled;
    std::set<std::wstring, std::less<>> revealableFocusIds;
    std::set<std::wstring, std::less<>> realizableFocusIds;
    std::map<std::wstring, RenderLogicalCollection, std::less<>> logicalCollections;
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

enum class ImageBitmapResourceDomain {
    None,
    RenderTarget,
    Device,
};

struct ImageBitmapCacheStats final {
    std::size_t entries{};
    std::size_t bytes{};
    std::uint64_t hits{};
    std::uint64_t creates{};
    std::uint64_t evictions{};
    std::uint64_t countPressureEvictions{};
    std::uint64_t bytePressureEvictions{};
    std::uint64_t supersededArtworkEvictions{};
    std::uint64_t resourceInvalidations{};
    std::uint64_t resourceGeneration{};
    std::size_t maximumEntries{};
    std::size_t maximumEntryBytes{};
    std::size_t maximumBytes{};
    ImageBitmapResourceDomain resourceDomain{ImageBitmapResourceDomain::None};
};

enum class IncrementalPresentationWork {
    NoRaster,
    PaintOnly,
    LocalLayout,
    FullRaster,
    ScrollOnly,
};

enum class CollectionPreparationStatus { Ready, Pending, Failed };
struct CollectionPreparationBudget final {
    std::size_t maximumNewMeasurements{8};
    std::uint64_t maximumMicroseconds{2000};
};
struct CollectionPreparationResult final {
    CollectionPreparationStatus status{CollectionPreparationStatus::Failed};
    std::size_t newMeasurements{};
    std::uint64_t elapsedMicroseconds{};
    std::shared_ptr<const RenderResult> focusGeometry;
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

enum class FocusedFreeScrollPlanDisposition {
    Planned,
    MissingCheckpoint,
    InstanceMismatch,
    SequenceMismatch,
    FocusMismatch,
    InvalidAxis,
    InvalidDelta,
    ViewportMismatch,
    MissingTarget,
    MissingScrollViewport,
    MissingScrollBox,
    AxisMismatch,
    EmptyScrollViewport,
    OffsetBoundary,
    EmptyDamage,
    PreparationPending,
    PreparationFailed,
};

/// Bounded reason data for a rejected free-scroll plan. The host records this
/// only on an already-coalesced right-stick drop, so renderer safety failures
/// remain diagnosable without introducing per-frame logging.
struct FocusedFreeScrollPlanDiagnostic final {
    FocusedFreeScrollPlanDisposition disposition{
        FocusedFreeScrollPlanDisposition::MissingCheckpoint};
    declarative::Rect cachedViewport;
    declarative::Rect requestedViewport;
    declarative::Rect scrollViewport;
    declarative::Rect scrollBox;
    long long cachedSequence{};
    long long requestedSequence{};
    declarative::ScrollAxis requestedAxis{declarative::ScrollAxis::None};
    declarative::ScrollAxis scrollAxis{declarative::ScrollAxis::None};
    float priorOffset{};
    float maximumOffset{};
};

struct DeclarativeRenderOptions final {
    bool compositorWidgetTransitions{};
    bool suppressWidgetCompositionMotion{};
    animation::Options widgetAnimations;
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
    bool rasterWidgetTransitionsForTesting{};
#endif
    bool playStationControls{controller::UsePlayStationControls()};
    bool collectInspection{};
    // Host acknowledges only after EndDraw/composition submission succeeds.
    // Default synchronous callers retain immediate publication behavior.
    bool deferPublication{};
    std::function<Microsoft::WRL::ComPtr<ID2D1Bitmap1>(ID2D1RenderTarget*, std::wstring_view)> windowPreviewBitmap;
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
    /// Exact focused slider whose value is currently controller-adjustable.
    /// This is deliberately distinct from a momentary pressed presentation.
    std::wstring activeSliderElementId;
    /// Current bridge widget ID used only to bind lazy opaque artwork demand.
    std::wstring artworkWidgetId;
    std::wstring artworkRuntimeGeneration;
    std::wstring artworkPresentationGeneration;
    std::wstring packageContentDigest;
    std::vector<WidgetPackageIconAsset> packageIconAssets;
    /// Exact runtime/presentation authority for host-retained focused
    /// BackgroundSurface selection. Empty preserves test/source compatibility.
    std::wstring artworkAuthorityId;
    bool compositorBackgroundAvailable{};
    bool sizeArtworkToDisplay{};
    ImageDecodeSize artworkDecodeSize{};
    /// Last admitted compositor selection, used only when focus leaves the
    /// widget or its offscreen cursor item is evicted. Exact owner checks apply.
    std::optional<ComputedCompositorBackground> retainedCompositorBackground;
    /// Right-stick free scroll deliberately retains semantic focus without
    /// allowing that descendant to pull the viewport back until re-entry.
    bool suppressFocusedDescendantFollow{};
    // Host-authorized reveal independent of controller focus (for UIA).
    std::wstring realizeElementId;
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
    /// Compare whole-item retention with the conservative clipped painter.
    bool disableIndependentCapturesForTesting{};
    bool disableRetainedLayoutForTesting{};
    /// Injects a terminal diagnostic after target-backed drawing so tests can
    /// prove rejected frames do not commit renderer-owned presentation state.
    bool failAfterNodeDrawForTesting{};
#endif
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
    /// Cancels pixel-only transitions when a surface loses presentation authority.
    void CancelWidgetTransitions() noexcept;
    using ArtworkRenderDiagnosticCallback =
        std::function<void(std::wstring_view message)>;

    DeclarativeRenderer(
        ID2D1Factory* d2dFactory,
        IDWriteFactory* writeFactory,
        RemoteImageCache* imageCache,
        ArtworkRenderDiagnosticCallback artworkRenderDiagnostic = {},
        std::shared_ptr<ScrollDiagnostics> scrollDiagnostics = {}) noexcept;

    ~DeclarativeRenderer();
    void SetChromeImageProtection(std::set<std::wstring> keys);
    [[nodiscard]] bool VisibleContentImageCompleted(std::uint64_t resourceHash) const noexcept;
    DeclarativeRenderer(const DeclarativeRenderer&) = delete;
    DeclarativeRenderer& operator=(const DeclarativeRenderer&) = delete;

    [[nodiscard]] RenderResult Render(
        ID2D1RenderTarget* renderTarget,
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId,
        declarative::Rect viewport,
        const DeclarativeRenderOptions& options = {});
    [[nodiscard]] bool CommitFramePublication(std::uint64_t publicationId);
    void RejectFramePublication() noexcept;
    /// Host-thread preparation only: no paint, focus admission or published
    /// scroll mutation. Call before beginning a frame; Pending retains reusable
    /// measurements for a later slice. One indivisible item may exceed the time
    /// budget. Hosts must retain the previous scene until Ready, then Render
    /// the same admitted request. Render still supports synchronous fallback.
    [[nodiscard]] CollectionPreparationResult PrepareCollections(
        const WidgetSnapshot& snapshot, std::wstring_view focusedElementId,
        declarative::Rect viewport, const DeclarativeRenderOptions& options = {},
        CollectionPreparationBudget budget = {}, bool collectFocusGeometry = false);
    void CancelCollectionPreparation() noexcept;
    [[nodiscard]] bool PaintPackageIcon(
        ID2D1RenderTarget* renderTarget,
        const WidgetPackageIcon& icon,
        declarative::Rect destination,
        NativeColor tint,
        const DeclarativeRenderOptions& options);
    [[nodiscard]] static std::optional<PackageIconDemandAuthority>
    ResolvePackageIconDemandAuthority(
        const WidgetPackageIcon& icon,
        declarative::Rect destination,
        const DeclarativeRenderOptions& options);

    /// Computes the exact destination layout and shared focus eligibility used
    /// by Render without drawing or publishing renderer-owned state. This is a
    /// bounded preflight for an already-admitted one-shot group-entry request.
    [[nodiscard]] RenderResult PrepareFocusEntry(
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId,
        declarative::Rect viewport,
        const DeclarativeRenderOptions& options = {});
    [[nodiscard]] Microsoft::WRL::ComPtr<ID2D1Bitmap>
    ResolveCompositorBackgroundBitmap(
        ID2D1RenderTarget* renderTarget,
        const ComputedCompositorBackground& background);
    [[nodiscard]] bool PaintCompositorBackground(
        ID2D1RenderTarget* renderTarget,
        const ComputedCompositorBackground& background,
        ID2D1Bitmap* bitmap,
        bool baseOnly,
        float opacity = 1.0F,
        bool surfaceComposite = false) const;

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
        std::wstring_view exactScrollId = {},
        FocusedFreeScrollPlanDiagnostic* diagnostic = nullptr);
    /// Tentatively plans movement and prepares its demand. Pending/Failed
    /// restores the exact previous offset and paint plan, without movement debt.
    [[nodiscard]] std::optional<FocusedFreeScrollPlan> PlanPreparedFreeScroll(
        const WidgetSnapshot&, std::wstring_view focusedElementId, declarative::ScrollAxis,
        float deltaDip, declarative::Rect viewport, std::wstring_view exactScrollId,
        const DeclarativeRenderOptions&, FocusedFreeScrollPlanDiagnostic* = nullptr,
        CollectionPreparationBudget = {});

    /// Reuses current committed geometry for artwork or animation paints.
    /// Omitted damage covers the widget; pending scroll layout is preserved.
    [[nodiscard]] std::optional<IncrementalPresentationPlan>
    PlanRetainedPaint(
        const WidgetSnapshot& snapshot,
        std::optional<declarative::Rect> damage = std::nullopt);
    [[nodiscard]] std::optional<IncrementalPresentationPlan>
    PlanBackgroundSurfaceAnimationFrame(declarative::Rect damage);

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

    /// Releases cached image resources when an entire presentation endpoint
    /// is retired. Ordinary target recreation continues to use
    /// DiscardTargetResources so a live endpoint can reuse its bounded cache.
    void ReleaseCachedImages() noexcept;

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
    NativeTextLayoutCache textLayoutCache_;
    struct StyleCacheEntry {
        WidgetComputedStyle base;
        WidgetComputedStyle focused;
        WidgetComputedStyle pressed;
        NativeStyleContext context;
        NativeAccessibilityPolicy accessibility;
        NativeStyleResult result;
        bool isFocused{};
        bool isPressed{};
    };
    std::unordered_map<std::wstring, std::vector<StyleCacheEntry>> styleCache_;
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
    // Exact inputs used by MeasureLeaf, distinct from node identity and pixels.
    // Adding an intrinsic leaf kind requires updating this proof or opting out.
    struct IntrinsicInput final {
        std::wstring kind, text, prompt, indicator;
        NativeRenderStyle style;
        NativeTextAlign buttonAlignment{};
        float pixelScale{};
        bool image{}, artwork{}, glyph{}, packageIcon{}, stateCue{};
        friend bool operator==(const IntrinsicInput&, const IntrinsicInput&) = default;
    };
    struct IntrinsicEntry final {
        IntrinsicInput input;
        std::uint64_t revision{};
        std::optional<TextMeasurementProof> text;
        std::vector<TextMeasurementProof> queries;
    };
    struct RetainedLayoutPass final {
        std::unique_ptr<declarative::LayoutSession> session;
        std::map<std::string, IntrinsicEntry, std::less<>> inputs;
        std::uint64_t revision{};
    };
    // The estimate and correction passes must not replace each other's
    // constraints/measurements. Local measurement and modal trees stay stateless.
    std::array<RetainedLayoutPass, 2> retainedLayout_;
    std::wstring retainedLayoutOwner_;
    struct CollectionItemLayout final {
        // Measurement dependencies are immutable between revisions. Staging a
        // frame must not deep-copy every offscreen semantic subtree.
        std::shared_ptr<const WidgetNode> source;
        std::uint64_t revision{};
        std::uint64_t measuredContext{};
        declarative::LayoutResult layout;
        std::map<std::wstring, TextMeasurementProof, std::less<>> text;
        std::map<std::wstring, std::vector<TextMeasurementProof>, std::less<>> queries;
    };
    struct CollectionMeasureContext final {
        NativeRenderStyle style;
        float width{}, height{}, viewportWidth{}, viewportHeight{}, rootFont{}, pixelScale{}, textScale{};
        int minimumFontWeight{};
        bool compact{}, horizontal{}, playStationControls{}, adaptiveGrid{};
        bool operator==(const CollectionMeasureContext&) const = default;
    };
    struct CollectionRenderState final {
        collection::CollectionLayoutState geometry;
        std::map<std::wstring, CollectionItemLayout, std::less<>> items;
        CollectionMeasureContext context;
        std::uint64_t contextRevision{}, itemRevision{};
        std::wstring lastFocusedKey;
        std::vector<std::size_t> realized;
        float columnWidth{}, columnGap{};
        double leadingExtent{}, trailingExtent{};
        std::uint64_t resetGeneration{};
        [[nodiscard]] std::pair<float, float> AdmittedScrollRange(const float viewportExtent) const noexcept {
            const auto distance = geometry.Extent() - viewportExtent;
            return {static_cast<float>(leadingExtent),
                static_cast<float>(leadingExtent + (distance > 0 ? distance : 0))};
        }
    };
    std::unordered_map<std::wstring, CollectionRenderState> collections_;
    struct CollectionPreparation final {
        std::unordered_map<std::wstring, CollectionRenderState> collections;
        std::wstring instance, scope, focus, realization;
        long long sequence{};
        bool ready{};
        [[nodiscard]] bool Matches(const WidgetSnapshot& snapshot) const noexcept {
            return instance == snapshot.instanceId && scope == snapshot.activeInputScopeId && sequence == snapshot.sequence;
        }
    };
    // One published source and one incoming source may prepare concurrently.
    // LRU-bounded speculative measurements never own scroll or scene authority.
    std::vector<CollectionPreparation> collectionPreparations_;
    struct IncrementalNodeState final {
        NativeRenderStyle baseStyle;
        NativeStyleContext styleContext;
        std::optional<NativeColor> effectiveBackground;
        declarative::Rect borderBox;
        declarative::Rect ancestorClip;
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
        std::map<std::wstring, std::vector<TextMeasurementProof>, std::less<>> textMeasurementQueries;
        std::map<std::wstring, CollectionDiagnosticObservation, std::less<>>
            collections;
        std::map<std::wstring, RenderScrollViewport, std::less<>> scrollViewports;
        // Regular clipped item roots and their owning scroll. Decoration can
        // be deferred while complete logical layout remains authoritative.
        std::map<std::wstring, std::wstring, std::less<>> viewportItems;
    };
    struct PendingIncrementalPlan final {
        std::wstring instanceId;
        long long baseSequence{};
        long long sequence{};
        IncrementalPresentationWork work{IncrementalPresentationWork::PaintOnly};
        declarative::Rect damage;
        std::vector<std::wstring> layoutBoundaries;
        std::uint64_t comparisonMicroseconds{};
        std::uint64_t planningMicroseconds{};
    };
    struct ScrollStateEntry final {
        std::uint64_t collectionResetGeneration{};
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
        std::wstring imageIdentity;
    };
    struct FocusBackgroundEntry final {
        std::wstring widgetId;
        std::wstring widgetInstanceId;
        std::wstring authorityId;
        std::wstring sessionId;
        std::wstring imageSource;
        std::wstring artworkHandle;
        std::wstring imageFit;
        Microsoft::WRL::ComPtr<ID2D1Bitmap> committedBitmap;
        bool committedBitmapIsSurfaceComposite{};
        std::size_t committedSurfaceCompositeBytes{};
        std::wstring incomingImageSource;
        std::wstring incomingArtworkHandle;
        std::wstring incomingImageFit;
        Microsoft::WRL::ComPtr<ID2D1Bitmap> incomingBitmap;
        std::uint64_t transitionStartedAt{};
        std::wstring candidateImageSource;
        std::wstring candidateArtworkHandle;
        std::wstring candidateImageFit;
        std::uint64_t candidateObservedAt{};
        bool candidatePresent{};
        std::uint64_t lastUse{};
    };

    [[nodiscard]] Microsoft::WRL::ComPtr<ID2D1Bitmap> GetImageBitmap(
        ID2D1RenderTarget* renderTarget,
        const WidgetNode& node,
        RenderPass& pass,
        std::wstring_view artworkWidgetId,
        ImagePresentationState& presentationState);
    [[nodiscard]] Microsoft::WRL::ComPtr<ID2D1Bitmap> GetPackageIconBitmap(
        ID2D1RenderTarget* renderTarget,
        const WidgetPackageIcon& icon,
        declarative::Rect destination,
        const DeclarativeRenderOptions& options);
    [[nodiscard]] bool EnsureSurfaceClip(
        ID2D1RenderTarget* renderTarget,
        declarative::Rect viewport,
        float radius);
    [[nodiscard]] bool BindBitmapResourceDomain(
        ID2D1RenderTarget* renderTarget) noexcept;
    void ClearBitmapCache(bool resourceInvalidation) noexcept;
    bool TrimBitmapCache(std::size_t incomingBytes) noexcept;
    void PublishImageProtection();
    bool ImageProtected(std::wstring_view key) const;
    std::set<std::wstring> protectedImageKeys_;
    std::set<std::wstring> chromeImageKeys_;
    std::set<std::uint64_t> visibleContentImageHashes_;
    void RecalculateFocusBackgroundCompositeBytes() noexcept;
    void ReportArtworkRenderDiagnostic(
        const WidgetNode& node,
        std::wstring_view artworkWidgetId,
        std::wstring_view stage,
        std::wstring_view disposition,
        declarative::Size bitmapSize = {},
        declarative::Rect destination = {},
        declarative::Rect source = {},
        declarative::Rect clip = {},
        float opacity = 0.0F) noexcept;
    [[nodiscard]] bool ArtworkRenderDiagnosticsEnabled() const noexcept {
        return static_cast<bool>(artworkRenderDiagnostic_);
    }
    ID2D1Factory* d2dFactory_{};
    IDWriteFactory* writeFactory_{};
    RemoteImageCache* imageCache_{};
    ArtworkRenderDiagnosticCallback artworkRenderDiagnostic_;
    std::shared_ptr<ScrollDiagnostics> scrollDiagnostics_;
    long long scrollDiagnosticLastSequence_{};
    std::uint64_t scrollDiagnosticLastGeometryTime_{};
    static constexpr std::size_t maximumArtworkDiagnosticRecords_{64};
    std::array<std::uint64_t, maximumArtworkDiagnosticRecords_>
        artworkDiagnosticKeys_{};
    std::size_t artworkDiagnosticKeyCount_{};
    Microsoft::WRL::ComPtr<IUnknown> bitmapResourceDomain_;
    bool bitmapResourceDomainIsDevice_{};
    ID2D1RenderTarget* surfaceClipTarget_{};
    declarative::Rect surfaceClipRect_{};
    float surfaceClipRadius_{};
    Microsoft::WRL::ComPtr<ID2D1Layer> surfaceClipLayer_;
    Microsoft::WRL::ComPtr<ID2D1RoundedRectangleGeometry> surfaceClipGeometry_;
    struct TileClipResources final {
        float width{}, height{}, radius{};
        Microsoft::WRL::ComPtr<ID2D1Layer> layer;
        Microsoft::WRL::ComPtr<ID2D1RoundedRectangleGeometry> geometry;
    };
    ID2D1RenderTarget* tileClipTarget_{};
    std::vector<TileClipResources> tileClipResources_;
    surface::ShadowCache surfaceShadows_;
    std::unordered_map<std::wstring, BitmapCacheEntry> bitmaps_;
    std::size_t bitmapBytes_{};
    std::uint64_t bitmapAccessClock_{};
    std::uint64_t bitmapHits_{};
    std::uint64_t bitmapCreates_{};
    std::uint64_t bitmapEvictions_{};
    std::uint64_t bitmapCountPressureEvictions_{};
    std::uint64_t bitmapBytePressureEvictions_{};
    std::uint64_t bitmapSupersededArtworkEvictions_{};
    std::uint64_t bitmapResourceInvalidations_{};
    std::uint64_t bitmapResourceGeneration_{};
    declarative::FocusSurfaceSelectionMemory focusSelectionMemory_;
    std::map<std::wstring, std::wstring> selectedPresentationSources_;
    std::unordered_map<std::wstring, FocusBackgroundEntry> focusBackgrounds_;
    std::size_t focusBackgroundCompositeBytes_{};
    std::uint64_t focusBackgroundAccessClock_{};
    std::unordered_map<std::wstring, ScrollStateEntry> scrollOffsets_;
    std::uint64_t scrollStateAccessClock_{};
    DeclarativeMotionTimeline motionTimeline_;
    WidgetTransitionCoordinator widgetTransitions_;
    struct TransitionVisual final {
        Microsoft::WRL::ComPtr<ID2D1Bitmap> current, outgoing;
        Microsoft::WRL::ComPtr<ID2D1BitmapRenderTarget> currentTarget, outgoingTarget, spareTarget;
        declarative::Rect bounds, outgoingBounds;
        std::wstring key;
        std::size_t bytes{}, outgoingBytes{}, spareBytes{};
        NativeColor scrim{0, 0, 0, .60F};
        float opacity{}, fromOpacity{};
        std::uint64_t revision{};
        bool seen{};
    };
    std::map<std::wstring, TransitionVisual> transitionVisuals_;
    std::size_t compatiblePaintDepth_{};
    std::wstring compositionInstance_;
    struct CompositionCapture final {
        Microsoft::WRL::ComPtr<ID2D1BitmapRenderTarget> target;
        std::weak_ptr<void> lease;
        std::size_t bytes{};
    };
    std::vector<CompositionCapture> compositionCaptures_;
    std::map<std::wstring, CompositionPaintEntry> compositionPaintCache_;
    std::optional<IncrementalLayoutCache> incrementalLayoutCache_;
    std::optional<PendingIncrementalPlan> pendingIncrementalPlan_;
    struct FramePublication final {
        std::uint64_t id{}, resourceGeneration{};
        std::wstring instance, scope, focus, realization;
        long long sequence{};
        decltype(incrementalLayoutCache_) layout;
        decltype(collections_) collections;
        decltype(scrollOffsets_) scrollOffsets;
        std::uint64_t scrollClock{};
        decltype(motionTimeline_) motion;
        decltype(widgetTransitions_) transitions;
        decltype(transitionVisuals_) visuals;
        decltype(focusSelectionMemory_) selection;
        decltype(selectedPresentationSources_) selectionSources;
        decltype(focusBackgrounds_) backgrounds;
        std::uint64_t backgroundClock{};
        decltype(compositionPaintCache_) paintCache;
        std::wstring compositionInstance;
        decltype(protectedImageKeys_) imageKeys;
        decltype(visibleContentImageHashes_) visibleImageHashes;
    };
    std::unique_ptr<FramePublication> pendingPublication_;
    std::uint64_t nextPublicationId_{};
};

#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
namespace testing {
void ResetRendererWidgetStateRetirementForTesting() noexcept;
[[nodiscard]] std::uint64_t RendererWidgetStateRetirementCountForTesting() noexcept;
[[nodiscard]] std::wstring_view LastRetiredRendererWidgetInstanceForTesting() noexcept;
} // namespace testing
#endif

} // namespace widgetrail
