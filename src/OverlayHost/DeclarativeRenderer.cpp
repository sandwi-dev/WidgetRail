#include "DeclarativeRenderer.h"

#include "NativeIcons.h"
#include "NativeTextLayout.h"
#include "RemoteImageCache.h"

#include <algorithm>
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

constexpr std::size_t kMaximumDiagnostics = 256;
constexpr std::size_t kMaximumBitmapEntries = 32;
constexpr std::size_t kMaximumBitmapBytes = 32U * 1024U * 1024U;
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
constexpr NativeColor kMutedText{0.725F, 0.741F, 0.784F, 1.0F};
constexpr NativeColor kDefaultFocus{1.0F, 1.0F, 1.0F, 1.0F};
constexpr NativeColor kDefaultButton{0.122F, 0.133F, 0.169F, 0.96F};
constexpr NativeColor kDefaultTrack{0.25F, 0.26F, 0.30F, 0.72F};
constexpr NativeColor kDefaultAccent{0.545F, 0.486F, 1.0F, 1.0F};

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

[[nodiscard]] bool SameLayoutBox(
    const declarative::LayoutBox& left,
    const declarative::LayoutBox& right) noexcept {
    return SameRect(left.borderBox, right.borderBox) &&
        SameRect(left.contentBox, right.contentBox) &&
        SameRect(left.visibleBox, right.visibleBox) &&
        left.overflowX == right.overflowX &&
        left.overflowY == right.overflowY &&
        left.clippedByAncestor == right.clippedByAncestor &&
        left.scrollAxis == right.scrollAxis &&
        std::abs(left.scrollOffset - right.scrollOffset) <= 0.01F &&
        std::abs(left.maximumScrollOffset - right.maximumScrollOffset) <= 0.01F;
}

[[nodiscard]] bool SameLayout(
    const declarative::LayoutResult& left,
    const declarative::LayoutResult& right) noexcept {
    if (!left.valid() || !right.valid() ||
        left.compactMode != right.compactMode ||
        left.boxes.size() != right.boxes.size()) return false;
    auto leftBox = left.boxes.begin();
    auto rightBox = right.boxes.begin();
    for (; leftBox != left.boxes.end(); ++leftBox, ++rightBox) {
        if (leftBox->first != rightBox->first ||
            !SameLayoutBox(leftBox->second, rightBox->second)) return false;
    }
    return true;
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
};

struct DeclarativeRenderer::RenderPass final {
    enum class CollectionAnchorPolicy {
        Reconcile,
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

    DeclarativeRenderer* owner{};
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
    std::map<std::wstring, CollectionReconciliationTrace, std::less<>>
        collectionReconciliationOffsets;

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

    [[nodiscard]] NativeStyleResult Adapt(
        const WidgetNode& node,
        const bool focused,
        const bool pressed,
        const float parentWidth,
        const float parentHeight,
        const float parentFontSize,
        const std::optional<NativeColor>& inheritedBackground) {
        auto adapted = NativeStyleAdapter::Adapt(
            ResolveDeclarativeComputedStyle(node, focused, pressed),
            NativeStyleContext{
                viewport.width,
                viewport.height,
                parentWidth,
                parentHeight,
                parentFontSize,
                options.rootFontSizePx,
                focused,
                inheritedBackground,
                (node.kind == L"button" || node.kind == L"actionSurface")
                    ? std::optional<NativeColor>{kDefaultButton}
                    : std::nullopt,
            },
            options.accessibility);
        for (const auto& diagnostic : adapted.diagnostics) {
            Add(node.id, L"invalid_style",
                diagnostic.property + L": " + diagnostic.message);
        }
        return adapted;
    }

    [[nodiscard]] LayoutElement PrepareNode(
        const WidgetNode& node,
        const std::string_view parentId,
        const float fallbackParentWidth,
        const float fallbackParentHeight,
        const float parentFontSize,
        const std::optional<NativeColor>& inheritedBackground) {
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
        LayoutElement element;
        element.id = narrowId;
        if (node.kind == L"grid") {
            element.layoutMode = LayoutMode::ResponsiveGrid;
            if (node.gridMinimumColumnWidth)
                element.gridMinimumColumnWidth = static_cast<float>(*node.gridMinimumColumnWidth);
            element.gridMaximumColumns = node.gridMaximumColumns;
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
        if (node.kind == L"button") {
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
        if (node.kind == L"actionSurface")
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
            if (const auto offset = owner->scrollOffsets_.find(key);
                offset != owner->scrollOffsets_.end()) {
                if (!measurementOnly)
                    offset->second.lastAccess = ++owner->scrollStateAccessClock_;
                element.scrollOffset = measurementOnly ? 0.0F : offset->second.offset;
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
        element.children.reserve(node.children.size());
        const float textScale = std::isfinite(options.accessibility.textScale) &&
                options.accessibility.textScale >= 0.85F &&
                options.accessibility.textScale <= 1.5F
            ? options.accessibility.textScale
            : 1.0F;
        for (const auto& child : node.children) {
            if (!IsResponsiveVisible(child)) continue;
            element.children.push_back(PrepareNode(
                child,
                narrowId,
                parentWidth,
                parentHeight,
                style.fontSizePx() / textScale,
                effectiveBackground));
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
        const auto motion = owner->motionTimeline_.Resolve(
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
        for (const auto& child : node.children) {
            ResolvePresentation(
                child, translationX, translationY, childClip);
        }
    }

    void VisitScrollNodes(
        const WidgetNode& node,
        const std::function<void(const WidgetNode&)>& callback) const {
        if (!IsResponsiveVisible(node)) return;
        if (node.kind == L"scroll") callback(node);
        for (const auto& child : node.children) VisitScrollNodes(child, callback);
    }

    void StoreScrollOffset(const std::wstring_view key, const float offset) {
        auto& state = owner->scrollOffsets_[std::wstring{key}];
        state.offset = offset;
        state.lastAccess = ++owner->scrollStateAccessClock_;
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

    [[nodiscard]] bool ReconcileCollectionAnchors() {
        bool changed{};
        VisitScrollNodes(snapshot->root, [&](const WidgetNode& scroll) {
            if (scroll.collectionAnchorKey.empty()) return;
            const auto stateKey = ScrollStateKey(scroll.id);
            const auto existing = owner->scrollOffsets_.find(stateKey);
            const auto* scrollBox = layout.Find(NarrowStableId(scroll.id));
            if (existing == owner->scrollOffsets_.end() ||
                !existing->second.hasAnchorPosition || !scrollBox ||
                scrollBox->scrollAxis == declarative::ScrollAxis::None) {
                return;
            }

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

            if (existing->second.anchorKey == scroll.collectionAnchorKey) {
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
                return;
            }

            CollectionDiagnosticObservation current;
            CollectCollectionItems(scroll, scroll, scrollBox, current);
            if (current.itemsTruncated ||
                std::ranges::find(
                    current.itemKeys, existing->second.anchorKey) !=
                    current.itemKeys.end()) {
                return;
            }
            if (std::abs(previousScrollBox->contentBox.width -
                          scrollBox->contentBox.width) > 0.01F ||
                std::abs(previousScrollBox->contentBox.height -
                         scrollBox->contentBox.height) > 0.01F) {
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
            if (!retainedPrior || !retainedCurrent) return;
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
            if (const auto state = owner->scrollOffsets_.find(stateKey);
                state != owner->scrollOffsets_.end()) {
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
            const auto focusVisibleAt = [&](const float candidateOffset) {
                const float delta = scrollBox->scrollOffset - candidateOffset;
                if (scrollBox->scrollAxis == declarative::ScrollAxis::Vertical) {
                    const float top = targetRect.y + delta;
                    return top >= viewportBox.y - kRevealEpsilon &&
                           top + targetRect.height <=
                               viewportBox.y + viewportBox.height + kRevealEpsilon;
                }
                const float left = targetRect.x + delta;
                return left >= viewportBox.x - kRevealEpsilon &&
                       left + targetRect.width <=
                           viewportBox.x + viewportBox.width + kRevealEpsilon;
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
            } else if (scrollBox->scrollAxis == declarative::ScrollAxis::Vertical) {
                if (targetRect.y < viewportBox.y)
                    desired -= viewportBox.y - targetRect.y;
                else if (targetRect.y + targetRect.height > viewportBox.y + viewportBox.height)
                    desired += targetRect.y + targetRect.height - viewportBox.y - viewportBox.height;
            } else {
                if (targetRect.x < viewportBox.x)
                    desired -= viewportBox.x - targetRect.x;
                else if (targetRect.x + targetRect.width > viewportBox.x + viewportBox.width)
                    desired += targetRect.x + targetRect.width - viewportBox.x - viewportBox.width;
            }
            desired = std::clamp(desired, 0.0F, scrollBox->maximumScrollOffset);
            const auto key = ScrollStateKey(scroll.id);
            const auto existing = owner->scrollOffsets_.find(key);
            if (existing == owner->scrollOffsets_.end() ||
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
            const auto retained = owner->scrollOffsets_.find(key);
            const float offset = retained != owner->scrollOffsets_.end()
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
            const bool visible = scrollBox->scrollAxis ==
                    declarative::ScrollAxis::Vertical
                ? targetRect.y >= viewportBox.y - kRevealEpsilon &&
                    targetRect.y + targetRect.height <=
                        viewportBox.y + viewportBox.height + kRevealEpsilon
                : targetRect.x >= viewportBox.x - kRevealEpsilon &&
                    targetRect.x + targetRect.width <=
                        viewportBox.x + viewportBox.width + kRevealEpsilon;
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
        return RecordFocusFollowPass(
            before, after, changed, usePresentationGeometry);
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
        if (targetStart >= clipStart - rasterEdgeTolerance &&
            targetStart + targetSize <=
                clipStart + clipSize + rasterEdgeTolerance) {
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

        float requiredMinimum{};
        float requiredMaximum{};
        if (targetSize <= clipSize + kRevealEpsilon) {
            requiredMinimum = clipStart - targetStart;
            requiredMaximum = clipStart + clipSize - targetStart - targetSize;
        } else {
            // Oversized controls cannot be wholly contained; require a
            // non-trivial visible intersection and clip their focus outline.
            requiredMinimum = clipStart - targetStart - targetSize + kRevealEpsilon;
            requiredMaximum = clipStart + clipSize - targetStart - kRevealEpsilon;
        }
        return std::max(minimumDelta, requiredMinimum) <=
            std::min(maximumDelta, requiredMaximum) + kRevealEpsilon;
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
                auto& state = owner->scrollOffsets_[key];
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
        std::erase_if(owner->scrollOffsets_, [&](const auto& entry) {
            return entry.first.starts_with(prefix) && !activeKeys.contains(entry.first);
        });
        if (owner->scrollOffsets_.size() <= kMaximumScrollStateEntries) return;

        // Evict only the overflow, oldest inactive entries first. The active
        // snapshot (at most the protocol's bounded node count) survives a cap
        // transition instead of losing its scroll position with the old map
        // clear behavior.
        std::vector<std::pair<std::uint64_t, std::wstring>> inactive;
        inactive.reserve(owner->scrollOffsets_.size() - activeKeys.size());
        for (const auto& [key, state] : owner->scrollOffsets_) {
            if (!activeKeys.contains(key)) inactive.emplace_back(state.lastAccess, key);
        }
        std::ranges::sort(inactive);
        const auto overflow = owner->scrollOffsets_.size() - kMaximumScrollStateEntries;
        const auto count = std::min(overflow, inactive.size());
        for (std::size_t index = 0; index < count; ++index)
            owner->scrollOffsets_.erase(inactive[index].second);
    }

    [[nodiscard]] Size MeasureText(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const declarative::MeasureConstraints& constraints) {
        const auto maximumWidth = std::max(1.0F, constraints.maximumWidth);
        const auto lineHeight = style.fontSizePx() * style.lineHeight();
        const auto maximumHeight = std::max(
            lineHeight,
            std::min(constraints.maximumHeight, lineHeight * static_cast<float>(style.maxLines())));
        const auto availableHeight = std::isfinite(constraints.maximumHeight) &&
                constraints.maximumHeight > 0.0F
            ? std::max(maximumHeight, constraints.maximumHeight)
            : maximumHeight + style.fontSizePx();
        const auto plan = CreateNativeTextLayoutPlan(
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
                !node.artworkHandle.empty() || !node.glyph.empty();
            const bool hasText = !node.text.empty();
            const bool reserveStateCue = hasText &&
                (node.isBusy || node.isSelected || node.isDisabled);
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

    void BuildLayout(
        const bool followStaticFocus = true,
        const bool fillAutoRoot = true,
        const CollectionAnchorPolicy collectionAnchorPolicy =
            CollectionAnchorPolicy::Reconcile) {
        // First pass gives percentage/em adaptation a deterministic parent estimate.
        prepared.clear();
        textMeasurements.clear();
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
        layout = declarative::ComputeLayout(
            root,
            viewport,
            [this](const LayoutElement& element, const declarative::MeasureConstraints& constraints) {
                return MeasureLeaf(element, constraints);
            },
            layoutOptions);
        // One correction pass resolves parent-relative values against measured boxes.
        prepared.clear();
        textMeasurements.clear();
        auto correctedRoot = PrepareNode(
            snapshot->root,
            {},
            viewport.width,
            viewport.height,
            options.rootFontSizePx,
            options.surfaceBackground);
        layout = declarative::ComputeLayout(
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
        if (collectionAnchorPolicy == CollectionAnchorPolicy::Reconcile &&
            ReconcileCollectionAnchors()) {
            prepared.clear();
            textMeasurements.clear();
            auto anchoredRoot = PrepareNode(
                snapshot->root, {}, viewport.width, viewport.height,
                options.rootFontSizePx, options.surfaceBackground);
            layout = declarative::ComputeLayout(
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
                lastFollowChanged = FollowFocusedDescendant(false).changed;
                if (!lastFollowChanged) break;
                prepared.clear();
                textMeasurements.clear();
                auto revealedRoot = PrepareNode(
                    snapshot->root,
                    {},
                    viewport.width,
                    viewport.height,
                    options.rootFontSizePx,
                    options.surfaceBackground);
                layout = declarative::ComputeLayout(
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
        prepared.clear();
        (void)PrepareNode(
            snapshot->root,
            {},
            viewport.width,
            viewport.height,
            options.rootFontSizePx,
            options.surfaceBackground);
    }

    [[nodiscard]] bool BuildLocalLayout(
        const std::vector<std::wstring>& boundaryIds,
        const std::map<std::wstring, IncrementalNodeState, std::less<>>& nodes) {
        if (boundaryIds.empty()) return false;
        if (std::find(boundaryIds.begin(), boundaryIds.end(), snapshot->root.id) !=
            boundaryIds.end()) {
            BuildLayout();
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
            const auto parent = nodes.find(boundaryId);
            const auto narrowParent = parent == nodes.end()
                ? std::string{}
                : NarrowStableId(parent->second.parentId);
            auto recompute = [&]() {
                prepared.clear();
                auto root = PrepareNode(
                    *boundary,
                    narrowParent,
                    priorBox->borderBox.width,
                    priorBox->borderBox.height,
                    options.rootFontSizePx,
                    options.surfaceBackground);
                return declarative::ComputeLayout(
                    root,
                    priorBox->borderBox,
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
        const auto plan = CreateNativeTextLayoutPlan(
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

    void DrawImage(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const Rect rect,
        const float opacity,
        const bool focused) {
        if (!target) return;
        auto presentationState = ImagePresentationState::Pending;
        auto bitmap = owner->GetImageBitmap(
            target, node, *this, options.artworkWidgetId, presentationState);
        if (!bitmap) {
            if (presentationState == ImagePresentationState::TrustedArtworkUnavailable) {
                // AppTile's no-artwork presentation uses this same closed
                // semantic glyph. A terminal host resolution failure must not
                // leave the authored image box blank or change tile geometry.
                DrawSemanticIcon(
                    node, style,
                    Inset(rect, std::min(rect.width, rect.height) * 0.32F),
                    opacity * 0.75F, L"play");
            } else if (presentationState == ImagePresentationState::Failed) {
                DrawSemanticIcon(
                    node, style,
                    Inset(rect, std::min(rect.width, rect.height) * 0.32F),
                    opacity * 0.65F, L"warning");
            }
            return;
        }
        auto fit = style.imageFit();
        if (!HasComputedProperty(node, L"object-fit", focused, node.id == pressedId)) {
            if (const auto explicitFit = ExplicitImageFit(node)) fit = *explicitFit;
        }
        const auto imageSize = bitmap->GetSize();
        const auto placement = DeclarativeRenderer::ComputeImagePlacement(
            {imageSize.width, imageSize.height}, rect, fit, style.objectPosition());
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
        target->DrawBitmap(
            bitmap.Get(),
            D2DRect(placement.destination),
            opacity,
            D2D1_BITMAP_INTERPOLATION_MODE_LINEAR,
            D2DRect(placement.source));
        if (style.imageTint()) {
            auto tint = Brush(target, WithOpacity(*style.imageTint(), opacity));
            if (tint) target->FillRectangle(D2DRect(rect), tint.Get());
        }
        if (style.scrimColor()) {
            auto scrim = Brush(target, WithOpacity(*style.scrimColor(), opacity));
            if (scrim) {
                const Rect bottom{rect.x, rect.y + rect.height * 0.55F, rect.width, rect.height * 0.45F};
                target->FillRectangle(D2DRect(bottom), scrim.Get());
            }
        }
        if (pushed) target->PopLayer();
        else target->PopAxisAlignedClip();
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
        const bool focused) {
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
        const auto thumbRadius = focused ? 8.0F : 6.5F;
        const auto trackHeight = focused ? 6.0F : 4.5F;
        const auto trackInset = thumbRadius + 2.0F;
        const Rect track{
            rect.x + trackInset,
            rect.y + (rect.height - trackHeight) * 0.5F,
            std::max(0.0F, rect.width - trackInset * 2.0F),
            trackHeight,
        };
        const auto accent = style.foreground().value_or(kDefaultAccent);
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
                const auto radius = std::clamp(rect.height * 0.105F, 4.0F, 6.0F);
                target->DrawEllipse(
                    D2D1::Ellipse(
                        D2D1::Point2F(rect.x + rect.width - radius - 4.0F,
                                     rect.y + radius + 4.0F),
                        radius, radius),
                    brush.Get(), 2.0F);
            } else if (node.isSelected) {
                const auto radius = std::clamp(rect.height * 0.065F, 2.5F, 4.0F);
                target->FillEllipse(
                    D2D1::Ellipse(
                        D2D1::Point2F(rect.x + rect.width * 0.5F,
                                     rect.y + rect.height - radius - 3.0F),
                        radius, radius),
                    brush.Get());
            } else if (node.isDisabled) {
                target->DrawLine(
                    D2D1::Point2F(rect.x + 9.0F, rect.y + rect.height - 9.0F),
                    D2D1::Point2F(rect.x + rect.width - 9.0F, rect.y + 9.0F),
                    brush.Get(), 2.0F);
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
        if (node.isBusy) {
            DrawSemanticIcon(node, style, cue, opacity, L"refresh");
        } else if (node.isSelected) {
            DrawSemanticIcon(node, style, cue, opacity, L"check");
        } else if (node.isDisabled) {
            const auto cueColor = UseAccessibleDeclarativeStateCue(options.accessibility)
                ? style.foreground().value_or(kMutedText)
                : kMutedText;
            auto brush = Brush(target, WithOpacity(cueColor, opacity));
            if (brush) target->DrawLine(
                D2D1::Point2F(cue.x, cue.y + cue.height),
                D2D1::Point2F(cue.x + cue.width, cue.y),
                brush.Get(),
                2.0F);
        }
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
        const bool semanticNode =
            node.kind == L"button" || node.kind == L"slider" ||
            node.kind == L"actionSurface" || node.kind == L"image" ||
            node.kind == L"icon" || node.kind == L"loadingIndicator" ||
            node.kind == L"progress" ||
            (node.kind == L"text" &&
                (!node.text.empty() || !node.accessibilityLabel.empty()));
        if (options.collectAccessibility && semanticNode &&
            visibleRect.width > 0.5F && visibleRect.height > 0.5F) {
            result.accessibilityRegions.push_back({node.id, visibleRect});
        }

        if (node.kind == L"button" || node.kind == L"slider" ||
            node.kind == L"actionSurface") {
            result.navigationRects[node.id] = presented.borderBox;
            // A busy slider retains its place in the focus graph while its
            // value is pending, but the host suppresses adjustment/activation.
            // Disabled and busy describe activation state, not navigability.
            // Focus remains stable so status changes never teleport the user.
            result.navigationEnabled[node.id] = true;
            result.focusScopes[node.id] = std::wstring{inputScope};
            if (CanRevealNode(node.id)) result.revealableFocusIds.insert(node.id);
            // Controller focus and pointer hit-testing must use the geometry a
            // user can actually see. A clipped/offscreen child remains in the
            // declarative tree but is not a navigation candidate.
            if (visibleRect.width > 0.5F && visibleRect.height > 0.5F) {
                result.hitRegions.push_back(
                    {node.id, visibleRect, !node.isDisabled && !node.isBusy});
                result.focusRects[node.id] = visibleRect;
                if (focused) result.currentFocusRect = visibleRect;
            }
            if (focused)
                result.currentFocusOutlineClip = EffectiveFocusVisibilityClip(node.id);
        }

        if (!target) {
            for (const auto& child : node.children) DrawNode(child, inputScope);
            return;
        }
        target->PushAxisAlignedClip(
            D2DRect(presented.visibleBox), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        // Slider background is its track color. Painting the generic surface
        // first duplicates that color across the complete 44-DIP hit target.
        // Authors can wrap a Slider in a Card/Row when they want a filled
        // control surface; the Slider itself stays visually lightweight.
        if (node.kind != L"slider" && node.kind != L"loadingIndicator")
            DrawSurface(node, style, paintRect, opacity);

        if (node.kind == L"text") {
            DrawTextContent(node, style, presented.contentBox, opacity);
        } else if (node.kind == L"button") {
            auto textRect = presented.contentBox;
            const bool hasLeading = !node.imageSource.empty() ||
                !node.artworkHandle.empty() || !node.glyph.empty();
            const bool hasText = !node.text.empty();
            const bool reserveStateCue = hasText &&
                (node.isBusy || node.isSelected || node.isDisabled);
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
                else if (!node.glyph.empty())
                    DrawSemanticIcon(node, style, iconRect, opacity, node.glyph);
            }
            if (hasText) DrawTextContent(
                node, style, textRect, opacity, NativeTextVerticalAlignment::Center);
            DrawStateCue(node, style, paintRect, opacity, placement.trailingStateCue);
        } else if (node.kind == L"progress") {
            DrawProgress(node, style, presented.contentBox, opacity);
        } else if (node.kind == L"slider") {
            DrawSlider(node, style, presented.contentBox, opacity, focused);
        } else if (node.kind == L"image") {
            const auto visibleImageRect = Intersection(
                paintRect, presented.visibleBox);
            if (visibleImageRect.width > 0.5F && visibleImageRect.height > 0.5F)
                DrawImage(node, style, paintRect, opacity, focused);
        } else if (node.kind == L"icon") {
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
        } else if (node.kind != L"stack" && node.kind != L"row" &&
                   node.kind != L"scroll" && node.kind != L"grid" &&
                   node.kind != L"spacer") {
            if (node.kind != L"actionSurface")
            Add(node.id, L"unknown_kind", L"Unsupported declarative node kind: " + node.kind);
        }

        // This clip bounds the node's own paint. Descendants are drawn from
        // their precomputed presentation clips so overflow-visible containers
        // do not accidentally become clipping ancestors merely because the
        // renderer recurses through them.
        target->PopAxisAlignedClip();
        for (const auto& child : node.children) DrawNode(child, inputScope);
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

DeclarativeRenderer::DeclarativeRenderer(
    ID2D1Factory* d2dFactory,
    IDWriteFactory* writeFactory,
    RemoteImageCache* imageCache) noexcept
    : d2dFactory_(d2dFactory),
      writeFactory_(writeFactory),
      imageCache_(imageCache) {}

std::optional<IncrementalPresentationPlan>
DeclarativeRenderer::PlanPresentationUpdate(
    const WidgetSnapshot& snapshot,
    const WidgetPresentationImpact& impact,
    const Rect viewport) {
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
        // Text is the one layout-affecting update that can sometimes retain
        // committed Taffy geometry. Prove that with the exact prior options,
        // DirectWrite constraints/line metrics, overflow flags, clipping, and
        // every adjacent layout box. The proof pass borrows but never changes
        // the renderer's sole scroll-state owner.
        const auto savedScrollOffsets = scrollOffsets_;
        const auto savedScrollClock = scrollStateAccessClock_;
        RenderPass proof;
        proof.owner = this;
        proof.snapshot = &snapshot;
        proof.focusedId = cache->focusedElementId;
        proof.viewport = viewport;
        proof.options = cache->options;
        const auto responsiveViewport = proof.options.responsiveViewport.value_or(
            Size{viewport.width, viewport.height});
        proof.compactMode = IsCompactResponsiveSurface(responsiveViewport);
        proof.BuildLayout(false);
        scrollOffsets_ = savedScrollOffsets;
        scrollStateAccessClock_ = savedScrollClock;

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
        bool textMeasurementStable = SameLayout(cache->layout, proof.layout);
        for (const auto& nodeId : impact.textMeasurementNodeIds) {
            const auto prior = cache->textMeasurements.find(nodeId);
            const auto next = proof.textMeasurements.find(nodeId);
            if (prior == cache->textMeasurements.end() ||
                next == proof.textMeasurements.end() ||
                !sameProof(prior->second, next->second)) {
                textMeasurementStable = false;
                break;
            }
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
    const Rect viewport) {
    pendingIncrementalPlan_.reset();
    const auto& cache = incrementalLayoutCache_;
    if (!cache || cache->instanceId != snapshot.instanceId ||
        cache->sequence != snapshot.sequence ||
        cache->focusedElementId != focusedElementId ||
        axis == declarative::ScrollAxis::None || !std::isfinite(deltaDip) ||
        std::abs(deltaDip) <= 0.001F || !SameRect(cache->viewport, viewport)) {
        return std::nullopt;
    }

    std::vector<const WidgetNode*> path;
    if (!FindNodePath(snapshot.root, focusedElementId, path))
        return std::nullopt;

    for (auto item = path.rbegin(); item != path.rend(); ++item) {
        const WidgetNode& candidate = **item;
        if (candidate.kind != L"scroll") continue;
        const auto visible = cache->scrollViewports.find(candidate.id);
        const auto* box = cache->layout.Find(NarrowStableId(candidate.id));
        if (visible == cache->scrollViewports.end() || !box ||
            box->scrollAxis != axis ||
            visible->second.rect.width <= 0.0F ||
            visible->second.rect.height <= 0.0F) {
            continue;
        }
        const float next = std::clamp(
            box->scrollOffset + deltaDip, 0.0F, box->maximumScrollOffset);
        if (std::abs(next - box->scrollOffset) <= 0.001F) continue;

        std::wstring stateKey(snapshot.instanceId);
        stateKey.push_back(L'\x1f');
        stateKey.append(snapshot.activeInputScopeId);
        stateKey.push_back(L'\x1f');
        stateKey.append(candidate.id);
        auto& state = scrollOffsets_[stateKey];
        state.offset = next;
        state.lastAccess = ++scrollStateAccessClock_;

        const auto damage = Intersection(visible->second.rect, viewport);
        if (damage.width <= 0.0F || damage.height <= 0.0F) {
            state.offset = box->scrollOffset;
            return std::nullopt;
        }
        pendingIncrementalPlan_ = PendingIncrementalPlan{
            snapshot.instanceId,
            snapshot.sequence,
            snapshot.sequence,
            IncrementalPresentationWork::LocalLayout,
            damage,
            {candidate.id},
        };
        return FocusedFreeScrollPlan{
            IncrementalPresentationPlan{
                IncrementalPresentationWork::LocalLayout, damage},
            candidate.id,
            axis,
            visible->second.rect,
            box->scrollOffset,
            next,
            box->maximumScrollOffset,
        };
    }
    return std::nullopt;
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
    const bool pendingMatches = pendingIncrementalPlan_ &&
        pendingIncrementalPlan_->work != IncrementalPresentationWork::NoRaster &&
        incrementalLayoutCache_ &&
        pendingIncrementalPlan_->instanceId == snapshot.instanceId &&
        pendingIncrementalPlan_->baseSequence == incrementalLayoutCache_->sequence &&
        pendingIncrementalPlan_->sequence == snapshot.sequence &&
        SameRect(incrementalLayoutCache_->viewport, viewport);
    if (pendingMatches) {
        pass.layout = incrementalLayoutCache_->layout;
        pass.textMeasurements = incrementalLayoutCache_->textMeasurements;
        if (pendingIncrementalPlan_->work ==
            IncrementalPresentationWork::PaintOnly) {
            pass.PrepareAgainstCurrentLayout();
        } else if (!pass.BuildLocalLayout(
                pendingIncrementalPlan_->layoutBoundaries,
                incrementalLayoutCache_->nodes)) {
            pass.BuildLayout(!options.suppressFocusedDescendantFollow);
        }
        if (pass.layout.valid()) pass.SynchronizeScrollState();
    } else {
        pass.BuildLayout(!options.suppressFocusedDescendantFollow);
    }
    const auto preparationFinished = std::chrono::steady_clock::now();
    const auto presentationFollowStarted = preparationFinished;
    bool presentationMatchesLayout = false;
    std::size_t presentationFollowAttempts{};
    bool lastPresentationFollowChanged{};
    bool presentationCorrectnessFallbackUsed{};
    for (std::size_t followPass = 0;
         !options.suppressFocusedDescendantFollow &&
             followPass < kMaximumFocusFollowPasses;
         ++followPass) {
        ++presentationFollowAttempts;
        pass.presentation.clear();
        pass.ResolvePresentation(snapshot.root, 0.0F, 0.0F, viewport);
        presentationMatchesLayout = true;
        if (pass.FocusedTargetVisibleInPresentation()) {
            pass.focusFollowTrace.converged = true;
            break;
        }
        const auto followAttempt = pass.FollowFocusedDescendant(true);
        lastPresentationFollowChanged = followAttempt.changed;
        const bool stableOrRepeated = !lastPresentationFollowChanged ||
            !followAttempt.meaningfulOffsetChange ||
            followAttempt.repeatedOffsetState;
        if (stableOrRepeated && !presentationCorrectnessFallbackUsed) {
            // The focused target is still outside at least one scroll viewport.
            // Retain any meaningful destination vector already written by the
            // presentation pass, then use the existing bounded static/full
            // focus-follow path once to establish a correctness checkpoint.
            presentationCorrectnessFallbackUsed = true;
            presentationMatchesLayout = false;
            pass.BuildLayout();
            continue;
        }
        // A translated focused descendant may cross a scroll boundary even
        // when its static layout box was visible. Rebuild against the updated
        // host-owned offset and converge with the same hard bound used by
        // nested static focus follow.
        presentationMatchesLayout = false;
        pass.BuildLayout(
            false,
            true,
            RenderPass::CollectionAnchorPolicy::PreserveFocusFollowOffsets);
    }
    if (options.suppressFocusedDescendantFollow) {
        pass.presentation.clear();
        pass.ResolvePresentation(snapshot.root, 0.0F, 0.0F, viewport);
        presentationMatchesLayout = true;
    }
    if (presentationFollowAttempts == kMaximumFocusFollowPasses &&
        (!presentationMatchesLayout ||
         !pass.FocusedTargetVisibleInPresentation())) {
        pass.focusFollowTrace.boundHit = true;
    }
    if (!presentationMatchesLayout) {
        pass.presentation.clear();
        pass.ResolvePresentation(snapshot.root, 0.0F, 0.0F, viewport);
    }
    const auto presentationFinished = std::chrono::steady_clock::now();
    pass.AddFocusFollowElapsed(
        RenderPass::FocusFollowPhase::Presentation,
        static_cast<std::uint64_t>(
            std::chrono::duration_cast<std::chrono::microseconds>(
                presentationFinished - presentationFollowStarted).count()));
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
    pass.DrawNode(snapshot.root);
    const auto nodeDrawFinished = std::chrono::steady_clock::now();
    pass.DrawDeferredFocus();
    const auto deferredFocusFinished = std::chrono::steady_clock::now();
    pass.result.animationActive =
        pass.result.animationActive || motionTimeline_.EndFrame();
    if (roundedClip) renderTarget->PopLayer();
    else if (cornerRadius > 0.0F && renderTarget) renderTarget->PopAxisAlignedClip();
    const auto hasErrors = std::any_of(
        pass.result.diagnostics.begin(),
        pass.result.diagnostics.end(),
        [](const RenderDiagnostic& item) {
            return item.severity == RenderDiagnosticSeverity::Error;
        });
    pass.result.succeeded = !hasErrors && pass.layout.valid() && renderTarget;
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
    pass.result.timing.focusFollowSummary = pass.BuildFocusFollowSummary();
    pass.result.timing.collectionAdmissionSummary =
        std::move(collectionAdmissionSummary);
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
    incrementalLayoutCache_.reset();
    pendingIncrementalPlan_.reset();
}

ImageBitmapCacheStats DeclarativeRenderer::GetImageBitmapCacheStats() const noexcept {
    return {
        bitmaps_.size(),
        bitmapBytes_,
        bitmapHits_,
        bitmapCreates_,
        bitmapEvictions_,
        bitmapResourceInvalidations_,
        bitmapResourceGeneration_,
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
    if (resourceInvalidation) ++bitmapResourceInvalidations_;
}

void DeclarativeRenderer::TrimBitmapCache(
    const std::size_t incomingBytes) noexcept {
    while (!bitmaps_.empty() &&
           (bitmaps_.size() >= kMaximumBitmapEntries ||
            incomingBytes > kMaximumBitmapBytes - bitmapBytes_)) {
        const auto oldest = std::min_element(
            bitmaps_.begin(), bitmaps_.end(),
            [](const auto& left, const auto& right) {
                return left.second.lastUse < right.second.lastUse;
            });
        bitmapBytes_ -= oldest->second.bytes;
        bitmaps_.erase(oldest);
        ++bitmapEvictions_;
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
    std::wstring prefix(widgetInstanceId);
    prefix.push_back(L'\x1f');
    std::erase_if(scrollOffsets_, [&](const auto& entry) {
        return entry.first.starts_with(prefix);
    });
    motionTimeline_.ForgetPrefix(prefix);
}

ComPtr<ID2D1Bitmap> DeclarativeRenderer::GetImageBitmap(
    ID2D1RenderTarget* renderTarget,
    const WidgetNode& node,
    RenderPass& pass,
    const std::wstring_view artworkWidgetId,
    ImagePresentationState& presentationState) {
    presentationState = ImagePresentationState::Failed;
    const bool trustedArtwork = !node.artworkHandle.empty() && node.imageSource.empty();
    std::wstring source = trustedArtwork
        ? RemoteImageCache::TrustedArtworkKey(
            artworkWidgetId, node.id, node.artworkHandle)
        : node.imageSource;
    if (!imageCache_ || !renderTarget || source.empty()) {
        pass.Add(node.id, L"missing_image", L"Image has no HTTPS source or image cache.");
        return {};
    }
    if (!trustedArtwork && !RemoteImageCache::IsAllowedImageSource(source)) {
        pass.Add(node.id, L"invalid_image_url",
            L"Only bounded HTTPS or canonical inline PNG image sources are accepted.");
        return {};
    }
    if (trustedArtwork) {
        const auto separator = source.rfind(L'\x1f');
        const auto identityPrefix = source.substr(0, separator + 1);
        for (auto iterator = bitmaps_.begin(); iterator != bitmaps_.end();) {
            if (iterator->first != source && iterator->first.starts_with(identityPrefix)) {
                bitmapBytes_ -= iterator->second.bytes;
                iterator = bitmaps_.erase(iterator);
                ++bitmapEvictions_;
            } else ++iterator;
        }
    }
    if (const auto existing = bitmaps_.find(source); existing != bitmaps_.end())
    {
        existing->second.lastUse = ++bitmapAccessClock_;
        ++bitmapHits_;
        presentationState = ImagePresentationState::Ready;
        return existing->second.bitmap;
    }
    const auto state = imageCache_->GetState(source);
    if (state == RemoteImageState::Missing) {
        const auto request = trustedArtwork
            ? imageCache_->RequestTrustedArtwork(source)
            : imageCache_->Request(source);
        if (trustedArtwork &&
            imageCache_->GetState(source) == RemoteImageState::Failed) {
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
        pass.Add(node.id, L"image_bitmap", L"Ready image could not create a render-target bitmap.");
        return {};
    }
    ++bitmapCreates_;
    presentationState = ImagePresentationState::Ready;
    const auto pixelSize = bitmap->GetPixelSize();
    const auto byteCount64 = static_cast<std::uint64_t>(pixelSize.width) *
        static_cast<std::uint64_t>(pixelSize.height) * 4ULL;
    if (byteCount64 <= kMaximumBitmapBytes) {
        const auto byteCount = static_cast<std::size_t>(byteCount64);
        TrimBitmapCache(byteCount);
        bitmapBytes_ += byteCount;
        bitmaps_.emplace(
            source,
            BitmapCacheEntry{bitmap, byteCount, ++bitmapAccessClock_});
    }
    return bitmap;
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
