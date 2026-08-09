#include "DeclarativeRenderer.h"

#include "NativeIcons.h"
#include "RemoteImageCache.h"

#include <dwrite_1.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cwctype>
#include <limits>
#include <set>
#include <utility>

namespace gba {
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
constexpr std::size_t kMaximumTextCharacters = 4096;
constexpr float kMinimumControlSize = 44.0F;
constexpr std::size_t kMaximumScrollStateEntries = 4096;
constexpr std::size_t kMaximumFocusFollowPasses = 32;
constexpr float kRevealEpsilon = 0.01F;
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

[[nodiscard]] std::wstring TransformText(
    std::wstring text,
    const NativeTextTransform transform) {
    if (transform == NativeTextTransform::Uppercase) {
        std::transform(text.begin(), text.end(), text.begin(), [](const wchar_t value) {
            return static_cast<wchar_t>(std::towupper(value));
        });
    } else if (transform == NativeTextTransform::Lowercase) {
        std::transform(text.begin(), text.end(), text.begin(), [](const wchar_t value) {
            return static_cast<wchar_t>(std::towlower(value));
        });
    }
    if (text.size() > kMaximumTextCharacters) text.resize(kMaximumTextCharacters);
    return text;
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
    struct PresentationNode final {
        Rect borderBox;
        Rect contentBox;
        Rect visibleBox;
        Rect ancestorClip;
        DeclarativeMotionSample motion;
    };

    DeclarativeRenderer* owner{};
    ID2D1RenderTarget* target{};
    const WidgetSnapshot* snapshot{};
    std::wstring focusedId;
    std::wstring pressedId;
    Rect viewport;
    bool compactMode{};
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
        if (node.kind == L"slider") {
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
                offset->second.lastAccess = ++owner->scrollStateAccessClock_;
                element.scrollOffset = offset->second.offset;
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
        element.stretchCrossAxis = element.crossAxisAlignment == declarative::CrossAxisAlignment::Stretch;
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
        owner->scrollOffsets_.insert_or_assign(
            std::wstring{key},
            DeclarativeRenderer::ScrollStateEntry{
                offset,
                ++owner->scrollStateAccessClock_,
            });
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

    [[nodiscard]] bool FollowFocusedDescendant(
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

    [[nodiscard]] bool AxisCanReveal(
        const std::vector<const WidgetNode*>& path,
        const std::size_t clipPathIndex,
        const declarative::ScrollAxis axis,
        const float targetStart,
        const float targetSize,
        const float clipStart,
        const float clipSize) const {
        if (targetStart >= clipStart - kRevealEpsilon &&
            targetStart + targetSize <= clipStart + clipSize + kRevealEpsilon) {
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

    [[nodiscard]] ComPtr<IDWriteTextFormat> TextFormat(const NativeRenderStyle& style) {
        ComPtr<IDWriteTextFormat> format;
        if (!owner->writeFactory_) return format;
        const auto weight = static_cast<DWRITE_FONT_WEIGHT>(
            std::clamp(style.fontWeight(), 100, 900));
        const auto family = style.fontFamily().empty()
            ? L"Segoe UI Variable Text"
            : style.fontFamily().c_str();
        const auto resultCode = owner->writeFactory_->CreateTextFormat(
            family,
            nullptr,
            weight,
            DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH_NORMAL,
            std::clamp(style.fontSizePx(), 8.0F, 128.0F),
            L"",
            format.ReleaseAndGetAddressOf());
        if (FAILED(resultCode)) return {};
        switch (style.textAlign()) {
        case NativeTextAlign::Center:
            (void)format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
            break;
        case NativeTextAlign::End:
            (void)format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_TRAILING);
            break;
        default:
            (void)format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_LEADING);
            break;
        }
        (void)format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        (void)format->SetWordWrapping(
            style.maxLines() == 1 ? DWRITE_WORD_WRAPPING_NO_WRAP : DWRITE_WORD_WRAPPING_WRAP);
        if (style.textOverflow() == NativeTextOverflow::Ellipsis) {
            DWRITE_TRIMMING trimming{DWRITE_TRIMMING_GRANULARITY_CHARACTER, 0, 0};
            ComPtr<IDWriteInlineObject> sign;
            if (SUCCEEDED(owner->writeFactory_->CreateEllipsisTrimmingSign(
                    format.Get(), sign.ReleaseAndGetAddressOf()))) {
                (void)format->SetTrimming(&trimming, sign.Get());
            }
        }
        return format;
    }

    [[nodiscard]] Size MeasureText(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        const declarative::MeasureConstraints& constraints) {
        const auto text = TransformText(node.text, style.textTransform());
        const auto maximumWidth = std::max(1.0F, constraints.maximumWidth);
        const auto lineHeight = style.fontSizePx() * style.lineHeight();
        const auto maximumHeight = std::max(
            lineHeight,
            std::min(constraints.maximumHeight, lineHeight * static_cast<float>(style.maxLines())));
        auto format = TextFormat(style);
        if (!format || !owner->writeFactory_) {
            const auto estimated = std::min(
                maximumWidth,
                static_cast<float>(text.size()) * style.fontSizePx() * 0.56F);
            const auto lines = std::max(1.0F,
                std::ceil(static_cast<float>(text.size()) * style.fontSizePx() * 0.56F /
                    maximumWidth));
            return {estimated, std::min(maximumHeight, lines * lineHeight)};
        }
        ComPtr<IDWriteTextLayout> textLayout;
        if (FAILED(owner->writeFactory_->CreateTextLayout(
                text.data(),
                static_cast<UINT32>(text.size()),
                format.Get(),
                maximumWidth,
                maximumHeight,
                textLayout.ReleaseAndGetAddressOf()))) {
            return {};
        }
        (void)textLayout->SetLineSpacing(
            DWRITE_LINE_SPACING_METHOD_UNIFORM,
            lineHeight,
            lineHeight * 0.8F);
        if (std::abs(style.letterSpacingPx()) > 0.001F) {
            ComPtr<IDWriteTextLayout1> layout1;
            if (SUCCEEDED(textLayout.As(&layout1))) {
                DWRITE_TEXT_RANGE range{0, static_cast<UINT32>(text.size())};
                (void)layout1->SetCharacterSpacing(
                    0.0F,
                    style.letterSpacingPx(),
                    0.0F,
                    range);
            }
        }
        DWRITE_TEXT_METRICS metrics{};
        if (FAILED(textLayout->GetMetrics(&metrics))) return {};
        return {
            std::min(maximumWidth, metrics.widthIncludingTrailingWhitespace),
            std::min(maximumHeight, metrics.height),
        };
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
            const auto text = MeasureText(node, style, constraints);
            const auto leadingWidth = !node.imageSource.empty()
                ? 52.0F
                : node.glyph.empty() ? 0.0F : 28.0F;
            return {
                std::min(constraints.maximumWidth, text.width + leadingWidth + 24.0F),
                std::max(kMinimumControlSize, text.height + 16.0F),
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

    void BuildLayout(const bool followStaticFocus = true) {
        // First pass gives percentage/em adaptation a deterministic parent estimate.
        prepared.clear();
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
        // Focus-follow runs after percentage/em correction. Nested scrollers
        // require a fixed point: revealing inside the innermost viewport moves
        // the target geometry observed by each outer viewport. The wire tree
        // depth is bounded to 32, so this loop has a matching hard ceiling and
        // performs no relayout once offsets are stable.
        if (followStaticFocus) {
            for (std::size_t pass = 0; pass < kMaximumFocusFollowPasses; ++pass) {
                if (!FollowFocusedDescendant()) break;
                prepared.clear();
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

    void DrawTextContent(
        const WidgetNode& node,
        const NativeRenderStyle& style,
        Rect rect,
        const float opacity) {
        if (!target || node.text.empty()) return;
        auto format = TextFormat(style);
        if (!format || !owner->writeFactory_) {
            Add(node.id, L"text_format", L"DirectWrite could not create a text format.");
            return;
        }
        auto text = TransformText(node.text, style.textTransform());
        const auto lineHeight = style.fontSizePx() * style.lineHeight();
        rect.height = std::min(rect.height, lineHeight * static_cast<float>(style.maxLines()));
        ComPtr<IDWriteTextLayout> textLayout;
        if (FAILED(owner->writeFactory_->CreateTextLayout(
                text.data(), static_cast<UINT32>(text.size()), format.Get(),
                std::max(1.0F, rect.width), std::max(1.0F, rect.height),
                textLayout.ReleaseAndGetAddressOf()))) {
            Add(node.id, L"text_layout", L"DirectWrite could not create a text layout.");
            return;
        }
        (void)textLayout->SetLineSpacing(
            DWRITE_LINE_SPACING_METHOD_UNIFORM, lineHeight, lineHeight * 0.8F);
        if (std::abs(style.letterSpacingPx()) > 0.001F) {
            ComPtr<IDWriteTextLayout1> layout1;
            if (SUCCEEDED(textLayout.As(&layout1))) {
                (void)layout1->SetCharacterSpacing(
                    0.0F, style.letterSpacingPx(), 0.0F,
                    DWRITE_TEXT_RANGE{0, static_cast<UINT32>(text.size())});
            }
        }
        const auto color = style.foreground().value_or(kDefaultText);
        auto brush = Brush(target, WithOpacity(color, opacity));
        if (brush) {
            target->DrawTextLayout(
                D2D1::Point2F(rect.x, rect.y),
                textLayout.Get(),
                brush.Get(),
                D2D1_DRAW_TEXT_OPTIONS_CLIP);
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
        auto bitmap = owner->GetImageBitmap(target, node, *this);
        if (!bitmap) {
            DrawSemanticIcon(node, style, Inset(rect, std::min(rect.width, rect.height) * 0.32F),
                opacity * 0.65F, L"warning");
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
#ifdef GBA_DECLARATIVE_RENDERER_TESTING
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
        const float opacity) {
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
        const Rect cue{
            rect.x + rect.width - size - 10.0F,
            rect.y + (rect.height - size) * 0.5F,
            size,
            size,
        };
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
#ifdef GBA_DECLARATIVE_RENDERER_TESTING
        result.elementRects[node.id] = presented.borderBox;
        result.elementVisibleRects[node.id] = presented.visibleBox;
#endif

        const auto visibleRect = presented.visibleBox;
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
            const bool hasLeading = !node.imageSource.empty() || !node.glyph.empty();
            const bool hasText = !node.text.empty();
            const bool reserveStateCue = hasText &&
                (node.isBusy || node.isSelected || node.isDisabled);
            const auto maximumLeadingSize = node.imageSource.empty() ? 32.0F : 44.0F;
            const auto iconSize = hasLeading
                ? std::min(maximumLeadingSize, std::max(0.0F, textRect.height))
                : 0.0F;
            const bool hasTextAlignment = HasComputedProperty(
                node, L"text-align", focused, node.id == pressedId);
            const auto alignment = hasText && hasTextAlignment
                ? style.textAlign()
                : NativeTextAlign::Center;
            const auto stateCueReserve = reserveStateCue
                ? std::min(34.0F, textRect.width * 0.25F)
                : 0.0F;
            const auto textBudget = std::max(
                1.0F, textRect.width - (hasLeading && hasText ? iconSize + 8.0F : 0.0F) -
                    stateCueReserve);
            const auto measured = hasText
                ? MeasureText(node, style, {textBudget, textRect.height})
                : Size{};
            const auto placement = DeclarativeRenderer::ComputeButtonContentPlacement(
                textRect, iconSize, measured.width, hasLeading, hasText,
                reserveStateCue, alignment);
            textRect = placement.text;
            if (hasLeading) {
                const Rect iconRect = placement.leading;
                if (!node.imageSource.empty()) {
                    const auto visibleImageRect = Intersection(
                        iconRect, presented.visibleBox);
                    if (visibleImageRect.width > 0.5F && visibleImageRect.height > 0.5F)
                        DrawImage(node, style, iconRect, opacity, focused);
                }
                else
                    DrawSemanticIcon(node, style, iconRect, opacity, node.glyph);
            }
            if (hasText) DrawTextContent(node, style, textRect, opacity);
            DrawStateCue(node, style, paintRect, opacity);
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

    if (bitmapTarget_ != renderTarget) {
        bitmaps_.clear();
        bitmapTarget_ = renderTarget;
    }
    pass.BuildLayout();
    bool presentationMatchesLayout = false;
    for (std::size_t followPass = 0;
         followPass < kMaximumFocusFollowPasses;
         ++followPass) {
        pass.presentation.clear();
        pass.ResolvePresentation(snapshot.root, 0.0F, 0.0F, viewport);
        presentationMatchesLayout = true;
        if (!pass.FollowFocusedDescendant(true)) break;
        // A translated focused descendant may cross a scroll boundary even
        // when its static layout box was visible. Rebuild against the updated
        // host-owned offset and converge with the same hard bound used by
        // nested static focus follow.
        presentationMatchesLayout = false;
        pass.BuildLayout(false);
    }
    if (!presentationMatchesLayout) {
        pass.presentation.clear();
        pass.ResolvePresentation(snapshot.root, 0.0F, 0.0F, viewport);
    }
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
    pass.DrawNode(snapshot.root);
    pass.DrawDeferredFocus();
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
    return pass.result;
}

void DeclarativeRenderer::DiscardTargetResources() noexcept {
    bitmaps_.clear();
    bitmapTarget_ = nullptr;
    surfaceClipLayer_.Reset();
    surfaceClipGeometry_.Reset();
    surfaceClipTarget_ = nullptr;
    surfaceClipRect_ = {};
    surfaceClipRadius_ = 0.0F;
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
    RenderPass& pass) {
    if (!imageCache_ || !renderTarget || node.imageSource.empty()) {
        pass.Add(node.id, L"missing_image", L"Image has no HTTPS source or image cache.");
        return {};
    }
    if (!RemoteImageCache::IsAllowedImageSource(node.imageSource)) {
        pass.Add(node.id, L"invalid_image_url",
            L"Only bounded HTTPS or canonical inline PNG image sources are accepted.");
        return {};
    }
    if (const auto existing = bitmaps_.find(node.imageSource); existing != bitmaps_.end())
        return existing->second;
    const auto state = imageCache_->GetState(node.imageSource);
    if (state == RemoteImageState::Missing) {
        const auto request = imageCache_->Request(node.imageSource);
        if (request == RemoteImageRequestResult::InvalidUrl ||
            request == RemoteImageRequestResult::CapacityExceeded ||
            request == RemoteImageRequestResult::ShuttingDown) {
            pass.Add(node.id, L"image_request", L"Remote image request was rejected by cache policy.");
        }
        return {};
    }
    if (state == RemoteImageState::Queued || state == RemoteImageState::Loading) return {};
    if (state == RemoteImageState::Failed) {
        pass.Add(node.id, L"image_failed", imageCache_->GetError(node.imageSource));
        return {};
    }
    ComPtr<ID2D1Bitmap> bitmap;
    const auto result = imageCache_->CreateBitmap(
        renderTarget, node.imageSource, bitmap.ReleaseAndGetAddressOf());
    if (FAILED(result) || !bitmap) {
        pass.Add(node.id, L"image_bitmap", L"Ready image could not create a render-target bitmap.");
        return {};
    }
    if (bitmaps_.size() >= kMaximumBitmapEntries) bitmaps_.clear();
    bitmaps_.emplace(node.imageSource, bitmap);
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
        return {{}, {}};
    }

    // A trailing semantic cue must not collide with the primary content, but
    // it consumes space on the trailing side only. Reserving the same amount
    // on both sides needlessly starves compact icon-label buttons (especially
    // disabled controls, which also render a trailing unavailable cue).
    const auto cueInset = reserveTrailingStateCue
        ? std::min(34.0F, content.width * 0.25F)
        : 0.0F;
    const Rect safe{
        content.x,
        content.y,
        std::max(0.0F, content.width - cueInset),
        content.height,
    };
    const auto resolvedLeading = hasLeading && std::isfinite(leadingSize)
        ? std::clamp(leadingSize, 0.0F, std::min(safe.width, safe.height))
        : 0.0F;
    const auto gap = hasLeading && hasText && safe.width > resolvedLeading
        ? std::min(8.0F, safe.width - resolvedLeading)
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
    auto textX = groupX + resolvedLeading + gap;
    if (alignment == NativeTextAlign::Center && hasText) {
        // A button label is the primary affordance. Keep the label itself on
        // the control's visual center and place a leading icon beside it when
        // there is room. Centering the complete icon-label group makes every
        // label drift right by half the icon/gap, which is especially visible
        // in paired controller actions such as Spotify Connect and Setup.
        const auto centeredTextX = content.x + (content.width - resolvedText) * 0.5F;
        const auto leadingX = centeredTextX - gap - resolvedLeading;
        if ((!hasLeading || leadingX >= safe.x) &&
            centeredTextX + resolvedText <= safe.x + safe.width) {
            textX = centeredTextX;
            groupX = hasLeading ? leadingX : centeredTextX;
        }
    }
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
    return {leading, text};
}

} // namespace gba
