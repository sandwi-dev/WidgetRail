#include "DeclarativeRenderer.h"

#include "BackgroundSurfaceTransitionPolicy.h"
#include "NativeIcons.h"
#include "NativeTextLayout.h"
#include "RemoteImageCache.h"

#include <algorithm>
#include <array>
#include <bit>
#include <chrono>
#include <cmath>
#include <limits>
#include <set>
#include <utility>

namespace widgetrail {
namespace {

using Microsoft::WRL::ComPtr;
using declarative::BoxSpacing;
using declarative::LayoutDirection;
using declarative::LayoutElement;
using declarative::LayoutMode;
using declarative::LayoutOptions;
using declarative::Rect;
using declarative::Size;

struct PreparationTimer {
    std::uint64_t& nanoseconds;
    std::chrono::steady_clock::time_point start{std::chrono::steady_clock::now()};
    ~PreparationTimer() {
        nanoseconds += static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(
            std::chrono::steady_clock::now() - start).count());
    }
};

constexpr std::size_t kMaximumDiagnostics = 256;
constexpr std::size_t kMaximumBitmapEntries = 256;
constexpr std::size_t kMaximumBitmapEntryBytes = 32U * 1024U * 1024U;
constexpr std::size_t kMaximumBitmapBytes = 96U * 1024U * 1024U;
constexpr auto kBackgroundSurfaceCrossfadeMilliseconds =
    background_surface_policy::FadeMilliseconds;
constexpr auto kBackgroundSurfaceProposalSettleMilliseconds =
    background_surface_policy::SettleMilliseconds;
constexpr float kMinimumControlSize = 44.0F;
constexpr float kButtonIconLabelGap = 8.0F;
constexpr float kButtonStateCueGap = 8.0F;
constexpr float kButtonStateCueMinimumSize = 14.0F;
constexpr float kButtonStateCueMaximumSize = 22.0F;
constexpr float kButtonStateCueHeightFactor = 0.72F;
constexpr std::size_t kMaximumScrollStateEntries = 4096;
constexpr std::size_t kMaximumFocusFollowPasses = 32;
constexpr std::size_t kMaximumFocusFollowDiagnosticNodes = 32;
constexpr std::size_t kFocusFollowRetainedSamples = 4;
constexpr std::size_t kMaximumFocusFollowDiagnosticIdentifierCharacters = 128;
constexpr std::size_t kMaximumFocusFollowSummaryCharacters = 16384;
constexpr std::size_t kMaximumCollectionDiagnosticItems = 256;
constexpr std::size_t kMaximumCollectionDiagnosticEvents = 16;
constexpr std::size_t kCollectionDiagnosticEdgeKeys = 3;
constexpr std::size_t kMaximumCollectionSummaryCharacters = 16384;
constexpr std::uint64_t kSlowFocusFollowMicroseconds = 100000;
constexpr float kRevealEpsilon = 0.01F;
// Native layout and Direct2D rasterization can put a child edge no more than
// one physical pixel beyond an otherwise matching fixed clip after scale
// conversion. Keep that native-pixel cap scale-aware; larger fixed-axis
// clipping still fails closed because no Scroll can repair it.
constexpr float kRevealRasterEdgePixelTolerance = 1.0F;
constexpr NativeColor kDefaultText{0.969F, 0.973F, 0.988F, 1.0F};
constexpr NativeColor kDefaultFocus{1.0F, 1.0F, 1.0F, 1.0F};
constexpr NativeColor kDefaultButton{0.122F, 0.133F, 0.169F, 0.96F};
constexpr NativeColor kDefaultTrack{0.25F, 0.26F, 0.30F, 0.72F};
constexpr NativeColor kDefaultAccent{0.545F, 0.486F, 1.0F, 1.0F};

[[nodiscard]] bool ReservesTrailingButtonStateCue(
    const WidgetNode& node) noexcept {
    return !node.text.empty() && (node.isSelect || node.isBusy);
}

#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
std::uint64_t gRendererWidgetStateRetirementCount{};
std::wstring gLastRetiredRendererWidgetInstance;
#endif

[[nodiscard]] bool FiniteRect(const Rect& value) noexcept {
    return std::isfinite(value.x) && std::isfinite(value.y) &&
        std::isfinite(value.width) && std::isfinite(value.height);
}

[[nodiscard]] bool SameRect(const Rect& left, const Rect& right) noexcept {
    return std::abs(left.x - right.x) <= 0.01F &&
        std::abs(left.y - right.y) <= 0.01F &&
        std::abs(left.width - right.width) <= 0.01F &&
        std::abs(left.height - right.height) <= 0.01F;
}



[[nodiscard]] D2D1_RECT_F D2DRect(const Rect& value) noexcept {
    return D2D1::RectF(value.x, value.y, value.x + value.width, value.y + value.height);
}

[[nodiscard]] Rect ScaleRect(const Rect& value, const float scale) noexcept {
    const auto bounded = std::isfinite(scale) ? std::clamp(scale, 0.5F, 2.0F) : 1.0F;
    const auto width = value.width * bounded;
    const auto height = value.height * bounded;
    return {
        value.x + (value.width - width) * 0.5F,
        value.y + (value.height - height) * 0.5F,
        width,
        height,
    };
}

[[nodiscard]] Rect TranslateRect(
    const Rect& value,
    const float translationX,
    const float translationY) noexcept {
    return {
        value.x + translationX,
        value.y + translationY,
        value.width,
        value.height,
    };
}

[[nodiscard]] float AddBoundedTranslation(
    const float inherited,
    const float local) noexcept {
    const auto sum = static_cast<double>(inherited) + static_cast<double>(local);
    return static_cast<float>(std::clamp(
        sum,
        -static_cast<double>(DeclarativeMotionTimeline::MaximumTranslationDips),
        static_cast<double>(DeclarativeMotionTimeline::MaximumTranslationDips)));
}

[[nodiscard]] Rect Inset(const Rect& value, const float amount) noexcept {
    const auto bounded = std::max(
        -std::min(value.width, value.height) * 0.5F,
        std::min(amount, std::min(value.width, value.height) * 0.5F));
    return {
        value.x + bounded,
        value.y + bounded,
        std::max(0.0F, value.width - bounded * 2.0F),
        std::max(0.0F, value.height - bounded * 2.0F),
    };
}

[[nodiscard]] Rect Intersection(const Rect& first, const Rect& second) noexcept {
    const float left = std::max(first.x, second.x);
    const float top = std::max(first.y, second.y);
    const float right = std::min(first.x + first.width, second.x + second.width);
    const float bottom = std::min(first.y + first.height, second.y + second.height);
    return {left, top, std::max(0.0F, right - left), std::max(0.0F, bottom - top)};
}

[[nodiscard]] Rect UnionRect(const Rect& first, const Rect& second) noexcept {
    if (first.width <= 0.0F || first.height <= 0.0F) return second;
    if (second.width <= 0.0F || second.height <= 0.0F) return first;
    const float left = std::min(first.x, second.x);
    const float top = std::min(first.y, second.y);
    const float right = std::max(
        first.x + first.width, second.x + second.width);
    const float bottom = std::max(
        first.y + first.height, second.y + second.height);
    return {left, top, right - left, bottom - top};
}

[[nodiscard]] NativeColor WithOpacity(
    NativeColor color,
    const float opacity) noexcept {
    color.alpha *= std::clamp(std::isfinite(opacity) ? opacity : 1.0F, 0.0F, 1.0F);
    return color;
}

[[nodiscard]] D2D1_COLOR_F D2DColor(const NativeColor& color) noexcept {
    return {color.red, color.green, color.blue, color.alpha};
}

[[nodiscard]] float RadiusFor(const NativeRenderStyle& style, const Rect& rect) noexcept {
    switch (style.shape()) {
    case NativeShape::Circle:
    case NativeShape::Pill:
        return std::max(0.0F, std::min(rect.width, rect.height) * 0.5F);
    case NativeShape::Rounded:
        return std::max(style.cornerRadiusPx(), 8.0F);
    case NativeShape::Rectangle:
    default:
        return std::max(0.0F, style.cornerRadiusPx());
    }
}

[[nodiscard]] std::string NarrowStableId(const std::wstring_view value) {
    std::string result;
    result.reserve(value.size());
    for (const auto character : value) {
        if (character > 0x7f) return {};
        result.push_back(static_cast<char>(character));
    }
    return result;
}

[[nodiscard]] std::wstring WidenStableId(const std::string_view value) {
    return {value.begin(), value.end()};
}

[[nodiscard]] bool FindNodePath(
    const WidgetNode& node,
    const std::wstring_view targetId,
    std::vector<const WidgetNode*>& path) {
    path.push_back(&node);
    if (node.id == targetId) return true;
    for (const auto& child : node.children) {
        if (FindNodePath(child, targetId, path)) return true;
    }
    path.pop_back();
    return false;
}

struct FocusBackgroundSelection final {
    const WidgetNode* surface{};
    const WidgetNode* focused{};
    std::wstring_view imageSource;
    std::wstring_view artworkHandle;
};

[[nodiscard]] FocusBackgroundSelection ResolveFocusBackgroundSelection(
    const WidgetNode& root,
    const std::wstring_view focusedElementId) {
    if (focusedElementId.empty()) return {};
    std::vector<const WidgetNode*> path;
    if (!FindNodePath(root, focusedElementId, path) || path.empty()) return {};
    const WidgetNode* nearestSurface{};
    for (const auto* node : path) {
        if (node->kind == L"backgroundSurface") nearestSurface = node;
    }
    if (!nearestSurface) return {};
    const auto* focused = path.back();
    return {
        nearestSurface,
        focused,
        {},
        nearestSurface->usesFocusedDescendantArtwork
            ? std::wstring_view(focused->focusBackgroundArtworkHandle)
            : std::wstring_view{},
    };
}

[[nodiscard]] bool SameFocusBackgroundSelection(
    const FocusBackgroundSelection& left,
    const FocusBackgroundSelection& right) noexcept {
    return left.surface == right.surface && left.imageSource == right.imageSource &&
        left.artworkHandle == right.artworkHandle;
}

struct FocusPresentationSelection final {
    const WidgetNode* surface{};
    const WidgetNode* fragment{};
};

[[nodiscard]] FocusPresentationSelection ResolveFocusPresentationSelection(
    const WidgetNode& root,
    const std::wstring_view focusedElementId) {
    if (focusedElementId.empty()) return {};
    std::vector<const WidgetNode*> path;
    if (!FindNodePath(root, focusedElementId, path) || path.empty()) return {};
    const WidgetNode* nearestSurface{};
    for (const auto* node : path) {
        if (node->kind == L"focusPresentationSurface") nearestSurface = node;
    }
    if (!nearestSurface) return {};
    const auto* focused = path.back();
    const auto* fragment = !focused->focusPresentation.empty()
        ? &focused->focusPresentation.front()
        : !nearestSurface->defaultFocusPresentation.empty()
            ? &nearestSurface->defaultFocusPresentation.front()
            : nullptr;
    return {nearestSurface, fragment};
}

[[nodiscard]] bool SameFocusPresentationSelection(
    const FocusPresentationSelection& left,
    const FocusPresentationSelection& right) noexcept {
    return left.surface == right.surface && left.fragment == right.fragment;
}

[[nodiscard]] float PositionFactorX(const NativeObjectPosition position) noexcept {
    switch (position) {
    case NativeObjectPosition::Left:
    case NativeObjectPosition::TopLeft:
    case NativeObjectPosition::BottomLeft: return 0.0F;
    case NativeObjectPosition::Right:
    case NativeObjectPosition::TopRight:
    case NativeObjectPosition::BottomRight: return 1.0F;
    default: return 0.5F;
    }
}

[[nodiscard]] float PositionFactorY(const NativeObjectPosition position) noexcept {
    switch (position) {
    case NativeObjectPosition::Top:
    case NativeObjectPosition::TopLeft:
    case NativeObjectPosition::TopRight: return 0.0F;
    case NativeObjectPosition::Bottom:
    case NativeObjectPosition::BottomLeft:
    case NativeObjectPosition::BottomRight: return 1.0F;
    default: return 0.5F;
    }
}

[[nodiscard]] ComPtr<ID2D1SolidColorBrush> Brush(
    ID2D1RenderTarget* target,
    const NativeColor color) {
    ComPtr<ID2D1SolidColorBrush> result;
    if (target) (void)target->CreateSolidColorBrush(D2DColor(color), result.ReleaseAndGetAddressOf());
    return result;
}

[[nodiscard]] std::optional<NativeImageFit> ExplicitImageFit(const WidgetNode& node) noexcept {
    if (node.imageFit == L"cover") return NativeImageFit::Cover;
    if (node.imageFit == L"contain") return NativeImageFit::Contain;
    if (node.imageFit == L"fill") return NativeImageFit::Fill;
    if (node.imageFit == L"none") return NativeImageFit::None;
    return std::nullopt;
}

[[nodiscard]] bool HasComputedProperty(
    const WidgetNode& node,
    const std::wstring_view property,
    const bool focused,
    const bool pressed) {
    return (pressed && node.pressedStyle.contains(std::wstring{property})) ||
        (focused && node.focusedStyle.contains(std::wstring{property})) ||
        node.baseStyle.contains(std::wstring{property});
}

[[nodiscard]] NativeImageFit EffectiveImageFit(
    const WidgetNode& node,
    const NativeRenderStyle& style,
    const bool focused,
    const bool pressed) {
    if (HasComputedProperty(node, L"object-fit", focused, pressed))
        return style.imageFit();
    return ExplicitImageFit(node).value_or(style.imageFit());
}

[[nodiscard]] std::wstring_view ImageFitName(const NativeImageFit fit) noexcept {
    switch (fit) {
    case NativeImageFit::Cover: return L"cover";
    case NativeImageFit::Contain: return L"contain";
    case NativeImageFit::Fill: return L"fill";
    case NativeImageFit::None: return L"none";
    }
    return L"contain";
}

[[nodiscard]] NativeTextAlign ResolveButtonContentAlignment(
    const WidgetNode& node,
    const NativeRenderStyle& style,
    const bool focused,
    const bool pressed) noexcept {
    if (HasComputedProperty(node, L"text-align", focused, pressed))
        return style.textAlign();
    if (!HasComputedProperty(node, L"justify", focused, pressed))
        return NativeTextAlign::Center;
    switch (style.justify()) {
    case NativeJustify::Start: return NativeTextAlign::Start;
    case NativeJustify::End: return NativeTextAlign::End;
    case NativeJustify::Center: return NativeTextAlign::Center;
    default: return NativeTextAlign::Center;
    }
}

struct ButtonContentTokens final {
    float leadingSize{};
    float leadingGap{};
    float stateCueSize{};
    float stateCueLane{};
};

[[nodiscard]] ButtonContentTokens ResolveButtonContentTokens(
    const Rect content,
    const float requestedLeadingSize,
    const bool hasLeading,
    const bool hasText,
    const bool reserveTrailingStateCue) noexcept {
    ButtonContentTokens result;
    result.leadingSize = hasLeading && std::isfinite(requestedLeadingSize)
        ? std::clamp(requestedLeadingSize, 0.0F, std::min(content.width, content.height))
        : 0.0F;
    result.leadingGap = hasLeading && hasText && content.width > result.leadingSize
        ? std::min(kButtonIconLabelGap, content.width - result.leadingSize)
        : 0.0F;
    if (!reserveTrailingStateCue) return result;

    const auto naturalCueSize = std::min({
        kButtonStateCueMaximumSize,
        std::max(kButtonStateCueMinimumSize, content.height * kButtonStateCueHeightFactor),
        content.width,
        content.height,
    });
    result.stateCueLane = std::min(
        naturalCueSize + kButtonStateCueGap,
        content.width * 0.5F);
    result.stateCueSize = std::min(naturalCueSize, result.stateCueLane);
    return result;
}

} // namespace

bool UseAccessibleDeclarativeStateCue(
    const NativeAccessibilityPolicy& accessibility) noexcept {
    return accessibility.reducedTransparency ||
        static_cast<bool>(accessibility.contrastHook);
}

float DeclarativeStateOpacityFactor(
    const bool disabled,
    const bool busy,
    const NativeAccessibilityPolicy& accessibility) noexcept {
    if (UseAccessibleDeclarativeStateCue(accessibility)) return 1.0F;
    return disabled ? 0.45F : (busy ? 0.72F : 1.0F);
}

struct DeclarativeRenderer::PreparedNode final {
    const WidgetNode* node{};
    NativeRenderStyle baseStyle;
    NativeRenderStyle paintStyle;
    std::string narrowId;
    std::optional<NativeColor> effectiveBackground;
};

struct DeclarativeRenderer::RenderPass final {
    enum class CollectionAnchorPolicy {
        Reconcile,
        ReconcileContentChanges,
        PreserveFocusFollowOffsets,
    };

    struct PresentationNode final {
        Rect borderBox;
        Rect contentBox;
        Rect visibleBox;
        Rect ancestorClip;
        DeclarativeMotionSample motion;
    };

    enum class FocusFollowPhase {
        Layout,
        Presentation,
    };

    struct FocusFollowNodeObservation final {
        std::wstring id;
        declarative::ScrollAxis axis{declarative::ScrollAxis::None};
        float offset{};
        float maximumOffset{};
        Rect target;
        Rect viewport;
        bool targetVisible{};
        bool leadingBoundary{};
        bool trailingBoundary{};
    };

    struct FocusFollowSample final {
        std::vector<FocusFollowNodeObservation> nodes;
        bool allTargetsVisible{true};
        bool complete{true};
    };

    struct FocusFollowAttemptResult final {
        bool changed{};
        bool meaningfulOffsetChange{};
        bool repeatedOffsetState{};
    };

    struct FocusFollowNodeSummary final {
        std::wstring id;
        declarative::ScrollAxis axis{declarative::ScrollAxis::None};
        float initialOffset{};
        float finalOffset{};
        float peakOffset{};
        float maximumOffset{};
        Rect target;
        Rect viewport;
        bool leadingBoundary{};
        bool trailingBoundary{};
    };

    struct FocusFollowTrace final {
        std::size_t passCount{};
        std::uint64_t layoutMicroseconds{};
        std::uint64_t presentationMicroseconds{};
        bool converged{};
        bool noProgress{};
        bool cycle{};
        bool boundHit{};
        std::vector<FocusFollowNodeSummary> nodes;
        std::vector<std::vector<float>> firstSamples;
        std::vector<std::vector<float>> lastSamples;
        std::vector<FocusFollowSample> layoutSeenSamples;
        std::vector<FocusFollowSample> presentationSeenSamples;
    } focusFollowTrace;

    struct CollectionReconciliationTrace final {
        float offsetBefore{};
        float offsetAfter{};
        std::wstring mode;
        std::wstring key;
    };

    std::uint64_t styleNanoseconds{};
    std::uint64_t textNanoseconds{};
    std::uint64_t layoutNanoseconds{};
    std::uint64_t styleCacheHits{};
    std::uint64_t styleCacheMisses{};
    DeclarativeRenderer* owner{};
    std::unordered_map<std::wstring, ScrollStateEntry>* scrollState{};
    std::uint64_t* scrollAccessClock{};
    DeclarativeMotionTimeline* motionTimeline{};
    ID2D1RenderTarget* target{};
    const WidgetSnapshot* snapshot{};
    std::wstring focusedId;
    std::wstring pressedId;
    Rect viewport;
    bool compactMode{};
    bool measurementOnly{};
    bool intrinsicRootWidth{};
    DeclarativeRenderOptions options;
    std::unordered_map<std::string, PreparedNode> prepared;
    std::unordered_map<std::string, PresentationNode> presentation;
    declarative::LayoutResult layout;
    RenderResult result;
    std::set<std::wstring, std::less<>> diagnosticKeys;
    const WidgetNode* deferredFocusNode{};
    const NativeRenderStyle* deferredFocusStyle{};
    Rect deferredFocusRect{};
    float deferredFocusOpacity{1.0F};
    std::optional<Rect> deferredFocusClip;
    std::map<std::wstring, TextMeasurementProof, std::less<>> textMeasurements;
    std::map<std::wstring, std::vector<TextMeasurementProof>, std::less<>> textMeasurementQueries;
    std::map<std::wstring, CollectionReconciliationTrace, std::less<>>
        collectionReconciliationOffsets;
    std::set<std::wstring, std::less<>> resetCollections;
    std::unordered_map<std::wstring, FocusBackgroundEntry> focusBackgrounds;
    std::uint64_t focusBackgroundAccessClock{};
    bool backgroundSurfaceAnimationActive{};
    std::optional<Rect> backgroundSurfaceAnimationDamage;
    std::optional<BackgroundSurfaceSettleWake> backgroundSurfaceSettleWake;
    std::wstring compositorBackgroundId;

    [[nodiscard]] auto& ScrollState() noexcept {
        return scrollState ? *scrollState : owner->scrollOffsets_;
    }

    [[nodiscard]] const auto& ScrollState() const noexcept {
        return scrollState ? *scrollState : owner->scrollOffsets_;
    }

    [[nodiscard]] std::uint64_t& ScrollAccessClock() noexcept {
        return scrollAccessClock ? *scrollAccessClock : owner->scrollStateAccessClock_;
    }

    [[nodiscard]] DeclarativeMotionTimeline& MotionTimeline() noexcept {
        return motionTimeline ? *motionTimeline : owner->motionTimeline_;
    }

    [[nodiscard]] bool IsResponsiveVisible(const WidgetNode& node) const noexcept {
        return node.visibleWhen.empty() || node.visibleWhen == L"always" ||
            (compactMode && node.visibleWhen == L"compactOnly") ||
            (!compactMode && node.visibleWhen == L"expandedOnly");
    }

    void Add(
        const std::wstring_view nodeId,
        const std::wstring_view code,
        const std::wstring_view message,
        const RenderDiagnosticSeverity severity = RenderDiagnosticSeverity::Warning) {
        if (result.diagnostics.size() >= kMaximumDiagnostics) return;
        auto key = std::wstring{nodeId};
        key += L"\n";
        key += code;
        key += L"\n";
        key += message;
        if (!diagnosticKeys.insert(key).second) return;
        result.diagnostics.push_back({
            severity,
            std::wstring{nodeId},
            std::wstring{code},
            std::wstring{message},
        });
    }

    void AddBackgroundSurfaceTransitionDiagnostic(
        const WidgetNode& node,
        const std::wstring_view event,
        const std::wstring_view reason = {}) {
        std::wstring message = L"event=" + std::wstring{event} +
            L" widget=" + options.artworkWidgetId +
            L" instance=" + snapshot->instanceId +
            L" surface=" + node.id;
        if (!reason.empty()) message += L" reason=" + std::wstring{reason};
        Add(node.id, L"background_crossfade_" + std::wstring{event}, message,
            RenderDiagnosticSeverity::Information);
    }

    [[nodiscard]] bool TrySelectCompositorBackground(
        const WidgetNode& surface,
        const NativeRenderStyle& style,
        const Rect bounds,
        const float opacity) {
        if (!options.compositorBackgroundAvailable ||
            !surface.usesFocusedDescendantArtwork) return false;
        if (!compositorBackgroundId.empty()) return compositorBackgroundId == surface.id;
        const auto selection = ResolveFocusBackgroundSelection(snapshot->root, focusedId);
        const auto* prior = options.retainedCompositorBackground
            ? &*options.retainedCompositorBackground : nullptr;
        const bool retainSelection = !selection.surface && prior &&
            prior->authorityId == options.artworkAuthorityId &&
            prior->artworkWidgetId == options.artworkWidgetId &&
            prior->artworkRuntimeGeneration == options.artworkRuntimeGeneration &&
            prior->artworkPresentationGeneration == options.artworkPresentationGeneration &&
            prior->widgetInstanceId == snapshot->instanceId && prior->nodeId == surface.id &&
            prior->resourceGeneration == owner->bitmapResourceGeneration_;
        if (!retainSelection && (selection.surface != &surface || !selection.focused)) return false;
        std::vector<const WidgetNode*> path;
        if (!FindNodePath(snapshot->root, surface.id, path)) return false;
        const auto paints = [&](const WidgetNode& node) {
            const auto found = prepared.find(NarrowStableId(node.id));
            if (found == prepared.end()) return true;
            const auto& candidate = found->second.paintStyle;
            const auto& edges = candidate.borderEdges();
            const std::array edgeSet{edges.top, edges.right, edges.bottom, edges.left};
            const bool border = std::any_of(
                edgeSet.begin(), edgeSet.end(),
                [](const NativeBorderEdgeStyle& edge) {
                    return edge.widthPx > 0.0F && edge.color && edge.color->alpha > 0.0F;
                });
            return (candidate.background() && candidate.background()->alpha > 0.0F) ||
                !node.imageSource.empty() || !node.artworkHandle.empty() ||
                candidate.imageTint() || candidate.scrimColor() || border ||
                candidate.backgroundBlurPx() > 0.0F || candidate.shadowBlurPx() > 0.0F ||
                std::abs(candidate.opacity() - 1.0F) > 0.001F ||
                std::abs(candidate.scale() - 1.0F) > 0.001F ||
                std::abs(candidate.translateXPx()) > 0.001F ||
                std::abs(candidate.translateYPx()) > 0.001F;
        };
        for (std::size_t index = 0; index + 1 < path.size(); ++index) {
            const auto visible = std::count_if(
                path[index]->children.begin(), path[index]->children.end(),
                [&](const WidgetNode& child) { return IsResponsiveVisible(child); });
            if (paints(*path[index]) || visible != 1) {
                AddBackgroundSurfaceTransitionDiagnostic(
                    surface, L"compositor-fallback",
                    paints(*path[index]) ? L"painted-ancestor" : L"complex-painter-order");
                return false;
            }
        }
        const auto& edges = style.borderEdges();
        const bool border = (edges.top.widthPx > 0.0F || edges.right.widthPx > 0.0F ||
            edges.bottom.widthPx > 0.0F || edges.left.widthPx > 0.0F);
        const auto shown = presentation.find(NarrowStableId(surface.id));
        if (border || style.backgroundBlurPx() > 0.0F || style.shadowBlurPx() > 0.0F ||
            std::abs(style.scale() - 1.0F) > 0.001F ||
            std::abs(style.translateXPx()) > 0.001F ||
            std::abs(style.translateYPx()) > 0.001F || shown == presentation.end() ||
            shown->second.visibleBox.width <= 0.0F ||
            shown->second.visibleBox.height <= 0.0F) {
            AddBackgroundSurfaceTransitionDiagnostic(
                surface, L"compositor-fallback",
                L"unsupported-surface-style border=" + std::to_wstring(border) +
                L" blur=" + std::to_wstring(style.backgroundBlurPx()) +
                L" shadow=" + std::to_wstring(style.shadowBlurPx()) +
                L" scale=" + std::to_wstring(style.scale()) +
                L" translate=" + std::to_wstring(style.translateXPx()) +
                    L"," + std::to_wstring(style.translateYPx()) +
                L" bounds=" + std::to_wstring(bounds.x) + L"," +
                    std::to_wstring(bounds.y) + L"," + std::to_wstring(bounds.width) +
                    L"," + std::to_wstring(bounds.height) +
                L" visible=" + (shown == presentation.end()
                    ? std::wstring{L"missing"}
                    : std::to_wstring(shown->second.visibleBox.x) + L"," +
                        std::to_wstring(shown->second.visibleBox.y) + L"," +
                        std::to_wstring(shown->second.visibleBox.width) + L"," +
                        std::to_wstring(shown->second.visibleBox.height)));
            return false;
        }
        compositorBackgroundId = surface.id;
        WidgetNode desired = surface;
        if (retainSelection) {
            desired.imageSource = prior->imageSource;
            desired.artworkHandle = prior->artworkHandle;
            desired.imageFit = prior->imageFit;
        } else if (!selection.imageSource.empty() || !selection.artworkHandle.empty()) {
            desired.imageSource = selection.imageSource;
            desired.artworkHandle = selection.artworkHandle;
        }
        if (desired.imageFit.empty()) desired.imageFit = L"cover";
        desired.imageFit = ImageFitName(
            EffectiveImageFit(desired, style, false, false));
        result.compositorBackground = ComputedCompositorBackground{
            options.artworkAuthorityId, options.artworkWidgetId,
            options.artworkRuntimeGeneration,
            options.artworkPresentationGeneration,
            snapshot->instanceId, surface.id,
            focusedId, desired.imageSource, desired.artworkHandle,
            desired.imageFit, bounds, style, opacity,
            snapshot->sequence, owner->bitmapResourceGeneration_,
            shown->second.visibleBox};
        result.compositorBackground->decodeSize = options.sizeArtworkToDisplay
            ? DisplayImageSize(bounds.width, bounds.height, options.pixelScale) : ImageDecodeSize{};
        result.compositorBackground->defaultArtworkKey = retainSelection
            ? prior->defaultArtworkKey
            : surface.imageSource + L"\x1f" + surface.artworkHandle + L"\x1f" + surface.imageFit;
        result.compositorBackground->retainCurrentArtwork = retainSelection
            ? prior->retainCurrentArtwork
            : selection.imageSource.empty() && selection.artworkHandle.empty();
        AddBackgroundSurfaceTransitionDiagnostic(surface, L"compositor-eligible");
        return true;
    }

    void MarkBackgroundSurfaceAnimation(const Rect damage) {
        const auto bounded = Intersection(damage, viewport);
        if (bounded.width <= 0.0F || bounded.height <= 0.0F) return;
        backgroundSurfaceAnimationActive = true;
        backgroundSurfaceAnimationDamage = backgroundSurfaceAnimationDamage
            ? std::optional{UnionRect(*backgroundSurfaceAnimationDamage, bounded)}
            : std::optional{bounded};
    }

    void MarkBackgroundSurfaceSettleWake(
        const Rect damage,
        const std::uint64_t deadlineMilliseconds) {
        const auto bounded = Intersection(damage, viewport);
        if (bounded.width <= 0.0F || bounded.height <= 0.0F) return;
        if (!backgroundSurfaceSettleWake ||
            deadlineMilliseconds <
                backgroundSurfaceSettleWake->deadlineMilliseconds) {
            backgroundSurfaceSettleWake = BackgroundSurfaceSettleWake{
                bounded, deadlineMilliseconds};
        } else if (deadlineMilliseconds ==
                   backgroundSurfaceSettleWake->deadlineMilliseconds) {
            backgroundSurfaceSettleWake->damage = UnionRect(
                backgroundSurfaceSettleWake->damage, bounded);
        }
    }

    [[nodiscard]] NativeStyleResult Adapt(
        const WidgetNode& node,
        const bool focused,
        const bool pressed,
        const float parentWidth,
        const float parentHeight,
        const float parentFontSize,
        const std::optional<NativeColor>& inheritedBackground) {
        PreparationTimer timer{styleNanoseconds};
        const NativeStyleContext context{
            viewport.width, viewport.height, parentWidth, parentHeight,
            parentFontSize, options.rootFontSizePx, focused, inheritedBackground,
            (node.kind == L"button" || node.kind == L"actionSurface")
                ? std::optional<NativeColor>{kDefaultButton} : std::nullopt};
        auto& entries = owner->styleCache_[node.id];
        const auto& policy = options.accessibility;
        const auto samePolicy = [&](const NativeAccessibilityPolicy& previous) {
            return !policy.contrastHook && !previous.contrastHook &&
                policy.reducedMotion == previous.reducedMotion &&
                policy.reducedTransparency == previous.reducedTransparency &&
                policy.minimumFontWeight == previous.minimumFontWeight &&
                policy.textScale == previous.textScale &&
                policy.minimumFocusRingPx == previous.minimumFocusRingPx;
        };
        NativeStyleResult adapted;
        const auto prior = std::find_if(entries.begin(), entries.end(), [&](const StyleCacheEntry& entry) {
            return entry.isFocused == focused && entry.isPressed == pressed &&
                entry.context == context && samePolicy(entry.accessibility) &&
                entry.base == node.baseStyle &&
                (!focused || entry.focused == node.focusedStyle) &&
                (!pressed || entry.pressed == node.pressedStyle);
        });
        if (prior != entries.end()) {
            ++styleCacheHits;
            adapted = prior->result;
        } else {
            ++styleCacheMisses;
            adapted = NativeStyleAdapter::Adapt(
                ResolveDeclarativeComputedStyle(node, focused, pressed), context, policy);
            if (entries.size() >= 4) entries.erase(entries.begin());
            entries.push_back({node.baseStyle, focused ? node.focusedStyle : WidgetComputedStyle{},
                pressed ? node.pressedStyle : WidgetComputedStyle{}, context, policy, adapted, focused, pressed});
        }
        for (const auto& diagnostic : adapted.diagnostics) {
            Add(node.id, L"invalid_style",
                diagnostic.property + L": " + diagnostic.message);
        }
        return adapted;
    }

    [[nodiscard]] static std::size_t VirtualCollectionItemCount(
        const WidgetNode& collectionRoot) noexcept {
        std::size_t count{};
        const auto visit = [&](const auto& self,
                               const WidgetNode& node,
                               const bool root) -> void {
            if (!root && node.kind == L"scroll") return;
            if (!node.collectionItemKey.empty()) {
                ++count;
                return;
            }
            for (const auto& child : node.children) self(self, child, false);
        };
        visit(visit, collectionRoot, true);
        return count;
    }

    [[nodiscard]] static std::optional<LayoutElement> VirtualCollectionSpacer(
        const std::string_view scrollId,
        const std::string_view suffix,
        const declarative::ScrollAxis axis,
        const std::uint64_t itemCount,
        const double estimatedItemExtent,
        const float gap) {
        if (itemCount == 0 || axis == declarative::ScrollAxis::None)
            return std::nullopt;
        const auto extent = static_cast<float>(std::max(
            0.0, static_cast<double>(itemCount) * estimatedItemExtent - gap));
        if (!(extent > 0.0F) || !std::isfinite(extent)) return std::nullopt;
        LayoutElement spacer;
        spacer.id = std::string{scrollId} + std::string{suffix};
        spacer.flexGrow = 0.0F;
        spacer.flexShrink = 0.0F;
        spacer.estimatesOffWindowScrollExtent = true;
        if (axis == declarative::ScrollAxis::Vertical)
            spacer.height = extent;
        else
            spacer.width = extent;
        return spacer;
    }

    [[nodiscard]] static bool IsPosterArtworkChild(
        const WidgetNode& parent, const std::size_t childIndex) noexcept {
        return parent.kind == L"actionSurface" &&
            parent.actionSurfacePresentation == L"poster" &&
            parent.children.size() == 2U && childIndex == 0U;
    }

    void PrepareStyle(
        const WidgetNode& node, const std::string_view parentId,
        const float fallbackParentWidth, const float fallbackParentHeight,
        const float parentFontSize, const std::optional<NativeColor>& inheritedBackground) {
        auto parentWidth = fallbackParentWidth;
        auto parentHeight = fallbackParentHeight;
        if (!parentId.empty()) {
            if (const auto* parentBox = layout.Find(parentId)) {
                parentWidth = parentBox->contentBox.width;
                parentHeight = parentBox->contentBox.height;
            }
        }
        const auto focused = node.id == focusedId;
        const auto pressed = focused && node.id == pressedId;
        auto base = Adapt(
            node, false, false, parentWidth, parentHeight, parentFontSize, inheritedBackground);
        auto paint = focused || pressed
            ? Adapt(node, focused, pressed, parentWidth, parentHeight, parentFontSize, inheritedBackground)
            : base;
        const auto narrowId = NarrowStableId(node.id);
        if (narrowId.empty()) {
            Add(node.id, L"invalid_id", L"Native layout IDs must be non-empty ASCII.",
                RenderDiagnosticSeverity::Error);
        }
        prepared[narrowId] = {&node, base.style, paint.style, narrowId};

        const auto& style = base.style;
        auto paintedBackground = style.background();
        if (!paintedBackground &&
            (node.kind == L"button" || node.kind == L"actionSurface"))
            paintedBackground = kDefaultButton;
        std::optional<NativeColor> effectiveBackground = ResolveNativeSurfaceColor(
            paintedBackground,
            inheritedBackground.value_or(NativeColor{0, 0, 0, 1}),
            style.opacity());
        prepared[narrowId].effectiveBackground = effectiveBackground;
    }

    [[nodiscard]] LayoutElement PrepareNode(
        const WidgetNode& node, const std::string_view parentId,
        const float fallbackParentWidth, const float fallbackParentHeight,
        const float parentFontSize, const std::optional<NativeColor>& inheritedBackground) {
        PrepareStyle(node, parentId, fallbackParentWidth, fallbackParentHeight,
            parentFontSize, inheritedBackground);
        const auto narrowId = NarrowStableId(node.id);
        const auto& style = prepared.at(narrowId).baseStyle;
        const auto effectiveBackground = prepared.at(narrowId).effectiveBackground;
        const auto* parentBox = layout.Find(parentId);
        const auto parentWidth = parentBox ? parentBox->contentBox.width : fallbackParentWidth;
        const auto parentHeight = parentBox ? parentBox->contentBox.height : fallbackParentHeight;
        LayoutElement element;
        element.id = narrowId;
        if (node.kind == L"grid") {
            element.layoutMode = LayoutMode::ResponsiveGrid;
            if (node.gridMinimumColumnWidth)
                element.gridMinimumColumnWidth = static_cast<float>(*node.gridMinimumColumnWidth);
            element.gridMaximumColumns = node.gridMaximumColumns;
            if (!node.children.empty() && std::ranges::all_of(node.children,
                    [](const WidgetNode& child) { return !child.collectionItemKey.empty(); })) {
                std::vector<const WidgetNode*> path;
                if (FindNodePath(snapshot->root, node.id, path)) {
                    for (auto parent = path.rbegin(); parent != path.rend(); ++parent) {
                        if ((*parent)->kind != L"scroll") continue;
                        element.gridStartIndex = static_cast<std::int32_t>((*parent)->collectionStartIndex.value_or(0));
                        break;
                    }
                }
            }
        }
        const auto semanticRow = node.kind == L"row" ||
            (node.kind == L"actionSurface" &&
             node.actionSurfaceOrientation == L"horizontal");
        element.direction = node.kind == L"scroll"
            ? (node.scrollAxis == L"horizontal"
                ? LayoutDirection::Row
                : LayoutDirection::Column)
            : (style.direction() == NativeDirection::Row ||
                (style.direction() == NativeDirection::Unspecified && semanticRow)
                    ? LayoutDirection::Row
                    : LayoutDirection::Column);
        element.width = style.widthPx();
        element.height = style.heightPx();
        element.minWidth = style.minWidthPx();
        element.minHeight = style.minHeightPx();
        element.maxWidth = style.maxWidthPx();
        element.maxHeight = style.maxHeightPx();
        element.padding = BoxSpacing::Four(
            style.paddingPx().top,
            style.paddingPx().right,
            style.paddingPx().bottom,
            style.paddingPx().left);
        element.margin = BoxSpacing::Four(
            style.marginPx().top,
            style.marginPx().right,
            style.marginPx().bottom,
            style.marginPx().left);
        element.gap = node.kind == L"grid"
            ? style.gapPx().right
            : element.direction == LayoutDirection::Row
                ? style.gapPx().right
                : style.gapPx().top;
        element.crossGap = style.gapPx().top;
        if (node.kind == L"grid" && style.flexWrap() == NativeFlexWrap::Wrap) {
            Add(node.id, L"invalid_style",
                L"Grid owns responsive wrapping; flex-wrap was ignored.");
        } else if (style.flexWrap() == NativeFlexWrap::Wrap) {
            if (element.direction == LayoutDirection::Row && node.kind != L"scroll") {
                element.wrap = declarative::WrapBehavior::Wrap;
            } else {
                Add(node.id, L"invalid_style",
                    L"flex-wrap: wrap applies only to non-scroll row containers and was ignored.");
            }
        }
        element.flexGrow = style.flexGrow();
        element.flexShrink = style.flexShrink();
        if (!style.flexBasisAuto()) element.flexBasis = style.flexBasisPx();
        element.aspectRatio = style.aspectRatio();
        if (node.kind == L"windowPreview" && !element.aspectRatio)
            element.aspectRatio = static_cast<float>(node.previewAspectRatio);
        if (node.kind == L"mediaViewport" && snapshot->embeddedMediaSession &&
            node.mediaSessionId == snapshot->embeddedMediaSession->id) {
            const auto& media = *snapshot->embeddedMediaSession;
            if (!element.width && media.surface.preferredWidth)
                element.width = static_cast<float>(*media.surface.preferredWidth);
            if (media.surface.minimumWidth)
                element.minWidth = std::max(
                    element.minWidth.value_or(0.0F),
                    static_cast<float>(*media.surface.minimumWidth));
            if (media.surface.minimumHeight)
                element.minHeight = std::max(
                    element.minHeight.value_or(0.0F),
                    static_cast<float>(*media.surface.minimumHeight));
            if (!element.aspectRatio)
                element.aspectRatio = static_cast<float>(media.aspectRatio);
            element.overflow = declarative::OverflowBehavior::Clip;
        } else if (node.kind == L"button") {
            element.minHeight = std::max(
                element.minHeight.value_or(0.0F), kMinimumControlSize);
        } else if (node.kind == L"slider") {
            element.minWidth = std::max(element.minWidth.value_or(0.0F), 160.0F);
            element.minHeight = std::max(element.minHeight.value_or(0.0F), kMinimumControlSize);
        } else if (node.kind == L"actionSurface") {
            // A rich tile is one controller and pointer target. Enforce the
            // platform's minimum target and clip its presentational subtree so
            // painted content can never extend beyond the actionable bounds.
            element.minWidth = std::max(element.minWidth.value_or(0.0F), kMinimumControlSize);
            element.minHeight = std::max(element.minHeight.value_or(0.0F), kMinimumControlSize);
        } else if (node.kind == L"backgroundSurface") {
            // The single foreground child owns intrinsic geometry. The image
            // is paint-only and is clipped to that resulting bounded surface.
            element.overflow = declarative::OverflowBehavior::Clip;
        } else if (node.kind == L"loadingIndicator") {
            const auto semanticSize = node.indicatorSize == L"compact"
                ? 16.0F
                : node.indicatorSize == L"large" ? 32.0F : 24.0F;
            element.width = semanticSize;
            element.height = semanticSize;
            element.minWidth = semanticSize;
            element.minHeight = semanticSize;
            element.maxWidth = semanticSize;
            element.maxHeight = semanticSize;
            element.flexGrow = 0.0F;
            element.flexShrink = 0.0F;
        }
        element.overflow = style.overflow() == NativeOverflow::Clip
            ? declarative::OverflowBehavior::Clip
            : declarative::OverflowBehavior::Visible;
        if (node.kind == L"actionSurface" || node.kind == L"windowPreview" || node.kind == L"mediaViewport" ||
            node.kind == L"backgroundSurface")
            element.overflow = declarative::OverflowBehavior::Clip;
        if (node.kind == L"scroll") {
            element.overflow = declarative::OverflowBehavior::Clip;
            if (node.scrollAxis == L"vertical")
                element.scrollAxis = declarative::ScrollAxis::Vertical;
            else if (node.scrollAxis == L"horizontal")
                element.scrollAxis = declarative::ScrollAxis::Horizontal;
            else
                Add(node.id, L"invalid_scroll_axis",
                    L"Scroll requires the vertical or horizontal axis.",
                    RenderDiagnosticSeverity::Error);
            const auto key = ScrollStateKey(node.id);
            if (!measurementOnly && node.collectionResetGeneration) {
                auto& state = ScrollState()[key];
                if (state.collectionResetGeneration != *node.collectionResetGeneration) {
                    state = {};
                    state.collectionResetGeneration = *node.collectionResetGeneration;
                    resetCollections.insert(node.id);
                }
            }
            if (const auto offset = ScrollState().find(key);
                offset != ScrollState().end()) {
                if (!measurementOnly)
                    offset->second.lastAccess = ++ScrollAccessClock();
                element.scrollOffset = measurementOnly ? 0.0F : offset->second.offset;
            } else if (!measurementOnly && node.virtualCollectionWindow &&
                       node.virtualCollectionWindow->firstItemIndex) {
                const auto leading = static_cast<float>(
                    static_cast<double>(*node.virtualCollectionWindow->firstItemIndex) *
                    node.virtualCollectionWindow->estimatedItemExtent);
                if (std::isfinite(leading)) {
                    element.scrollOffset = leading;
                    StoreScrollOffset(key, leading);
                }
            }
        }
        switch (style.justify()) {
        case NativeJustify::Center: element.mainAxisAlignment = declarative::MainAxisAlignment::Center; break;
        case NativeJustify::End: element.mainAxisAlignment = declarative::MainAxisAlignment::End; break;
        case NativeJustify::SpaceBetween: element.mainAxisAlignment = declarative::MainAxisAlignment::SpaceBetween; break;
        case NativeJustify::SpaceAround: element.mainAxisAlignment = declarative::MainAxisAlignment::SpaceAround; break;
        default: element.mainAxisAlignment = declarative::MainAxisAlignment::Start; break;
        }
        switch (style.align()) {
        case NativeAlign::Start: element.crossAxisAlignment = declarative::CrossAxisAlignment::Start; break;
        case NativeAlign::Center: element.crossAxisAlignment = declarative::CrossAxisAlignment::Center; break;
        case NativeAlign::End: element.crossAxisAlignment = declarative::CrossAxisAlignment::End; break;
        default: element.crossAxisAlignment = declarative::CrossAxisAlignment::Stretch; break;
        }
        // `align` is align-items: it positions this node's children. It must
        // not also become align-self for the node, otherwise a row which
        // centers its children unexpectedly shrinks inside a stretching
        // parent. Ordinary auto-sized nodes keep Taffy's inherited stretch;
        // definite dimensions, constraints, and aspect ratio still bound it.
        if (node.kind == L"loadingIndicator") element.stretchCrossAxis = false;
        element.children.reserve(node.children.size() + 2U);
        const float textScale = std::isfinite(options.accessibility.textScale) &&
                options.accessibility.textScale >= 0.85F &&
                options.accessibility.textScale <= 1.5F
            ? options.accessibility.textScale
            : 1.0F;
        if (node.kind == L"scroll" && node.virtualCollectionWindow &&
            node.virtualCollectionWindow->firstItemIndex) {
            if (auto leading = VirtualCollectionSpacer(
                    narrowId, "\x1fvirtual-leading", element.scrollAxis,
                    *node.virtualCollectionWindow->firstItemIndex,
                    node.virtualCollectionWindow->estimatedItemExtent,
                    element.gap)) {
                element.children.push_back(std::move(*leading));
            }
        }
        if (node.kind == L"focusPresentationSurface") {
            const auto selection = ResolveFocusPresentationSelection(
                snapshot->root, focusedId);
            const auto* fragment = selection.surface == &node
                ? selection.fragment
                : !node.defaultFocusPresentation.empty()
                    ? &node.defaultFocusPresentation.front()
                    : nullptr;
            if (fragment && IsResponsiveVisible(*fragment)) {
                element.children.push_back(PrepareNode(
                    *fragment, narrowId, parentWidth, parentHeight,
                    style.fontSizePx() / textScale, effectiveBackground));
            }
        }
        for (std::size_t childIndex = 0; childIndex < node.children.size(); ++childIndex) {
            const auto& child = node.children[childIndex];
            if (!IsResponsiveVisible(child)) continue;
            auto preparedChild = PrepareNode(
                child,
                narrowId,
                parentWidth,
                parentHeight,
                style.fontSizePx() / textScale,
                effectiveBackground);
            if (!IsPosterArtworkChild(node, childIndex))
                element.children.push_back(std::move(preparedChild));
        }
        if (node.kind == L"scroll" && node.virtualCollectionWindow &&
            node.virtualCollectionWindow->firstItemIndex &&
            node.virtualCollectionWindow->totalItemCount) {
            const auto admittedItems = VirtualCollectionItemCount(node);
            const auto first = *node.virtualCollectionWindow->firstItemIndex;
            const auto total = *node.virtualCollectionWindow->totalItemCount;
            const auto after = first + admittedItems <= total
                ? total - first - admittedItems
                : 0U;
            if (auto trailing = VirtualCollectionSpacer(
                    narrowId, "\x1fvirtual-trailing", element.scrollAxis,
                    after, node.virtualCollectionWindow->estimatedItemExtent,
                    element.gap)) {
                element.children.push_back(std::move(*trailing));
            }
        }
        return element;
    }

    [[nodiscard]] std::wstring ScrollStateKey(const std::wstring_view nodeId) const {
        std::wstring key(snapshot->instanceId);
        key.push_back(L'\x1f');
        key.append(snapshot->activeInputScopeId);
        key.push_back(L'\x1f');
        key.append(nodeId);
        return key;
    }

    [[nodiscard]] std::wstring MotionStateKey(const std::wstring_view nodeId) const {
        std::wstring key(snapshot->instanceId);
        key.push_back(L'\x1f');
        key.append(nodeId);
        return key;
    }

    [[nodiscard]] static bool ClipsDescendants(
        const WidgetNode& node,
        const NativeRenderStyle& style) noexcept {
        return node.kind == L"scroll" || node.kind == L"actionSurface" ||
            node.kind == L"backgroundSurface" ||
            style.overflow() == NativeOverflow::Clip;
    }

    void ResolvePresentation(
        const WidgetNode& node,
        const float inheritedTranslationX,
        const float inheritedTranslationY,
        const Rect ancestorClip) {
        if (!IsResponsiveVisible(node)) return;
        const auto narrowId = NarrowStableId(node.id);
        const auto preparedNode = prepared.find(narrowId);
        const auto* box = layout.Find(narrowId);
        if (preparedNode == prepared.end() || !box) return;
        const auto& style = preparedNode->second.paintStyle;
        const auto disabledFactor = DeclarativeStateOpacityFactor(
            node.isDisabled, node.isBusy, options.accessibility);
        const auto motion = MotionTimeline().Resolve(
            MotionStateKey(node.id),
            DeclarativeMotionValue{
                style.opacity() * disabledFactor,
                style.scale(),
                style.translateXPx(),
                style.translateYPx(),
            },
            style.transitionDurationMilliseconds(),
            style.transitionEasing(),
            options.accessibility.reducedMotion);
        const auto translationX = AddBoundedTranslation(
            inheritedTranslationX, motion.value.translationX);
        const auto translationY = AddBoundedTranslation(
            inheritedTranslationY, motion.value.translationY);
        const auto borderBox = TranslateRect(box->borderBox, translationX, translationY);
        const auto contentBox = TranslateRect(box->contentBox, translationX, translationY);
        const auto visibleBox = Intersection(borderBox, ancestorClip);
        presentation.insert_or_assign(narrowId, PresentationNode{
            borderBox,
            contentBox,
            visibleBox,
            ancestorClip,
            motion,
        });

        const auto childClip = ClipsDescendants(node, preparedNode->second.baseStyle)
            ? Intersection(ancestorClip, contentBox)
            : ancestorClip;
        if (node.kind == L"focusPresentationSurface") {
            const auto selection = ResolveFocusPresentationSelection(
                snapshot->root, focusedId);
            const auto* fragment = selection.surface == &node
                ? selection.fragment
                : !node.defaultFocusPresentation.empty()
                    ? &node.defaultFocusPresentation.front()
                    : nullptr;
            if (fragment)
                ResolvePresentation(
                    *fragment, translationX, translationY, childClip);
        }
        for (const auto& child : node.children) {
            ResolvePresentation(
                child, translationX, translationY, childClip);
        }
    }

    void ResolvePresentationWithFocusFollow() {
        const auto started = std::chrono::steady_clock::now();
        bool presentationMatchesLayout = false;
        std::size_t presentationFollowAttempts{};
        bool lastPresentationFollowChanged{};
        for (std::size_t followPass = 0;
             !options.suppressFocusedDescendantFollow &&
                 followPass < kMaximumFocusFollowPasses;
             ++followPass) {
            ++presentationFollowAttempts;
            presentation.clear();
            ResolvePresentation(snapshot->root, 0.0F, 0.0F, viewport);
            presentationMatchesLayout = true;
            if (FocusedTargetVisibleInPresentation()) {
                focusFollowTrace.converged = true;
                break;
            }
            const auto followAttempt = FollowFocusedDescendant(true);
            lastPresentationFollowChanged = followAttempt.changed;
            const bool stableOrRepeated = !lastPresentationFollowChanged ||
                !followAttempt.meaningfulOffsetChange ||
                followAttempt.repeatedOffsetState;
            if (stableOrRepeated) break;
            presentationMatchesLayout = false;
            BuildLayout(
                false,
                true,
                CollectionAnchorPolicy::PreserveFocusFollowOffsets);
        }
        if (options.suppressFocusedDescendantFollow) {
            presentation.clear();
            ResolvePresentation(snapshot->root, 0.0F, 0.0F, viewport);
            presentationMatchesLayout = true;
        }
        if (presentationFollowAttempts == kMaximumFocusFollowPasses &&
            (!presentationMatchesLayout ||
             !FocusedTargetVisibleInPresentation())) {
            focusFollowTrace.boundHit = true;
        }
        if (!presentationMatchesLayout) {
            presentation.clear();
            ResolvePresentation(snapshot->root, 0.0F, 0.0F, viewport);
        }
        AddFocusFollowElapsed(
            FocusFollowPhase::Presentation,
            static_cast<std::uint64_t>(
                std::chrono::duration_cast<std::chrono::microseconds>(
                    std::chrono::steady_clock::now() - started).count()));
    }

    void VisitScrollNodes(
        const WidgetNode& node,
        const std::function<void(const WidgetNode&)>& callback) const {
        if (!IsResponsiveVisible(node)) return;
        if (node.kind == L"scroll") callback(node);
        for (const auto& child : node.children) VisitScrollNodes(child, callback);
    }

    void StoreScrollOffset(const std::wstring_view key, const float offset) {
        auto& state = ScrollState()[std::wstring{key}];
        state.offset = offset;
        state.lastAccess = ++ScrollAccessClock();
    }

    [[nodiscard]] const WidgetNode* FindCollectionItem(
        const WidgetNode& node, const std::wstring_view key) const noexcept {
        if (node.collectionItemKey == key) return &node;
        for (const auto& child : node.children) {
            if (const auto* found = FindCollectionItem(child, key)) return found;
        }
        return nullptr;
    }

    void CollectCollectionItems(
        const WidgetNode& node,
        const WidgetNode& collectionRoot,
        const declarative::LayoutBox* scrollBox,
        CollectionDiagnosticObservation& observation) const {
        if (&node != &collectionRoot && node.kind == L"scroll") return;
        if (!node.collectionItemKey.empty()) {
            if (observation.itemKeys.size() < kMaximumCollectionDiagnosticItems) {
                observation.itemKeys.push_back(node.collectionItemKey);
                CollectionDiagnosticItemGeometry geometry;
                if (scrollBox &&
                    scrollBox->scrollAxis != declarative::ScrollAxis::None) {
                    if (const auto* itemBox = layout.Find(NarrowStableId(node.id))) {
                        if (scrollBox->scrollAxis ==
                            declarative::ScrollAxis::Vertical) {
                            geometry.position =
                                itemBox->borderBox.y - scrollBox->contentBox.y;
                            geometry.extent = itemBox->borderBox.height;
                        } else {
                            geometry.position =
                                itemBox->borderBox.x - scrollBox->contentBox.x;
                            geometry.extent = itemBox->borderBox.width;
                        }
                        if (std::isfinite(geometry.position) &&
                            std::isfinite(geometry.extent) &&
                            geometry.extent >= 0.0F) {
                            geometry.valid = true;
                        }
                    }
                }
                observation.itemGeometry.push_back(geometry);
            } else {
                observation.itemsTruncated = true;
            }
            return;
        }
        for (const auto& child : node.children)
            CollectCollectionItems(
                child, collectionRoot, scrollBox, observation);
    }

    [[nodiscard]] bool ReconcileCollectionAnchors(const bool contentChangesOnly = false) {
        bool changed{};
        VisitScrollNodes(snapshot->root, [&](const WidgetNode& scroll) {
            if (scroll.collectionAnchorKey.empty() || resetCollections.contains(scroll.id)) return;
            const auto stateKey = ScrollStateKey(scroll.id);
            const auto existing = ScrollState().find(stateKey);
            const auto* scrollBox = layout.Find(NarrowStableId(scroll.id));
            if (existing == ScrollState().end() ||
                !scrollBox || scrollBox->scrollAxis == declarative::ScrollAxis::None) {
                return;
            }

            const auto* cached = owner->incrementalLayoutCache_ ? &*owner->incrementalLayoutCache_ : nullptr;
            CollectionDiagnosticObservation observed;
            CollectCollectionItems(scroll, scroll, scrollBox, observed);
            const auto* priorCollection = cached && cached->instanceId == snapshot->instanceId &&
                    cached->collections.contains(scroll.id)
                ? &cached->collections.at(scroll.id) : nullptr;
            const bool contentChanged = priorCollection && priorCollection->itemKeys != observed.itemKeys;
            if (contentChangesOnly && !contentChanged) return;

            const auto apply = [&](const float position,
                                   const float priorPosition,
                                   const std::wstring_view mode,
                                   const std::wstring_view retainedKey) {
                const auto desired = std::clamp(
                    scrollBox->scrollOffset + position - priorPosition,
                    0.0F, scrollBox->maximumScrollOffset);
                const auto [trace, inserted] =
                    collectionReconciliationOffsets.try_emplace(
                        scroll.id,
                        CollectionReconciliationTrace{
                            scrollBox->scrollOffset,
                            desired,
                            std::wstring{mode},
                            std::wstring{retainedKey},
                        });
                if (!inserted) {
                    trace->second.offsetAfter = desired;
                    if (trace->second.mode != L"overlap" || mode == L"overlap") {
                        trace->second.mode = mode;
                        trace->second.key = retainedKey;
                    }
                }
                if (std::abs(desired - scrollBox->scrollOffset) <= 0.01F)
                    return;
                StoreScrollOffset(stateKey, desired);
                changed = true;
            };

            if ((!contentChangesOnly || !contentChanged) && existing->second.hasAnchorPosition &&
                existing->second.anchorKey == scroll.collectionAnchorKey) {
                const auto* item = FindCollectionItem(
                    scroll, scroll.collectionAnchorKey);
                const auto* itemBox = item
                    ? layout.Find(NarrowStableId(item->id)) : nullptr;
                if (!itemBox) return;
                const auto position = scrollBox->scrollAxis ==
                        declarative::ScrollAxis::Vertical
                    ? itemBox->borderBox.y - scrollBox->contentBox.y
                    : itemBox->borderBox.x - scrollBox->contentBox.x;
                apply(
                    position, existing->second.anchorPosition,
                    L"exact", scroll.collectionAnchorKey);
                return;
            }

            const auto reconcileReplacementWindow = [&] {
                if (!scroll.virtualCollectionWindow ||
                    scroll.virtualCollectionWindow->change !=
                        VirtualCollectionWindowChange::Replace) {
                    return;
                }
                CollectionDiagnosticObservation current;
                CollectCollectionItems(scroll, scroll, scrollBox, current);
                if (current.itemsTruncated || current.itemKeys.empty()) return;
                const auto viewportExtent = scrollBox->scrollAxis ==
                        declarative::ScrollAxis::Vertical
                    ? scrollBox->contentBox.height
                    : scrollBox->contentBox.width;
                if (!std::isfinite(viewportExtent) ||
                    viewportExtent <= kRevealEpsilon) {
                    return;
                }

                // A replacement is an arbitrary bounded window. Retain the
                // current offset when any admitted row still occupies that
                // viewport; virtual leading/trailing spacers alone do not make
                // the replacement usable. When no row is reachable, place the
                // replacement's declared anchor at the leading edge so the
                // host cannot preserve an empty viewport deep in the virtual
                // extent after the widget resets its private window.
                const CollectionDiagnosticItemGeometry* anchorGeometry{};
                for (std::size_t index = 0;
                     index < current.itemGeometry.size() &&
                         index < current.itemKeys.size();
                     ++index) {
                    const auto& geometry = current.itemGeometry[index];
                    if (!geometry.valid) continue;
                    if (geometry.position + geometry.extent > kRevealEpsilon &&
                        geometry.position < viewportExtent - kRevealEpsilon) {
                        return;
                    }
                    if (current.itemKeys[index] == scroll.collectionAnchorKey)
                        anchorGeometry = &geometry;
                }
                if (!anchorGeometry) return;
                apply(
                    anchorGeometry->position,
                    0.0F,
                    L"replace-window",
                    scroll.collectionAnchorKey);
            };

            // A bounded retained-window shift can remove the declared anchor
            // while leaving visible keyed rows in both snapshots. Preserve one
            // such row's prior screen-relative position before focus-follow
            // minimally reveals the newly requested target. A retained old
            // anchor, changed viewport/axis, missing geometry, or truncated key
            // set leaves the established exact-key policy unchanged.
            const auto* previousCache = owner->incrementalLayoutCache_
                ? &*owner->incrementalLayoutCache_ : nullptr;
            if (!previousCache ||
                previousCache->instanceId != snapshot->instanceId ||
                !SameRect(previousCache->viewport, viewport)) {
                reconcileReplacementWindow();
                return;
            }
            const auto previous = previousCache->collections.find(scroll.id);
            const auto* previousScrollBox = previousCache->layout.Find(
                NarrowStableId(scroll.id));
            if (previous == previousCache->collections.end() ||
                previous->second.itemsTruncated ||
                previous->second.anchorKey != existing->second.anchorKey ||
                !previousScrollBox ||
                previousScrollBox->scrollAxis != scrollBox->scrollAxis) {
                reconcileReplacementWindow();
                return;
            }

            CollectionDiagnosticObservation current;
            CollectCollectionItems(scroll, scroll, scrollBox, current);
            if (current.itemsTruncated) return;
            const bool retainedAnchorSurvives = std::ranges::find(
                    current.itemKeys, existing->second.anchorKey) !=
                current.itemKeys.end();
            const bool replacementWindow = scroll.virtualCollectionWindow &&
                scroll.virtualCollectionWindow->change ==
                    VirtualCollectionWindowChange::Replace;
            // Free scrolling must preserve a visible row when the loaded
            // window shifts, even if its declared (possibly offscreen) anchor
            // survives. This adjusts content coordinates, never live focus.
            if (retainedAnchorSurvives && !replacementWindow && !contentChangesOnly) {
                return;
            }
            if (std::abs(previousScrollBox->contentBox.width -
                          scrollBox->contentBox.width) > 0.01F ||
                std::abs(previousScrollBox->contentBox.height -
                         scrollBox->contentBox.height) > 0.01F) {
                reconcileReplacementWindow();
                return;
            }

            const auto viewportExtent = scrollBox->scrollAxis ==
                    declarative::ScrollAxis::Vertical
                ? previousScrollBox->contentBox.height
                : previousScrollBox->contentBox.width;
            const CollectionDiagnosticItemGeometry* retainedPrior{};
            const CollectionDiagnosticItemGeometry* retainedCurrent{};
            std::wstring_view retainedKey;
            float bestDistance = std::numeric_limits<float>::max();
            for (std::size_t priorIndex = 0;
                 priorIndex < previous->second.itemKeys.size(); ++priorIndex) {
                if (priorIndex >= previous->second.itemGeometry.size()) break;
                const auto& priorKey = previous->second.itemKeys[priorIndex];
                const auto& prior = previous->second.itemGeometry[priorIndex];
                if (!prior.valid ||
                    prior.position + prior.extent <= kRevealEpsilon ||
                    prior.position >= viewportExtent - kRevealEpsilon) {
                    continue;
                }
                const auto match = std::ranges::find(
                    current.itemKeys, priorKey);
                if (match == current.itemKeys.end()) continue;
                const auto currentIndex = static_cast<std::size_t>(
                    match - current.itemKeys.begin());
                if (currentIndex >= current.itemGeometry.size() ||
                    !current.itemGeometry[currentIndex].valid) {
                    continue;
                }
                const auto distance = std::abs(prior.position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                retainedPrior = &prior;
                retainedCurrent = &current.itemGeometry[currentIndex];
                retainedKey = priorKey;
            }
            if (!retainedPrior || !retainedCurrent) {
                reconcileReplacementWindow();
                return;
            }
            apply(
                retainedCurrent->position,
                retainedPrior->position,
                L"overlap",
                retainedKey);
        });
        return changed;
    }

    [[nodiscard]] bool CollectionContainsNode(
        const WidgetNode& node,
        const WidgetNode& collectionRoot,
        const std::wstring_view nodeId) const noexcept {
        if (nodeId.empty()) return false;
        if (&node != &collectionRoot && node.kind == L"scroll") return false;
        if (node.id == nodeId) return true;
        return std::ranges::any_of(node.children, [&](const WidgetNode& child) {
            return CollectionContainsNode(child, collectionRoot, nodeId);
        });
    }

    [[nodiscard]] std::map<
        std::wstring, CollectionDiagnosticObservation, std::less<>>
    CaptureCollectionObservations() const {
        std::map<std::wstring, CollectionDiagnosticObservation, std::less<>>
            observations;
        VisitScrollNodes(snapshot->root, [&](const WidgetNode& scroll) {
            if (scroll.collectionAnchorKey.empty()) return;
            CollectionDiagnosticObservation observation;
            const auto* scrollBox = layout.Find(NarrowStableId(scroll.id));
            CollectCollectionItems(scroll, scroll, scrollBox, observation);
            observation.anchorKey = scroll.collectionAnchorKey;
            observation.containsFocusedElement = CollectionContainsNode(
                scroll, scroll, focusedId);
            observation.containsRequestedFocus = CollectionContainsNode(
                scroll, scroll, snapshot->initialFocusId);

            const auto stateKey = ScrollStateKey(scroll.id);
            if (const auto state = ScrollState().find(stateKey);
                state != ScrollState().end()) {
                observation.offset = state->second.offset;
                observation.anchorPosition = state->second.anchorPosition;
                observation.hasAnchorPosition = state->second.hasAnchorPosition;
            } else if (const auto* box = layout.Find(NarrowStableId(scroll.id))) {
                observation.offset = box->scrollOffset;
            }
            if (const auto reconciliation =
                    collectionReconciliationOffsets.find(scroll.id);
                reconciliation != collectionReconciliationOffsets.end()) {
                observation.reconciliationOffsetBefore =
                    reconciliation->second.offsetBefore;
                observation.reconciliationOffsetAfter =
                    reconciliation->second.offsetAfter;
                observation.hasReconciliationOffsets = true;
                observation.reconciliationMode =
                    reconciliation->second.mode;
                observation.reconciliationKey =
                    reconciliation->second.key;
            }

            if (const auto presented = presentation.find(NarrowStableId(scroll.id));
                presented != presentation.end()) {
                observation.viewport = presented->second.contentBox;
                observation.hasViewport = true;
            } else if (const auto* box = layout.Find(NarrowStableId(scroll.id))) {
                observation.viewport = box->contentBox;
                observation.hasViewport = true;
            }

            if (observation.containsFocusedElement) {
                if (const auto presentedFocus = presentation.find(
                        NarrowStableId(focusedId));
                    presentedFocus != presentation.end()) {
                    observation.focusRect = presentedFocus->second.borderBox;
                    observation.hasFocusRect = true;
                }
            }
            observations.insert_or_assign(scroll.id, std::move(observation));
        });
        return observations;
    }

    [[nodiscard]] static std::optional<std::size_t> ContiguousKeyPosition(
        const std::vector<std::wstring>& needle,
        const std::vector<std::wstring>& haystack) {
        if (needle.empty() || needle.size() > haystack.size()) return std::nullopt;
        for (std::size_t start = 0;
             start + needle.size() <= haystack.size(); ++start) {
            if (std::equal(
                    needle.begin(), needle.end(), haystack.begin() + start)) {
                return start;
            }
        }
        return std::nullopt;
    }

    [[nodiscard]] static std::wstring CollectionChangeText(
        const CollectionDiagnosticObservation* previous,
        const CollectionDiagnosticObservation* current) {
        if (!previous) return L"initial";
        if (!current) return L"removed";
        const auto& oldKeys = previous->itemKeys;
        const auto& newKeys = current->itemKeys;
        if (oldKeys == newKeys) {
            return previous->anchorKey == current->anchorKey
                ? L"requested-focus" : L"anchor";
        }
        if (oldKeys.empty()) return L"append";
        if (newKeys.empty()) return L"trim-all";
        if (const auto oldInNew = ContiguousKeyPosition(oldKeys, newKeys)) {
            if (*oldInNew == 0) return L"append";
            if (*oldInNew + oldKeys.size() == newKeys.size()) return L"prepend";
            return L"prepend+append";
        }
        if (const auto newInOld = ContiguousKeyPosition(newKeys, oldKeys)) {
            if (*newInOld == 0) return L"trim-end";
            if (*newInOld + newKeys.size() == oldKeys.size()) return L"trim-start";
            return L"trim-both";
        }
        const auto overlapLimit = std::min(oldKeys.size(), newKeys.size());
        for (std::size_t overlap = overlapLimit; overlap > 0; --overlap) {
            if (std::equal(
                    oldKeys.end() - overlap, oldKeys.end(), newKeys.begin())) {
                return L"trim-start+append";
            }
            if (std::equal(
                    newKeys.end() - overlap, newKeys.end(), oldKeys.begin())) {
                return L"prepend+trim-end";
            }
        }
        return L"replace";
    }

    [[nodiscard]] static std::wstring CollectionEdgesText(
        const CollectionDiagnosticObservation* observation) {
        if (!observation || observation->itemKeys.empty()) return L"[]";
        std::wstring result{L"["};
        const auto& keys = observation->itemKeys;
        const auto appendKey = [&](const std::wstring_view key) {
            if (result.size() > 1) result += L",";
            result += BoundedDiagnosticIdentifier(key);
        };
        if (keys.size() <= kCollectionDiagnosticEdgeKeys * 2U) {
            for (const auto& key : keys) appendKey(key);
        } else {
            for (std::size_t index = 0;
                 index < kCollectionDiagnosticEdgeKeys; ++index) {
                appendKey(keys[index]);
            }
            result += L",...,";
            for (std::size_t index = keys.size() - kCollectionDiagnosticEdgeKeys;
                 index < keys.size(); ++index) {
                if (index != keys.size() - kCollectionDiagnosticEdgeKeys)
                    result += L",";
                result += BoundedDiagnosticIdentifier(keys[index]);
            }
        }
        if (observation->itemsTruncated) result += L",truncated";
        result += L"]";
        return result;
    }

    [[nodiscard]] static std::wstring CollectionPositionText(
        const CollectionDiagnosticObservation* observation) {
        return observation && observation->hasAnchorPosition
            ? std::to_wstring(observation->anchorPosition)
            : std::wstring{L"none"};
    }

    [[nodiscard]] static std::wstring CollectionCountText(
        const CollectionDiagnosticObservation* observation) {
        if (!observation) return L"0";
        auto result = std::to_wstring(observation->itemKeys.size());
        if (observation->itemsTruncated) result += L"+";
        return result;
    }

    [[nodiscard]] static std::wstring CollectionRectText(
        const Rect rect,
        const bool present) {
        return present ? DiagnosticRectText(rect) : std::wstring{L"none"};
    }

    [[nodiscard]] std::wstring BuildCollectionAdmissionSummary(
        const IncrementalLayoutCache* previousCache,
        const std::map<
            std::wstring, CollectionDiagnosticObservation, std::less<>>& current)
        const {
        const auto* previous = previousCache &&
                previousCache->instanceId == snapshot->instanceId
            ? &previousCache->collections
            : nullptr;
        std::set<std::wstring, std::less<>> scrollIds;
        if (previous) {
            for (const auto& entry : *previous) scrollIds.insert(entry.first);
        }
        for (const auto& entry : current) scrollIds.insert(entry.first);

        std::wstring events;
        std::size_t eventCount{};
        bool eventsTruncated{};
        for (const auto& scrollId : scrollIds) {
            const CollectionDiagnosticObservation* oldObservation{};
            const CollectionDiagnosticObservation* newObservation{};
            if (previous) {
                if (const auto found = previous->find(scrollId);
                    found != previous->end()) {
                    oldObservation = &found->second;
                }
            }
            if (const auto found = current.find(scrollId); found != current.end())
                newObservation = &found->second;

            const bool collectionChanged = !oldObservation || !newObservation ||
                oldObservation->itemKeys != newObservation->itemKeys ||
                oldObservation->itemsTruncated != newObservation->itemsTruncated ||
                oldObservation->anchorKey != newObservation->anchorKey;
            const bool requestedFocusChanged = previousCache &&
                previousCache->instanceId == snapshot->instanceId &&
                previousCache->requestedFocusId != snapshot->initialFocusId &&
                ((oldObservation && oldObservation->containsRequestedFocus) ||
                 (newObservation && newObservation->containsRequestedFocus));
            if (!collectionChanged && !requestedFocusChanged) continue;
            if (eventCount >= kMaximumCollectionDiagnosticEvents) {
                eventsTruncated = true;
                break;
            }

            const auto oldAnchor = oldObservation &&
                    !oldObservation->anchorKey.empty()
                ? BoundedDiagnosticIdentifier(oldObservation->anchorKey)
                : std::wstring{L"none"};
            const auto newAnchor = newObservation &&
                    !newObservation->anchorKey.empty()
                ? BoundedDiagnosticIdentifier(newObservation->anchorKey)
                : std::wstring{L"none"};
            const auto finalFocus = newObservation
                ? CollectionRectText(
                    newObservation->focusRect, newObservation->hasFocusRect)
                : std::wstring{L"none"};
            const auto finalViewport = newObservation
                ? CollectionRectText(
                    newObservation->viewport, newObservation->hasViewport)
                : std::wstring{L"none"};
            std::wstring event =
                L"{scroll=" + BoundedDiagnosticIdentifier(scrollId) +
                L",change=" + CollectionChangeText(oldObservation, newObservation) +
                L",old-count=" + CollectionCountText(oldObservation) +
                L",new-count=" + CollectionCountText(newObservation) +
                L",old-edges=" + CollectionEdgesText(oldObservation) +
                L",new-edges=" + CollectionEdgesText(newObservation) +
                L",old-anchor=" + oldAnchor +
                L",old-anchor-position=" +
                    CollectionPositionText(oldObservation) +
                L",new-anchor=" + newAnchor +
                L",new-anchor-position=" +
                    CollectionPositionText(newObservation) +
                L",reconciliation=" +
                    (newObservation &&
                            !newObservation->reconciliationMode.empty()
                        ? BoundedDiagnosticIdentifier(
                            newObservation->reconciliationMode)
                        : std::wstring{L"none"}) +
                L",retained-key=" +
                    (newObservation &&
                            !newObservation->reconciliationKey.empty()
                        ? BoundedDiagnosticIdentifier(
                            newObservation->reconciliationKey)
                        : std::wstring{L"none"}) +
                L",offset-before=" +
                    (newObservation &&
                            newObservation->hasReconciliationOffsets
                        ? std::to_wstring(
                            newObservation->reconciliationOffsetBefore)
                        : oldObservation
                        ? std::to_wstring(oldObservation->offset)
                        : std::wstring{L"none"}) +
                L",offset-after=" +
                    (newObservation &&
                            newObservation->hasReconciliationOffsets
                        ? std::to_wstring(
                            newObservation->reconciliationOffsetAfter)
                        : newObservation
                        ? std::to_wstring(newObservation->offset)
                        : std::wstring{L"none"}) +
                L",final-offset=" +
                    (newObservation
                        ? std::to_wstring(newObservation->offset)
                        : std::wstring{L"none"}) +
                L",prior-focused=" +
                    (previousCache && !previousCache->focusedElementId.empty()
                        ? BoundedDiagnosticIdentifier(
                            previousCache->focusedElementId)
                        : std::wstring{L"none"}) +
                L",focused=" +
                    (focusedId.empty()
                        ? std::wstring{L"none"}
                        : BoundedDiagnosticIdentifier(focusedId)) +
                L",prior-requested-focus=" +
                    (previousCache && !previousCache->requestedFocusId.empty()
                        ? BoundedDiagnosticIdentifier(
                            previousCache->requestedFocusId)
                        : std::wstring{L"none"}) +
                L",requested-focus=" +
                    (snapshot->initialFocusId.empty()
                        ? std::wstring{L"none"}
                        : BoundedDiagnosticIdentifier(snapshot->initialFocusId)) +
                L",final-focus=" + finalFocus +
                L",final-viewport=" + finalViewport + L"}";
            if (events.size() + event.size() + 1U >
                kMaximumCollectionSummaryCharacters) {
                eventsTruncated = true;
                break;
            }
            if (!events.empty()) events += L";";
            events += std::move(event);
            ++eventCount;
        }
        if (events.empty()) return {};

        std::wstring summary =
            L"collection-admission instance=" +
            BoundedDiagnosticIdentifier(snapshot->instanceId) +
            L" sequence=" + std::to_wstring(snapshot->sequence) +
            L" previous-sequence=" +
            (previousCache && previousCache->instanceId == snapshot->instanceId
                ? std::to_wstring(previousCache->sequence)
                : std::wstring{L"none"}) +
            L" events=[" + events + L"]";
        if (eventsTruncated) summary += L" events-truncated=true";
        if (summary.size() > kMaximumCollectionSummaryCharacters)
            summary.resize(kMaximumCollectionSummaryCharacters);
        return summary;
    }

    [[nodiscard]] std::vector<const WidgetNode*> FocusPath() const {
        std::vector<const WidgetNode*> path;
        if (!prepared.contains(NarrowStableId(focusedId))) return path;
        if (!focusedId.empty()) (void)FindNodePath(snapshot->root, focusedId, path);
        return path;
    }

    void CollectFocusableDescendants(
        const WidgetNode& node,
        const std::wstring_view inheritedScope,
        const std::wstring_view matchingScope,
        std::vector<std::wstring_view>& ids) const {
        if (!IsResponsiveVisible(node)) return;
        const std::wstring_view inputScope = !node.inputScopeId.empty()
            ? std::wstring_view(node.inputScopeId)
            : inheritedScope.empty() ? std::wstring_view(node.id) : inheritedScope;
        if ((node.kind == L"button" || node.kind == L"slider" ||
             node.kind == L"actionSurface") &&
            inputScope == matchingScope) {
            ids.emplace_back(node.id);
        }
        for (const auto& child : node.children) {
            CollectFocusableDescendants(child, inputScope, matchingScope, ids);
        }
    }

    static std::wstring_view ScopeForPath(
        const std::vector<const WidgetNode*>& path,
        const std::size_t count) {
        std::wstring_view scope;
        for (std::size_t index = 0; index < std::min(count, path.size()); ++index) {
            const auto& node = *path[index];
            if (!node.inputScopeId.empty()) scope = node.inputScopeId;
            else if (scope.empty()) scope = node.id;
        }
        return scope;
    }

    struct AxisFocusRevealRange final {
        float minimumDelta{};
        float maximumDelta{};
        bool targetFits{};
        bool valid{};
    };

    [[nodiscard]] static AxisFocusRevealRange ResolveAxisFocusRevealRange(
        const float targetStart,
        const float targetExtent,
        const float viewportStart,
        const float viewportExtent) noexcept {
        if (!std::isfinite(targetStart) || !std::isfinite(targetExtent) ||
            !std::isfinite(viewportStart) || !std::isfinite(viewportExtent) ||
            targetExtent <= kRevealEpsilon || viewportExtent <= kRevealEpsilon) {
            return {};
        }
        const bool targetFits = targetExtent <= viewportExtent + kRevealEpsilon;
        // Delta is the screen-space translation applied to the target. A
        // fit-sized target must be inside the viewport; an oversized target
        // must contain the viewport. The latter exposes one complete,
        // stable viewport-sized portion instead of alternating target edges.
        const float minimumDelta = targetFits
            ? viewportStart - targetStart
            : viewportStart + viewportExtent - targetStart - targetExtent;
        const float maximumDelta = targetFits
            ? viewportStart + viewportExtent - targetStart - targetExtent
            : viewportStart - targetStart;
        return {minimumDelta, maximumDelta, targetFits, true};
    }

    [[nodiscard]] static bool AxisFocusIsRevealed(
        const AxisFocusRevealRange& range,
        const float delta = 0.0F,
        const float tolerance = kRevealEpsilon) noexcept {
        return range.valid && delta >= range.minimumDelta - tolerance &&
            delta <= range.maximumDelta + tolerance;
    }

    [[nodiscard]] static float ResolveAxisFocusOffset(
        const AxisFocusRevealRange& range,
        const float currentOffset,
        const float maximumOffset) noexcept {
        if (!range.valid || AxisFocusIsRevealed(range)) return currentOffset;
        const float targetDelta = std::clamp(
            0.0F, range.minimumDelta, range.maximumDelta);
        return std::clamp(
            currentOffset - targetDelta, 0.0F, maximumOffset);
    }

    [[nodiscard]] std::pair<bool, bool> FocusedBoundaryOf(
        const WidgetNode& scroll) const {
        const auto focusPath = FocusPath();
        if (focusPath.empty()) return {};
        const auto focusedScope = ScopeForPath(focusPath, focusPath.size());
        std::vector<const WidgetNode*> scrollPath;
        if (!FindNodePath(snapshot->root, scroll.id, scrollPath) ||
            scrollPath.empty()) return {};
        const auto inheritedScope = ScopeForPath(
            scrollPath, scrollPath.size() - 1U);
        std::vector<std::wstring_view> focusableIds;
        CollectFocusableDescendants(
            scroll, inheritedScope, focusedScope, focusableIds);
        if (focusableIds.empty()) return {};
        return {
            focusableIds.front() == focusedId,
            focusableIds.back() == focusedId,
        };
    }

    [[nodiscard]] bool ApplyFocusedDescendantFollow(
        const bool usePresentationGeometry = false) {
        if (focusedId.empty()) return false;
        const auto* focusBox = layout.Find(NarrowStableId(focusedId));
        if (!focusBox) return false;
        const auto presentedFocus = presentation.find(NarrowStableId(focusedId));
        if (usePresentationGeometry && presentedFocus == presentation.end()) return false;
        const auto path = FocusPath();
        if (path.empty()) return false;
        bool changed = false;
        // Inner offsets change the target geometry seen by outer viewports.
        // Visit the exact ancestor path from inner to outer; BuildLayout then
        // repeats this bounded pass against freshly measured geometry.
        for (auto item = path.rbegin(); item != path.rend(); ++item) {
            const auto& scroll = **item;
            if (scroll.kind != L"scroll") continue;
            const auto* scrollBox = layout.Find(NarrowStableId(scroll.id));
            if (!scrollBox || scrollBox->scrollAxis == declarative::ScrollAxis::None) continue;
            const auto presentedScroll = presentation.find(NarrowStableId(scroll.id));
            if (usePresentationGeometry && presentedScroll == presentation.end()) continue;
            auto desired = scrollBox->scrollOffset;
            const auto viewportBox = usePresentationGeometry
                ? presentedScroll->second.contentBox
                : scrollBox->contentBox;
            const auto targetRect = usePresentationGeometry
                ? presentedFocus->second.borderBox
                : focusBox->borderBox;
            const auto [atLeadingBoundary, atTrailingBoundary] =
                FocusedBoundaryOf(scroll);
            RecordFocusFollowNodeMetadata(
                scroll.id,
                scrollBox->scrollAxis,
                scrollBox->scrollOffset,
                scrollBox->maximumScrollOffset,
                targetRect,
                viewportBox,
                atLeadingBoundary,
                atTrailingBoundary);
            const auto axisRange = scrollBox->scrollAxis ==
                    declarative::ScrollAxis::Vertical
                ? ResolveAxisFocusRevealRange(
                    targetRect.y, targetRect.height,
                    viewportBox.y, viewportBox.height)
                : ResolveAxisFocusRevealRange(
                    targetRect.x, targetRect.width,
                    viewportBox.x, viewportBox.width);
            const auto focusVisibleAt = [&](const float candidateOffset) {
                const float delta = scrollBox->scrollOffset - candidateOffset;
                return AxisFocusIsRevealed(axisRange, delta);
            };
            if (atLeadingBoundary && focusVisibleAt(0.0F)) {
                // A just-enough reveal would stop as soon as the first control
                // became visible and could leave non-focusable heading/content
                // above it clipped. The first focus target represents the true
                // leading edge of a controller-only scroll surface.
                desired = 0.0F;
            } else if (atTrailingBoundary &&
                       focusVisibleAt(scrollBox->maximumScrollOffset)) {
                // Reaching the last control must expose trailing status/help
                // content and the complete rounded card boundary as well.
                desired = scrollBox->maximumScrollOffset;
            } else {
                desired = ResolveAxisFocusOffset(
                    axisRange,
                    scrollBox->scrollOffset,
                    scrollBox->maximumScrollOffset);
            }
            desired = std::clamp(desired, 0.0F, scrollBox->maximumScrollOffset);
            const auto key = ScrollStateKey(scroll.id);
            const auto existing = ScrollState().find(key);
            if (existing == ScrollState().end() ||
                std::abs(existing->second.offset - desired) > 0.01F) {
                StoreScrollOffset(key, desired);
                changed = true;
            }
        }
        return changed;
    }

    [[nodiscard]] static std::wstring BoundedDiagnosticIdentifier(
        const std::wstring_view value) {
        std::wstring result;
        result.reserve(std::min(
            value.size(), kMaximumFocusFollowDiagnosticIdentifierCharacters));
        for (const auto character : value) {
            if (result.size() >=
                kMaximumFocusFollowDiagnosticIdentifierCharacters) {
                break;
            }
            result.push_back(character >= L' ' && character != 0x7f
                ? character
                : L'_');
        }
        return result;
    }

    void RecordFocusFollowNodeMetadata(
        const std::wstring_view id,
        const declarative::ScrollAxis axis,
        const float offset,
        const float maximumOffset,
        const Rect targetBounds,
        const Rect viewportBounds,
        const bool leadingBoundary,
        const bool trailingBoundary) {
        const auto boundedId = BoundedDiagnosticIdentifier(id);
        auto found = std::find_if(
            focusFollowTrace.nodes.begin(), focusFollowTrace.nodes.end(),
            [&](const FocusFollowNodeSummary& node) {
                return node.id == boundedId;
            });
        if (found == focusFollowTrace.nodes.end()) {
            if (focusFollowTrace.nodes.size() >=
                kMaximumFocusFollowDiagnosticNodes) {
                return;
            }
            focusFollowTrace.nodes.push_back(FocusFollowNodeSummary{
                boundedId,
                axis,
                offset,
                offset,
                offset,
                maximumOffset,
                targetBounds,
                viewportBounds,
                leadingBoundary,
                trailingBoundary,
            });
            return;
        }
        found->axis = axis;
        found->maximumOffset = maximumOffset;
        found->target = targetBounds;
        found->viewport = viewportBounds;
        found->leadingBoundary = leadingBoundary;
        found->trailingBoundary = trailingBoundary;
    }

    [[nodiscard]] FocusFollowSample CaptureFocusFollowSample(
        const bool usePresentationGeometry) const {
        FocusFollowSample sample;
        const auto* focusBox = layout.Find(NarrowStableId(focusedId));
        const auto presentedFocus = presentation.find(NarrowStableId(focusedId));
        if (focusedId.empty()) return sample;
        if (!focusBox ||
            (usePresentationGeometry && presentedFocus == presentation.end())) {
            sample.complete = false;
            return sample;
        }
        const auto path = FocusPath();
        if (path.empty()) {
            sample.complete = false;
            return sample;
        }
        for (const auto* item : path) {
            if (sample.nodes.size() >= kMaximumFocusFollowDiagnosticNodes) break;
            if (!item || item->kind != L"scroll") continue;
            const auto* scrollBox = layout.Find(NarrowStableId(item->id));
            if (!scrollBox) {
                sample.complete = false;
                continue;
            }
            if (scrollBox->scrollAxis == declarative::ScrollAxis::None) continue;
            const auto presentedScroll = presentation.find(NarrowStableId(item->id));
            if (usePresentationGeometry && presentedScroll == presentation.end()) {
                sample.complete = false;
                continue;
            }

            const auto key = ScrollStateKey(item->id);
            const auto retained = ScrollState().find(key);
            const float offset = retained != ScrollState().end()
                ? retained->second.offset
                : scrollBox->scrollOffset;
            const auto viewportBox = usePresentationGeometry
                ? presentedScroll->second.contentBox
                : scrollBox->contentBox;
            auto targetRect = usePresentationGeometry
                ? presentedFocus->second.borderBox
                : focusBox->borderBox;
            const float offsetDelta = scrollBox->scrollOffset - offset;
            if (scrollBox->scrollAxis == declarative::ScrollAxis::Vertical)
                targetRect.y += offsetDelta;
            else
                targetRect.x += offsetDelta;
            const auto axisRange = scrollBox->scrollAxis ==
                    declarative::ScrollAxis::Vertical
                ? ResolveAxisFocusRevealRange(
                    targetRect.y, targetRect.height,
                    viewportBox.y, viewportBox.height)
                : ResolveAxisFocusRevealRange(
                    targetRect.x, targetRect.width,
                    viewportBox.x, viewportBox.width);
            const bool visible = AxisFocusIsRevealed(axisRange);
            sample.nodes.push_back(FocusFollowNodeObservation{
                BoundedDiagnosticIdentifier(item->id),
                scrollBox->scrollAxis,
                offset,
                scrollBox->maximumScrollOffset,
                targetRect,
                viewportBox,
                visible,
                false,
                false,
            });
            sample.allTargetsVisible = sample.allTargetsVisible && visible;
        }
        return sample;
    }

    [[nodiscard]] static bool SameFocusFollowOffsets(
        const FocusFollowSample& left,
        const FocusFollowSample& right) noexcept {
        if (!left.complete || !right.complete ||
            left.nodes.size() != right.nodes.size()) return false;
        for (std::size_t index = 0; index < left.nodes.size(); ++index) {
            if (left.nodes[index].id != right.nodes[index].id ||
                left.nodes[index].axis != right.nodes[index].axis ||
                std::abs(left.nodes[index].offset - right.nodes[index].offset) >
                    kRevealEpsilon) {
                return false;
            }
        }
        return true;
    }

    [[nodiscard]] bool FocusedTargetVisibleInPresentation() const {
        if (focusedId.empty()) return true;
        const auto sample = CaptureFocusFollowSample(true);
        return sample.complete && sample.allTargetsVisible;
    }

    [[nodiscard]] static std::vector<float> FocusFollowOffsets(
        const FocusFollowSample& sample) {
        std::vector<float> result;
        result.reserve(sample.nodes.size());
        for (const auto& node : sample.nodes) result.push_back(node.offset);
        return result;
    }

    void RetainFocusFollowSample(
        const FocusFollowSample& sample,
        const bool presentationGeometry) {
        const auto offsets = FocusFollowOffsets(sample);
        if (focusFollowTrace.firstSamples.size() < kFocusFollowRetainedSamples)
            focusFollowTrace.firstSamples.push_back(offsets);
        focusFollowTrace.lastSamples.push_back(offsets);
        if (focusFollowTrace.lastSamples.size() > kFocusFollowRetainedSamples)
            focusFollowTrace.lastSamples.erase(focusFollowTrace.lastSamples.begin());
        auto& seenSamples = presentationGeometry
            ? focusFollowTrace.presentationSeenSamples
            : focusFollowTrace.layoutSeenSamples;
        constexpr auto maximumSeenSamples = kMaximumFocusFollowPasses + 1U;
        if (seenSamples.size() < maximumSeenSamples)
            seenSamples.push_back(sample);
    }

    void UpdateFocusFollowNodes(
        const FocusFollowSample& sample,
        const bool initial) {
        for (const auto& observed : sample.nodes) {
            auto found = std::find_if(
                focusFollowTrace.nodes.begin(), focusFollowTrace.nodes.end(),
                [&](const FocusFollowNodeSummary& node) {
                    return node.id == observed.id;
                });
            if (found == focusFollowTrace.nodes.end()) {
                if (focusFollowTrace.nodes.size() >=
                    kMaximumFocusFollowDiagnosticNodes) {
                    continue;
                }
                focusFollowTrace.nodes.push_back(FocusFollowNodeSummary{
                    observed.id,
                    observed.axis,
                    observed.offset,
                    observed.offset,
                    observed.offset,
                    observed.maximumOffset,
                    observed.target,
                    observed.viewport,
                    observed.leadingBoundary,
                    observed.trailingBoundary,
                });
                continue;
            }
            if (initial) found->initialOffset = observed.offset;
            found->finalOffset = observed.offset;
            found->peakOffset = std::max(found->peakOffset, observed.offset);
            found->maximumOffset = observed.maximumOffset;
            found->target = observed.target;
            found->viewport = observed.viewport;
        }
    }

    [[nodiscard]] FocusFollowAttemptResult RecordFocusFollowPass(
        const FocusFollowSample& before,
        const FocusFollowSample& after,
        const bool changed,
        const bool presentationGeometry) {
        if (before.nodes.empty() && after.nodes.empty())
            return FocusFollowAttemptResult{changed, false, false};
        auto& seenSamples = presentationGeometry
            ? focusFollowTrace.presentationSeenSamples
            : focusFollowTrace.layoutSeenSamples;
        if (seenSamples.empty())
            RetainFocusFollowSample(before, presentationGeometry);
        if (focusFollowTrace.passCount == 0) {
            UpdateFocusFollowNodes(before, true);
        }
        ++focusFollowTrace.passCount;
        const bool repeatedWithoutProgress =
            SameFocusFollowOffsets(before, after);
        if (changed && repeatedWithoutProgress) focusFollowTrace.noProgress = true;
        bool seenBefore{};
        if (changed && !repeatedWithoutProgress) {
            seenBefore = std::any_of(
                seenSamples.begin(),
                seenSamples.end(),
                [&](const FocusFollowSample& prior) {
                    return SameFocusFollowOffsets(prior, after);
                });
            if (seenBefore) focusFollowTrace.cycle = true;
        } else if (!changed) {
            if (after.allTargetsVisible) focusFollowTrace.converged = true;
            else focusFollowTrace.noProgress = true;
        }
        UpdateFocusFollowNodes(after, false);
        RetainFocusFollowSample(after, presentationGeometry);
        return FocusFollowAttemptResult{
            changed,
            changed && !repeatedWithoutProgress,
            changed && seenBefore,
        };
    }

    [[nodiscard]] FocusFollowAttemptResult FollowFocusedDescendant(
        const bool usePresentationGeometry) {
        const auto before = CaptureFocusFollowSample(usePresentationGeometry);
        const bool changed = ApplyFocusedDescendantFollow(usePresentationGeometry);
        const auto after = CaptureFocusFollowSample(usePresentationGeometry);
        auto attempt = RecordFocusFollowPass(
            before, after, changed, usePresentationGeometry);
        if (attempt.changed &&
            (!attempt.meaningfulOffsetChange || attempt.repeatedOffsetState)) {
            // Apply tentatively writes the exact retained vector so cycle
            // detection can compare it. Restore the layout-owning vector
            // before terminating; callers must not relayout a repeated state.
            for (const auto& node : before.nodes)
                StoreScrollOffset(ScrollStateKey(node.id), node.offset);
            attempt.changed = false;
            attempt.meaningfulOffsetChange = false;
        }
        return attempt;
    }

    void AddFocusFollowElapsed(
        const FocusFollowPhase phase,
        const std::uint64_t microseconds) noexcept {
        if (phase == FocusFollowPhase::Layout)
            focusFollowTrace.layoutMicroseconds += microseconds;
        else
            focusFollowTrace.presentationMicroseconds += microseconds;
    }

    [[nodiscard]] static std::wstring DiagnosticRectText(const Rect value) {
        return std::to_wstring(value.x) + L"," + std::to_wstring(value.y) +
            L"," + std::to_wstring(value.width) + L"," +
            std::to_wstring(value.height);
    }

    [[nodiscard]] static std::wstring FocusFollowOffsetsText(
        const std::vector<std::vector<float>>& samples) {
        std::wstring result{L"["};
        for (std::size_t sampleIndex = 0;
             sampleIndex < samples.size(); ++sampleIndex) {
            if (sampleIndex != 0) result += L";";
            result += L"(";
            for (std::size_t offsetIndex = 0;
                 offsetIndex < samples[sampleIndex].size(); ++offsetIndex) {
                if (offsetIndex != 0) result += L",";
                result += std::to_wstring(samples[sampleIndex][offsetIndex]);
            }
            result += L")";
        }
        result += L"]";
        return result;
    }

    [[nodiscard]] std::wstring BuildFocusFollowSummary() const {
        const auto aggregateMicroseconds =
            focusFollowTrace.layoutMicroseconds +
            focusFollowTrace.presentationMicroseconds;
        const bool shouldEmit =
            aggregateMicroseconds > kSlowFocusFollowMicroseconds ||
            focusFollowTrace.passCount > 2U ||
            focusFollowTrace.noProgress ||
            focusFollowTrace.cycle ||
            focusFollowTrace.boundHit;
        if (!shouldEmit || focusFollowTrace.nodes.empty()) return {};

        const std::wstring_view disposition = focusFollowTrace.boundHit
            ? L"bound-hit"
            : focusFollowTrace.cycle
                ? L"cycle"
                : focusFollowTrace.noProgress
                    ? L"no-progress"
                    : L"converged";
        std::wstring summary =
            L"focus-follow instance=" + BoundedDiagnosticIdentifier(
                snapshot ? std::wstring_view{snapshot->instanceId}
                         : std::wstring_view{}) +
            L" sequence=" + std::to_wstring(snapshot ? snapshot->sequence : 0) +
            L" focus=" + BoundedDiagnosticIdentifier(focusedId) +
            L" passes=" + std::to_wstring(focusFollowTrace.passCount) +
            L" layout-us=" +
                std::to_wstring(focusFollowTrace.layoutMicroseconds) +
            L" presentation-us=" +
                std::to_wstring(focusFollowTrace.presentationMicroseconds) +
            L" aggregate-us=" + std::to_wstring(aggregateMicroseconds) +
            L" disposition=" + std::wstring{disposition} +
            L" scrolls=[";
        for (std::size_t index = 0;
             index < focusFollowTrace.nodes.size(); ++index) {
            if (index != 0) summary += L";";
            const auto& node = focusFollowTrace.nodes[index];
            const std::wstring_view axis =
                node.axis == declarative::ScrollAxis::Vertical
                ? L"vertical"
                : node.axis == declarative::ScrollAxis::Horizontal
                    ? L"horizontal"
                    : L"none";
            summary +=
                L"{id=" + node.id +
                L",axis=" + std::wstring{axis} +
                L",initial=" + std::to_wstring(node.initialOffset) +
                L",final=" + std::to_wstring(node.finalOffset) +
                L",peak=" + std::to_wstring(node.peakOffset) +
                L",maximum=" + std::to_wstring(node.maximumOffset) +
                L",target=" + DiagnosticRectText(node.target) +
                L",viewport=" + DiagnosticRectText(node.viewport) +
                L",leading=" + (node.leadingBoundary ? L"true" : L"false") +
                L",trailing=" + (node.trailingBoundary ? L"true" : L"false") +
                L"}";
        }
        summary +=
            L"] first-offsets=" +
                FocusFollowOffsetsText(focusFollowTrace.firstSamples) +
            L" last-offsets=" +
                FocusFollowOffsetsText(focusFollowTrace.lastSamples);
        if (summary.size() > kMaximumFocusFollowSummaryCharacters) {
            constexpr std::wstring_view suffix{L"...<bounded>"};
            summary.resize(
                kMaximumFocusFollowSummaryCharacters - suffix.size());
            summary += suffix;
        }
        return summary;
    }

    [[nodiscard]] bool AxisCanReveal(
        const std::vector<const WidgetNode*>& path,
        const std::size_t clipPathIndex,
        const declarative::ScrollAxis axis,
        const float targetStart,
        const float targetSize,
        const float clipStart,
        const float clipSize) const {
        const auto rasterEdgeTolerance =
            std::isfinite(options.pixelScale) && options.pixelScale > 0.0F
            ? kRevealRasterEdgePixelTolerance / options.pixelScale
            : 0.0F;
        const auto revealRange = ResolveAxisFocusRevealRange(
            targetStart, targetSize, clipStart, clipSize);
        if (AxisFocusIsRevealed(revealRange, 0.0F, rasterEdgeTolerance)) {
            return true;
        }

        float minimumDelta{};
        float maximumDelta{};
        bool hasMatchingScroll{};
        for (std::size_t index = clipPathIndex; index + 1 < path.size(); ++index) {
            const auto& candidate = *path[index];
            if (candidate.kind != L"scroll") continue;
            const auto* box = layout.Find(NarrowStableId(candidate.id));
            if (!box || box->scrollAxis != axis || box->maximumScrollOffset <= kRevealEpsilon)
                continue;
            // Changing offset from o to n moves descendants by o - n.
            minimumDelta += box->scrollOffset - box->maximumScrollOffset;
            maximumDelta += box->scrollOffset;
            hasMatchingScroll = true;
        }
        if (!hasMatchingScroll) return false;

        // Layout scroll limits and raster-snapped presentation edges can differ
        // by one pixel. Use the same tolerance before and after focus-follow.
        return revealRange.valid &&
            std::max(minimumDelta, revealRange.minimumDelta) <=
                std::min(maximumDelta, revealRange.maximumDelta) +
                    std::max(kRevealEpsilon, rasterEdgeTolerance);
    }

    [[nodiscard]] bool CanRevealNode(const std::wstring_view nodeId) const {
        const auto presentedTarget = presentation.find(NarrowStableId(nodeId));
        if (presentedTarget == presentation.end()) return false;
        std::vector<const WidgetNode*> path;
        if (!FindNodePath(snapshot->root, nodeId, path) || path.size() < 2) return false;
        const auto& targetRect = presentedTarget->second.borderBox;

        const auto canSatisfyClip = [&](const Rect& clip, const std::size_t index) {
            return AxisCanReveal(path, index, declarative::ScrollAxis::Horizontal,
                                 targetRect.x, targetRect.width, clip.x, clip.width) &&
                AxisCanReveal(path, index, declarative::ScrollAxis::Vertical,
                              targetRect.y, targetRect.height, clip.y, clip.height);
        };

        // The host viewport is an implicit non-moving clip around every tree.
        if (!canSatisfyClip(viewport, 0)) return false;
        bool hasScrollAncestor{};
        for (std::size_t index = 0; index + 1 < path.size(); ++index) {
            const auto& ancestor = *path[index];
            const auto preparedAncestor = prepared.find(NarrowStableId(ancestor.id));
            const auto presentedAncestor = presentation.find(NarrowStableId(ancestor.id));
            if (preparedAncestor == prepared.end() ||
                presentedAncestor == presentation.end()) return false;
            const auto clips = ClipsDescendants(
                ancestor, preparedAncestor->second.baseStyle);
            if (!clips) continue;
            hasScrollAncestor |= ancestor.kind == L"scroll";
            if (!canSatisfyClip(presentedAncestor->second.contentBox, index)) return false;
        }
        return hasScrollAncestor;
    }

    [[nodiscard]] std::optional<Rect> EffectiveFocusVisibilityClip(
        const std::wstring_view nodeId) const {
        const auto found = presentation.find(NarrowStableId(nodeId));
        if (found == presentation.end()) return std::nullopt;
        // The presentation pass has already accumulated the fixed host
        // viewport and every translated clipping ancestor. Ordinary
        // visible-overflow ancestors intentionally do not constrain outlines.
        return found->second.ancestorClip;
    }

    void SynchronizeScrollState() {
        std::set<std::wstring, std::less<>> activeKeys;
        VisitScrollNodes(snapshot->root, [&](const WidgetNode& scroll) {
            const auto key = ScrollStateKey(scroll.id);
            activeKeys.insert(key);
            if (const auto* box = layout.Find(NarrowStableId(scroll.id))) {
                StoreScrollOffset(key, box->scrollOffset);
                result.scrollOffsets[scroll.id] = box->scrollOffset;
                auto& state = ScrollState()[key];
                state.anchorKey = scroll.collectionAnchorKey;
                state.hasAnchorPosition = false;
                if (!scroll.collectionAnchorKey.empty()) {
                    const auto* item = FindCollectionItem(
                        scroll, scroll.collectionAnchorKey);
                    const auto* itemBox = item
                        ? layout.Find(NarrowStableId(item->id)) : nullptr;
                    if (itemBox) {
                        state.anchorPosition =
                            box->scrollAxis == declarative::ScrollAxis::Vertical
                            ? itemBox->borderBox.y - box->contentBox.y
                            : itemBox->borderBox.x - box->contentBox.x;
                        state.hasAnchorPosition = true;
                    }
                }
            }
        });
        std::wstring prefix(snapshot->instanceId);
        prefix.push_back(L'\x1f');
        prefix.append(snapshot->activeInputScopeId);
        prefix.push_back(L'\x1f');
        std::erase_if(ScrollState(), [&](const auto& entry) {
            return entry.first.starts_with(prefix) && !activeKeys.contains(entry.first);
        });
        if (ScrollState().size() <= kMaximumScrollStateEntries) return;

        // Evict only the overflow, oldest inactive entries first. The active
        // snapshot (at most the protocol's bounded node count) survives a cap
        // transition instead of losing its scroll position with the old map
        // clear behavior.
        std::vector<std::pair<std::uint64_t, std::wstring>> inactive;
        inactive.reserve(ScrollState().size() - activeKeys.size());
        for (const auto& [key, state] : ScrollState()) {
            if (!activeKeys.contains(key)) inactive.emplace_back(state.lastAccess, key);
        }
        std::ranges::sort(inactive);
        const auto overflow = ScrollState().size() - kMaximumScrollStateEntries;
        const auto count = std::min(overflow, inactive.size());
        for (std::size_t index = 0; index < count; ++index)
            ScrollState().erase(inactive[index].second);
    }

    [[nodiscard]] Size MeasureText(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const declarative::MeasureConstraints& constraints) {
        PreparationTimer timer{textNanoseconds};
        const auto maximumWidth = std::max(1.0F, constraints.maximumWidth);
        const auto lineHeight = style.fontSizePx() * style.lineHeight();
        const auto maximumHeight = std::max(
            lineHeight,
            std::min(constraints.maximumHeight, lineHeight * static_cast<float>(style.maxLines())));
        const auto availableHeight = std::isfinite(constraints.maximumHeight) &&
                constraints.maximumHeight > 0.0F
            ? std::max(maximumHeight, constraints.maximumHeight)
            : maximumHeight + style.fontSizePx();
        const auto plan = owner->textLayoutCache_.Get(
            owner->writeFactory_, node.text, style, maximumWidth,
            availableHeight);
        if (!plan.IsValid()) {
            textMeasurements.insert_or_assign(node.id, TextMeasurementProof{
                maximumWidth, availableHeight, 0.0F, 0.0F,
                maximumWidth, availableHeight, 0.0F, 0.0F, 0.0F, 0U, false});
            const auto estimated = std::min(
                maximumWidth,
                static_cast<float>(node.text.size()) * style.fontSizePx() * 0.56F);
            const auto lines = std::max(1.0F,
                std::ceil(static_cast<float>(node.text.size()) * style.fontSizePx() * 0.56F /
                    maximumWidth));
            return {estimated, std::min(maximumHeight, lines * lineHeight)};
        }
        // Intrinsic text becomes component geometry and is subsequently
        // snapped to the effective native-pixel grid. Round outward here so
        // that snap-to-nearest cannot make a later paint box fractionally
        // narrower or shorter than the DirectWrite plan that established its
        // natural size (which can otherwise reflow a tight one-line label).
        const auto pixelScale = std::isfinite(options.pixelScale) &&
                options.pixelScale > 0.0F
            ? options.pixelScale
            : 1.0F;
        const auto rasterCeiling = [pixelScale](const float value) {
            return std::ceil(std::max(0.0F, value) * pixelScale) / pixelScale;
        };
        const Size measured{
            std::min(maximumWidth, rasterCeiling(plan.measuredWidth)),
            std::min(availableHeight, rasterCeiling(plan.measuredHeight)),
        };
        DWRITE_TEXT_METRICS metrics{};
        const bool hasMetrics = SUCCEEDED(plan.layout->GetMetrics(&metrics));
        textMeasurements.insert_or_assign(node.id, TextMeasurementProof{
            maximumWidth,
            availableHeight,
            measured.width,
            measured.height,
            plan.layoutWidth,
            plan.layoutHeight,
            plan.inkInsetTop,
            plan.inkInsetBottom,
            plan.baseline,
            hasMetrics ? metrics.lineCount : 0U,
            hasMetrics,
        });
        auto& queries = textMeasurementQueries[node.id];
        const auto& proof = textMeasurements.at(node.id);
        const auto existing = std::find_if(queries.begin(), queries.end(), [&](const auto& prior) {
            return prior.maximumWidth == proof.maximumWidth && prior.maximumHeight == proof.maximumHeight;
        });
        if (existing == queries.end()) queries.push_back(proof);
        else *existing = proof;
        return measured;
    }

    [[nodiscard]] Size MeasureLeaf(
        const LayoutElement& element,
        const declarative::MeasureConstraints& constraints) {
        const auto found = prepared.find(element.id);
        if (found == prepared.end()) return {};
        const auto& node = *found->second.node;
        const auto& style = found->second.baseStyle;
        if (node.kind == L"text")
            return MeasureText(node, style, constraints);
        if (node.kind == L"button") {
            const bool hasLeading = !node.imageSource.empty() ||
                !node.artworkHandle.empty() || !node.glyph.empty() ||
                node.packageIcon.has_value();
            const bool hasText = !node.text.empty();
            const bool reserveStateCue = ReservesTrailingButtonStateCue(node);
            const auto lineHeight = style.fontSizePx() * style.lineHeight();
            const auto maximumLeadingSize =
                node.imageSource.empty() && node.artworkHandle.empty() ? 32.0F : 44.0F;
            const auto alignment = ResolveButtonContentAlignment(node, style, false, false);
            const auto& padding = style.paddingPx();
            const auto verticalPadding = padding.top + padding.bottom;
            const auto preferredOuterHeight = std::max({
                kMinimumControlSize,
                style.minHeightPx().value_or(0.0F),
                style.heightPx().value_or(0.0F),
            });
            const auto minimumContentHeight = std::max(
                0.0F, preferredOuterHeight - verticalPadding);
            const auto provisionalHeight = std::max(
                1.0F, std::min(constraints.maximumHeight,
                    std::max(lineHeight, minimumContentHeight)));
            const auto measureAtHeight = [&](const float contentHeight) {
                const auto leadingSize = hasLeading
                    ? std::min(maximumLeadingSize, contentHeight)
                    : 0.0F;
                const Rect available{
                    0.0F, 0.0F, constraints.maximumWidth, contentHeight};
                const auto budget = DeclarativeRenderer::ComputeButtonContentPlacement(
                    available, leadingSize, std::numeric_limits<float>::max(), hasLeading,
                    hasText, reserveStateCue, alignment);
                const auto text = hasText
                    ? MeasureText(node, style, {budget.text.width, constraints.maximumHeight})
                    : Size{};
                return std::pair{leadingSize, text};
            };
            const auto [firstLeadingSize, firstText] = measureAtHeight(provisionalHeight);
            const auto resolvedContentHeight = std::max({
                provisionalHeight, firstLeadingSize, firstText.height});
            const auto [leadingSize, text] = measureAtHeight(resolvedContentHeight);
            const Rect available{
                0.0F, 0.0F, constraints.maximumWidth,
                std::max({resolvedContentHeight, leadingSize, text.height})};
            const auto tokens = ResolveButtonContentTokens(
                available, leadingSize, hasLeading, hasText, reserveStateCue);
            const auto stateWidth = reserveStateCue
                ? tokens.stateCueLane * (alignment == NativeTextAlign::Center ? 2.0F : 1.0F)
                : 0.0F;
            return {
                std::min(constraints.maximumWidth,
                    text.width + tokens.leadingSize + tokens.leadingGap + stateWidth),
                std::max({minimumContentHeight, tokens.leadingSize, text.height}),
            };
        }
        if (node.kind == L"mediaViewport" && snapshot->embeddedMediaSession &&
            node.mediaSessionId == snapshot->embeddedMediaSession->id) {
            return {
                static_cast<float>(snapshot->embeddedMediaSession->surface.preferredWidth.value_or(0.0)),
                static_cast<float>(snapshot->embeddedMediaSession->surface.preferredHeight.value_or(0.0)),
            };
        }
        if (node.kind == L"windowPreview") return {240.0F, static_cast<float>(240.0 / node.previewAspectRatio)};
        if (node.kind == L"image") return {120.0F, 120.0F};
        if (node.kind == L"icon") return {24.0F, 24.0F};
        if (node.kind == L"loadingIndicator") {
            if (node.indicatorSize == L"compact") return {16.0F, 16.0F};
            if (node.indicatorSize == L"large") return {32.0F, 32.0F};
            return {24.0F, 24.0F};
        }
        if (node.kind == L"progress") return {160.0F, 8.0F};
        if (node.kind == L"slider") return {240.0F, kMinimumControlSize};
        return {};
    }

    [[nodiscard]] declarative::LayoutResult ComputeTimedLayout(
        const LayoutElement& root, const Rect bounds,
        const declarative::IntrinsicMeasureCallback& measure, const LayoutOptions& settings) {
        PreparationTimer timer{layoutNanoseconds};
        return declarative::ComputeLayout(root, bounds, measure, settings);
    }

    void BuildLayout(
        const bool followStaticFocus = true,
        const bool fillAutoRoot = true,
        const CollectionAnchorPolicy collectionAnchorPolicy =
            CollectionAnchorPolicy::Reconcile) {
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
        ++result.fullLayoutBuildCount;
#endif
        // First pass gives percentage/em adaptation a deterministic parent estimate.
        prepared.clear();
        textMeasurements.clear();
        textMeasurementQueries.clear();
        layout = {};
        auto root = PrepareNode(
            snapshot->root,
            {},
            viewport.width,
            viewport.height,
            options.rootFontSizePx,
            options.surfaceBackground);
        LayoutOptions layoutOptions;
        layoutOptions.pixelScale = options.pixelScale;
        layoutOptions.fillAutoRoot = fillAutoRoot;
        if (measurementOnly)
            layoutOptions.fillAutoRootWidth = !intrinsicRootWidth;
        layoutOptions.intrinsicRootHeight = measurementOnly;
        layoutOptions.responsiveViewport = options.responsiveViewport.value_or(
            Size{viewport.width, viewport.height});
        layout = ComputeTimedLayout(
            root,
            viewport,
            [this](const LayoutElement& element, const declarative::MeasureConstraints& constraints) {
                return MeasureLeaf(element, constraints);
            },
            layoutOptions);
        // One correction pass resolves parent-relative values against measured boxes.
        prepared.clear();
        textMeasurements.clear();
        textMeasurementQueries.clear();
        auto correctedRoot = PrepareNode(
            snapshot->root,
            {},
            viewport.width,
            viewport.height,
            options.rootFontSizePx,
            options.surfaceBackground);
        layout = ComputeTimedLayout(
            correctedRoot,
            viewport,
            [this](const LayoutElement& element, const declarative::MeasureConstraints& constraints) {
                return MeasureLeaf(element, constraints);
            },
            layoutOptions);
        if (measurementOnly) {
            for (const auto& issue : layout.issues) {
                Add(WidenStableId(issue.elementId),
                    std::wstring{issue.code.begin(), issue.code.end()},
                    std::wstring{issue.message.begin(), issue.message.end()},
                    issue.severity == declarative::LayoutIssueSeverity::Error
                        ? RenderDiagnosticSeverity::Error
                        : RenderDiagnosticSeverity::Warning);
            }
            return;
        }
        if (collectionAnchorPolicy != CollectionAnchorPolicy::PreserveFocusFollowOffsets &&
            ReconcileCollectionAnchors(collectionAnchorPolicy == CollectionAnchorPolicy::ReconcileContentChanges)) {
            prepared.clear();
            textMeasurements.clear();
            textMeasurementQueries.clear();
            auto anchoredRoot = PrepareNode(
                snapshot->root, {}, viewport.width, viewport.height,
                options.rootFontSizePx, options.surfaceBackground);
            layout = ComputeTimedLayout(
                anchoredRoot, viewport,
                [this](const LayoutElement& element,
                       const declarative::MeasureConstraints& constraints) {
                    return MeasureLeaf(element, constraints);
                }, layoutOptions);
        }
        // Focus-follow runs after percentage/em correction. Nested scrollers
        // require a fixed point: revealing inside the innermost viewport moves
        // the target geometry observed by each outer viewport. The wire tree
        // depth is bounded to 32, so this loop has a matching hard ceiling and
        // performs no relayout once offsets are stable.
        if (followStaticFocus) {
            const auto followStarted = std::chrono::steady_clock::now();
            std::size_t followAttempts{};
            bool lastFollowChanged{};
            for (std::size_t pass = 0; pass < kMaximumFocusFollowPasses; ++pass) {
                ++followAttempts;
                const auto follow = FollowFocusedDescendant(false);
                lastFollowChanged = follow.changed;
                if (!lastFollowChanged || !follow.meaningfulOffsetChange ||
                    follow.repeatedOffsetState) break;
                prepared.clear();
                textMeasurements.clear();
                textMeasurementQueries.clear();
                auto revealedRoot = PrepareNode(
                    snapshot->root,
                    {},
                    viewport.width,
                    viewport.height,
                    options.rootFontSizePx,
                    options.surfaceBackground);
                layout = ComputeTimedLayout(
                    revealedRoot,
                    viewport,
                    [this](const LayoutElement& element, const declarative::MeasureConstraints& constraints) {
                        return MeasureLeaf(element, constraints);
                    },
                    layoutOptions);
            }
            if (followAttempts == kMaximumFocusFollowPasses &&
                lastFollowChanged) {
                focusFollowTrace.boundHit = true;
            }
            AddFocusFollowElapsed(
                FocusFollowPhase::Layout,
                static_cast<std::uint64_t>(
                    std::chrono::duration_cast<std::chrono::microseconds>(
                        std::chrono::steady_clock::now() - followStarted).count()));
        }
        SynchronizeScrollState();
        for (const auto& issue : layout.issues) {
            Add(WidenStableId(issue.elementId),
                std::wstring{issue.code.begin(), issue.code.end()},
                std::wstring{issue.message.begin(), issue.message.end()},
                issue.severity == declarative::LayoutIssueSeverity::Error
                    ? RenderDiagnosticSeverity::Error
                    : RenderDiagnosticSeverity::Warning);
        }
    }

    void PrepareAgainstCurrentLayout() {
        // Paint needs current semantic pointers and resolved styles, not a new
        // Taffy input tree. Rebind pointers while reusing exact style contexts.
        prepared.clear();
        const float textScale = std::clamp(options.accessibility.textScale, 0.85F, 1.5F);
        const auto visit = [&](const auto& self, const WidgetNode& node,
                               const std::string& parent, const float font,
                               const std::optional<NativeColor>& background) -> void {
            const auto id = NarrowStableId(node.id);
            if (!layout.Find(id)) return;
            PrepareStyle(node, parent, viewport.width, viewport.height, font, background);
            const auto style = prepared.at(id).baseStyle;
            const auto surface = prepared.at(id).effectiveBackground;
            if (node.kind == L"focusPresentationSurface") {
                const auto selection = ResolveFocusPresentationSelection(snapshot->root, focusedId);
                const auto* fragment = selection.surface == &node ? selection.fragment
                    : !node.defaultFocusPresentation.empty() ? &node.defaultFocusPresentation.front() : nullptr;
                if (fragment) self(self, *fragment, id, style.fontSizePx() / textScale, surface);
            }
            for (std::size_t index = 0; index < node.children.size(); ++index) {
                const auto& child = node.children[index];
                if (!IsResponsiveVisible(child)) continue;
                if (IsPosterArtworkChild(node, index)) {
                    // This image paints in its parent's box and intentionally
                    // has no Taffy box. It still needs its own current style
                    // and semantic pointer for DrawImage on retained paints.
                    PrepareStyle(child, id, viewport.width, viewport.height,
                        style.fontSizePx() / textScale, surface);
                } else {
                    self(self, child, id, style.fontSizePx() / textScale, surface);
                }
            }
        };
        visit(visit, snapshot->root, {}, options.rootFontSizePx, options.surfaceBackground);
    }

    [[nodiscard]] bool BuildLocalLayout(
        const std::vector<std::wstring>& boundaryIds,
        const std::map<std::wstring, IncrementalNodeState, std::less<>>& nodes) {
        if (boundaryIds.empty()) return false;
        bool collectionChanged{};
        if (owner->incrementalLayoutCache_) {
            VisitScrollNodes(snapshot->root, [&](const WidgetNode& scroll) {
                if (scroll.collectionAnchorKey.empty()) return;
                CollectionDiagnosticObservation current;
                CollectCollectionItems(scroll, scroll, nullptr, current);
                const auto& prior = owner->incrementalLayoutCache_->collections;
                if (!prior.contains(scroll.id) || prior.at(scroll.id).itemKeys != current.itemKeys)
                    collectionChanged = true;
            });
        }
        if (collectionChanged) return false;
        if (std::find(boundaryIds.begin(), boundaryIds.end(), snapshot->root.id) !=
            boundaryIds.end()) {
            BuildLayout(
                !options.suppressFocusedDescendantFollow,
                true,
                options.suppressFocusedDescendantFollow
                    ? CollectionAnchorPolicy::ReconcileContentChanges
                    : CollectionAnchorPolicy::Reconcile);
            return true;
        }
        LayoutOptions layoutOptions;
        layoutOptions.pixelScale = options.pixelScale;
        layoutOptions.responsiveViewport = options.responsiveViewport.value_or(
            Size{viewport.width, viewport.height});
        for (const auto& boundaryId : boundaryIds) {
            std::vector<const WidgetNode*> path;
            if (!FindNodePath(snapshot->root, boundaryId, path) || path.empty())
                return false;
            const auto* boundary = path.back();
            const auto narrowBoundary = NarrowStableId(boundaryId);
            const auto* priorBox = layout.Find(narrowBoundary);
            if (!priorBox || priorBox->borderBox.width <= 0.0F ||
                priorBox->borderBox.height <= 0.0F) {
                return false;
            }
            const auto localViewport = priorBox->unroundedBorderBox.width > 0.0F &&
                    priorBox->unroundedBorderBox.height > 0.0F
                ? priorBox->unroundedBorderBox : priorBox->borderBox;
            const auto parent = nodes.find(boundaryId);
            const auto narrowParent = parent == nodes.end()
                ? std::string{}
                : NarrowStableId(parent->second.parentId);
            // Re-rooting a subtree does not make it a new style root. Resolve
            // the same inherited font/background context as a full-tree pass
            // before clearing preparation for the local layout.
            PrepareAgainstCurrentLayout();
            const auto preparedParent = prepared.find(narrowParent);
            const float textScale = std::isfinite(options.accessibility.textScale) &&
                    options.accessibility.textScale >= 0.85F &&
                    options.accessibility.textScale <= 1.5F
                ? options.accessibility.textScale : 1.0F;
            const float parentFontSize = preparedParent != prepared.end()
                ? preparedParent->second.baseStyle.fontSizePx() / textScale
                : options.rootFontSizePx;
            const auto parentBackground = preparedParent != prepared.end()
                ? preparedParent->second.effectiveBackground
                : options.surfaceBackground;
            auto recompute = [&]() {
                prepared.clear();
                auto root = PrepareNode(
                    *boundary,
                    narrowParent,
                    localViewport.width,
                    localViewport.height,
                    parentFontSize,
                    parentBackground);
                // The retained border box already excludes the parent's
                // allocation for this node's margins.
                root.margin = {};
                return ComputeTimedLayout(
                    root,
                    localViewport,
                    [this](const LayoutElement& element,
                           const declarative::MeasureConstraints& constraints) {
                        return MeasureLeaf(element, constraints);
                    },
                    layoutOptions);
            };
            auto replacement = recompute();
            if (!replacement.valid()) return false;
            for (auto& [id, box] : replacement.boxes)
                layout.boxes.insert_or_assign(std::move(id), std::move(box));
        }
        PrepareAgainstCurrentLayout();
        return true;
    }

    void DrawTextContent(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        Rect rect,
        const float opacity,
        const NativeTextVerticalAlignment verticalAlignment =
            NativeTextVerticalAlignment::Start) {
        if (!target || node.text.empty()) return;
        const auto plan = owner->textLayoutCache_.Get(
            owner->writeFactory_, node.text, style,
            std::max(1.0F, rect.width), std::max(1.0F, rect.height));
        if (!plan.IsValid()) {
            Add(node.id, L"text_layout", L"DirectWrite could not create a text layout.");
            return;
        }
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
        DWRITE_TEXT_METRICS textMetrics{};
        if (SUCCEEDED(plan.layout->GetMetrics(&textMetrics)))
            result.textLineCounts[node.id] = textMetrics.lineCount;
#endif
        const auto color = style.foreground().value_or(kDefaultText);
        auto brush = Brush(target, WithOpacity(color, opacity));
        if (brush) {
            target->DrawTextLayout(
                D2D1::Point2F(
                    rect.x,
                    plan.LayoutOriginY(rect.y, rect.height, verticalAlignment)),
                plan.layout.Get(),
                brush.Get(),
                D2D1_DRAW_TEXT_OPTIONS_NONE);
        }
    }

    void DrawSurface(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity) {
        if (!target || rect.width <= 0.0F || rect.height <= 0.0F) return;
        const auto radius = RadiusFor(style, rect);
        if (style.shadowColor() && style.shadowColor()->alpha > 0.0F) {
            const Rect shadowRect{
                rect.x + style.shadowOffsetXPx(),
                rect.y + style.shadowOffsetYPx(),
                rect.width,
                rect.height,
            };
            auto shadow = Brush(target, WithOpacity(*style.shadowColor(), opacity * 0.65F));
            if (shadow) {
                target->FillRoundedRectangle(
                    {D2DRect(shadowRect), radius, radius},
                    shadow.Get());
            }
            if (style.shadowBlurPx() > 0.0F)
                Add(node.id, L"shadow_blur_fallback", L"Blur is approximated by a bounded offset shadow.");
        }
        auto background = style.background();
        if (!background &&
            (node.kind == L"button" || node.kind == L"actionSurface"))
            background = kDefaultButton;
        if (background) {
            auto brush = Brush(target, WithOpacity(*background, opacity));
            if (brush)
                target->FillRoundedRectangle({D2DRect(rect), radius, radius}, brush.Get());
        }
        if (style.backgroundBlurPx() > 0.0F)
            Add(node.id, L"background_blur_fallback", L"Background blur is unavailable on the base render target; opaque fallback is used.");
        const auto& edges = style.borderEdges();
        const auto sameEdges = edges.top == edges.right &&
            edges.top == edges.bottom && edges.top == edges.left;
        if (sameEdges) {
            if (edges.top.widthPx > 0.0F && edges.top.color &&
                edges.top.color->alpha > 0.0F) {
                auto brush = Brush(target, WithOpacity(*edges.top.color, opacity));
                if (brush) {
                    // D2D strokes are centered on their geometry. Inset the
                    // path so the complete border stays inside the declared
                    // hit/paint box instead of being clipped at viewport edges.
                    const auto strokeRect = Inset(rect, edges.top.widthPx * 0.5F);
                    const auto strokeRadius = RadiusFor(style, strokeRect);
                    target->DrawRoundedRectangle(
                        {D2DRect(strokeRect), strokeRadius, strokeRadius},
                        brush.Get(), edges.top.widthPx);
                }
            }
            return;
        }

        const auto topWidth = std::clamp(edges.top.widthPx, 0.0F, rect.height);
        const auto rightWidth = std::clamp(edges.right.widthPx, 0.0F, rect.width);
        const auto bottomWidth = std::clamp(edges.bottom.widthPx, 0.0F, rect.height);
        const auto leftWidth = std::clamp(edges.left.widthPx, 0.0F, rect.width);

        // Mixed physical edges are painted as bounded inner strips, clipped to
        // an outer-minus-inner rounded ring. The inner contour matters for
        // thick/asymmetric borders: clipping only to the outer surface leaves
        // visibly square inner corners.
        ComPtr<ID2D1Layer> borderLayer;
        ComPtr<ID2D1RoundedRectangleGeometry> outerBorderGeometry;
        ComPtr<ID2D1RoundedRectangleGeometry> innerBorderGeometry;
        ComPtr<ID2D1PathGeometry> borderRingGeometry;
        bool roundedBorderClip = false;
        if (owner->d2dFactory_ && radius > 0.0F &&
            SUCCEEDED(owner->d2dFactory_->CreateRoundedRectangleGeometry(
                {D2DRect(rect), radius, radius},
                outerBorderGeometry.ReleaseAndGetAddressOf()))) {
            ID2D1Geometry* borderMask = outerBorderGeometry.Get();
            const Rect innerRect{
                rect.x + leftWidth,
                rect.y + topWidth,
                std::max(0.0F, rect.width - leftWidth - rightWidth),
                std::max(0.0F, rect.height - topWidth - bottomWidth),
            };
            if (innerRect.width > 0.0F && innerRect.height > 0.0F) {
                const auto innerRadiusX = std::max(
                    0.0F, radius - std::max(leftWidth, rightWidth));
                const auto innerRadiusY = std::max(
                    0.0F, radius - std::max(topWidth, bottomWidth));
                ComPtr<ID2D1GeometrySink> sink;
                if (SUCCEEDED(owner->d2dFactory_->CreateRoundedRectangleGeometry(
                        {D2DRect(innerRect), innerRadiusX, innerRadiusY},
                        innerBorderGeometry.ReleaseAndGetAddressOf())) &&
                    SUCCEEDED(owner->d2dFactory_->CreatePathGeometry(
                        borderRingGeometry.ReleaseAndGetAddressOf())) &&
                    SUCCEEDED(borderRingGeometry->Open(sink.ReleaseAndGetAddressOf()))) {
                    const auto combined = outerBorderGeometry->CombineWithGeometry(
                        innerBorderGeometry.Get(), D2D1_COMBINE_MODE_EXCLUDE,
                        nullptr, sink.Get());
                    const auto closed = sink->Close();
                    if (SUCCEEDED(combined) && SUCCEEDED(closed))
                        borderMask = borderRingGeometry.Get();
                }
            }
            if (SUCCEEDED(target->CreateLayer(
                    nullptr, borderLayer.ReleaseAndGetAddressOf()))) {
                D2D1_LAYER_PARAMETERS parameters{};
                parameters.contentBounds = D2DRect(rect);
                parameters.geometricMask = borderMask;
                parameters.maskAntialiasMode = D2D1_ANTIALIAS_MODE_PER_PRIMITIVE;
                parameters.maskTransform = D2D1::Matrix3x2F::Identity();
                parameters.opacity = 1.0F;
                target->PushLayer(parameters, borderLayer.Get());
                roundedBorderClip = true;
            }
        }
        if (!roundedBorderClip) {
            target->PushAxisAlignedClip(D2DRect(rect), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        }

        const auto paintEdge = [&](const NativeBorderEdgeStyle& edge,
                                   const Rect edgeRect) {
            if (edge.widthPx <= 0.0F || !edge.color || edge.color->alpha <= 0.0F ||
                edgeRect.width <= 0.0F || edgeRect.height <= 0.0F) return;
            auto brush = Brush(target, WithOpacity(*edge.color, opacity));
            if (brush) target->FillRectangle(D2DRect(edgeRect), brush.Get());
        };
        const auto middleTop = rect.y + topWidth;
        const auto middleHeight = std::max(
            0.0F, rect.height - topWidth - bottomWidth);
        paintEdge(edges.top, {rect.x, rect.y, rect.width, topWidth});
        paintEdge(edges.bottom, {
            rect.x, rect.y + rect.height - bottomWidth, rect.width, bottomWidth});
        paintEdge(edges.left, {
            rect.x, middleTop,
            leftWidth, middleHeight});
        paintEdge(edges.right, {
            rect.x + rect.width - rightWidth, middleTop, rightWidth, middleHeight});

        if (roundedBorderClip) target->PopLayer();
        else target->PopAxisAlignedClip();
    }

    void DrawFocus(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity) {
        if (!target || node.id != focusedId) return;
        const auto width = std::max(
            options.accessibility.minimumFocusRingPx,
            std::max(2.0F, style.outlineWidthPx()));
        const auto expanded = Inset(rect, -(style.outlineOffsetPx() + width * 0.5F));
        const auto radius = RadiusFor(style, expanded);
        auto brush = Brush(target, WithOpacity(
            style.outlineColor().value_or(kDefaultFocus), opacity));
        if (brush) target->DrawRoundedRectangle(
            {D2DRect(expanded), radius, radius}, brush.Get(), width);
    }

    void DrawSemanticIcon(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity,
        const std::wstring_view glyph) {
        if (!target || glyph.empty()) return;
        icons::NativeIcon icon{};
        if (!icons::TryParseNativeIcon(glyph, icon)) {
            Add(node.id, L"unknown_icon", L"Unknown semantic icon: " + std::wstring{glyph});
            return;
        }
        auto brush = Brush(target, WithOpacity(
            style.foreground().value_or(kDefaultText), opacity));
        if (brush && !icons::DrawNativeIcon(
                target, icon, D2DRect(rect), brush.Get(), 2.0F)) {
            Add(node.id, L"icon_draw", L"Semantic icon could not be drawn.");
        }
    }

    [[nodiscard]] bool DrawPackageIcon(
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity,
        const WidgetPackageIcon& icon) {
        if (!target) return false;
        return owner->PaintPackageIcon(
            target, icon, rect,
            WithOpacity(style.foreground().value_or(kDefaultText), opacity),
            options);
    }

    void DrawImageBitmapLayer(
        ID2D1RenderTarget* const destinationTarget,
        const WidgetNode& imageNode,
        ID2D1Bitmap* const bitmap,
        const NativeRenderStyle& style,
        const Rect destination,
        const float opacity,
        const bool focused,
        const bool surfaceComposite) {
        if (!destinationTarget || !bitmap) return;
        const auto imageSize = bitmap->GetSize();
        const auto placement = surfaceComposite
            ? ImagePlacement{
                destination,
                {0.0F, 0.0F, imageSize.width, imageSize.height}}
            : [&] {
                const auto fit = EffectiveImageFit(
                    imageNode, style, focused, imageNode.id == pressedId);
                return DeclarativeRenderer::ComputeImagePlacement(
                    {imageSize.width, imageSize.height}, destination, fit,
                    style.objectPosition());
            }();
        destinationTarget->DrawBitmap(
            bitmap, D2DRect(placement.destination),
            std::clamp(opacity, 0.0F, 1.0F),
            D2D1_BITMAP_INTERPOLATION_MODE_LINEAR,
            D2DRect(placement.source));
        if (owner->ArtworkRenderDiagnosticsEnabled() &&
            !imageNode.artworkHandle.empty()) {
            const auto nodeKey = NarrowStableId(imageNode.id);
            const auto shown = presentation.find(nodeKey);
            owner->ReportArtworkRenderDiagnostic(
                imageNode,
                options.artworkWidgetId,
                L"draw",
                L"bitmap",
                {imageSize.width, imageSize.height},
                placement.destination,
                placement.source,
                shown != presentation.end() ? shown->second.visibleBox : destination,
                opacity);
        }
    }

    void DrawBackgroundSurfaceOverlays(
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity) {
        const auto radius = RadiusFor(style, rect);
        ComPtr<ID2D1Layer> layer;
        ComPtr<ID2D1RoundedRectangleGeometry> geometry;
        bool rounded{};
        if (owner->d2dFactory_ && radius > 0.0F &&
            SUCCEEDED(owner->d2dFactory_->CreateRoundedRectangleGeometry(
                {D2DRect(rect), radius, radius},
                geometry.ReleaseAndGetAddressOf())) &&
            SUCCEEDED(target->CreateLayer(nullptr, layer.ReleaseAndGetAddressOf()))) {
            D2D1_LAYER_PARAMETERS parameters{};
            parameters.contentBounds = D2DRect(rect);
            parameters.geometricMask = geometry.Get();
            parameters.maskAntialiasMode = D2D1_ANTIALIAS_MODE_PER_PRIMITIVE;
            parameters.opacity = 1.0F;
            target->PushLayer(parameters, layer.Get());
            rounded = true;
        } else {
            target->PushAxisAlignedClip(
                D2DRect(rect), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        }
        if (style.imageTint()) {
            auto tint = Brush(target, WithOpacity(*style.imageTint(), opacity));
            if (tint) target->FillRectangle(D2DRect(rect), tint.Get());
        }
        if (style.scrimColor()) {
            auto scrim = Brush(target, WithOpacity(*style.scrimColor(), opacity));
            if (scrim) {
                const Rect bottom{rect.x, rect.y + rect.height * 0.55F,
                    rect.width, rect.height * 0.45F};
                target->FillRectangle(D2DRect(bottom), scrim.Get());
            }
        }
        if (rounded) target->PopLayer();
        else target->PopAxisAlignedClip();
    }

    bool DrawResolvedImageLayers(
        const WidgetNode& committedNode,
        ID2D1Bitmap* const committedBitmap,
        const bool committedIsSurfaceComposite,
        const WidgetNode* const incomingNode,
        ID2D1Bitmap* const incomingBitmap,
        const float incomingOpacity,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity,
        const bool focused,
        const bool drawOverlays = true) {
        if (!target || !committedBitmap) return false;
        const auto radius = RadiusFor(style, rect);

        ComPtr<ID2D1Layer> layer;
        ComPtr<ID2D1RoundedRectangleGeometry> geometry;
        bool pushed = false;
        if (owner->d2dFactory_ && radius > 0.0F &&
            SUCCEEDED(owner->d2dFactory_->CreateRoundedRectangleGeometry(
                {D2DRect(rect), radius, radius}, geometry.ReleaseAndGetAddressOf())) &&
            SUCCEEDED(target->CreateLayer(nullptr, layer.ReleaseAndGetAddressOf()))) {
            D2D1_LAYER_PARAMETERS parameters{};
            parameters.contentBounds = D2DRect(rect);
            parameters.geometricMask = geometry.Get();
            parameters.maskAntialiasMode = D2D1_ANTIALIAS_MODE_PER_PRIMITIVE;
            parameters.maskTransform = D2D1::Matrix3x2F::Identity();
            parameters.opacity = 1.0F;
            target->PushLayer(parameters, layer.Get());
            pushed = true;
        } else {
            target->PushAxisAlignedClip(D2DRect(rect), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        }

        DrawImageBitmapLayer(
            target, committedNode, committedBitmap, style, rect, opacity,
            focused, committedIsSurfaceComposite);
        if (incomingNode && incomingBitmap && incomingOpacity > 0.0F)
            DrawImageBitmapLayer(
                target, *incomingNode, incomingBitmap, style, rect,
                opacity * incomingOpacity, focused, false);

        // Tint and scrim are surface styling, not texture content. Apply them
        // once after the two image layers so their authored opacity remains
        // stable throughout the blend.
        if (drawOverlays) DrawBackgroundSurfaceOverlays(style, rect, opacity);
        if (pushed) target->PopLayer();
        else target->PopAxisAlignedClip();
        return true;
    }

    [[nodiscard]] ComPtr<ID2D1Bitmap> CreateBackgroundSurfaceRebase(
        const WidgetNode& node,
        const WidgetNode& committedNode,
        ID2D1Bitmap* const committedBitmap,
        const bool committedIsSurfaceComposite,
        const WidgetNode& incomingNode,
        ID2D1Bitmap* const incomingBitmap,
        const float incomingOpacity,
        const NativeRenderStyle& style,
        const Rect rect,
        std::size_t& byteCount) {
        byteCount = 0;
        if (!target || !committedBitmap || !incomingBitmap ||
            rect.width <= 0.0F || rect.height <= 0.0F) return {};

        FLOAT dpiX{96.0F};
        FLOAT dpiY{96.0F};
        target->GetDpi(&dpiX, &dpiY);
        D2D1_MATRIX_3X2_F transform{};
        target->GetTransform(&transform);
        constexpr double transformEpsilon = 0.0001;
        constexpr double pixelAlignmentEpsilon = 0.01;
        if (!std::isfinite(dpiX) || !std::isfinite(dpiY) ||
            dpiX <= 0.0F || dpiY <= 0.0F ||
            !std::isfinite(transform._11) || !std::isfinite(transform._12) ||
            !std::isfinite(transform._21) || !std::isfinite(transform._22) ||
            !std::isfinite(transform._31) || !std::isfinite(transform._32) ||
            transform._11 <= 0.0F || transform._22 <= 0.0F ||
            std::abs(transform._12) > transformEpsilon ||
            std::abs(transform._21) > transformEpsilon) {
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"rebase-failed", L"invalid-target-transform");
            return {};
        }
        const double dpiScaleX = static_cast<double>(dpiX) / 96.0;
        const double dpiScaleY = static_cast<double>(dpiY) / 96.0;
        const double physicalLeft =
            (static_cast<double>(rect.x) * transform._11 + transform._31) *
            dpiScaleX;
        const double physicalRight =
            (static_cast<double>(rect.x + rect.width) * transform._11 +
             transform._31) * dpiScaleX;
        const double physicalTop =
            (static_cast<double>(rect.y) * transform._22 + transform._32) *
            dpiScaleY;
        const double physicalBottom =
            (static_cast<double>(rect.y + rect.height) * transform._22 +
             transform._32) * dpiScaleY;
        if (!std::isfinite(physicalLeft) || !std::isfinite(physicalRight) ||
            !std::isfinite(physicalTop) || !std::isfinite(physicalBottom)) {
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"rebase-failed", L"invalid-device-geometry");
            return {};
        }
        const double roundedLeft = std::round(physicalLeft);
        const double roundedRight = std::round(physicalRight);
        const double roundedTop = std::round(physicalTop);
        const double roundedBottom = std::round(physicalBottom);
        if (std::abs(physicalLeft - roundedLeft) > pixelAlignmentEpsilon ||
            std::abs(physicalRight - roundedRight) > pixelAlignmentEpsilon ||
            std::abs(physicalTop - roundedTop) > pixelAlignmentEpsilon ||
            std::abs(physicalBottom - roundedBottom) > pixelAlignmentEpsilon) {
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"rebase-failed", L"unaligned-device-geometry");
            return {};
        }
        const double pixelWidthValue = roundedRight - roundedLeft;
        const double pixelHeightValue = roundedBottom - roundedTop;
        if (pixelWidthValue <= 0.0 || pixelHeightValue <= 0.0 ||
            pixelWidthValue > std::numeric_limits<UINT32>::max() ||
            pixelHeightValue > std::numeric_limits<UINT32>::max()) {
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"rebase-failed", L"invalid-device-extent");
            return {};
        }
        const auto pixelWidth = static_cast<std::uint64_t>(pixelWidthValue);
        const auto pixelHeight = static_cast<std::uint64_t>(pixelHeightValue);
        if (pixelWidth > std::numeric_limits<std::uint64_t>::max() / 4ULL ||
            pixelHeight >
                std::numeric_limits<std::uint64_t>::max() / 4ULL / pixelWidth) {
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"rebase-failed", L"surface-bitmap-budget");
            return {};
        }
        const auto estimatedBytes64 = pixelWidth * pixelHeight * 4ULL;
        const auto retainedBytes = owner->focusBackgroundCompositeBytes_;
        if (estimatedBytes64 > background_surface_policy::MaximumRebaseBytes ||
            retainedBytes > kMaximumBitmapBytes ||
            estimatedBytes64 > kMaximumBitmapBytes - retainedBytes) {
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"rebase-failed", L"surface-bitmap-budget");
            return {};
        }
        if (!owner->TrimBitmapCache(static_cast<std::size_t>(estimatedBytes64))) return {};

        const auto desiredSize = D2D1::SizeF(rect.width, rect.height);
        const auto desiredPixels = D2D1::SizeU(
            static_cast<UINT32>(pixelWidth),
            static_cast<UINT32>(pixelHeight));
        ComPtr<ID2D1BitmapRenderTarget> compositeTarget;
        if (FAILED(target->CreateCompatibleRenderTarget(
                &desiredSize, &desiredPixels, nullptr,
                D2D1_COMPATIBLE_RENDER_TARGET_OPTIONS_NONE,
                compositeTarget.ReleaseAndGetAddressOf())) ||
            !compositeTarget) {
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"rebase-failed", L"compatible-target");
            return {};
        }

        const Rect localRect{0.0F, 0.0F, rect.width, rect.height};
        compositeTarget->SetTransform(D2D1::Matrix3x2F::Identity());
        compositeTarget->BeginDraw();
        compositeTarget->Clear(D2D1::ColorF(0.0F, 0.0F, 0.0F, 0.0F));
        DrawImageBitmapLayer(
            compositeTarget.Get(), committedNode, committedBitmap, style,
            localRect, 1.0F, false, committedIsSurfaceComposite);
        DrawImageBitmapLayer(
            compositeTarget.Get(), incomingNode, incomingBitmap, style,
            localRect, incomingOpacity, false, false);
        if (FAILED(compositeTarget->EndDraw())) {
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"rebase-failed", L"compatible-target-draw");
            return {};
        }

        ComPtr<ID2D1Bitmap> bitmap;
        if (FAILED(compositeTarget->GetBitmap(bitmap.ReleaseAndGetAddressOf())) ||
            !bitmap) {
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"rebase-failed", L"compatible-target-bitmap");
            return {};
        }
        const auto pixelSize = bitmap->GetPixelSize();
        const auto byteCount64 = static_cast<std::uint64_t>(pixelSize.width) *
            static_cast<std::uint64_t>(pixelSize.height) * 4ULL;
        if (byteCount64 > background_surface_policy::MaximumRebaseBytes) {
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"rebase-failed", L"surface-bitmap-budget");
            return {};
        }
        byteCount = static_cast<std::size_t>(byteCount64);
        return bitmap;
    }

    std::map<std::wstring, Rect> posterArtworkBounds;
    ImageDecodeSize ImageSize(const WidgetNode& node) const {
        if (!options.sizeArtworkToDisplay || node.imageSource.starts_with(L"data:")) return {};
        if (options.artworkDecodeSize.width) return options.artworkDecodeSize;
        if (const auto poster = posterArtworkBounds.find(node.id); poster != posterArtworkBounds.end())
            return DisplayImageSize(poster->second.width, poster->second.height, options.pixelScale);
        const auto geometry = presentation.find(NarrowStableId(node.id));
        if (geometry == presentation.end()) return {};
        return DisplayImageSize(geometry->second.borderBox.width, geometry->second.borderBox.height, options.pixelScale);
    }
    std::wstring ImageKey(const WidgetNode& node, ImageDecodeSize size) const {
        return !node.artworkHandle.empty() && node.imageSource.empty()
            ? RemoteImageCache::TrustedArtworkKey(options.artworkWidgetId, node.id, node.artworkHandle, size)
            : RemoteImageCache::VariantKey(node.imageSource, size);
    }
    ImageDecodeSize CachedImageSize(const WidgetNode& node) const {
        auto size = ImageSize(node);
        while (owner->imageCache_ && (size.width > 64 || size.height > 64) &&
               owner->imageCache_->BudgetRejected(ImageKey(node, size))) {
            size = {std::max(64U, size.width / 2), std::max(64U, size.height / 2)};
        }
        return size;
    }
    std::set<std::wstring> visibleImageKeys;
    std::set<std::wstring> visibleContentImageKeys;
    std::map<std::wstring, std::set<std::wstring>> visibleBackgroundImageKeys;
    void GatherVisibleImages(const WidgetNode& node) {
        const auto track = [&](std::wstring key) {
            visibleImageKeys.insert(key);
            if (node.kind == L"backgroundSurface") visibleBackgroundImageKeys[node.id].insert(std::move(key));
            else visibleContentImageKeys.insert(std::move(key));
        };
        const auto geometry = presentation.find(NarrowStableId(node.id));
        // Poster artwork is deliberately excluded from layout. Both its decode
        // size and visibility come from the full-bleed parent tile used by DrawNode.
        const WidgetNode* posterArtwork = node.kind == L"actionSurface" &&
            node.actionSurfacePresentation == L"poster" && node.children.size() == 2U
            ? &node.children.front() : nullptr;
        if (posterArtwork && geometry != presentation.end())
            posterArtworkBounds.insert_or_assign(posterArtwork->id, geometry->second.borderBox);
        if (geometry != presentation.end() && geometry->second.visibleBox.width > 0.5F && geometry->second.visibleBox.height > 0.5F) {
            if (posterArtwork)
                track(ImageKey(*posterArtwork, CachedImageSize(*posterArtwork)));
            const auto size = CachedImageSize(node);
            if (!node.imageSource.empty() || !node.artworkHandle.empty()) track(ImageKey(node, size));
            if (node.kind == L"backgroundSurface") {
                const auto focused = ResolveFocusBackgroundSelection(snapshot->root, focusedId);
                const auto protect = [&](std::wstring_view url, std::wstring_view handle) {
                    if (url.empty() && handle.empty()) return;
                    WidgetNode image;
                    image.id = node.id;
                    image.imageSource = url;
                    image.artworkHandle = handle;
                    track(ImageKey(image, CachedImageSize(image)));
                };
                if (focused.surface == &node) protect(focused.imageSource, focused.artworkHandle);
                for (const auto& [key, entry] : focusBackgrounds) {
                    if (entry.sessionId != node.id || entry.widgetInstanceId != snapshot->instanceId || entry.widgetId != options.artworkWidgetId) continue;
                    protect(entry.imageSource, entry.artworkHandle);
                    protect(entry.incomingImageSource, entry.incomingArtworkHandle);
                }
            }
        }
        for (const auto& child : node.children) GatherVisibleImages(child);
    }

    bool DrawImage(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity,
        const bool focused,
        const bool drawFailureFallback = true,
        ComPtr<ID2D1Bitmap>* const resolvedBitmap = nullptr,
        ImagePresentationState* const resolvedState = nullptr) {
        if (!target) return false;
        auto presentationState = ImagePresentationState::Pending;
        auto bitmap = owner->GetImageBitmap(
            target, node, *this, options.artworkWidgetId, presentationState);
        if (resolvedState) *resolvedState = presentationState;
        if (!bitmap) {
            if (resolvedBitmap) resolvedBitmap->Reset();
            if (drawFailureFallback &&
                presentationState == ImagePresentationState::TrustedArtworkUnavailable) {
                // Tile's no-artwork presentation uses this same closed
                // semantic glyph. A terminal host resolution failure must not
                // leave the authored image box blank or change tile geometry.
                DrawSemanticIcon(
                    node, style,
                    Inset(rect, std::min(rect.width, rect.height) * 0.32F),
                    opacity * 0.75F, L"play");
                if (owner->ArtworkRenderDiagnosticsEnabled() &&
                    !node.artworkHandle.empty()) {
                    const auto shown = presentation.find(NarrowStableId(node.id));
                    owner->ReportArtworkRenderDiagnostic(
                        node, options.artworkWidgetId,
                        L"fallback", L"trusted-unavailable", {}, rect, {},
                        shown != presentation.end() ? shown->second.visibleBox : rect,
                        opacity);
                }
            } else if (drawFailureFallback &&
                       presentationState == ImagePresentationState::Failed) {
                DrawSemanticIcon(
                    node, style,
                    Inset(rect, std::min(rect.width, rect.height) * 0.32F),
                    opacity * 0.65F, L"warning");
                if (owner->ArtworkRenderDiagnosticsEnabled() &&
                    !node.artworkHandle.empty()) {
                    const auto shown = presentation.find(NarrowStableId(node.id));
                    owner->ReportArtworkRenderDiagnostic(
                        node, options.artworkWidgetId,
                        L"fallback", L"failed", {}, rect, {},
                        shown != presentation.end() ? shown->second.visibleBox : rect,
                        opacity);
                }
            } else {
                if (owner->ArtworkRenderDiagnosticsEnabled() &&
                    !node.artworkHandle.empty()) {
                    const auto shown = presentation.find(NarrowStableId(node.id));
                    owner->ReportArtworkRenderDiagnostic(
                        node, options.artworkWidgetId,
                        L"draw", L"unavailable", {}, rect, {},
                        shown != presentation.end() ? shown->second.visibleBox : rect,
                        opacity);
                }
            }
            return false;
        }
        if (resolvedBitmap) *resolvedBitmap = bitmap;
        return DrawResolvedImageLayers(
            node, bitmap.Get(), false, nullptr, nullptr, 0.0F,
            style, rect, opacity, focused);
    }

    void DrawBackgroundSurfaceImage(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity) {
        const std::wstring authorityId = options.artworkAuthorityId.empty()
            ? options.artworkWidgetId
            : options.artworkAuthorityId;
        std::wstring authorityPrefix = authorityId;
        authorityPrefix.push_back(L'\x1f');
        authorityPrefix.append(snapshot->instanceId);
        authorityPrefix.push_back(L'\x1f');
        std::wstring authority = authorityPrefix;
        authority.append(node.id);

        if (!node.usesFocusedDescendantArtwork) {
            if (focusBackgrounds.contains(authority))
                AddBackgroundSurfaceTransitionDiagnostic(
                    node, L"retirement", L"focus-artwork-opt-out");
            focusBackgrounds.erase(authority);
            if ((!node.imageSource.empty() || !node.artworkHandle.empty()) &&
                DrawImage(node, style, rect, opacity, false, false)) {
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
                if (!node.artworkHandle.empty())
                    result.backgroundArtworkHandles[node.id] = node.artworkHandle;
#endif
            }
            return;
        }

        auto retained = focusBackgrounds.find(authority);
        const bool defaultChanged = retained != focusBackgrounds.end() &&
            (retained->second.defaultImageSource != node.imageSource ||
             retained->second.defaultArtworkHandle != node.artworkHandle ||
             retained->second.defaultImageFit != node.imageFit);
        const auto acceptDefaultDeclaration = [&](FocusBackgroundEntry& entry) {
            entry.defaultImageSource = node.imageSource;
            entry.defaultArtworkHandle = node.artworkHandle;
            entry.defaultImageFit = node.imageFit;
        };

        const auto sameProposal = [](
            const std::wstring_view imageSource,
            const std::wstring_view artworkHandle,
            const std::wstring_view imageFit,
            const WidgetNode& desired) {
            return imageSource == desired.imageSource &&
                artworkHandle == desired.artworkHandle &&
                imageFit == desired.imageFit;
        };
        const auto sameCommittedProposal = [&](
            const FocusBackgroundEntry& entry,
            const WidgetNode& desired) {
            return !entry.committedBitmapIsSurfaceComposite && sameProposal(
                entry.imageSource, entry.artworkHandle, entry.imageFit, desired);
        };
        const auto sameIncomingProposal = [](
            const FocusBackgroundEntry& entry,
            const WidgetNode& desired) {
            return entry.incomingBitmap &&
                entry.incomingImageSource == desired.imageSource &&
                entry.incomingArtworkHandle == desired.artworkHandle &&
                entry.incomingImageFit == desired.imageFit;
        };
        const auto sameCandidateProposal = [&](
            const FocusBackgroundEntry& entry,
            const WidgetNode& desired) {
            return entry.candidatePresent && sameProposal(
                entry.candidateImageSource, entry.candidateArtworkHandle,
                entry.candidateImageFit, desired);
        };
        const auto proposalNode = [&](const FocusBackgroundEntry& entry,
                                      const bool incoming) {
            WidgetNode proposal = node;
            proposal.imageSource = incoming
                ? entry.incomingImageSource : entry.imageSource;
            proposal.artworkHandle = incoming
                ? entry.incomingArtworkHandle : entry.artworkHandle;
            proposal.imageFit = incoming
                ? entry.incomingImageFit : entry.imageFit;
            return proposal;
        };
        const auto candidateNode = [&](const FocusBackgroundEntry& entry) {
            WidgetNode proposal = node;
            proposal.imageSource = entry.candidateImageSource;
            proposal.artworkHandle = entry.candidateArtworkHandle;
            proposal.imageFit = entry.candidateImageFit;
            return proposal;
        };
        const auto clearIncoming = [](FocusBackgroundEntry& entry) {
            entry.incomingImageSource.clear();
            entry.incomingArtworkHandle.clear();
            entry.incomingImageFit.clear();
            entry.incomingBitmap.Reset();
            entry.transitionStartedAt = 0;
        };
        const auto clearCandidate = [](FocusBackgroundEntry& entry) {
            entry.candidateImageSource.clear();
            entry.candidateArtworkHandle.clear();
            entry.candidateImageFit.clear();
            entry.candidateObservedAt = 0;
            entry.candidatePresent = false;
        };
        const auto queueCandidate = [&](FocusBackgroundEntry& entry,
                                        const WidgetNode& desired,
                                        const std::uint64_t now) {
            if (sameCandidateProposal(entry, desired)) return;
            entry.candidateImageSource = desired.imageSource;
            entry.candidateArtworkHandle = desired.artworkHandle;
            entry.candidateImageFit = desired.imageFit;
            entry.candidateObservedAt = now;
            entry.candidatePresent = true;
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"candidate", L"latest-proposal");
        };
        const auto startTransition = [&](FocusBackgroundEntry& entry,
                                         const WidgetNode& desired,
                                         ComPtr<ID2D1Bitmap> bitmap,
                                         const std::uint64_t now,
                                         const std::wstring_view event) {
            entry.incomingImageSource = desired.imageSource;
            entry.incomingArtworkHandle = desired.artworkHandle;
            entry.incomingImageFit = desired.imageFit;
            entry.incomingBitmap = std::move(bitmap);
            entry.transitionStartedAt = now;
            AddBackgroundSurfaceTransitionDiagnostic(node, event);
        };
        const auto remember = [&] (
            const WidgetNode& desired,
            ComPtr<ID2D1Bitmap> bitmap) {
            constexpr std::size_t maximumRetainedSurfaces = 256;
            if (!focusBackgrounds.contains(authority) &&
                focusBackgrounds.size() >= maximumRetainedSurfaces) {
                const auto oldest = std::min_element(
                    focusBackgrounds.begin(),
                    focusBackgrounds.end(),
                    [](const auto& left, const auto& right) {
                        return left.second.lastUse < right.second.lastUse;
                    });
                if (oldest != focusBackgrounds.end())
                    focusBackgrounds.erase(oldest);
            }
            FocusBackgroundEntry entry;
            entry.widgetId = options.artworkWidgetId;
            entry.widgetInstanceId = snapshot->instanceId;
            entry.authorityId = authorityId;
            entry.sessionId = node.id;
            entry.imageSource = desired.imageSource;
            entry.artworkHandle = desired.artworkHandle;
            entry.imageFit = desired.imageFit;
            entry.defaultImageSource = node.imageSource;
            entry.defaultArtworkHandle = node.artworkHandle;
            entry.defaultImageFit = node.imageFit;
            entry.committedBitmap = std::move(bitmap);
            entry.committedBitmapIsSurfaceComposite = false;
            entry.committedSurfaceCompositeBytes = 0;
            entry.lastUse = ++focusBackgroundAccessClock;
            focusBackgrounds.insert_or_assign(authority, std::move(entry));
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
            if (!desired.artworkHandle.empty())
                result.backgroundArtworkHandles[node.id] = desired.artworkHandle;
#endif
        };
        const auto now = options.animationTimestampMilliseconds.value_or(0);
        const auto completeTransition = [&](FocusBackgroundEntry& entry) {
            if (!entry.incomingBitmap) return;
            const auto elapsed = now >= entry.transitionStartedAt
                ? now - entry.transitionStartedAt
                : std::uint64_t{0};
            if (elapsed < kBackgroundSurfaceCrossfadeMilliseconds) return;
            entry.imageSource = entry.incomingImageSource;
            entry.artworkHandle = entry.incomingArtworkHandle;
            entry.imageFit = entry.incomingImageFit;
            entry.committedBitmap = entry.incomingBitmap;
            entry.committedBitmapIsSurfaceComposite = false;
            entry.committedSurfaceCompositeBytes = 0;
            clearIncoming(entry);
            const auto committed = proposalNode(entry, false);
            if (sameCandidateProposal(entry, committed)) clearCandidate(entry);
            AddBackgroundSurfaceTransitionDiagnostic(
                node, L"completion", L"settled");
        };
        const auto drawRetained = [&]() {
            retained = focusBackgrounds.find(authority);
            if (retained == focusBackgrounds.end()) return false;
            auto& entry = retained->second;
            entry.lastUse = ++focusBackgroundAccessClock;
            WidgetNode fallback = proposalNode(entry, false);
            if (!entry.committedBitmap) {
                auto state = ImagePresentationState::Pending;
                entry.committedBitmap = owner->GetImageBitmap(
                    target, fallback, *this, options.artworkWidgetId, state);
            }
            if (!entry.committedBitmap) return false;

            if (entry.incomingBitmap) {
                const auto elapsed = now >= entry.transitionStartedAt
                    ? now - entry.transitionStartedAt
                    : std::uint64_t{0};
                const float linear = static_cast<float>(elapsed) /
                    static_cast<float>(kBackgroundSurfaceCrossfadeMilliseconds);
                const float inverse = 1.0F - std::clamp(linear, 0.0F, 1.0F);
                const float progress = 1.0F - inverse * inverse * inverse;
                const auto incoming = proposalNode(entry, true);
                if (!DrawResolvedImageLayers(
                        fallback, entry.committedBitmap.Get(),
                        entry.committedBitmapIsSurfaceComposite,
                        &incoming, entry.incomingBitmap.Get(), progress,
                        style, rect, opacity, false)) return false;
                MarkBackgroundSurfaceAnimation(rect);
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
                if (!entry.incomingArtworkHandle.empty())
                    result.backgroundArtworkHandles[node.id] =
                        entry.incomingArtworkHandle;
#endif
                return true;
            }
            if (!DrawResolvedImageLayers(
                    fallback, entry.committedBitmap.Get(),
                    entry.committedBitmapIsSurfaceComposite,
                    nullptr, nullptr, 0.0F,
                    style, rect, opacity, false)) return false;
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
            if (!fallback.artworkHandle.empty())
                result.backgroundArtworkHandles[node.id] = fallback.artworkHandle;
#endif
            return true;
        };

        const auto selection = ResolveFocusBackgroundSelection(
            snapshot->root, focusedId);
        std::optional<WidgetNode> desired;
        if (selection.surface == &node && selection.focused) {
            if (!selection.artworkHandle.empty()) {
                desired = node;
                desired->imageSource = selection.imageSource;
                desired->artworkHandle = selection.artworkHandle;
                if (desired->imageFit.empty()) desired->imageFit = L"cover";
            }
        } else if (selection.surface) {
            if (focusBackgrounds.contains(authority))
                AddBackgroundSurfaceTransitionDiagnostic(
                    node, L"retirement", L"different-effective-surface");
            focusBackgrounds.erase(authority);
            // This node still paints its authored fallback, but it is not the
            // effective focus-artwork surface and therefore must not recreate
            // retained transition authority on every frame.
            if ((!node.imageSource.empty() || !node.artworkHandle.empty()) &&
                DrawImage(node, style, rect, opacity, false, false)) {
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
                if (!node.artworkHandle.empty())
                    result.backgroundArtworkHandles[node.id] = node.artworkHandle;
#endif
            }
            return;
        }

        // Updating a default is a new artwork proposal, not a new surface.
        // An active override wins. With no widget focus, preserve the displayed
        // selection and any fade already underway until focus returns.
        const bool transitionDefault = defaultChanged && !desired &&
            selection.surface == &node && selection.focused;
        if (transitionDefault) desired = node;
        retained = focusBackgrounds.find(authority);
        if (retained != focusBackgrounds.end()) {
            auto& entry = retained->second;
            if (defaultChanged && desired && !transitionDefault)
                acceptDefaultDeclaration(entry);
            completeTransition(entry);
            if (transitionDefault && sameCommittedProposal(entry, node))
                acceptDefaultDeclaration(entry);

            if (entry.incomingBitmap) {
                if (!desired) {
                    clearCandidate(entry);
                } else if (sameIncomingProposal(entry, *desired)) {
                    clearCandidate(entry);
                } else {
                    queueCandidate(entry, *desired, now);
                }

                if (entry.candidatePresent && desired &&
                    sameCandidateProposal(entry, *desired) &&
                    now >= entry.candidateObservedAt &&
                    now - entry.candidateObservedAt >=
                        kBackgroundSurfaceProposalSettleMilliseconds) {
                    const auto queued = candidateNode(entry);
                    auto queuedState = ImagePresentationState::Pending;
                    auto queuedBitmap = owner->GetImageBitmap(
                        target, queued, *this,
                        options.artworkWidgetId, queuedState);
                    if (queuedBitmap) {
                        const auto elapsed = now >= entry.transitionStartedAt
                            ? now - entry.transitionStartedAt
                            : std::uint64_t{0};
                        const float linear = static_cast<float>(elapsed) /
                            static_cast<float>(
                                kBackgroundSurfaceCrossfadeMilliseconds);
                        const float inverse =
                            1.0F - std::clamp(linear, 0.0F, 1.0F);
                        const float progress =
                            1.0F - inverse * inverse * inverse;
                        const auto committed = proposalNode(entry, false);
                        const auto incoming = proposalNode(entry, true);
                        std::size_t rebaseBytes{};
                        auto rebase = CreateBackgroundSurfaceRebase(
                            node, committed, entry.committedBitmap.Get(),
                            entry.committedBitmapIsSurfaceComposite,
                            incoming, entry.incomingBitmap.Get(), progress,
                            style, rect, rebaseBytes);
                        if (rebase) {
                            entry.imageSource.clear();
                            entry.artworkHandle.clear();
                            entry.imageFit.clear();
                            entry.committedBitmap = std::move(rebase);
                            entry.committedBitmapIsSurfaceComposite = true;
                            entry.committedSurfaceCompositeBytes = rebaseBytes;
                            startTransition(
                                entry, queued, std::move(queuedBitmap), now,
                                L"retarget");
                            clearCandidate(entry);
                        } else {
                            MarkBackgroundSurfaceAnimation(rect);
                        }
                    } else if (queuedState != ImagePresentationState::Pending) {
                        clearCandidate(entry);
                    }
                }
                (void)drawRetained();
                return;
            }

            if (entry.candidatePresent) {
                if (!desired || sameCommittedProposal(entry, *desired)) {
                    clearCandidate(entry);
                } else if (!sameCandidateProposal(entry, *desired)) {
                    queueCandidate(entry, *desired, now);
                }
                if (entry.candidatePresent && desired &&
                    sameCandidateProposal(entry, *desired)) {
                    if (now >= entry.candidateObservedAt &&
                        now - entry.candidateObservedAt >=
                            kBackgroundSurfaceProposalSettleMilliseconds) {
                        const auto queued = candidateNode(entry);
                        auto queuedState = ImagePresentationState::Pending;
                        auto queuedBitmap = owner->GetImageBitmap(
                            target, queued, *this,
                            options.artworkWidgetId, queuedState);
                        if (queuedBitmap) {
                            startTransition(
                                entry, queued, std::move(queuedBitmap), now,
                                L"start");
                            clearCandidate(entry);
                        } else if (queuedState !=
                                   ImagePresentationState::Pending) {
                            clearCandidate(entry);
                        }
                    } else {
                        const auto deadline = entry.candidateObservedAt <=
                                std::numeric_limits<std::uint64_t>::max() -
                                    kBackgroundSurfaceProposalSettleMilliseconds
                            ? entry.candidateObservedAt +
                                kBackgroundSurfaceProposalSettleMilliseconds
                            : std::numeric_limits<std::uint64_t>::max();
                        MarkBackgroundSurfaceSettleWake(rect, deadline);
                    }
                }
                (void)drawRetained();
                return;
            }

            if (!desired || sameCommittedProposal(entry, *desired)) {
                (void)drawRetained();
                return;
            }
        }

        if (desired) {
            auto desiredState = ImagePresentationState::Pending;
            auto desiredBitmap = owner->GetImageBitmap(
                target, *desired, *this,
                options.artworkWidgetId, desiredState);
            if (!desiredBitmap) {
                if (drawRetained()) return;
                const bool trustedDefault =
                    !node.artworkHandle.empty() && node.imageSource.empty();
                const std::wstring defaultKey = trustedDefault
                    ? RemoteImageCache::TrustedArtworkKey(
                        options.artworkWidgetId, node.id,
                        node.artworkHandle)
                    : node.imageSource;
                if (owner->imageCache_ && !defaultKey.empty() &&
                    owner->imageCache_->GetState(defaultKey) ==
                        RemoteImageState::Ready) {
                    ComPtr<ID2D1Bitmap> defaultBitmap;
                    if (DrawImage(
                            node, style, rect, opacity, false, false,
                            &defaultBitmap)) {
                        remember(node, std::move(defaultBitmap));
                    }
                }
                return;
            }

            retained = focusBackgrounds.find(authority);
            if (retained == focusBackgrounds.end()) {
                if (DrawResolvedImageLayers(
                        *desired, desiredBitmap.Get(), false,
                        nullptr, nullptr, 0.0F,
                        style, rect, opacity, false)) {
                    remember(*desired, std::move(desiredBitmap));
                }
                return;
            }

            auto& entry = retained->second;
            if (!entry.committedBitmap) {
                const auto committed = proposalNode(entry, false);
                auto committedState = ImagePresentationState::Pending;
                entry.committedBitmap = owner->GetImageBitmap(
                    target, committed, *this,
                    options.artworkWidgetId, committedState);
            }
            if (!entry.committedBitmap) {
                entry.imageSource = desired->imageSource;
                entry.artworkHandle = desired->artworkHandle;
                entry.imageFit = desired->imageFit;
                entry.committedBitmap = std::move(desiredBitmap);
                entry.committedBitmapIsSurfaceComposite = false;
                entry.committedSurfaceCompositeBytes = 0;
                clearIncoming(entry);
                clearCandidate(entry);
                AddBackgroundSurfaceTransitionDiagnostic(
                    node, L"completion", L"committed-texture-unavailable");
                (void)drawRetained();
                return;
            }

            startTransition(
                entry, *desired, std::move(desiredBitmap), now, L"start");
            (void)drawRetained();
            return;
        }

        if (drawRetained()) return;

        if (!node.imageSource.empty() || !node.artworkHandle.empty()) {
            ComPtr<ID2D1Bitmap> bitmap;
            if (DrawImage(
                    node, style, rect, opacity, false, false,
                    &bitmap)) {
                remember(node, std::move(bitmap));
            }
        }
    }

    void RetireAbsentFocusBackgroundSurfaces() {
        const std::wstring authorityId = options.artworkAuthorityId.empty()
            ? options.artworkWidgetId
            : options.artworkAuthorityId;

        std::set<std::wstring> present;
        const auto collect = [&](const auto& self, const WidgetNode& node) -> void {
            if (!IsResponsiveVisible(node)) return;
            const auto narrowId = NarrowStableId(node.id);
            if (!prepared.contains(narrowId) || !presentation.contains(narrowId))
                return;
            if (node.kind == L"backgroundSurface")
                present.insert(node.id);
            if (node.kind == L"focusPresentationSurface") {
                const auto selection = ResolveFocusPresentationSelection(
                    snapshot->root, focusedId);
                const auto* fragment = selection.surface == &node
                    ? selection.fragment
                    : !node.defaultFocusPresentation.empty()
                        ? &node.defaultFocusPresentation.front()
                        : nullptr;
                if (fragment) self(self, *fragment);
            }
            for (const auto& child : node.children) self(self, child);
        };
        collect(collect, snapshot->root);
        for (auto& [_, retained] : focusBackgrounds) {
            if (retained.widgetId == options.artworkWidgetId &&
                retained.widgetInstanceId == snapshot->instanceId &&
                retained.authorityId == authorityId) continue;
            // A renderer can retain last-valid metadata for another widget,
            // but transition textures never outlive the currently rendered
            // exact authority. They can be recreated from the bounded decoded
            // cache when that widget becomes current again.
            retained.committedBitmap.Reset();
            retained.committedBitmapIsSurfaceComposite = false;
            retained.committedSurfaceCompositeBytes = 0;
            retained.incomingImageSource.clear();
            retained.incomingArtworkHandle.clear();
            retained.incomingImageFit.clear();
            retained.incomingBitmap.Reset();
            retained.transitionStartedAt = 0;
            retained.candidateImageSource.clear();
            retained.candidateArtworkHandle.clear();
            retained.candidateImageFit.clear();
            retained.candidateObservedAt = 0;
            retained.candidatePresent = false;
        }
        std::erase_if(focusBackgrounds, [&](const auto& entry) {
            const auto& retained = entry.second;
            if (retained.widgetId != options.artworkWidgetId ||
                retained.widgetInstanceId != snapshot->instanceId) {
                return false;
            }
            const bool retire = retained.authorityId != authorityId ||
                !present.contains(retained.sessionId);
            if (retire) {
                Add(
                    retained.sessionId, L"background_crossfade_retirement",
                    L"event=retirement widget=" + retained.widgetId +
                        L" instance=" + retained.widgetInstanceId +
                        L" surface=" + retained.sessionId +
                        (retained.authorityId != authorityId
                            ? L" reason=authority-changed"
                            : L" reason=surface-removed"),
                    RenderDiagnosticSeverity::Information);
            }
            return retire;
        });
    }

    void DrawProgress(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity) {
        if (!target) return;
        const auto trackHeight = std::clamp(rect.height, 2.0F, 12.0F);
        const Rect track{rect.x, rect.y + (rect.height - trackHeight) * 0.5F, rect.width, trackHeight};
        auto trackBrush = Brush(target, WithOpacity(
            style.background().value_or(kDefaultTrack), opacity));
        if (trackBrush) target->FillRoundedRectangle(
            {D2DRect(track), trackHeight * 0.5F, trackHeight * 0.5F},
            trackBrush.Get());
        auto ratio = 0.0;
        if (node.hasProgress && std::isfinite(node.value) && std::isfinite(node.maximum) &&
            node.maximum > 0.0) {
            ratio = std::clamp(node.value / node.maximum, 0.0, 1.0);
        } else {
            Add(node.id, L"invalid_progress", L"Progress requires finite 0 <= value <= maximum and maximum > 0.");
        }
        const Rect fill{track.x, track.y, track.width * static_cast<float>(ratio), track.height};
        auto fillBrush = Brush(target, WithOpacity(
            style.foreground().value_or(kDefaultAccent), opacity));
        if (fillBrush && fill.width > 0.0F) target->FillRoundedRectangle(
            {D2DRect(fill), trackHeight * 0.5F, trackHeight * 0.5F},
            fillBrush.Get());
    }

    void DrawSlider(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity,
        const bool focused,
        const bool adjustmentActive) {
        if (!target || rect.width <= 0.0F || rect.height <= 0.0F) return;
        const auto overrideValue = options.sliderValueOverrides.find(node.id);
        const auto presentedValue = overrideValue == options.sliderValueOverrides.end()
            ? node.value
            : overrideValue->second;
        auto ratio = 0.0;
        const auto range = node.maximum - node.minimum;
        const auto offset = presentedValue - node.minimum;
        if (node.hasProgress && node.hasSliderRange &&
            std::isfinite(node.minimum) && std::isfinite(node.maximum) &&
            std::isfinite(presentedValue) && std::isfinite(node.step) &&
            std::isfinite(range) && range > 0.0 && std::isfinite(offset) &&
            node.minimum < node.maximum && presentedValue >= node.minimum &&
            presentedValue <= node.maximum && node.step > 0.0 &&
            node.step <= range && std::isfinite(offset / range)) {
            ratio = std::clamp(offset / range, 0.0, 1.0);
        } else {
            Add(node.id, L"invalid_slider_range",
                L"Slider requires finite minimum < maximum, an in-range value, and a positive bounded step.",
                RenderDiagnosticSeverity::Error);
        }

        // Keep the controller hit target at 44 DIP while painting a restrained
        // track inside it. The target size is an interaction contract, not a
        // reason to turn the whole control into a heavy filled pill.
        const auto thumbRadius = adjustmentActive ? 9.5F : focused ? 8.0F : 6.5F;
        const auto trackHeight = adjustmentActive ? 8.0F : focused ? 6.0F : 4.5F;
        const auto trackInset = thumbRadius + 2.0F;
        const Rect track{
            rect.x + trackInset,
            rect.y + (rect.height - trackHeight) * 0.5F,
            std::max(0.0F, rect.width - trackInset * 2.0F),
            trackHeight,
        };
        const auto accent = adjustmentActive
            ? style.outlineColor().value_or(
                style.foreground().value_or(kDefaultFocus))
            : style.foreground().value_or(kDefaultAccent);
        auto trackBrush = Brush(target, WithOpacity(
            style.background().value_or(kDefaultTrack), opacity));
        if (trackBrush) target->FillRoundedRectangle(
            {D2DRect(track), trackHeight * 0.5F, trackHeight * 0.5F},
            trackBrush.Get());
        const auto thumbX = track.x + track.width * static_cast<float>(ratio);
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
        result.sliderThumbXs[node.id] = thumbX;
#endif
        const Rect fill{track.x, track.y, std::max(0.0F, thumbX - track.x), track.height};
        auto accentBrush = Brush(target, WithOpacity(accent, opacity));
        if (accentBrush && fill.width > 0.0F) target->FillRoundedRectangle(
            {D2DRect(fill), trackHeight * 0.5F, trackHeight * 0.5F},
            accentBrush.Get());
        if (!accentBrush) return;

        const auto center = D2D1::Point2F(thumbX, rect.y + rect.height * 0.5F);
        const auto thumb = D2D1::Ellipse(center, thumbRadius, thumbRadius);
        if (node.isDisabled) {
            target->DrawEllipse(thumb, accentBrush.Get(), 2.0F);
        } else {
            target->FillEllipse(thumb, accentBrush.Get());
        }
        if (node.isBusy) {
            const auto busyRadius = thumbRadius + 4.0F;
            target->DrawEllipse(
                D2D1::Ellipse(center, busyRadius, busyRadius),
                accentBrush.Get(), 2.0F);
        }
        if (adjustmentActive) {
            target->DrawEllipse(
                D2D1::Ellipse(center, thumbRadius + 4.0F, thumbRadius + 4.0F),
                accentBrush.Get(), 2.5F);
        }
    }

    void DrawStateCue(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity,
        const std::optional<Rect> assignedCue = std::nullopt) {
        if (!target || (node.kind != L"button" && node.kind != L"actionSurface")) return;
        if (node.text.empty() && node.kind != L"actionSurface") {
            // Icon-only controls have no trailing-label space for a checkmark
            // or spinner. Use compact peripheral cues so state never covers
            // the control's primary glyph.
            auto brush = Brush(target, WithOpacity(
                style.foreground().value_or(kDefaultAccent), opacity));
            if (!brush) return;
            if (node.isBusy) {
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
                result.buttonStateCues.insert_or_assign(node.id, L"busy-spinner");
#endif
                const auto radius = std::clamp(rect.height * 0.105F, 4.0F, 6.0F);
                target->DrawEllipse(
                    D2D1::Ellipse(
                        D2D1::Point2F(rect.x + rect.width - radius - 4.0F,
                                     rect.y + radius + 4.0F),
                        radius, radius),
                    brush.Get(), 2.0F);
            } else if (node.isSelected) {
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
                result.buttonStateCues.insert_or_assign(node.id, L"selected-dot");
#endif
                const auto radius = std::clamp(rect.height * 0.065F, 2.5F, 4.0F);
                target->FillEllipse(
                    D2D1::Ellipse(
                        D2D1::Point2F(rect.x + rect.width * 0.5F,
                                     rect.y + rect.height - radius - 3.0F),
                        radius, radius),
                    brush.Get());
            }
            return;
        }
        const auto size = std::clamp(rect.height * 0.36F, 14.0F, 22.0F);
        const Rect fallbackCue{
            rect.x + rect.width - size - 10.0F,
            rect.y + (rect.height - size) * 0.5F,
            size,
            size,
        };
        const auto cue = assignedCue && assignedCue->width > 0.0F && assignedCue->height > 0.0F
            ? *assignedCue
            : fallbackCue;
        if (node.isSelect) {
            auto brush = Brush(target, WithOpacity(
                style.foreground().value_or(kDefaultAccent), opacity));
            if (!brush) return;
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
            result.buttonStateCues.insert_or_assign(node.id, L"select-chevron");
#endif
            const float centerX = cue.x + cue.width * 0.5F;
            const float centerY = cue.y + cue.height * 0.5F;
            const float half = std::clamp(cue.width * 0.22F, 3.0F, 5.0F);
            const float drop = half * 0.7F;
            target->DrawLine(
                D2D1::Point2F(centerX - half, centerY - drop * 0.5F),
                D2D1::Point2F(centerX, centerY + drop * 0.5F),
                brush.Get(), 1.8F);
            target->DrawLine(
                D2D1::Point2F(centerX, centerY + drop * 0.5F),
                D2D1::Point2F(centerX + half, centerY - drop * 0.5F),
                brush.Get(), 1.8F);
        } else if (node.isBusy) {
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
            result.buttonStateCues.insert_or_assign(node.id, L"busy-spinner");
#endif
            DrawSemanticIcon(node, style, cue, opacity, L"refresh");
        } else if (node.isSelected && node.kind == L"actionSurface") {
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
            result.buttonStateCues.insert_or_assign(node.id, L"selected-check");
#endif
            DrawSemanticIcon(node, style, cue, opacity, L"check");
        }
    }

    void DrawCollectionLoading(const WidgetNode& node, const NativeRenderStyle& style,
        const Rect viewportRect, const float opacity) {
        if (!target || node.kind != L"scroll" || (node.collectionLoading.empty() || node.collectionLoading == L"idle") ||
            viewportRect.width < 32.0F || viewportRect.height < 24.0F) return;
        const float height = std::min(viewportRect.height - 8.0F, std::max(32.0F, style.fontSizePx() + 14.0F));
        const float width = std::min(viewportRect.width - 8.0F, std::max(130.0F, style.fontSizePx() * 6.0F + 38.0F));
        Rect badge{viewportRect.x + (viewportRect.width - width) * 0.5F,
            node.collectionLoading == L"before" ? viewportRect.y + 4.0F : viewportRect.y + viewportRect.height - height - 4.0F,
            width, height};
        if (node.scrollAxis == L"horizontal") {
            badge.x = node.collectionLoading == L"before" ? viewportRect.x + 4.0F : viewportRect.x + viewportRect.width - width - 4.0F;
            badge.y = viewportRect.y + viewportRect.height - height - 4.0F;
        }
        const auto foreground = style.foreground().value_or(kDefaultText);
        const auto luminance = foreground.red * 0.2126F + foreground.green * 0.7152F + foreground.blue * 0.0722F;
        const NativeColor background = luminance > 0.5F ? kDefaultButton : NativeColor{0.96F, 0.97F, 0.99F, 0.96F};
        auto fill = Brush(target, WithOpacity(background, opacity));
        auto outline = Brush(target, WithOpacity(foreground, opacity * 0.3F));
        target->PushAxisAlignedClip(D2DRect(viewportRect), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        const D2D1_ROUNDED_RECT rounded{D2DRect(badge), height * 0.5F, height * 0.5F};
        if (fill) target->FillRoundedRectangle(rounded, fill.Get());
        if (outline) target->DrawRoundedRectangle(rounded, outline.Get(), 1.0F);
        const float iconSize = std::min(18.0F, height - 8.0F);
        DrawLoadingIndicatorNode(node, style, {badge.x + 10.0F, badge.y + (height - iconSize) * 0.5F, iconSize, iconSize}, opacity);
        WidgetNode label; label.id = node.id; label.text = L"Loading\u2026";
        DrawTextContent(label, style, {badge.x + 36.0F, badge.y, std::max(0.0F, badge.width - 44.0F), badge.height},
            opacity, NativeTextVerticalAlignment::Center);
        target->PopAxisAlignedClip();
        // This badge contributes no layout or input node. It follows existing
        // scroll paints without forcing the entire widget to animate while waiting.
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
        result.collectionLoadingRects[node.id] = badge;
#endif
    }

    void DrawLoadingIndicatorNode(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity) {
        if (!target || rect.width <= 0.5F || rect.height <= 0.5F) return;
        auto brush = Brush(target, WithOpacity(
            style.foreground().value_or(kDefaultAccent), opacity));
        if (!brush) return;
        const auto timestamp = options.animationTimestampMilliseconds.value_or(0);
        const auto stroke = style.borderWidthPx() > 0.0F
            ? style.borderWidthPx()
            : 2.0F;
        if (!icons::DrawLoadingIndicator(
                target,
                D2DRect(rect),
                brush.Get(),
                timestamp,
                options.accessibility.reducedMotion,
                stroke)) {
            Add(node.id, L"loading_indicator_draw",
                L"The native loading indicator could not be drawn.");
        }
    }

    void CollectFocusGeometry(
        const WidgetNode& node,
        const std::wstring_view inputScope,
        const PresentationNode& presented) {
        if (node.kind != L"actionSurface" && !node.contextMenuButton.empty() &&
            !node.contextActions.empty() && presented.visibleBox.width > 0.5F &&
            presented.visibleBox.height > 0.5F)
            result.contextMenuRects[node.id] = presented.visibleBox;
        if (node.kind != L"button" && node.kind != L"slider" &&
            node.kind != L"actionSurface") {
            return;
        }
        result.navigationRects[node.id] = presented.borderBox;
        // Disabled and busy describe activation, not navigability. This is the
        // same focus graph used by both preparation and target-backed drawing.
        result.navigationEnabled[node.id] = true;
        result.focusScopes[node.id] = std::wstring{inputScope};
        if (CanRevealNode(node.id)) result.revealableFocusIds.insert(node.id);
        const auto visibleRect = presented.visibleBox;
        if (visibleRect.width > 0.5F && visibleRect.height > 0.5F) {
            result.hitRegions.push_back(
                {node.id, visibleRect, !node.isDisabled && !node.isBusy});
            result.focusRects[node.id] = visibleRect;
            if (node.id == focusedId) result.currentFocusRect = visibleRect;
        }
        if (node.id == focusedId)
            result.currentFocusOutlineClip = EffectiveFocusVisibilityClip(node.id);
    }

    void CollectFocusGeometryTree(
        const WidgetNode& node,
        const std::wstring_view inheritedInputScope = {}) {
        const std::wstring_view inputScope = !node.inputScopeId.empty()
            ? std::wstring_view(node.inputScopeId)
            : inheritedInputScope.empty()
                ? std::wstring_view(node.id)
                : inheritedInputScope;
        const auto presented = presentation.find(NarrowStableId(node.id));
        if (presented == presentation.end()) return;
        CollectFocusGeometry(node, inputScope, presented->second);
        if (node.kind == L"focusPresentationSurface") {
            const auto selection = ResolveFocusPresentationSelection(
                snapshot->root, focusedId);
            const auto* fragment = selection.surface == &node
                ? selection.fragment
                : !node.defaultFocusPresentation.empty()
                    ? &node.defaultFocusPresentation.front()
                    : nullptr;
            if (fragment) CollectFocusGeometryTree(*fragment, inputScope);
        }
        for (const auto& child : node.children)
            CollectFocusGeometryTree(child, inputScope);
    }

    [[nodiscard]] static bool PosterArtworkWithinAdmissionBand(
        const PresentationNode& presented) noexcept {
        if (!FiniteRect(presented.borderBox) ||
            !FiniteRect(presented.ancestorClip) ||
            presented.borderBox.width <= 0.5F ||
            presented.borderBox.height <= 0.5F ||
            presented.ancestorClip.width <= 0.5F ||
            presented.ancestorClip.height <= 0.5F) {
            return false;
        }

        const auto posterLeft = static_cast<double>(presented.borderBox.x);
        const auto posterTop = static_cast<double>(presented.borderBox.y);
        const auto posterRight = posterLeft + presented.borderBox.width;
        const auto posterBottom = posterTop + presented.borderBox.height;
        const auto admissionLeft =
            static_cast<double>(presented.ancestorClip.x) -
            presented.borderBox.width;
        const auto admissionTop =
            static_cast<double>(presented.ancestorClip.y) -
            presented.borderBox.height;
        const auto admissionRight =
            static_cast<double>(presented.ancestorClip.x) +
            presented.ancestorClip.width + presented.borderBox.width;
        const auto admissionBottom =
            static_cast<double>(presented.ancestorClip.y) +
            presented.ancestorClip.height + presented.borderBox.height;
        return std::min(posterRight, admissionRight) -
                    std::max(posterLeft, admissionLeft) > 0.5 &&
            std::min(posterBottom, admissionBottom) -
                    std::max(posterTop, admissionTop) > 0.5;
    }

    void DrawNode(
        const WidgetNode& node,
        const std::wstring_view inheritedInputScope = {}) {
        const std::wstring_view inputScope = !node.inputScopeId.empty()
            ? std::wstring_view(node.inputScopeId)
            : inheritedInputScope.empty() ? std::wstring_view(node.id) : inheritedInputScope;
        const auto narrowId = NarrowStableId(node.id);
        const auto preparedNode = prepared.find(narrowId);
        const auto presentedNode = presentation.find(narrowId);
        if (preparedNode == prepared.end() || presentedNode == presentation.end()) return;
        const auto& style = preparedNode->second.paintStyle;
        const auto& presented = presentedNode->second;
        const auto focused = node.id == focusedId;
        const auto opacity = presented.motion.value.opacity;
        const auto paintRect = ScaleRect(
            presented.borderBox, presented.motion.value.scale);
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
        result.elementRects[node.id] = presented.borderBox;
        result.elementVisibleRects[node.id] = presented.visibleBox;
        if (node.kind == L"actionSurface" &&
            node.actionSurfacePresentation == L"poster" &&
            node.children.size() == 2U) {
            result.posterArtworkRects[node.children.front().id] = presented.borderBox;
        }
#endif

        const auto visibleRect = presented.visibleBox;
        if (node.kind == L"scroll") {
            if (const auto* box = layout.Find(narrowId);
                box && box->scrollAxis != declarative::ScrollAxis::None) {
                result.scrollViewports.insert_or_assign(
                    node.id,
                    RenderScrollViewport{
                        box->scrollAxis,
                        Intersection(presented.contentBox, presented.ancestorClip),
                        box->scrollOffset,
                        box->maximumScrollOffset,
                    });
            }
        }
        if (node.kind == L"windowPreview") {
            const auto clip = Intersection(presented.contentBox, presented.ancestorClip);
            if (clip.width > 0.5F && clip.height > 0.5F)
                result.windowPreviewRegions.push_back({node.id, node.windowId, presented.contentBox, clip});
        }
        if (node.kind == L"mediaViewport") {
            const auto mediaClip = Intersection(
                presented.contentBox, presented.ancestorClip);
            if (presented.contentBox.width > 0.5F &&
                presented.contentBox.height > 0.5F &&
                mediaClip.width > 0.5F && mediaClip.height > 0.5F) {
                result.mediaViewportRegions.push_back({
                    node.id,
                    node.mediaSessionId,
                    presented.contentBox,
                    mediaClip,
                });
            }
        }
        const bool semanticNode =
            node.kind == L"button" || node.kind == L"slider" ||
            node.kind == L"actionSurface" || node.kind == L"image" ||
            node.kind == L"icon" || node.kind == L"loadingIndicator" ||
            node.kind == L"progress" || node.kind == L"windowPreview" || node.kind == L"mediaViewport" ||
            (node.kind == L"text" &&
                (!node.text.empty() || !node.accessibilityLabel.empty()));
        if (options.collectAccessibility && semanticNode &&
            visibleRect.width > 0.5F && visibleRect.height > 0.5F) {
            result.accessibilityRegions.push_back({node.id, visibleRect});
        }

        CollectFocusGeometry(node, inputScope, presented);

        if (!target) {
            if (node.kind == L"focusPresentationSurface") {
                const auto selection = ResolveFocusPresentationSelection(
                    snapshot->root, focusedId);
                const auto* fragment = selection.surface == &node
                    ? selection.fragment
                    : !node.defaultFocusPresentation.empty()
                        ? &node.defaultFocusPresentation.front()
                        : nullptr;
                if (fragment) DrawNode(*fragment, inputScope);
            }
            for (const auto& child : node.children) DrawNode(child, inputScope);
            DrawCollectionLoading(node, style, Intersection(presented.contentBox, presented.visibleBox), opacity);
            return;
        }
        target->PushAxisAlignedClip(
            D2DRect(presented.visibleBox), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        // Slider background is its track color. Painting the generic surface
        // first duplicates that color across the complete 44-DIP hit target.
        // Authors can wrap a Slider in a Card/Row when they want a filled
        // control surface; the Slider itself stays visually lightweight.
        const bool compositorBackground = node.kind == L"backgroundSurface" &&
            TrySelectCompositorBackground(node, style, paintRect, opacity);
        if (node.kind != L"slider" && node.kind != L"loadingIndicator" &&
            !compositorBackground)
            DrawSurface(node, style, paintRect, opacity);

        if (node.kind == L"backgroundSurface" && !compositorBackground)
            DrawBackgroundSurfaceImage(node, style, paintRect, opacity);
        else if (compositorBackground)
            DrawBackgroundSurfaceOverlays(style, paintRect, opacity);

        if (node.kind == L"actionSurface" &&
            node.actionSurfacePresentation == L"poster") {
            if (node.children.size() == 2U) {
                if (PosterArtworkWithinAdmissionBand(presented)) {
                    const auto& artwork = node.children.front();
                    if (const auto preparedArtwork = prepared.find(NarrowStableId(artwork.id));
                        preparedArtwork != prepared.end()) {
                        DrawImage(
                            artwork, preparedArtwork->second.paintStyle,
                            paintRect, opacity, false);
                    }
                }
            } else {
                DrawSemanticIcon(
                    node, style,
                    Inset(paintRect, std::min(paintRect.width, paintRect.height) * 0.34F),
                    opacity * 0.65F, L"play");
            }
        }

        if (node.kind == L"text") {
            DrawTextContent(node, style, presented.contentBox, opacity);
        } else if (node.kind == L"button") {
            auto textRect = presented.contentBox;
            const bool hasLeading = !node.imageSource.empty() ||
                !node.artworkHandle.empty() || !node.glyph.empty() ||
                node.packageIcon.has_value();
            const bool hasText = !node.text.empty();
            const bool reserveStateCue = ReservesTrailingButtonStateCue(node);
            const auto maximumLeadingSize =
                node.imageSource.empty() && node.artworkHandle.empty() ? 32.0F : 44.0F;
            const auto iconSize = hasLeading
                ? std::min(maximumLeadingSize, std::max(0.0F, textRect.height))
                : 0.0F;
            const auto alignment = hasText
                ? ResolveButtonContentAlignment(node, style, focused, node.id == pressedId)
                : NativeTextAlign::Center;
            const auto budget = DeclarativeRenderer::ComputeButtonContentPlacement(
                textRect, iconSize, std::numeric_limits<float>::max(), hasLeading, hasText,
                reserveStateCue, alignment);
            const auto measured = hasText
                ? MeasureText(node, style, {std::max(1.0F, budget.text.width), textRect.height})
                : Size{};
            const auto placement = DeclarativeRenderer::ComputeButtonContentPlacement(
                textRect, iconSize, measured.width, hasLeading, hasText,
                reserveStateCue, alignment);
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
            result.buttonContentPlacements[node.id] = placement;
#endif
            textRect = placement.text;
            if (hasLeading) {
                const Rect iconRect = placement.leading;
                if (!node.imageSource.empty()) {
                    const auto visibleImageRect = Intersection(
                        iconRect, presented.visibleBox);
                    if (visibleImageRect.width > 0.5F && visibleImageRect.height > 0.5F)
                        DrawImage(node, style, iconRect, opacity, focused);
                }
                else if (node.packageIcon && DrawPackageIcon(
                    style, iconRect, opacity, *node.packageIcon)) {
                    // The semantic glyph remains the deterministic fallback.
                }
                else if (!node.glyph.empty())
                    DrawSemanticIcon(node, style, iconRect, opacity, node.glyph);
            }
            if (hasText) DrawTextContent(
                node, style, textRect, opacity, NativeTextVerticalAlignment::Center);
            DrawStateCue(node, style, paintRect, opacity, placement.trailingStateCue);
        } else if (node.kind == L"progress") {
            DrawProgress(node, style, presented.contentBox, opacity);
        } else if (node.kind == L"slider") {
            DrawSlider(
                node, style, presented.contentBox, opacity, focused,
                node.id == options.activeSliderElementId &&
                    !node.isDisabled && !node.isBusy);
        } else if (node.kind == L"image") {
            const auto visibleImageRect = Intersection(
                paintRect, presented.visibleBox);
            if (visibleImageRect.width > 0.5F && visibleImageRect.height > 0.5F)
                DrawImage(node, style, paintRect, opacity, focused);
        } else if (node.kind == L"icon") {
            if (!node.packageIcon || !DrawPackageIcon(
                    style, presented.contentBox, opacity, *node.packageIcon))
                DrawSemanticIcon(node, style, presented.contentBox, opacity, node.glyph);
        } else if (node.kind == L"loadingIndicator") {
            const auto visibleIndicatorRect = Intersection(
                presented.contentBox, presented.visibleBox);
            DrawLoadingIndicatorNode(node, style, visibleIndicatorRect, opacity);
            if (!options.accessibility.reducedMotion &&
                visibleIndicatorRect.width > 0.5F && visibleIndicatorRect.height > 0.5F) {
                // The shell owns the next-frame cadence. Invisible or
                // reduced-motion indicators never keep it awake.
                result.animationActive = true;
            }
        } else if (node.kind == L"windowPreview") {
            if (visibleRect.width > 0.5F && visibleRect.height > 0.5F) {
                auto bitmap = options.windowPreviewBitmap
                    ? options.windowPreviewBitmap(target, node.windowId) : nullptr;
                if (bitmap) {
                    DrawResolvedImageLayers(node, bitmap.Get(), false, nullptr, nullptr, 0.0F,
                        style, presented.contentBox, opacity, false, false);
                } else {
                    WidgetNode fallback;
                    fallback.id = node.id + L".fallback";
                    fallback.kind = L"text";
                    fallback.text = L"Preview unavailable";
                    DrawTextContent(fallback, style, presented.contentBox, opacity,
                        NativeTextVerticalAlignment::Center);
                }
            }
        } else if (node.kind == L"mediaViewport") {
            // The native GBSS surface is the deterministic loading/error and
            // preview placeholder. The external media visual is composed into
            // this exact content box only after the render commits.
        } else if (node.kind != L"stack" && node.kind != L"row" &&
                   node.kind != L"scroll" && node.kind != L"grid" &&
                   node.kind != L"backgroundSurface" &&
                   node.kind != L"focusPresentationSurface" &&
                   node.kind != L"spacer") {
            if (node.kind != L"actionSurface")
            Add(node.id, L"unknown_kind", L"Unsupported declarative node kind: " + node.kind);
        }

        // This clip bounds the node's own paint. Descendants are drawn from
        // their precomputed presentation clips so overflow-visible containers
        // do not accidentally become clipping ancestors merely because the
        // renderer recurses through them.
        target->PopAxisAlignedClip();
        if (node.kind == L"focusPresentationSurface") {
            const auto selection = ResolveFocusPresentationSelection(
                snapshot->root, focusedId);
            const auto* fragment = selection.surface == &node
                ? selection.fragment
                : !node.defaultFocusPresentation.empty()
                    ? &node.defaultFocusPresentation.front()
                    : nullptr;
            if (fragment) DrawNode(*fragment, inputScope);
        }
        for (const auto& child : node.children) DrawNode(child, inputScope);
        DrawCollectionLoading(node, style, Intersection(presented.contentBox, presented.visibleBox), opacity);
        // Draw semantic state after descendants so it remains visible over a
        // composed tile while the entire surface stays the sole input target.
        if (node.kind == L"actionSurface") {
            target->PushAxisAlignedClip(
                D2DRect(presented.visibleBox), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
            DrawStateCue(node, style, paintRect, opacity);
            target->PopAxisAlignedClip();
        }
        // Defer the focus ring until the entire tree is out of its nested
        // overflow clips. An outline is presentation, not child content, and
        // clipping it at a row/root boundary produces broken half-rings.
        if (focused) {
            deferredFocusNode = &node;
            deferredFocusStyle = &style;
            deferredFocusRect = paintRect;
            deferredFocusOpacity = opacity;
            deferredFocusClip = EffectiveFocusVisibilityClip(node.id);
        }
    }

    void DrawDeferredFocus() {
        if (deferredFocusNode && deferredFocusStyle) {
            if (deferredFocusClip) {
                target->PushAxisAlignedClip(
                    D2DRect(*deferredFocusClip), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
            }
            DrawFocus(*deferredFocusNode, *deferredFocusStyle,
                      deferredFocusRect, deferredFocusOpacity);
            if (deferredFocusClip) target->PopAxisAlignedClip();
        }
    }
};

DeclarativeRenderer::~DeclarativeRenderer() {
    if (imageCache_) imageCache_->ReleaseImageProtection(this);
}
void DeclarativeRenderer::PublishImageProtection() {
    if (!imageCache_) return;
    auto keys = protectedImageKeys_;
    keys.insert(chromeImageKeys_.begin(), chromeImageKeys_.end());
    imageCache_->ProtectImages(this, std::move(keys));
}
void DeclarativeRenderer::SetChromeImageProtection(std::set<std::wstring> keys) {
    chromeImageKeys_ = std::move(keys);
    PublishImageProtection();
}
bool DeclarativeRenderer::VisibleContentImageCompleted(std::uint64_t resourceHash) const noexcept {
    return visibleContentImageHashes_.contains(resourceHash);
}
bool DeclarativeRenderer::ImageProtected(std::wstring_view key) const {
    return protectedImageKeys_.contains(std::wstring{key}) || chromeImageKeys_.contains(std::wstring{key});
}

DeclarativeRenderer::DeclarativeRenderer(
    ID2D1Factory* d2dFactory,
    IDWriteFactory* writeFactory,
    RemoteImageCache* imageCache,
    ArtworkRenderDiagnosticCallback artworkRenderDiagnostic) noexcept
    : d2dFactory_(d2dFactory),
      writeFactory_(writeFactory),
      imageCache_(imageCache),
      artworkRenderDiagnostic_(std::move(artworkRenderDiagnostic)) {}

void DeclarativeRenderer::ReportArtworkRenderDiagnostic(
    const WidgetNode& node,
    const std::wstring_view artworkWidgetId,
    const std::wstring_view stage,
    const std::wstring_view disposition,
    const Size bitmapSize,
    const Rect destination,
    const Rect source,
    const Rect clip,
    const float opacity) noexcept {
    if (!artworkRenderDiagnostic_ || node.artworkHandle.empty() ||
        artworkDiagnosticKeyCount_ >= maximumArtworkDiagnosticRecords_) return;

    try {
        const auto resourceKey = RemoteImageCache::TrustedArtworkKey(
            artworkWidgetId, node.id, node.artworkHandle);
        const auto resourceHash = RemoteImageCache::OpaqueDiagnosticHash(resourceKey);
        auto fingerprint = RemoteImageCache::OpaqueDiagnosticHash(node.id);
        const auto mix = [&](const std::uint64_t value) {
            fingerprint ^= value;
            fingerprint *= 1099511628211ULL;
        };
        mix(RemoteImageCache::OpaqueDiagnosticHash(node.artworkHandle));
        mix(resourceHash);
        mix(RemoteImageCache::OpaqueDiagnosticHash(stage));
        mix(RemoteImageCache::OpaqueDiagnosticHash(disposition));
        mix(std::bit_cast<std::uint32_t>(bitmapSize.width));
        mix(std::bit_cast<std::uint32_t>(bitmapSize.height));
        for (const auto value : {
                 destination.x, destination.y, destination.width,
                 destination.height, source.x, source.y, source.width,
                 source.height, clip.x, clip.y, clip.width, clip.height,
                 opacity}) {
            mix(std::bit_cast<std::uint32_t>(value));
        }
        const auto recordedEnd = artworkDiagnosticKeys_.begin() +
            static_cast<std::ptrdiff_t>(artworkDiagnosticKeyCount_);
        if (std::find(
                artworkDiagnosticKeys_.begin(), recordedEnd, fingerprint) !=
                recordedEnd) return;
        artworkDiagnosticKeys_[artworkDiagnosticKeyCount_++] = fingerprint;

        const auto rect = [](const Rect value) {
            return std::to_wstring(value.x) + L"," + std::to_wstring(value.y) +
                L"," + std::to_wstring(value.width) + L"," +
                std::to_wstring(value.height);
        };
        artworkRenderDiagnostic_(
            L"stage=" + std::wstring{stage} +
            L" node=" + node.id +
            L" resource-hash=" + std::to_wstring(resourceHash) +
            L" handle-hash=" + std::to_wstring(
                RemoteImageCache::OpaqueDiagnosticHash(node.artworkHandle)) +
            L" disposition=" + std::wstring{disposition} +
            L" bitmap=" + std::to_wstring(bitmapSize.width) + L"x" +
                std::to_wstring(bitmapSize.height) +
            L" destination=" + rect(destination) +
            L" source=" + rect(source) +
            L" clip=" + rect(clip) +
            L" opacity=" + std::to_wstring(opacity));
    } catch (...) {
        // Diagnostics cannot own renderer or frame lifetime.
    }
}

ComPtr<ID2D1Bitmap> DeclarativeRenderer::ResolveCompositorBackgroundBitmap(
    ID2D1RenderTarget* const renderTarget,
    const ComputedCompositorBackground& background) {
    if (!renderTarget || background.resourceGeneration != bitmapResourceGeneration_ ||
        !BindBitmapResourceDomain(renderTarget) ||
        background.resourceGeneration != bitmapResourceGeneration_) return {};
    WidgetNode node;
    node.id = background.nodeId;
    node.imageSource = background.imageSource;
    node.artworkHandle = background.artworkHandle;
    node.imageFit = background.imageFit;
    WidgetSnapshot snapshot;
    snapshot.instanceId = background.widgetInstanceId;
    RenderPass pass;
    pass.owner = this;
    pass.target = renderTarget;
    pass.snapshot = &snapshot;
    pass.options.artworkWidgetId = background.artworkWidgetId;
    pass.options.artworkRuntimeGeneration = background.artworkRuntimeGeneration;
    pass.options.artworkPresentationGeneration =
        background.artworkPresentationGeneration;
    pass.options.sizeArtworkToDisplay = background.decodeSize.width != 0;
    pass.options.artworkDecodeSize = background.decodeSize;
    const auto key = pass.ImageKey(node, pass.CachedImageSize(node));
    protectedImageKeys_.insert(key);
    PublishImageProtection();
    auto state = ImagePresentationState::Pending;
    return GetImageBitmap(
        renderTarget, node, pass, pass.options.artworkWidgetId, state);
}

bool DeclarativeRenderer::PaintCompositorBackground(
    ID2D1RenderTarget* const renderTarget,
    const ComputedCompositorBackground& background,
    ID2D1Bitmap* const bitmap,
    const bool baseOnly,
    const float opacity,
    const bool surfaceComposite) const {
    if (!renderTarget) return false;
    WidgetNode node;
    node.id = background.nodeId;
    node.imageFit = background.imageFit;
    RenderPass pass;
    pass.owner = const_cast<DeclarativeRenderer*>(this);
    pass.target = renderTarget;
    if (!baseOnly && surfaceComposite && bitmap) {
        // A rebase already contains image fitting, clipping, and surface
        // opacity. Only the new transition's opacity belongs on this draw.
        renderTarget->DrawBitmap(bitmap, D2DRect(background.bounds), opacity,
            D2D1_BITMAP_INTERPOLATION_MODE_LINEAR);
        return true;
    }
    if (background.clipBounds)
        renderTarget->PushAxisAlignedClip(
            D2DRect(*background.clipBounds), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    bool painted;
    if (baseOnly) {
        pass.DrawSurface(node, background.style, background.bounds,
            background.opacity);
        painted = true;
    } else {
        painted = bitmap && pass.DrawResolvedImageLayers(
            node, bitmap, false, nullptr, nullptr, 0.0F,
            background.style, background.bounds,
            background.opacity * opacity, false, false);
    }
    if (background.clipBounds) renderTarget->PopAxisAlignedClip();
    return painted;
}

std::optional<IncrementalPresentationPlan>
DeclarativeRenderer::PlanPresentationUpdate(
    const WidgetSnapshot& snapshot,
    const WidgetPresentationImpact& impact,
    const Rect viewport) {
    const auto planningStarted = std::chrono::steady_clock::now();
    pendingIncrementalPlan_.reset();
    const auto& cache = incrementalLayoutCache_;
    if (!cache || cache->instanceId != snapshot.instanceId ||
        cache->sequence != impact.baseSequence ||
        snapshot.sequence != impact.sequence ||
        !SameRect(cache->viewport, viewport) ||
        HasWidgetPresentationEffect(
            impact.effects, WidgetPresentationEffect::Structure) ||
        HasWidgetPresentationEffect(
            impact.effects, WidgetPresentationEffect::SurfacePlacement) ||
        HasWidgetPresentationEffect(
            impact.effects, WidgetPresentationEffect::Unknown)) {
        return std::nullopt;
    }
    bool localLayout = HasWidgetPresentationEffect(
        impact.effects, WidgetPresentationEffect::MeasureLayout);
    if (localLayout && !impact.hasNonTextMeasureLayout &&
        !impact.textMeasurementNodeIds.empty()) {
        // Re-measure only changed leaves at every constraint used by the
        // committed layout. Equal intrinsic answers preserve the layout inputs;
        // any difference uses the existing safe-boundary relayout path.
        RenderPass proof;
        proof.owner = this;
        proof.snapshot = &snapshot;
        proof.options = cache->options;
        const auto sameProof = [](const TextMeasurementProof& left,
                                  const TextMeasurementProof& right) noexcept {
            const auto same = [](const float a, const float b) {
                return std::abs(a - b) <= 0.01F;
            };
            return left.valid && right.valid &&
                left.lineCount == right.lineCount &&
                same(left.maximumWidth, right.maximumWidth) &&
                same(left.maximumHeight, right.maximumHeight) &&
                same(left.measuredWidth, right.measuredWidth) &&
                same(left.measuredHeight, right.measuredHeight) &&
                same(left.layoutWidth, right.layoutWidth) &&
                same(left.layoutHeight, right.layoutHeight) &&
                same(left.inkInsetTop, right.inkInsetTop) &&
                same(left.inkInsetBottom, right.inkInsetBottom) &&
                same(left.baseline, right.baseline);
        };
        bool textMeasurementStable = true;
        for (const auto& nodeId : impact.textMeasurementNodeIds) {
            std::vector<const WidgetNode*> path;
            const auto prior = cache->textMeasurementQueries.find(nodeId);
            const auto state = cache->nodes.find(nodeId);
            if (prior == cache->textMeasurementQueries.end() || prior->second.empty() ||
                state == cache->nodes.end() || !FindNodePath(snapshot.root, nodeId, path) ||
                (path.back()->kind != L"text" && path.back()->kind != L"button")) {
                textMeasurementStable = false;
                break;
            }
            for (const auto& query : prior->second) {
                (void)proof.MeasureText(*path.back(), state->second.baseStyle,
                    {query.maximumWidth, query.maximumHeight});
                if (!sameProof(query, proof.textMeasurements.at(nodeId))) {
                    textMeasurementStable = false;
                    break;
                }
            }
            if (!textMeasurementStable) break;
        }
        if (textMeasurementStable) localLayout = false;
    }
    const bool rasterWork = localLayout || HasWidgetPresentationEffect(
        impact.effects, WidgetPresentationEffect::Paint) ||
        HasWidgetPresentationEffect(
            impact.effects, WidgetPresentationEffect::Resource);
    if (!rasterWork) {
        pendingIncrementalPlan_ = PendingIncrementalPlan{
            snapshot.instanceId,
            impact.baseSequence,
            impact.sequence,
            IncrementalPresentationWork::NoRaster,
            {},
            {},
        };
        pendingIncrementalPlan_->comparisonMicroseconds = impact.comparisonMicroseconds;
        pendingIncrementalPlan_->planningMicroseconds = static_cast<std::uint64_t>(
            std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - planningStarted).count());
        return IncrementalPresentationPlan{
            IncrementalPresentationWork::NoRaster, {}};
    }
    if (impact.affectedNodeIds.empty()) return std::nullopt;

    Rect damage{};
    std::vector<std::wstring> boundaries;
    for (const auto& targetId : impact.affectedNodeIds) {
        const auto boundary = localLayout
            ? cache->nodes.find(targetId)
            : cache->nodes.end();
        const auto& damageId = boundary != cache->nodes.end()
            ? boundary->second.safeBoundaryId
            : targetId;
        const auto bounds = cache->nodes.find(damageId);
        if (bounds == cache->nodes.end()) return std::nullopt;
        damage = UnionRect(
            damage, Inset(bounds->second.paintBounds, -8.0F));
        if (localLayout &&
            std::find(boundaries.begin(), boundaries.end(), damageId) ==
                boundaries.end()) {
            boundaries.push_back(damageId);
        }
    }
    damage = Intersection(damage, viewport);
    if (damage.width <= 0.0F || damage.height <= 0.0F) return std::nullopt;

    if (localLayout && boundaries.size() > 1) {
        const auto allBoundaries = boundaries;
        std::erase_if(boundaries, [&](const std::wstring& candidate) {
            auto parent = cache->nodes.find(candidate);
            while (parent != cache->nodes.end() &&
                   !parent->second.parentId.empty()) {
                if (std::find(allBoundaries.begin(), allBoundaries.end(),
                              parent->second.parentId) != allBoundaries.end()) {
                    return true;
                }
                parent = cache->nodes.find(parent->second.parentId);
            }
            return false;
        });
    }

    const auto work = localLayout
        ? IncrementalPresentationWork::LocalLayout
        : IncrementalPresentationWork::PaintOnly;
    pendingIncrementalPlan_ = PendingIncrementalPlan{
        snapshot.instanceId,
        impact.baseSequence,
        impact.sequence,
        work,
        damage,
        std::move(boundaries),
    };
    pendingIncrementalPlan_->comparisonMicroseconds = impact.comparisonMicroseconds;
    pendingIncrementalPlan_->planningMicroseconds = static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - planningStarted).count());
    return IncrementalPresentationPlan{work, damage};
}

std::optional<IncrementalPresentationPlan>
DeclarativeRenderer::PlanFocusUpdate(
    const WidgetSnapshot& snapshot,
    const std::wstring_view priorFocusedElementId,
    const std::wstring_view nextFocusedElementId,
    const Rect viewport) {
    return PlanFocusUpdate(
        snapshot, priorFocusedElementId, nextFocusedElementId, viewport, {});
}

std::optional<IncrementalPresentationPlan>
DeclarativeRenderer::PlanFocusUpdate(
    const WidgetSnapshot& snapshot,
    const std::wstring_view priorFocusedElementId,
    const std::wstring_view nextFocusedElementId,
    const Rect viewport,
    const std::vector<std::wstring>& additionalPaintNodeIds) {
    pendingIncrementalPlan_.reset();
    const auto& cache = incrementalLayoutCache_;
    if (!cache || cache->instanceId != snapshot.instanceId ||
        cache->sequence != snapshot.sequence ||
        cache->focusedElementId != priorFocusedElementId ||
        priorFocusedElementId == nextFocusedElementId ||
        !SameRect(cache->viewport, viewport)) {
        return std::nullopt;
    }

    Rect damage{};
    const auto addNode = [&](const std::wstring_view nodeId,
                             const bool includeFutureFocus) -> bool {
        if (nodeId.empty()) return true;
        const auto found = cache->nodes.find(std::wstring(nodeId));
        if (found == cache->nodes.end()) return false;

        const bool targetVisible =
            found->second.visibleBounds.width > 0.0F &&
            found->second.visibleBounds.height > 0.0F;
        if (!targetVisible && !includeFutureFocus) return false;

        std::vector<Rect> scrollViewports;
        std::set<std::wstring, std::less<>> visited;
        auto ancestor = found;
        while (ancestor != cache->nodes.end() &&
               !ancestor->second.parentId.empty()) {
            if (!visited.insert(ancestor->second.parentId).second)
                return false;
            ancestor = cache->nodes.find(ancestor->second.parentId);
            if (ancestor != cache->nodes.end() &&
                ancestor->second.scrollBoundary) {
                const auto bounded = Intersection(
                    ancestor->second.paintBounds, viewport);
                if (bounded.width <= 0.0F || bounded.height <= 0.0F)
                    return false;
                scrollViewports.push_back(bounded);
            }
        }

        if (!targetVisible) {
            // A logically valid navigation target can be outside the current
            // clip precisely because focus-follow has not scrolled it into
            // view yet. Only one committed scroll viewport is a safe bounded
            // owner for both the exposed and vacated pixels. Nested or absent
            // owners remain a conservative full-raster fallback.
            if (scrollViewports.size() != 1) return false;
            damage = UnionRect(damage, scrollViewports.front());
            return true;
        }

        damage = UnionRect(
            damage,
            includeFutureFocus
                ? Inset(found->second.visibleBounds, -8.0F)
                : found->second.paintBounds);
        for (const auto scrollViewport : scrollViewports)
            damage = UnionRect(damage, scrollViewport);
        return true;
    };
    if (!addNode(priorFocusedElementId, false) ||
        !addNode(nextFocusedElementId, true)) {
        return std::nullopt;
    }
    const auto priorBackground = ResolveFocusBackgroundSelection(
        snapshot.root, priorFocusedElementId);
    const auto nextBackground = ResolveFocusBackgroundSelection(
        snapshot.root, nextFocusedElementId);
    if (!SameFocusBackgroundSelection(priorBackground, nextBackground)) {
        for (const auto* surface :
             {priorBackground.surface, nextBackground.surface}) {
            if (!surface) continue;
            const auto found = cache->nodes.find(surface->id);
            if (found == cache->nodes.end()) return std::nullopt;
            const auto bounded = Intersection(found->second.paintBounds, viewport);
            if (bounded.width > 0.0F && bounded.height > 0.0F)
                damage = UnionRect(damage, bounded);
        }
    }
    const auto priorPresentation = ResolveFocusPresentationSelection(
        snapshot.root, priorFocusedElementId);
    const auto nextPresentation = ResolveFocusPresentationSelection(
        snapshot.root, nextFocusedElementId);
    if (!SameFocusPresentationSelection(priorPresentation, nextPresentation)) {
        // The selected fragment participates in intrinsic layout and
        // accessibility. A focus move therefore requires a complete native
        // rerender from the already-admitted immutable snapshot, never a
        // worker request or semantic/action-authority change.
        return std::nullopt;
    }
    for (const auto& nodeId : additionalPaintNodeIds) {
        const auto found = cache->nodes.find(nodeId);
        if (found == cache->nodes.end()) return std::nullopt;
        const auto bounded = Intersection(found->second.paintBounds, viewport);
        if (bounded.width > 0.0F && bounded.height > 0.0F)
            damage = UnionRect(damage, bounded);
    }
    damage = Intersection(damage, viewport);
    if (damage.width <= 0.0F || damage.height <= 0.0F) return std::nullopt;

    pendingIncrementalPlan_ = PendingIncrementalPlan{
        snapshot.instanceId,
        snapshot.sequence,
        snapshot.sequence,
        IncrementalPresentationWork::PaintOnly,
        damage,
        {},
    };
    return IncrementalPresentationPlan{
        IncrementalPresentationWork::PaintOnly, damage};
}

std::optional<FocusedFreeScrollPlan>
DeclarativeRenderer::PlanFocusedFreeScroll(
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId,
    const declarative::ScrollAxis axis,
    const float deltaDip,
    const Rect viewport,
    const std::wstring_view exactScrollId,
    FocusedFreeScrollPlanDiagnostic* diagnostic) {
    FocusedFreeScrollPlanDiagnostic localDiagnostic;
    localDiagnostic.requestedViewport = viewport;
    localDiagnostic.requestedSequence = snapshot.sequence;
    localDiagnostic.requestedAxis = axis;
    const auto reject = [&](const FocusedFreeScrollPlanDisposition disposition) {
        localDiagnostic.disposition = disposition;
        if (diagnostic) *diagnostic = localDiagnostic;
        return std::optional<FocusedFreeScrollPlan>{};
    };
    const auto& cache = incrementalLayoutCache_;
    if (!cache)
        return reject(FocusedFreeScrollPlanDisposition::MissingCheckpoint);
    localDiagnostic.cachedViewport = cache->viewport;
    localDiagnostic.cachedSequence = cache->sequence;
    if (cache->instanceId != snapshot.instanceId)
        return reject(FocusedFreeScrollPlanDisposition::InstanceMismatch);
    if (cache->sequence != snapshot.sequence)
        return reject(FocusedFreeScrollPlanDisposition::SequenceMismatch);
    if (exactScrollId.empty() && cache->focusedElementId != focusedElementId)
        return reject(FocusedFreeScrollPlanDisposition::FocusMismatch);
    if (axis == declarative::ScrollAxis::None)
        return reject(FocusedFreeScrollPlanDisposition::InvalidAxis);
    if (!std::isfinite(deltaDip) || std::abs(deltaDip) <= 0.001F)
        return reject(FocusedFreeScrollPlanDisposition::InvalidDelta);
    if (!SameRect(cache->viewport, viewport))
        return reject(FocusedFreeScrollPlanDisposition::ViewportMismatch);

    std::vector<const WidgetNode*> path;
    if (!FindNodePath(
            snapshot.root,
            exactScrollId.empty() ? focusedElementId : exactScrollId,
            path)) {
        return reject(FocusedFreeScrollPlanDisposition::MissingTarget);
    }

    bool foundScroll = false;
    for (auto item = path.rbegin(); item != path.rend(); ++item) {
        const WidgetNode& candidate = **item;
        if (candidate.kind != L"scroll") continue;
        if (!exactScrollId.empty() && candidate.id != exactScrollId) continue;
        foundScroll = true;
        const auto visible = cache->scrollViewports.find(candidate.id);
        const auto* box = cache->layout.Find(NarrowStableId(candidate.id));
        if (visible == cache->scrollViewports.end()) {
            localDiagnostic.disposition =
                FocusedFreeScrollPlanDisposition::MissingScrollViewport;
            continue;
        }
        localDiagnostic.scrollViewport = visible->second.rect;
        if (!box) {
            localDiagnostic.disposition =
                FocusedFreeScrollPlanDisposition::MissingScrollBox;
            continue;
        }
        localDiagnostic.scrollBox = box->borderBox;
        localDiagnostic.scrollAxis = box->scrollAxis;
        localDiagnostic.priorOffset = box->scrollOffset;
        localDiagnostic.maximumOffset = box->maximumScrollOffset;
        if (box->scrollAxis != axis) {
            localDiagnostic.disposition =
                FocusedFreeScrollPlanDisposition::AxisMismatch;
            continue;
        }
        if (visible->second.rect.width <= 0.0F ||
            visible->second.rect.height <= 0.0F) {
            localDiagnostic.disposition =
                FocusedFreeScrollPlanDisposition::EmptyScrollViewport;
            continue;
        }
        std::wstring stateKey(snapshot.instanceId);
        stateKey.push_back(L'\x1f');
        stateKey.append(snapshot.activeInputScopeId);
        stateKey.push_back(L'\x1f');
        stateKey.append(candidate.id);
        // More than one input sample may arrive before the pending paint.
        // Accumulate against the requested offset, not the older painted box.
        const auto existing = scrollOffsets_.find(stateKey);
        const float priorOffset = existing != scrollOffsets_.end()
            ? existing->second.offset : box->scrollOffset;
        float minimumOffset = 0.0F;
        float maximumOffset = box->maximumScrollOffset;
        // Sequential cursors cannot seek into spacer-only ranges. Keep a real
        // boundary item visible so the existing edge demand can load more.
        if (candidate.virtualCollectionWindow && cache->collections.contains(candidate.id)) {
            const auto& collection = cache->collections.at(candidate.id);
            float first = std::numeric_limits<float>::max();
            float last = std::numeric_limits<float>::lowest();
            for (const auto& geometry : collection.itemGeometry) {
                if (!geometry.valid) continue;
                first = std::min(first, geometry.position + box->scrollOffset);
                last = std::max(last, geometry.position + geometry.extent + box->scrollOffset);
            }
            if (first <= last) {
                minimumOffset = std::clamp(first, 0.0F, maximumOffset);
                const float extent = axis == declarative::ScrollAxis::Vertical
                    ? box->contentBox.height : box->contentBox.width;
                maximumOffset = std::clamp(last - extent, minimumOffset, maximumOffset);
            }
        }
        const float next = std::clamp(priorOffset + deltaDip, minimumOffset, maximumOffset);
        if (std::abs(next - priorOffset) <= 0.001F) {
            localDiagnostic.disposition =
                FocusedFreeScrollPlanDisposition::OffsetBoundary;
            continue;
        }

        auto damage = Intersection(visible->second.rect, viewport);
        if (damage.width <= 0.0F || damage.height <= 0.0F) {
            return reject(FocusedFreeScrollPlanDisposition::EmptyDamage);
        }
        std::vector<std::wstring> boundaries{candidate.id};
        if (pendingIncrementalPlan_ &&
            pendingIncrementalPlan_->instanceId == snapshot.instanceId &&
            pendingIncrementalPlan_->baseSequence == cache->sequence &&
            pendingIncrementalPlan_->sequence == snapshot.sequence &&
            pendingIncrementalPlan_->work != IncrementalPresentationWork::NoRaster) {
            damage = UnionRect(damage, pendingIncrementalPlan_->damage);
            for (const auto& boundary : pendingIncrementalPlan_->layoutBoundaries)
                if (std::ranges::find(boundaries, boundary) == boundaries.end())
                    boundaries.push_back(boundary);
        }
        auto& state = scrollOffsets_[stateKey];
        state.offset = next;
        state.lastAccess = ++scrollStateAccessClock_;
        pendingIncrementalPlan_ = PendingIncrementalPlan{
            snapshot.instanceId,
            snapshot.sequence,
            snapshot.sequence,
            IncrementalPresentationWork::LocalLayout,
            damage,
            std::move(boundaries),
        };
        localDiagnostic.disposition = FocusedFreeScrollPlanDisposition::Planned;
        if (diagnostic) *diagnostic = localDiagnostic;
        return FocusedFreeScrollPlan{
            IncrementalPresentationPlan{
                IncrementalPresentationWork::LocalLayout, damage},
            candidate.id,
            axis,
            visible->second.rect,
            priorOffset,
            next,
            box->maximumScrollOffset,
        };
    }
    if (!foundScroll)
        localDiagnostic.disposition =
            FocusedFreeScrollPlanDisposition::MissingTarget;
    if (diagnostic) *diagnostic = localDiagnostic;
    return std::nullopt;
}

std::optional<IncrementalPresentationPlan>
DeclarativeRenderer::PlanRetainedPaint(
    const WidgetSnapshot& snapshot, const std::optional<Rect> requestedDamage) {
    const auto& cache = incrementalLayoutCache_;
    if (!cache || cache->instanceId != snapshot.instanceId ||
        cache->sequence != snapshot.sequence) return std::nullopt;
    return PlanBackgroundSurfaceAnimationFrame(requestedDamage.value_or(cache->viewport));
}

std::optional<IncrementalPresentationPlan>
DeclarativeRenderer::PlanBackgroundSurfaceAnimationFrame(Rect damage) {
    const auto& cache = incrementalLayoutCache_;
    if (!cache || !FiniteRect(damage)) return std::nullopt;
    damage = Intersection(damage, cache->viewport);
    if (damage.width <= 0.0F || damage.height <= 0.0F) return std::nullopt;
    if (pendingIncrementalPlan_ &&
        pendingIncrementalPlan_->instanceId == cache->instanceId &&
        pendingIncrementalPlan_->baseSequence == cache->sequence &&
        pendingIncrementalPlan_->sequence == cache->sequence &&
        pendingIncrementalPlan_->work != IncrementalPresentationWork::NoRaster) {
        pendingIncrementalPlan_->damage = UnionRect(
            pendingIncrementalPlan_->damage, damage);
        return IncrementalPresentationPlan{
            pendingIncrementalPlan_->work, pendingIncrementalPlan_->damage};
    }
    pendingIncrementalPlan_ = PendingIncrementalPlan{
        cache->instanceId,
        cache->sequence,
        cache->sequence,
        IncrementalPresentationWork::PaintOnly,
        damage,
        {},
    };
    return IncrementalPresentationPlan{
        IncrementalPresentationWork::PaintOnly, damage};
}

void DeclarativeRenderer::CancelPresentationUpdatePlan() noexcept {
    pendingIncrementalPlan_.reset();
}

bool DeclarativeRenderer::AcceptNoRasterPresentationUpdate(
    const WidgetSnapshot& snapshot,
    const WidgetPresentationImpact& impact,
    const Rect viewport) noexcept {
    if (!pendingIncrementalPlan_ || !incrementalLayoutCache_ ||
        pendingIncrementalPlan_->work != IncrementalPresentationWork::NoRaster ||
        pendingIncrementalPlan_->instanceId != snapshot.instanceId ||
        pendingIncrementalPlan_->baseSequence != impact.baseSequence ||
        pendingIncrementalPlan_->sequence != impact.sequence ||
        incrementalLayoutCache_->instanceId != snapshot.instanceId ||
        incrementalLayoutCache_->sequence != impact.baseSequence ||
        snapshot.sequence != impact.sequence ||
        !SameRect(incrementalLayoutCache_->viewport, viewport)) {
        pendingIncrementalPlan_.reset();
        return false;
    }
    incrementalLayoutCache_->sequence = impact.sequence;
    pendingIncrementalPlan_.reset();
    return true;
}

WidgetComputedStyle ResolveDeclarativeComputedStyle(
    const WidgetNode& node,
    const bool focused,
    const bool pressed) {
    auto result = node.baseStyle;
    if (focused) {
        for (const auto& [property, value] : node.focusedStyle)
            result[property] = value;
    }
    if (focused && pressed) {
        for (const auto& [property, value] : node.pressedStyle)
            result[property] = value;
    }
    return result;
}

RenderResult DeclarativeRenderer::Render(
    ID2D1RenderTarget* renderTarget,
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId,
    const Rect viewport,
    const DeclarativeRenderOptions& options) {
    const auto renderStarted = std::chrono::steady_clock::now();
    const auto textHitsBefore = textLayoutCache_.hits;
    const auto textMissesBefore = textLayoutCache_.misses;
    RenderPass pass;
    pass.owner = this;
    pass.target = renderTarget;
    pass.snapshot = &snapshot;
    pass.focusedId = focusedElementId;
    pass.pressedId = options.pressedElementId;
    pass.viewport = viewport;
    const auto responsiveViewport = options.responsiveViewport.value_or(
        Size{viewport.width, viewport.height});
    pass.compactMode = IsCompactResponsiveSurface(responsiveViewport);
    pass.options = options;
    const auto* previousCollectionCache = incrementalLayoutCache_ &&
            incrementalLayoutCache_->instanceId == snapshot.instanceId
        ? &*incrementalLayoutCache_
        : nullptr;
    if (!FiniteRect(viewport) || viewport.width < 0.0F || viewport.height < 0.0F) {
        pass.Add({}, L"invalid_viewport", L"Viewport must contain finite non-negative geometry.",
            RenderDiagnosticSeverity::Error);
        return pass.result;
    }
    if (!renderTarget)
        pass.Add({}, L"missing_render_target", L"Render planning completed without an ID2D1 render target.",
            RenderDiagnosticSeverity::Error);
    if (!writeFactory_)
        pass.Add({}, L"missing_write_factory", L"Intrinsic text uses fallback metrics without DirectWrite.");

    const auto animationTimestamp = options.animationTimestampMilliseconds.value_or(
        static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now().time_since_epoch()).count()));
    pass.options.animationTimestampMilliseconds = animationTimestamp;
    motionTimeline_.BeginFrame(animationTimestamp);

    if (renderTarget && !BindBitmapResourceDomain(renderTarget)) {
        pass.Add({}, L"image_resource_domain",
            L"Image bitmap cache could not identify the Direct2D resource domain.");
    }
    // BindBitmapResourceDomain retires target-domain bitmap state when the
    // Direct2D owner changes. Copy transition state only after that boundary,
    // so a pass can never draw or re-adopt committed/incoming COM references
    // from the prior render target.
    pass.focusBackgrounds = focusBackgrounds_;
    pass.focusBackgroundAccessClock = focusBackgroundAccessClock_;
    const auto preparationOptionsMatch = [&]() {
        if (!incrementalLayoutCache_) return false;
        const auto& previous = incrementalLayoutCache_->options;
        const auto& a = previous.accessibility;
        const auto& b = options.accessibility;
        const auto oldResponsive = previous.responsiveViewport.value_or(Size{viewport.width, viewport.height});
        const auto newResponsive = options.responsiveViewport.value_or(Size{viewport.width, viewport.height});
        return previous.pixelScale == options.pixelScale && previous.rootFontSizePx == options.rootFontSizePx &&
            previous.surfaceBackground == options.surfaceBackground &&
            oldResponsive.width == newResponsive.width && oldResponsive.height == newResponsive.height &&
            a.textScale == b.textScale && a.minimumFontWeight == b.minimumFontWeight &&
            a.minimumFocusRingPx == b.minimumFocusRingPx && a.reducedMotion == b.reducedMotion &&
            a.reducedTransparency == b.reducedTransparency && !a.contrastHook && !b.contrastHook;
    };
    const bool pendingMatches = preparationOptionsMatch() && pendingIncrementalPlan_ &&
        pendingIncrementalPlan_->work != IncrementalPresentationWork::NoRaster &&
        incrementalLayoutCache_ &&
        pendingIncrementalPlan_->instanceId == snapshot.instanceId &&
        pendingIncrementalPlan_->baseSequence == incrementalLayoutCache_->sequence &&
        pendingIncrementalPlan_->sequence == snapshot.sequence &&
        SameRect(incrementalLayoutCache_->viewport, viewport);
    if (pendingMatches) {
        pass.layout = incrementalLayoutCache_->layout;
        pass.textMeasurements = incrementalLayoutCache_->textMeasurements;
        pass.textMeasurementQueries = incrementalLayoutCache_->textMeasurementQueries;
        if (pendingIncrementalPlan_->work ==
            IncrementalPresentationWork::PaintOnly) {
            pass.PrepareAgainstCurrentLayout();
        } else if (!pass.BuildLocalLayout(
                pendingIncrementalPlan_->layoutBoundaries,
                incrementalLayoutCache_->nodes)) {
            pass.BuildLayout(
                !options.suppressFocusedDescendantFollow,
                true,
                options.suppressFocusedDescendantFollow
                    ? RenderPass::CollectionAnchorPolicy::ReconcileContentChanges
                    : RenderPass::CollectionAnchorPolicy::Reconcile);
        }
        if (pass.layout.valid()) pass.SynchronizeScrollState();
    } else {
        pass.BuildLayout(
            !options.suppressFocusedDescendantFollow, true,
            options.suppressFocusedDescendantFollow
                ? RenderPass::CollectionAnchorPolicy::ReconcileContentChanges
                : RenderPass::CollectionAnchorPolicy::Reconcile);
    }
    const auto preparationFinished = std::chrono::steady_clock::now();
    pass.ResolvePresentationWithFocusFollow();
    const auto presentationFinished = std::chrono::steady_clock::now();
    const auto cornerRadius = std::isfinite(options.surfaceCornerRadiusPx)
        ? std::clamp(options.surfaceCornerRadiusPx, 0.0F,
                     std::min(viewport.width, viewport.height) * 0.5F)
        : 0.0F;
    const bool roundedClip = cornerRadius > 0.0F && renderTarget &&
        EnsureSurfaceClip(renderTarget, viewport, cornerRadius);
    if (roundedClip) {
        D2D1_LAYER_PARAMETERS parameters{};
        parameters.contentBounds = D2DRect(viewport);
        parameters.geometricMask = surfaceClipGeometry_.Get();
        parameters.maskAntialiasMode = D2D1_ANTIALIAS_MODE_PER_PRIMITIVE;
        parameters.maskTransform = D2D1::Matrix3x2F::Identity();
        parameters.opacity = 1.0F;
        renderTarget->PushLayer(parameters, surfaceClipLayer_.Get());
    } else if (cornerRadius > 0.0F && renderTarget) {
        pass.Add({}, L"surface_clip_fallback",
                 L"Rounded host viewport clip could not be created.");
        renderTarget->PushAxisAlignedClip(
            D2DRect(viewport), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    }
    const auto clipSetupFinished = std::chrono::steady_clock::now();
    const auto priorImageProtection = protectedImageKeys_;
    if (options.sizeArtworkToDisplay) {
        pass.GatherVisibleImages(snapshot.root);
        if (options.retainedCompositorBackground) {
            const auto& background = *options.retainedCompositorBackground;
            if (background.widgetInstanceId == snapshot.instanceId && background.artworkWidgetId == options.artworkWidgetId)
                pass.visibleImageKeys.insert(!background.artworkHandle.empty()
                    ? RemoteImageCache::TrustedArtworkKey(background.artworkWidgetId, background.nodeId, background.artworkHandle, background.decodeSize)
                    : RemoteImageCache::VariantKey(background.imageSource, background.decodeSize));
        }
        protectedImageKeys_.insert(pass.visibleImageKeys.begin(), pass.visibleImageKeys.end());
        PublishImageProtection();
    }
    pass.DrawNode(snapshot.root);
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
    if (options.failAfterNodeDrawForTesting) {
        pass.Add({}, L"forced_post_draw_failure",
            L"The test fixture rejected the frame after target-backed drawing.",
            RenderDiagnosticSeverity::Error);
    }
#endif
    const auto nodeDrawFinished = std::chrono::steady_clock::now();
    pass.DrawDeferredFocus();
    const auto deferredFocusFinished = std::chrono::steady_clock::now();
    const bool otherAnimationActive =
        pass.result.animationActive || motionTimeline_.EndFrame();
    pass.result.animationActive =
        otherAnimationActive || pass.backgroundSurfaceAnimationActive;
    if (pass.backgroundSurfaceAnimationActive && !otherAnimationActive)
        pass.result.backgroundSurfaceAnimationDamage =
            pass.backgroundSurfaceAnimationDamage;
    if (roundedClip) renderTarget->PopLayer();
    else if (cornerRadius > 0.0F && renderTarget) renderTarget->PopAxisAlignedClip();
    const auto hasErrors = std::any_of(
        pass.result.diagnostics.begin(),
        pass.result.diagnostics.end(),
        [](const RenderDiagnostic& item) {
            return item.severity == RenderDiagnosticSeverity::Error;
        });
    pass.result.succeeded = !hasErrors && pass.layout.valid() && renderTarget;
    if (pass.result.succeeded) {
        pass.RetireAbsentFocusBackgroundSurfaces();
        focusBackgrounds_ = std::move(pass.focusBackgrounds);
        RecalculateFocusBackgroundCompositeBytes();
        focusBackgroundAccessClock_ = pass.focusBackgroundAccessClock;
        if (pass.backgroundSurfaceSettleWake &&
            !pass.backgroundSurfaceAnimationActive && !otherAnimationActive)
            pass.result.backgroundSurfaceSettleWake =
                pass.backgroundSurfaceSettleWake;
        pass.result.responsiveSurface = ResponsiveSurfacePresentation{
            responsiveViewport,
            pass.compactMode
                ? ResponsiveSurfaceMode::Compact
                : ResponsiveSurfaceMode::Expanded,
        };
    }
    std::wstring collectionAdmissionSummary;
    if (pass.result.succeeded) {
        auto collectionObservations = pass.CaptureCollectionObservations();
        collectionAdmissionSummary = pass.BuildCollectionAdmissionSummary(
            previousCollectionCache, collectionObservations);
        IncrementalLayoutCache cache;
        cache.instanceId = snapshot.instanceId;
        cache.sequence = snapshot.sequence;
        cache.focusedElementId = std::wstring{focusedElementId};
        cache.requestedFocusId = snapshot.initialFocusId;
        cache.viewport = viewport;
        cache.layout = std::move(pass.layout);
        cache.options = options;
        cache.textMeasurements = std::move(pass.textMeasurements);
        cache.collections = std::move(collectionObservations);
        cache.scrollViewports = pass.result.scrollViewports;
        const auto retain = [&](const auto& self,
                                const WidgetNode& node,
                                const std::wstring_view parentId,
                                const std::wstring_view inheritedBoundary) -> void {
            const auto narrowId = NarrowStableId(node.id);
            const auto prepared = pass.prepared.find(narrowId);
            const auto presented = pass.presentation.find(narrowId);
            // A target may use only an already-committed clipping ancestor as
            // its local relayout boundary. Fixed dimensions alone do not
            // contain visible-overflow descendants, and the boundary itself
            // cannot safely relayout against its own old bounds.
            std::wstring descendantBoundary{inheritedBoundary};
            if (prepared != pass.prepared.end() &&
                RenderPass::ClipsDescendants(
                    node, prepared->second.baseStyle) &&
                prepared->second.baseStyle.widthPx() &&
                prepared->second.baseStyle.heightPx() &&
                prepared->second.baseStyle.marginPx().top == 0.0F &&
                prepared->second.baseStyle.marginPx().right == 0.0F &&
                prepared->second.baseStyle.marginPx().bottom == 0.0F &&
                prepared->second.baseStyle.marginPx().left == 0.0F) {
                descendantBoundary = node.id;
            }
            IncrementalNodeState state;
            if (prepared != pass.prepared.end()) state.baseStyle = prepared->second.baseStyle;
            state.parentId = parentId;
            state.safeBoundaryId = std::wstring{inheritedBoundary};
            state.scrollBoundary = node.kind == L"scroll";
            if (presented != pass.presentation.end()) {
                state.visibleBounds = presented->second.visibleBox;
                auto paintBounds = presented->second.visibleBox;
                if (node.id == pass.focusedId &&
                    prepared != pass.prepared.end()) {
                    const auto& style = prepared->second.paintStyle;
                    const auto width = std::max(
                        pass.options.accessibility.minimumFocusRingPx,
                        std::max(2.0F, style.outlineWidthPx()));
                    auto outline = Inset(
                        presented->second.borderBox,
                        -(style.outlineOffsetPx() + width * 0.5F));
                    if (pass.result.currentFocusOutlineClip)
                        outline = Intersection(
                            outline, *pass.result.currentFocusOutlineClip);
                    paintBounds = UnionRect(paintBounds, outline);
                }
                state.paintBounds = paintBounds;
            }
            cache.nodes.insert_or_assign(node.id, std::move(state));
            if (node.kind == L"focusPresentationSurface") {
                const auto selection = ResolveFocusPresentationSelection(
                    snapshot.root, pass.focusedId);
                const auto* fragment = selection.surface == &node
                    ? selection.fragment
                    : !node.defaultFocusPresentation.empty()
                        ? &node.defaultFocusPresentation.front()
                        : nullptr;
                if (fragment)
                    self(self, *fragment, node.id, descendantBoundary);
            }
            for (const auto& child : node.children)
                self(self, child, node.id, descendantBoundary);
        };
        retain(retain, snapshot.root, std::wstring_view{}, std::wstring_view{});
        if (!pass.focusedId.empty()) {
            if (const auto boundary =
                    cache.nodes.find(pass.focusedId);
                boundary != cache.nodes.end()) {
                if (const auto focusedBounds =
                        cache.nodes.find(pass.focusedId);
                    focusedBounds != cache.nodes.end()) {
                    auto boundaryBounds = cache.nodes.find(
                        boundary->second.safeBoundaryId);
                    if (boundaryBounds != cache.nodes.end()) {
                        boundaryBounds->second.paintBounds = UnionRect(
                            boundaryBounds->second.paintBounds,
                            focusedBounds->second.paintBounds);
                    }
                }
            }
        }
        incrementalLayoutCache_ = std::move(cache);
    } else {
        incrementalLayoutCache_.reset();
    }
    if (styleCache_.size() > 4096) {
        std::erase_if(styleCache_, [&](const auto& entry) {
            return !pass.prepared.contains(NarrowStableId(entry.first));
        });
    }
    const auto snapshotComparisonMicroseconds = pendingMatches ? pendingIncrementalPlan_->comparisonMicroseconds : 0;
    const auto updatePlanningMicroseconds = pendingMatches ? pendingIncrementalPlan_->planningMicroseconds : 0;
    pendingIncrementalPlan_.reset();
    const auto finalizationFinished = std::chrono::steady_clock::now();
    const auto elapsed = [](const auto started, const auto finished) {
        return static_cast<std::uint64_t>(
            std::chrono::duration_cast<std::chrono::microseconds>(
                finished - started).count());
    };
    pass.result.timing = DeclarativeRenderTiming{
        elapsed(renderStarted, finalizationFinished),
        elapsed(renderStarted, preparationFinished),
        elapsed(preparationFinished, presentationFinished),
        elapsed(presentationFinished, clipSetupFinished),
        elapsed(clipSetupFinished, nodeDrawFinished),
        elapsed(nodeDrawFinished, deferredFocusFinished),
        elapsed(deferredFocusFinished, finalizationFinished),
    };
    pass.result.timing.snapshotComparisonMicroseconds = snapshotComparisonMicroseconds;
    pass.result.timing.updatePlanningMicroseconds = updatePlanningMicroseconds;
    pass.result.timing.styleResolutionMicroseconds = pass.styleNanoseconds / 1000;
    pass.result.timing.textMeasurementMicroseconds = pass.textNanoseconds / 1000;
    pass.result.timing.layoutMicroseconds = pass.layoutNanoseconds / 1000;
    pass.result.timing.styleCacheHits = pass.styleCacheHits;
    pass.result.timing.styleCacheMisses = pass.styleCacheMisses;
    pass.result.timing.textLayoutCacheHits = textLayoutCache_.hits - textHitsBefore;
    pass.result.timing.textLayoutCacheMisses = textLayoutCache_.misses - textMissesBefore;

    pass.result.timing.focusFollowSummary = pass.BuildFocusFollowSummary();
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
    pass.result.focusFollowPassCount = pass.focusFollowTrace.passCount;
    pass.result.focusFollowConverged = pass.focusFollowTrace.converged;
    pass.result.focusFollowNoProgress = pass.focusFollowTrace.noProgress;
    pass.result.focusFollowCycle = pass.focusFollowTrace.cycle;
    pass.result.focusFollowBoundHit = pass.focusFollowTrace.boundHit;
#endif
    pass.result.timing.collectionAdmissionSummary =
        std::move(collectionAdmissionSummary);
    if (pass.result.succeeded) {
        for (const auto& [surfaceId, keys] : pass.visibleBackgroundImageKeys) {
            if (!pass.result.compositorBackground || pass.result.compositorBackground->nodeId != surfaceId)
                pass.visibleContentImageKeys.insert(keys.begin(), keys.end());
        }
        visibleContentImageHashes_.clear();
        for (const auto& key : pass.visibleContentImageKeys)
            visibleContentImageHashes_.insert(RemoteImageCache::OpaqueDiagnosticHash(key));
    }
    protectedImageKeys_ = pass.result.succeeded ? std::move(pass.visibleImageKeys) : priorImageProtection;
    PublishImageProtection();
    return pass.result;
}

RenderResult DeclarativeRenderer::PrepareFocusEntry(
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId,
    const Rect viewport,
    const DeclarativeRenderOptions& options) {
    RenderPass pass;
    pass.owner = this;
    pass.snapshot = &snapshot;
    pass.focusedId = focusedElementId;
    pass.pressedId = options.pressedElementId;
    pass.viewport = viewport;
    pass.options = options;
    pass.options.collectAccessibility = false;
    pass.options.compositorBackgroundAvailable = false;
    const auto responsiveViewport = options.responsiveViewport.value_or(
        Size{viewport.width, viewport.height});
    pass.compactMode = IsCompactResponsiveSurface(responsiveViewport);

    auto preparedScrollState = scrollOffsets_;
    auto preparedScrollClock = scrollStateAccessClock_;
    auto preparedMotionTimeline = motionTimeline_;
    pass.scrollState = &preparedScrollState;
    pass.scrollAccessClock = &preparedScrollClock;
    pass.motionTimeline = &preparedMotionTimeline;

    if (!FiniteRect(viewport) || viewport.width < 0.0F || viewport.height < 0.0F) {
        pass.Add({}, L"invalid_viewport",
            L"Focus-entry preparation requires finite non-negative geometry.",
            RenderDiagnosticSeverity::Error);
        return pass.result;
    }
    if (!writeFactory_)
        pass.Add({}, L"missing_write_factory",
            L"Intrinsic text uses fallback metrics without DirectWrite.");
    const auto animationTimestamp = options.animationTimestampMilliseconds.value_or(
        static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now().time_since_epoch()).count()));
    pass.options.animationTimestampMilliseconds = animationTimestamp;
    preparedMotionTimeline.BeginFrame(animationTimestamp);
    pass.BuildLayout(!options.suppressFocusedDescendantFollow);
    pass.ResolvePresentationWithFocusFollow();
    pass.CollectFocusGeometryTree(snapshot.root);
    (void)preparedMotionTimeline.EndFrame();

    const auto hasErrors = std::any_of(
        pass.result.diagnostics.begin(), pass.result.diagnostics.end(),
        [](const RenderDiagnostic& diagnostic) {
            return diagnostic.severity == RenderDiagnosticSeverity::Error;
        });
    pass.result.succeeded = !hasErrors && pass.layout.valid();
    if (pass.result.succeeded) {
        pass.result.responsiveSurface = ResponsiveSurfacePresentation{
            responsiveViewport,
            pass.compactMode
                ? ResponsiveSurfaceMode::Compact
                : ResponsiveSurfaceMode::Expanded,
        };
    }
    return pass.result;
}

ContentMeasureResult DeclarativeRenderer::MeasureContent(
    const WidgetSnapshot& snapshot,
    const Size admittedMaximumExtent,
    const bool intrinsicWidth,
    const DeclarativeRenderOptions& options) {
    ContentMeasureResult result;
    if (!std::isfinite(admittedMaximumExtent.width) ||
        !std::isfinite(admittedMaximumExtent.height) ||
        admittedMaximumExtent.width <= 0.0F ||
        admittedMaximumExtent.height <= 0.0F) {
        result.diagnostics.push_back({
            RenderDiagnosticSeverity::Error, {}, L"invalid_measure_extent",
            L"Content measurement requires finite positive admitted bounds."});
        return result;
    }

    RenderPass pass;
    pass.owner = this;
    pass.snapshot = &snapshot;
    pass.viewport = {0.0F, 0.0F,
        admittedMaximumExtent.width, admittedMaximumExtent.height};
    pass.options = options;
    pass.options.responsiveViewport = admittedMaximumExtent;
    pass.compactMode = IsCompactResponsiveSurface(admittedMaximumExtent);
    pass.measurementOnly = true;
    pass.intrinsicRootWidth = intrinsicWidth;
    if (!writeFactory_)
        pass.Add({}, L"missing_write_factory",
            L"Intrinsic text uses fallback metrics without DirectWrite.");
    pass.BuildLayout(false, false);

    const auto* root = pass.layout.Find(NarrowStableId(snapshot.root.id));
    if (root && pass.layout.valid() &&
        std::isfinite(root->borderBox.width) && root->borderBox.width >= 0.0F &&
        std::isfinite(root->borderBox.height) && root->borderBox.height >= 0.0F) {
        result.succeeded = true;
        result.extent = {root->borderBox.width, root->borderBox.height};
    }
    result.diagnostics = std::move(pass.result.diagnostics);
    return result;
}

void DeclarativeRenderer::DiscardTargetResources() noexcept {
    // BeginDraw may return another transient device-context interface for the
    // same D2D device. Target-local clips/layout are discarded here, while the
    // bounded bitmap cache remains owned by its explicit resource domain and
    // is invalidated by BindBitmapResourceDomain when that domain changes.
    surfaceClipLayer_.Reset();
    surfaceClipGeometry_.Reset();
    surfaceClipTarget_ = nullptr;
    surfaceClipRect_ = {};
    surfaceClipRadius_ = 0.0F;
    // Surface-space rebases are exact only for the geometry on which they were
    // produced. Source bitmaps remain reusable and will recompute object-fit on
    // the resized target; a flattened transition is retired instead.
    std::erase_if(focusBackgrounds_, [](const auto& item) {
        return item.second.committedBitmapIsSurfaceComposite;
    });
    RecalculateFocusBackgroundCompositeBytes();
    incrementalLayoutCache_.reset();
    pendingIncrementalPlan_.reset();
}

void DeclarativeRenderer::ReleaseCachedImages() noexcept {
    ClearBitmapCache(true);
    bitmapResourceDomain_.Reset();
    bitmapResourceDomainIsDevice_ = false;
}

ImageBitmapCacheStats DeclarativeRenderer::GetImageBitmapCacheStats() const noexcept {
    return {
        bitmaps_.size(),
        bitmapBytes_,
        bitmapHits_,
        bitmapCreates_,
        bitmapEvictions_,
        bitmapCountPressureEvictions_,
        bitmapBytePressureEvictions_,
        bitmapSupersededArtworkEvictions_,
        bitmapResourceInvalidations_,
        bitmapResourceGeneration_,
        kMaximumBitmapEntries,
        kMaximumBitmapEntryBytes,
        kMaximumBitmapBytes,
        !bitmapResourceDomain_
            ? ImageBitmapResourceDomain::None
            : bitmapResourceDomainIsDevice_
                ? ImageBitmapResourceDomain::Device
                : ImageBitmapResourceDomain::RenderTarget,
    };
}

bool DeclarativeRenderer::BindBitmapResourceDomain(
    ID2D1RenderTarget* renderTarget) noexcept {
    if (!renderTarget) return false;

    ComPtr<IUnknown> identity;
    bool isDevice = false;
    ComPtr<ID2D1DeviceContext> context;
    if (SUCCEEDED(renderTarget->QueryInterface(IID_PPV_ARGS(&context))) && context) {
        ComPtr<ID2D1Device> device;
        context->GetDevice(device.ReleaseAndGetAddressOf());
        if (device && SUCCEEDED(device.As(&identity))) isDevice = true;
    }
    if (!identity && FAILED(renderTarget->QueryInterface(IID_PPV_ARGS(&identity))))
        return false;

    if (bitmapResourceDomain_ &&
        (bitmapResourceDomain_.Get() != identity.Get() ||
         bitmapResourceDomainIsDevice_ != isDevice)) {
        ClearBitmapCache(true);
        bitmapResourceDomain_.Reset();
    }
    if (!bitmapResourceDomain_) {
        bitmapResourceDomain_ = std::move(identity);
        bitmapResourceDomainIsDevice_ = isDevice;
        ++bitmapResourceGeneration_;
    }
    return true;
}

void DeclarativeRenderer::ClearBitmapCache(
    const bool resourceInvalidation) noexcept {
    bitmaps_.clear();
    bitmapBytes_ = 0;
    if (resourceInvalidation) {
        // Transition bitmaps belong to the same Direct2D resource domain.
        // Device/target replacement retires both sides atomically; the exact
        // current proposal can be resolved again on the replacement target.
        focusBackgrounds_.clear();
        focusBackgroundCompositeBytes_ = 0;
        ++bitmapResourceInvalidations_;
    }
}

bool DeclarativeRenderer::TrimBitmapCache(
    const std::size_t incomingBytes) noexcept {
    while (!bitmaps_.empty()) {
        const bool countPressure = bitmaps_.size() >= kMaximumBitmapEntries;
        const bool retainedPressure =
            focusBackgroundCompositeBytes_ > kMaximumBitmapBytes ||
            incomingBytes >
                kMaximumBitmapBytes - std::min(
                    focusBackgroundCompositeBytes_, kMaximumBitmapBytes);
        const auto availableAfterRetained = retainedPressure
            ? std::size_t{0}
            : kMaximumBitmapBytes - focusBackgroundCompositeBytes_ - incomingBytes;
        const bool bytePressure = retainedPressure ||
            bitmapBytes_ > availableAfterRetained;
        if (!countPressure && !bytePressure) break;
        auto oldest = bitmaps_.end();
        for (auto entry = bitmaps_.begin(); entry != bitmaps_.end(); ++entry) {
            if (ImageProtected(entry->first)) continue;
            if (oldest == bitmaps_.end() || entry->second.lastUse < oldest->second.lastUse) oldest = entry;
        }
        if (oldest == bitmaps_.end()) return false;
        bitmapBytes_ -= oldest->second.bytes;
        bitmaps_.erase(oldest);
        ++bitmapEvictions_;
        if (bytePressure) ++bitmapBytePressureEvictions_;
        else ++bitmapCountPressureEvictions_;
    }
    return incomingBytes <= kMaximumBitmapBytes - std::min(kMaximumBitmapBytes, bitmapBytes_ + focusBackgroundCompositeBytes_);
}

void DeclarativeRenderer::RecalculateFocusBackgroundCompositeBytes() noexcept {
    focusBackgroundCompositeBytes_ = 0;
    for (const auto& [_, entry] : focusBackgrounds_) {
        if (entry.committedBitmapIsSurfaceComposite &&
            entry.committedSurfaceCompositeBytes <=
                kMaximumBitmapBytes - focusBackgroundCompositeBytes_) {
            focusBackgroundCompositeBytes_ +=
                entry.committedSurfaceCompositeBytes;
        } else if (entry.committedBitmapIsSurfaceComposite) {
            focusBackgroundCompositeBytes_ = kMaximumBitmapBytes;
            break;
        }
    }
}

bool DeclarativeRenderer::EnsureSurfaceClip(
    ID2D1RenderTarget* renderTarget,
    const Rect viewport,
    const float radius) {
    const auto sameRect = [](const Rect& left, const Rect& right) {
        return std::abs(left.x - right.x) <= 0.01F &&
               std::abs(left.y - right.y) <= 0.01F &&
               std::abs(left.width - right.width) <= 0.01F &&
               std::abs(left.height - right.height) <= 0.01F;
    };
    if (surfaceClipTarget_ == renderTarget && surfaceClipLayer_ &&
        surfaceClipGeometry_ && sameRect(surfaceClipRect_, viewport) &&
        std::abs(surfaceClipRadius_ - radius) <= 0.01F) {
        return true;
    }

    surfaceClipLayer_.Reset();
    surfaceClipGeometry_.Reset();
    surfaceClipTarget_ = nullptr;
    if (!d2dFactory_ || !renderTarget ||
        FAILED(d2dFactory_->CreateRoundedRectangleGeometry(
            {D2DRect(viewport), radius, radius},
            surfaceClipGeometry_.ReleaseAndGetAddressOf())) ||
        FAILED(renderTarget->CreateLayer(
            nullptr, surfaceClipLayer_.ReleaseAndGetAddressOf()))) {
        surfaceClipLayer_.Reset();
        surfaceClipGeometry_.Reset();
        return false;
    }
    surfaceClipTarget_ = renderTarget;
    surfaceClipRect_ = viewport;
    surfaceClipRadius_ = radius;
    return true;
}

void DeclarativeRenderer::ForgetWidgetState(
    const std::wstring_view widgetInstanceId) noexcept {
    if (widgetInstanceId.empty()) return;
#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
    ++gRendererWidgetStateRetirementCount;
    gLastRetiredRendererWidgetInstance = widgetInstanceId;
#endif
    std::wstring prefix(widgetInstanceId);
    prefix.push_back(L'\x1f');
    std::erase_if(scrollOffsets_, [&](const auto& entry) {
        return entry.first.starts_with(prefix);
    });
    motionTimeline_.ForgetPrefix(prefix);
    std::erase_if(focusBackgrounds_, [&](const auto& entry) {
        return entry.second.widgetInstanceId == widgetInstanceId;
    });
    RecalculateFocusBackgroundCompositeBytes();
}

#ifdef WRAIL_WIDGET_SURFACE_COORDINATOR_TESTING
namespace testing {
void ResetRendererWidgetStateRetirementForTesting() noexcept {
    gRendererWidgetStateRetirementCount = 0;
    gLastRetiredRendererWidgetInstance.clear();
}

std::uint64_t RendererWidgetStateRetirementCountForTesting() noexcept {
    return gRendererWidgetStateRetirementCount;
}

std::wstring_view LastRetiredRendererWidgetInstanceForTesting() noexcept {
    return gLastRetiredRendererWidgetInstance;
}
} // namespace testing
#endif

ComPtr<ID2D1Bitmap> DeclarativeRenderer::GetImageBitmap(
    ID2D1RenderTarget* renderTarget,
    const WidgetNode& node,
    RenderPass& pass,
    const std::wstring_view artworkWidgetId,
    ImagePresentationState& presentationState) {
    presentationState = ImagePresentationState::Failed;
    const bool trustedArtwork = !node.artworkHandle.empty() && node.imageSource.empty();
    const TrustedArtworkDemandAuthority artworkAuthority{
        std::wstring{artworkWidgetId},
        pass.options.artworkRuntimeGeneration,
        pass.options.artworkPresentationGeneration};
    const auto decodeSize = pass.CachedImageSize(node);
    std::wstring source = trustedArtwork
        ? RemoteImageCache::TrustedArtworkKey(artworkWidgetId, node.id, node.artworkHandle, decodeSize)
        : RemoteImageCache::VariantKey(node.imageSource, decodeSize);
    if (!imageCache_ || !renderTarget || source.empty()) {
        pass.Add(node.id, L"missing_image", L"Image has no HTTPS source or image cache.");
        return {};
    }
    if (!trustedArtwork && !RemoteImageCache::IsAllowedImageSource(node.imageSource)) {
        pass.Add(node.id, L"invalid_image_url",
            L"Only bounded HTTPS or canonical inline PNG image sources are accepted.");
        return {};
    }
    if (const auto existing = bitmaps_.find(source); existing != bitmaps_.end())
    {
        existing->second.lastUse = ++bitmapAccessClock_;
        ++bitmapHits_;
        presentationState = ImagePresentationState::Ready;
        if (ArtworkRenderDiagnosticsEnabled()) {
            const auto bitmapSize = existing->second.bitmap->GetSize();
            ReportArtworkRenderDiagnostic(
                node, artworkWidgetId, L"gpu", L"hit",
                {bitmapSize.width, bitmapSize.height});
        }
        return existing->second.bitmap;
    }
    auto state = trustedArtwork
        ? imageCache_->GetTrustedArtworkState(source, artworkAuthority)
        : imageCache_->GetState(source);
    if (state == RemoteImageState::Failed && imageCache_->ReleaseBudgetRejection(source))
        state = RemoteImageState::Missing;
    if (state == RemoteImageState::Missing) {
        if (pass.options.sizeArtworkToDisplay && !imageCache_->CanPrefetch(source)) {
            presentationState = ImagePresentationState::Pending;
            return {};
        }
        const auto request = trustedArtwork
            ? imageCache_->RequestTrustedArtwork(source, artworkAuthority, decodeSize)
            : imageCache_->Request(node.imageSource, decodeSize);
        if (trustedArtwork &&
            imageCache_->GetTrustedArtworkState(source, artworkAuthority) ==
                RemoteImageState::Failed) {
            // A synchronous transport refusal or another node already known
            // to share this terminal handle uses the cache-owned fallback.
            presentationState = ImagePresentationState::TrustedArtworkUnavailable;
        } else if (request == RemoteImageRequestResult::InvalidUrl ||
            request == RemoteImageRequestResult::CapacityExceeded ||
            request == RemoteImageRequestResult::ShuttingDown) {
            pass.Add(node.id, L"image_request", L"Remote image request was rejected by cache policy.");
        } else {
            presentationState = ImagePresentationState::Pending;
        }
        return {};
    }
    if (state == RemoteImageState::Queued || state == RemoteImageState::Loading) {
        presentationState = ImagePresentationState::Pending;
        return {};
    }
    if (state == RemoteImageState::Failed && imageCache_->BudgetRejected(source)) {
        presentationState = ImagePresentationState::Pending;
        return {};
    }
    if (state == RemoteImageState::Failed) {
        if (trustedArtwork) {
            presentationState = ImagePresentationState::TrustedArtworkUnavailable;
        } else {
            pass.Add(node.id, L"image_failed", imageCache_->GetError(source));
        }
        return {};
    }
    ComPtr<ID2D1Bitmap> bitmap;
    const auto result = imageCache_->CreateBitmap(
        renderTarget, source, bitmap.ReleaseAndGetAddressOf());
    if (FAILED(result) || !bitmap) {
        if (ArtworkRenderDiagnosticsEnabled())
            ReportArtworkRenderDiagnostic(
                node, artworkWidgetId, L"gpu", L"create-failed");
        pass.Add(node.id, L"image_bitmap", L"Ready image could not create a render-target bitmap.");
        return {};
    }
    ++bitmapCreates_;
    presentationState = ImagePresentationState::Ready;
    const auto pixelSize = bitmap->GetPixelSize();
    if (ArtworkRenderDiagnosticsEnabled())
        ReportArtworkRenderDiagnostic(
            node, artworkWidgetId, L"gpu", L"created",
            {static_cast<float>(pixelSize.width), static_cast<float>(pixelSize.height)});
    const auto byteCount64 = static_cast<std::uint64_t>(pixelSize.width) *
        static_cast<std::uint64_t>(pixelSize.height) * 4ULL;
    if (byteCount64 <= kMaximumBitmapEntryBytes) {
        const auto byteCount = static_cast<std::size_t>(byteCount64);
        if (!TrimBitmapCache(byteCount)) return bitmap;
        bitmapBytes_ += byteCount;
        bitmaps_.emplace(
            source,
            BitmapCacheEntry{bitmap, byteCount, ++bitmapAccessClock_});
    }
    return bitmap;
}

std::optional<PackageIconDemandAuthority>
DeclarativeRenderer::ResolvePackageIconDemandAuthority(
    const WidgetPackageIcon& icon,
    const Rect destination,
    const DeclarativeRenderOptions& options) {
    if (destination.width <= 0.0F ||
        destination.height <= 0.0F || options.artworkWidgetId.empty() ||
        options.packageContentDigest.empty()) return {};
    const auto metadata = std::find_if(
        options.packageIconAssets.begin(), options.packageIconAssets.end(),
        [&](const WidgetPackageIconAsset& asset) { return asset.id == icon.assetId; });
    if (metadata == options.packageIconAssets.end()) return {};
    const auto boundedDimension = [](const float dip, const float scale) {
        return static_cast<UINT32>(std::clamp(
            std::ceil(std::max(1.0F, dip * std::max(0.01F, scale))),
            1.0F, 512.0F));
    };
    return PackageIconDemandAuthority{
        options.artworkWidgetId,
        options.artworkRuntimeGeneration,
        options.artworkPresentationGeneration,
        options.packageContentDigest,
        icon.assetId,
        metadata->sourceSha256,
        metadata->normalizedSha256,
        boundedDimension(destination.width, options.pixelScale),
        boundedDimension(destination.height, options.pixelScale),
        icon.colorMode == WidgetPackageIconColorMode::ThemeTint
            ? PackageIconRasterVariant::AlphaMask
            : PackageIconRasterVariant::OriginalColor,
    };
}

ComPtr<ID2D1Bitmap> DeclarativeRenderer::GetPackageIconBitmap(
    ID2D1RenderTarget* renderTarget,
    const WidgetPackageIcon& icon,
    const Rect destination,
    const DeclarativeRenderOptions& options) {
    if (!imageCache_ || !renderTarget ||
        !BindBitmapResourceDomain(renderTarget)) return {};
    const auto resolvedAuthority = ResolvePackageIconDemandAuthority(
        icon, destination, options);
    if (!resolvedAuthority) return {};
    auto authority = *resolvedAuthority;
    const auto key = RemoteImageCache::PackageIconKey(authority);
    if (const auto existing = bitmaps_.find(key); existing != bitmaps_.end()) {
        existing->second.lastUse = ++bitmapAccessClock_;
        ++bitmapHits_;
        return existing->second.bitmap;
    }
    const auto state = imageCache_->GetPackageIconState(key, authority);
    if (state == RemoteImageState::Missing) {
        const auto request = imageCache_->RequestPackageIcon(key, std::move(authority));
        if (request == RemoteImageRequestResult::InvalidUrl ||
            request == RemoteImageRequestResult::CapacityExceeded ||
            request == RemoteImageRequestResult::ShuttingDown) return {};
        return {};
    }
    if (state != RemoteImageState::Ready) return {};
    ComPtr<ID2D1Bitmap> bitmap;
    if (FAILED(imageCache_->CreateBitmap(
            renderTarget, key, bitmap.ReleaseAndGetAddressOf())) || !bitmap) return {};
    ++bitmapCreates_;
    const auto pixelSize = bitmap->GetPixelSize();
    const auto byteCount64 = static_cast<std::uint64_t>(pixelSize.width) *
        static_cast<std::uint64_t>(pixelSize.height) * 4ULL;
    if (byteCount64 <= kMaximumBitmapEntryBytes) {
        const auto byteCount = static_cast<std::size_t>(byteCount64);
        if (!TrimBitmapCache(byteCount)) return bitmap;
        bitmapBytes_ += byteCount;
        bitmaps_.emplace(key, BitmapCacheEntry{bitmap, byteCount, ++bitmapAccessClock_});
    }
    return bitmap;
}

bool DeclarativeRenderer::PaintPackageIcon(
    ID2D1RenderTarget* renderTarget,
    const WidgetPackageIcon& icon,
    const Rect destination,
    const NativeColor tint,
    const DeclarativeRenderOptions& options) {
    auto bitmap = GetPackageIconBitmap(
        renderTarget, icon, destination, options);
    if (!bitmap) return false;
    const auto bitmapSize = bitmap->GetSize();
    const auto source = D2D1::RectF(
        0.0F, 0.0F, bitmapSize.width, bitmapSize.height);
    const auto destinationRect = D2DRect(destination);
    if (icon.colorMode == WidgetPackageIconColorMode::OriginalColor) {
        renderTarget->DrawBitmap(bitmap.Get(), destinationRect,
            std::clamp(tint.alpha, 0.0F, 1.0F),
            D2D1_BITMAP_INTERPOLATION_MODE_LINEAR, source);
        return true;
    }
    ComPtr<ID2D1SolidColorBrush> brush;
    if (FAILED(renderTarget->CreateSolidColorBrush(
            D2D1::ColorF(tint.red, tint.green, tint.blue, tint.alpha),
            brush.ReleaseAndGetAddressOf())) || !brush) return false;
    const auto antialiasMode = renderTarget->GetAntialiasMode();
    renderTarget->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
    renderTarget->FillOpacityMask(
        bitmap.Get(), brush.Get(), D2D1_OPACITY_MASK_CONTENT_GRAPHICS,
        &destinationRect, &source);
    renderTarget->SetAntialiasMode(antialiasMode);
    return true;
}

ImagePlacement DeclarativeRenderer::ComputeImagePlacement(
    const Size imageSize,
    const Rect destination,
    const NativeImageFit fit,
    const NativeObjectPosition position) noexcept {
    if (!FiniteRect(destination) || !std::isfinite(imageSize.width) ||
        !std::isfinite(imageSize.height) || imageSize.width <= 0.0F ||
        imageSize.height <= 0.0F || destination.width <= 0.0F ||
        destination.height <= 0.0F) {
        return {destination, {0.0F, 0.0F, 0.0F, 0.0F}};
    }
    const auto factorX = PositionFactorX(position);
    const auto factorY = PositionFactorY(position);
    const Rect fullSource{0.0F, 0.0F, imageSize.width, imageSize.height};
    if (fit == NativeImageFit::Fill) return {destination, fullSource};

    if (fit == NativeImageFit::Cover) {
        const auto destinationAspect = destination.width / destination.height;
        const auto imageAspect = imageSize.width / imageSize.height;
        auto source = fullSource;
        if (imageAspect > destinationAspect) {
            source.width = imageSize.height * destinationAspect;
            source.x = (imageSize.width - source.width) * factorX;
        } else if (imageAspect < destinationAspect) {
            source.height = imageSize.width / destinationAspect;
            source.y = (imageSize.height - source.height) * factorY;
        }
        return {destination, source};
    }

    const auto scale = fit == NativeImageFit::Contain
        ? std::min(destination.width / imageSize.width, destination.height / imageSize.height)
        : 1.0F;
    Rect natural{
        destination.x + (destination.width - imageSize.width * scale) * factorX,
        destination.y + (destination.height - imageSize.height * scale) * factorY,
        imageSize.width * scale,
        imageSize.height * scale,
    };
    if (fit == NativeImageFit::Contain) return {natural, fullSource};

    // "none" preserves intrinsic DIPs and crops only the portion outside bounds.
    const auto left = std::max(natural.x, destination.x);
    const auto top = std::max(natural.y, destination.y);
    const auto right = std::min(natural.x + natural.width, destination.x + destination.width);
    const auto bottom = std::min(natural.y + natural.height, destination.y + destination.height);
    const Rect visible{
        left,
        top,
        std::max(0.0F, right - left),
        std::max(0.0F, bottom - top),
    };
    const Rect source{
        visible.x - natural.x,
        visible.y - natural.y,
        visible.width,
        visible.height,
    };
    return {visible, source};
}

ButtonContentPlacement DeclarativeRenderer::ComputeButtonContentPlacement(
    const Rect content,
    const float leadingSize,
    const float measuredTextWidth,
    const bool hasLeading,
    const bool hasText,
    const bool reserveTrailingStateCue,
    const NativeTextAlign alignment) noexcept {
    if (!FiniteRect(content) || content.width <= 0.0F || content.height <= 0.0F) {
        return {{}, {}, {}};
    }

    const auto tokens = ResolveButtonContentTokens(
        content, leadingSize, hasLeading, hasText, reserveTrailingStateCue);
    const auto centeredCueInset = reserveTrailingStateCue && alignment == NativeTextAlign::Center
        ? tokens.stateCueLane
        : 0.0F;
    const auto trailingCueInset = reserveTrailingStateCue
        ? tokens.stateCueLane
        : 0.0F;
    const Rect safe{
        content.x + centeredCueInset,
        content.y,
        std::max(0.0F, content.width - centeredCueInset - trailingCueInset),
        content.height,
    };
    const auto resolvedLeading = std::min(tokens.leadingSize, std::min(safe.width, safe.height));
    const auto gap = hasLeading && hasText && safe.width > resolvedLeading
        ? std::min(tokens.leadingGap, safe.width - resolvedLeading)
        : 0.0F;
    const auto textBudget = std::max(0.0F, safe.width - resolvedLeading - gap);
    const auto resolvedText = hasText && std::isfinite(measuredTextWidth)
        ? std::clamp(measuredTextWidth, 0.0F, textBudget)
        : 0.0F;
    const auto groupWidth = resolvedLeading + gap + resolvedText;
    const auto remaining = std::max(0.0F, safe.width - groupWidth);
    auto groupX = safe.x + std::clamp(
        alignment == NativeTextAlign::Start ? 0.0F :
        alignment == NativeTextAlign::End ? remaining : remaining * 0.5F,
        0.0F,
        remaining);
    const auto textX = groupX + resolvedLeading + gap;
    const Rect leading{
        groupX,
        safe.y + (safe.height - resolvedLeading) * 0.5F,
        resolvedLeading,
        resolvedLeading,
    };
    const Rect text{
        textX,
        safe.y,
        resolvedText,
        safe.height,
    };
    const Rect trailingStateCue = reserveTrailingStateCue
        ? Rect{
            content.x + content.width - tokens.stateCueSize,
            content.y + (content.height - tokens.stateCueSize) * 0.5F,
            tokens.stateCueSize,
            tokens.stateCueSize,
        }
        : Rect{};
    return {leading, text, trailingStateCue};
}

} // namespace widgetrail
