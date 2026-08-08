#include "DeclarativeLayout.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <set>
#include <utility>

namespace gba::declarative {
namespace {

constexpr float kMaximumCoordinate = 1'000'000.0F;
constexpr float kMaximumSpacing = 256.0F;
constexpr float kMaximumFlex = 8.0F;
constexpr float kMinimumRatio = 0.2F;
constexpr float kMaximumRatio = 5.0F;
constexpr float kEpsilon = 0.0001F;
constexpr std::size_t kMaximumNodes = 4096;
constexpr std::size_t kMaximumDepth = 64;

struct Edges {
    float top{};
    float right{};
    float bottom{};
    float left{};
};

struct MeasuredNode {
    Size size;
};

struct FlexItem {
    const LayoutElement* element{};
    Edges margin;
    float main{};
    float minimumMain{};
    float maximumMain{kMaximumCoordinate};
    float grow{};
    float shrink{};
};

[[nodiscard]] float ClampFinite(
    const float value,
    const float fallback,
    const float minimum,
    const float maximum) noexcept {
    return std::isfinite(value) ? std::clamp(value, minimum, maximum) : fallback;
}

[[nodiscard]] float Horizontal(const Edges& edges) noexcept {
    return edges.left + edges.right;
}

[[nodiscard]] float Vertical(const Edges& edges) noexcept {
    return edges.top + edges.bottom;
}

[[nodiscard]] bool Contains(const Rect& outer, const Rect& inner) noexcept {
    return inner.x >= outer.x - kEpsilon && inner.y >= outer.y - kEpsilon &&
        inner.x + inner.width <= outer.x + outer.width + kEpsilon &&
        inner.y + inner.height <= outer.y + outer.height + kEpsilon;
}

[[nodiscard]] Rect Intersect(const Rect& first, const Rect& second) noexcept {
    const auto left = std::max(first.x, second.x);
    const auto top = std::max(first.y, second.y);
    const auto right = std::min(first.x + first.width, second.x + second.width);
    const auto bottom = std::min(first.y + first.height, second.y + second.height);
    return {
        left,
        top,
        std::max(0.0F, right - left),
        std::max(0.0F, bottom - top),
    };
}

[[nodiscard]] Rect Union(const Rect& first, const Rect& second) noexcept {
    if (first.width <= 0.0F && first.height <= 0.0F) return second;
    if (second.width <= 0.0F && second.height <= 0.0F) return first;
    const auto left = std::min(first.x, second.x);
    const auto top = std::min(first.y, second.y);
    const auto right = std::max(first.x + first.width, second.x + second.width);
    const auto bottom = std::max(first.y + first.height, second.y + second.height);
    return {left, top, right - left, bottom - top};
}

class Engine final {
public:
    Engine(
        const IntrinsicMeasureCallback& measureIntrinsic,
        LayoutOptions options,
        Rect viewport)
        : measureIntrinsic_(measureIntrinsic),
          options_(options),
          viewport_(SanitizeViewport(viewport)) {
        if (!std::isfinite(options_.pixelScale) ||
            options_.pixelScale < 0.25F ||
            options_.pixelScale > 8.0F) {
            AddIssue({}, "invalid_pixel_scale", "Pixel scale was replaced with 1.", LayoutIssueSeverity::Warning);
            options_.pixelScale = 1.0F;
        }
        options_.compactWidth = ClampFinite(options_.compactWidth, 960.0F, 1.0F, kMaximumCoordinate);
        options_.compactHeight = ClampFinite(options_.compactHeight, 540.0F, 1.0F, kMaximumCoordinate);
        const auto responsiveWidth = options_.responsiveViewport
            ? ClampFinite(options_.responsiveViewport->width, viewport_.width, 0.0F, kMaximumCoordinate)
            : viewport_.width;
        const auto responsiveHeight = options_.responsiveViewport
            ? ClampFinite(options_.responsiveViewport->height, viewport_.height, 0.0F, kMaximumCoordinate)
            : viewport_.height;
        result_.compactMode =
            responsiveWidth < options_.compactWidth || responsiveHeight < options_.compactHeight;
    }

    [[nodiscard]] LayoutResult Run(const LayoutElement& root) {
        std::set<std::string, std::less<>> ids;
        std::size_t nodes = 0;
        Preflight(root, 1, ids, nodes);
        if (!result_.valid()) return std::move(result_);

        const auto rootMargin = ResolveSpacing(root.margin, root.id, true);
        Rect available{
            viewport_.x + rootMargin.left,
            viewport_.y + rootMargin.top,
            std::max(0.0F, viewport_.width - Horizontal(rootMargin)),
            std::max(0.0F, viewport_.height - Vertical(rootMargin)),
        };
        const auto measured = Measure(root, available.width, available.height);
        auto rootWidth = root.width.has_value()
            ? ResolveOptional(root.width, root.id, "width", 0.0F, kMaximumCoordinate).value_or(0.0F)
            : (options_.fillAutoRoot ? available.width : measured.size.width);
        auto rootHeight = root.height.has_value()
            ? ResolveOptional(root.height, root.id, "height", 0.0F, kMaximumCoordinate).value_or(0.0F)
            : (options_.fillAutoRoot ? available.height : measured.size.height);
        ApplyAspectRatio(root, rootWidth, rootHeight, root.width.has_value(), root.height.has_value());
        ClampDimensions(root, rootWidth, rootHeight);
        rootWidth = std::min(rootWidth, available.width);
        rootHeight = std::min(rootHeight, available.height);
        (void)LayoutNode(root, {available.x, available.y, rootWidth, rootHeight}, viewport_);
        return std::move(result_);
    }

private:
    [[nodiscard]] Rect SanitizeViewport(const Rect value) {
        Rect result{
            ClampFinite(value.x, 0.0F, -kMaximumCoordinate, kMaximumCoordinate),
            ClampFinite(value.y, 0.0F, -kMaximumCoordinate, kMaximumCoordinate),
            ClampFinite(value.width, 0.0F, 0.0F, kMaximumCoordinate),
            ClampFinite(value.height, 0.0F, 0.0F, kMaximumCoordinate),
        };
        if (!std::isfinite(value.x) || !std::isfinite(value.y) ||
            !std::isfinite(value.width) || !std::isfinite(value.height) ||
            value.width < 0.0F || value.height < 0.0F) {
            AddIssue({}, "invalid_viewport", "Non-finite or negative viewport values were safely clamped.", LayoutIssueSeverity::Warning);
        }
        return result;
    }

    void Preflight(
        const LayoutElement& element,
        const std::size_t depth,
        std::set<std::string, std::less<>>& ids,
        std::size_t& nodes) {
        if (++nodes > kMaximumNodes) {
            if (nodes == kMaximumNodes + 1)
                AddIssue(element.id, "tree_too_large", "Layout tree exceeds 4096 elements.", LayoutIssueSeverity::Error);
            return;
        }
        if (depth > kMaximumDepth) {
            AddIssue(element.id, "tree_too_deep", "Layout tree exceeds 64 levels.", LayoutIssueSeverity::Error);
            return;
        }
        if (element.id.empty() || element.id.size() > 128) {
            AddIssue(element.id, "invalid_id", "Every layout element requires a 1-128 byte stable ID.", LayoutIssueSeverity::Error);
        } else if (!ids.insert(element.id).second) {
            AddIssue(element.id, "duplicate_id", "Layout element IDs must be unique.", LayoutIssueSeverity::Error);
        }
        if ((element.scrollAxis == ScrollAxis::Vertical &&
             element.direction != LayoutDirection::Column) ||
            (element.scrollAxis == ScrollAxis::Horizontal &&
             element.direction != LayoutDirection::Row)) {
            AddIssue(element.id, "scroll_axis_direction_mismatch",
                "Scroll axis must match the container layout direction.",
                LayoutIssueSeverity::Error);
        }
        for (const auto& child : element.children) Preflight(child, depth + 1, ids, nodes);
    }

    [[nodiscard]] MeasuredNode Measure(
        const LayoutElement& element,
        const float maximumWidth,
        const float maximumHeight) {
        const auto padding = ResolveSpacing(element.padding, element.id, false);
        const auto innerMaximumWidth = std::max(0.0F, maximumWidth - Horizontal(padding));
        const auto innerMaximumHeight = std::max(0.0F, maximumHeight - Vertical(padding));
        float width = 0.0F;
        float height = 0.0F;

        if (element.children.empty()) {
            Size intrinsic{};
            if (measureIntrinsic_) {
                intrinsic = measureIntrinsic_(element, {
                    innerMaximumWidth,
                    innerMaximumHeight,
                    result_.compactMode,
                });
                const auto cleanWidth = ClampFinite(intrinsic.width, 0.0F, 0.0F, kMaximumCoordinate);
                const auto cleanHeight = ClampFinite(intrinsic.height, 0.0F, 0.0F, kMaximumCoordinate);
                if (cleanWidth != intrinsic.width || cleanHeight != intrinsic.height) {
                    AddIssue(element.id, "invalid_intrinsic_size",
                        "Intrinsic measurement returned non-finite, negative, or excessive geometry.",
                        LayoutIssueSeverity::Warning);
                }
                intrinsic = {cleanWidth, cleanHeight};
            }
            width = intrinsic.width + Horizontal(padding);
            height = intrinsic.height + Vertical(padding);
        } else {
            const auto gap = ResolveNumber(element.gap, element.id, "gap", 0.0F, kMaximumSpacing, 0.0F);
            float totalMain = 0.0F;
            float largestCross = 0.0F;
            for (const auto& child : element.children) {
                const auto childMargin = ResolveSpacing(child.margin, child.id, true);
                const auto childMeasured = Measure(child, innerMaximumWidth, innerMaximumHeight).size;
                if (element.direction == LayoutDirection::Row) {
                    totalMain += childMeasured.width + Horizontal(childMargin);
                    largestCross = std::max(largestCross, childMeasured.height + Vertical(childMargin));
                } else {
                    totalMain += childMeasured.height + Vertical(childMargin);
                    largestCross = std::max(largestCross, childMeasured.width + Horizontal(childMargin));
                }
            }
            if (element.children.size() > 1)
                totalMain += gap * static_cast<float>(element.children.size() - 1);
            if (element.direction == LayoutDirection::Row) {
                width = totalMain + Horizontal(padding);
                height = largestCross + Vertical(padding);
            } else {
                width = largestCross + Horizontal(padding);
                height = totalMain + Vertical(padding);
            }
        }

        const auto explicitWidth = ResolveOptional(element.width, element.id, "width", 0.0F, kMaximumCoordinate);
        const auto explicitHeight = ResolveOptional(element.height, element.id, "height", 0.0F, kMaximumCoordinate);
        if (explicitWidth) width = *explicitWidth;
        if (explicitHeight) height = *explicitHeight;
        ApplyAspectRatio(element, width, height, explicitWidth.has_value(), explicitHeight.has_value());
        ClampDimensions(element, width, height);
        return {{width, height}};
    }

    [[nodiscard]] Rect LayoutNode(
        const LayoutElement& element,
        Rect assigned,
        const Rect ancestorClip) {
        assigned = SnapRect(SanitizeRect(assigned));
        const auto padding = ResolveSpacing(element.padding, element.id, false);
        Rect content{
            assigned.x + padding.left,
            assigned.y + padding.top,
            std::max(0.0F, assigned.width - Horizontal(padding)),
            std::max(0.0F, assigned.height - Vertical(padding)),
        };
        content = SnapRect(content);
        LayoutBox box{
            assigned,
            content,
            Intersect(ancestorClip, assigned),
            false,
            false,
            !Contains(ancestorClip, assigned),
            element.scrollAxis,
            0.0F,
            0.0F,
        };
        result_.boxes[element.id] = box;
        if (element.children.empty()) return assigned;

        const auto gap = ResolveNumber(element.gap, element.id, "gap", 0.0F, kMaximumSpacing, 0.0F);
        const auto row = element.direction == LayoutDirection::Row;
        const auto scrollsMainAxis =
            (row && element.scrollAxis == ScrollAxis::Horizontal) ||
            (!row && element.scrollAxis == ScrollAxis::Vertical);
        const auto availableMain = row ? content.width : content.height;
        const auto availableCross = row ? content.height : content.width;
        std::vector<FlexItem> items;
        items.reserve(element.children.size());
        float margins = 0.0F;

        for (const auto& child : element.children) {
            const auto margin = ResolveSpacing(child.margin, child.id, true);
            const auto measured = Measure(
                child,
                element.scrollAxis == ScrollAxis::Horizontal
                    ? kMaximumCoordinate : content.width,
                element.scrollAxis == ScrollAxis::Vertical
                    ? kMaximumCoordinate : content.height).size;
            const auto explicitMain = row ? child.width : child.height;
            const auto basis = ResolveOptional(child.flexBasis, child.id, "flexBasis", 0.0F, kMaximumCoordinate);
            const auto preferred = basis.value_or(
                explicitMain.has_value()
                    ? ResolveOptional(explicitMain, child.id, row ? "width" : "height", 0.0F, kMaximumCoordinate).value_or(0.0F)
                    : (row ? measured.width : measured.height));
            auto minimum = ResolveOptional(row ? child.minWidth : child.minHeight, child.id,
                row ? "minWidth" : "minHeight", 0.0F, kMaximumCoordinate).value_or(0.0F);
            auto maximum = ResolveOptional(row ? child.maxWidth : child.maxHeight, child.id,
                row ? "maxWidth" : "maxHeight", 0.0F, kMaximumCoordinate).value_or(kMaximumCoordinate);
            if (maximum < minimum) {
                AddIssue(child.id, "inverted_constraints", "Maximum size was raised to the minimum size.", LayoutIssueSeverity::Warning);
                maximum = minimum;
            }
            const auto grow = ResolveNumber(child.flexGrow, child.id, "flexGrow", 0.0F, kMaximumFlex, 0.0F);
            const auto shrink = ResolveNumber(child.flexShrink, child.id, "flexShrink", 0.0F, kMaximumFlex, 1.0F);
            // An auto-height intrinsic leaf (text, button, icon, image, and
            // other renderer-measured content) must not be flexed below the
            // height it was measured to paint. Doing so assigns DirectWrite a
            // shorter box than its wrapped line metrics and collapses
            // controller controls below their interaction contract. An
            // authored min-height is still only a lower bound (44 DIPs is a
            // common controller target); it is not permission to discard a
            // taller wrapped label plus padding. Authors can opt into a fixed,
            // potentially clipped box with an explicit height or flex-basis.
            // Keep row widths shrinkable so text can reflow responsively
            // instead of imposing a CSS-like min-content width on every label.
            if (!row && child.children.empty() && !child.height.has_value() &&
                !child.flexBasis.has_value()) {
                minimum = std::max(minimum, std::min(measured.height, maximum));
            }
            const auto main = std::clamp(preferred, minimum, maximum);
            items.push_back({&child, margin, main, minimum, maximum, grow, shrink});
            margins += row ? Horizontal(margin) : Vertical(margin);
        }

        const auto totalGap =
            items.size() > 1 ? gap * static_cast<float>(items.size() - 1) : 0.0F;
        auto targetForItems = std::max(0.0F, availableMain - margins - totalGap);
        if (scrollsMainAxis) {
            float naturalMain{};
            for (const auto& item : items) naturalMain += item.main;
            targetForItems = std::max(targetForItems, naturalMain);
        }
        DistributeFlex(items, targetForItems);

        const auto childClip = element.overflow == OverflowBehavior::Clip ||
                element.scrollAxis != ScrollAxis::None
            ? Intersect(ancestorClip, content)
            : ancestorClip;
        float occupiedMain = margins + totalGap;
        for (const auto& item : items) occupiedMain += item.main;
        if (scrollsMainAxis) {
            box.maximumScrollOffset = std::max(0.0F, occupiedMain - availableMain);
            box.scrollOffset = ResolveNumber(
                element.scrollOffset,
                element.id,
                "scrollOffset",
                0.0F,
                box.maximumScrollOffset,
                0.0F);
        }
        const float remainingMain = std::max(0.0F, availableMain - occupiedMain);
        float leadingMain = 0.0F;
        float distributedGap = gap;
        switch (element.mainAxisAlignment) {
        case MainAxisAlignment::Center:
            leadingMain = remainingMain * 0.5F;
            break;
        case MainAxisAlignment::End:
            leadingMain = remainingMain;
            break;
        case MainAxisAlignment::SpaceBetween:
            if (items.size() > 1)
                distributedGap += remainingMain / static_cast<float>(items.size() - 1);
            break;
        case MainAxisAlignment::SpaceAround:
            if (!items.empty()) {
                const float share = remainingMain / static_cast<float>(items.size());
                leadingMain = share * 0.5F;
                distributedGap += share;
            }
            break;
        default:
            break;
        }
        auto cursor = (row ? content.x : content.y) + leadingMain - box.scrollOffset;
        Rect descendants{};
        bool haveDescendants = false;
        for (auto& item : items) {
            const auto& child = *item.element;
            const auto beforeMargin = row ? item.margin.left : item.margin.top;
            const auto afterMargin = row ? item.margin.right : item.margin.bottom;
            const auto crossBefore = row ? item.margin.top : item.margin.left;
            const auto crossAfter = row ? item.margin.bottom : item.margin.right;
            cursor += beforeMargin;

            const auto remeasured = Measure(
                child,
                row ? item.main : (element.scrollAxis == ScrollAxis::Horizontal
                    ? kMaximumCoordinate : availableCross),
                row ? (element.scrollAxis == ScrollAxis::Vertical
                    ? kMaximumCoordinate : availableCross) : item.main).size;
            auto cross = row ? remeasured.height : remeasured.width;
            const auto explicitCross = row ? child.height : child.width;
            // Cross-axis alignment belongs to the container. A child's own
            // `align` value controls its descendants and must not opt that
            // child out of its parent's stretch behavior.
            const bool stretch = element.crossAxisAlignment == CrossAxisAlignment::Stretch;
            if (stretch && !explicitCross.has_value() && !child.aspectRatio.has_value())
                cross = std::max(0.0F, availableCross - crossBefore - crossAfter);
            if (child.aspectRatio.has_value() && !explicitCross.has_value()) {
                const auto ratio = ResolveOptional(child.aspectRatio, child.id, "aspectRatio", kMinimumRatio, kMaximumRatio).value_or(1.0F);
                cross = row ? item.main / ratio : item.main * ratio;
            }
            auto crossMinimum = ResolveOptional(row ? child.minHeight : child.minWidth, child.id,
                row ? "minHeight" : "minWidth", 0.0F, kMaximumCoordinate).value_or(0.0F);
            auto crossMaximum = ResolveOptional(row ? child.maxHeight : child.maxWidth, child.id,
                row ? "maxHeight" : "maxWidth", 0.0F, kMaximumCoordinate).value_or(kMaximumCoordinate);
            if (crossMaximum < crossMinimum) crossMaximum = crossMinimum;
            cross = std::clamp(cross, crossMinimum, crossMaximum);
            cross = std::min(cross, std::max(crossMinimum, availableCross - crossBefore - crossAfter));

            const float crossRoom = std::max(0.0F,
                availableCross - crossBefore - crossAfter - cross);
            float crossOffset = crossBefore;
            if (element.crossAxisAlignment == CrossAxisAlignment::Center)
                crossOffset += crossRoom * 0.5F;
            else if (element.crossAxisAlignment == CrossAxisAlignment::End)
                crossOffset += crossRoom;
            Rect childRect = row
                ? Rect{cursor, content.y + crossOffset, item.main, cross}
                : Rect{content.x + crossOffset, cursor, cross, item.main};
            const auto childBounds = LayoutNode(child, childRect, childClip);
            descendants = haveDescendants ? Union(descendants, childBounds) : childBounds;
            haveDescendants = true;
            cursor += item.main + afterMargin + distributedGap;
        }

        if (haveDescendants) {
            box.overflowX = descendants.x < content.x - kEpsilon ||
                descendants.x + descendants.width > content.x + content.width + kEpsilon;
            box.overflowY = descendants.y < content.y - kEpsilon ||
                descendants.y + descendants.height > content.y + content.height + kEpsilon;
            if (element.scrollAxis == ScrollAxis::Horizontal)
                box.overflowX = box.maximumScrollOffset > kEpsilon;
            else if (element.scrollAxis == ScrollAxis::Vertical)
                box.overflowY = box.maximumScrollOffset > kEpsilon;
            result_.boxes[element.id] = box;
            return Union(assigned, descendants);
        }
        return assigned;
    }

    void DistributeFlex(std::vector<FlexItem>& items, const float target) {
        for (int pass = 0; pass < 16; ++pass) {
            float used = 0.0F;
            for (const auto& item : items) used += item.main;
            const auto free = target - used;
            if (std::abs(free) <= kEpsilon) break;

            float weight = 0.0F;
            for (const auto& item : items) {
                if (free > 0.0F && item.grow > 0.0F && item.main < item.maximumMain - kEpsilon)
                    weight += item.grow;
                else if (free < 0.0F && item.shrink > 0.0F && item.main > item.minimumMain + kEpsilon)
                    weight += item.shrink * std::max(item.main, 1.0F);
            }
            if (weight <= kEpsilon) break;

            bool clampedAny = false;
            for (auto& item : items) {
                const auto itemWeight = free > 0.0F
                    ? (item.main < item.maximumMain - kEpsilon ? item.grow : 0.0F)
                    : (item.main > item.minimumMain + kEpsilon
                        ? item.shrink * std::max(item.main, 1.0F)
                        : 0.0F);
                if (itemWeight <= 0.0F) continue;
                const auto proposed = item.main + free * (itemWeight / weight);
                const auto bounded = std::clamp(proposed, item.minimumMain, item.maximumMain);
                clampedAny |= std::abs(proposed - bounded) > kEpsilon;
                item.main = bounded;
            }
            if (!clampedAny) break;
        }
    }

    void ApplyAspectRatio(
        const LayoutElement& element,
        float& width,
        float& height,
        const bool widthExplicit,
        const bool heightExplicit) {
        const auto ratio = ResolveOptional(
            element.aspectRatio,
            element.id,
            "aspectRatio",
            kMinimumRatio,
            kMaximumRatio);
        if (!ratio) return;
        if (widthExplicit && !heightExplicit) height = width / *ratio;
        else if (heightExplicit && !widthExplicit) width = height * *ratio;
        else if (!heightExplicit && width > 0.0F) height = width / *ratio;
        else if (!widthExplicit && height > 0.0F) width = height * *ratio;
    }

    void ClampDimensions(const LayoutElement& element, float& width, float& height) {
        auto minWidth = ResolveOptional(element.minWidth, element.id, "minWidth", 0.0F, kMaximumCoordinate).value_or(0.0F);
        auto minHeight = ResolveOptional(element.minHeight, element.id, "minHeight", 0.0F, kMaximumCoordinate).value_or(0.0F);
        auto maxWidth = ResolveOptional(element.maxWidth, element.id, "maxWidth", 0.0F, kMaximumCoordinate).value_or(kMaximumCoordinate);
        auto maxHeight = ResolveOptional(element.maxHeight, element.id, "maxHeight", 0.0F, kMaximumCoordinate).value_or(kMaximumCoordinate);
        if (maxWidth < minWidth) maxWidth = minWidth;
        if (maxHeight < minHeight) maxHeight = minHeight;
        width = std::clamp(ClampFinite(width, 0.0F, 0.0F, kMaximumCoordinate), minWidth, maxWidth);
        height = std::clamp(ClampFinite(height, 0.0F, 0.0F, kMaximumCoordinate), minHeight, maxHeight);
    }

    [[nodiscard]] Edges ResolveSpacing(
        const BoxSpacing& spacing,
        const std::string_view id,
        const bool allowNegative) {
        if (spacing.count > 4) {
            AddIssue(id, "invalid_spacing", "Spacing count was outside 0-4 and was ignored.", LayoutIssueSeverity::Warning);
            return {};
        }
        const auto minimum = allowNegative ? -kMaximumSpacing : 0.0F;
        std::array<float, 4> clean{};
        for (std::size_t index = 0; index < spacing.count; ++index) {
            clean[index] = ResolveNumber(
                spacing.values[index], id, allowNegative ? "margin" : "padding",
                minimum, kMaximumSpacing, 0.0F);
        }
        switch (spacing.count) {
        case 0: return {};
        case 1: return {clean[0], clean[0], clean[0], clean[0]};
        case 2: return {clean[0], clean[1], clean[0], clean[1]};
        case 3: return {clean[0], clean[1], clean[2], clean[1]};
        default: return {clean[0], clean[1], clean[2], clean[3]};
        }
    }

    [[nodiscard]] std::optional<float> ResolveOptional(
        const std::optional<float> value,
        const std::string_view id,
        const std::string_view name,
        const float minimum,
        const float maximum) {
        if (!value) return std::nullopt;
        return ResolveNumber(*value, id, name, minimum, maximum, minimum);
    }

    [[nodiscard]] float ResolveNumber(
        const float value,
        const std::string_view id,
        const std::string_view name,
        const float minimum,
        const float maximum,
        const float fallback) {
        const auto result = ClampFinite(value, fallback, minimum, maximum);
        if (!std::isfinite(value) || result != value) {
            AddIssue(id, "value_clamped",
                std::string{name} + " was non-finite or outside its safe range.",
                LayoutIssueSeverity::Warning);
        }
        return result;
    }

    [[nodiscard]] Rect SanitizeRect(const Rect value) {
        return {
            ClampFinite(value.x, 0.0F, -kMaximumCoordinate, kMaximumCoordinate),
            ClampFinite(value.y, 0.0F, -kMaximumCoordinate, kMaximumCoordinate),
            ClampFinite(value.width, 0.0F, 0.0F, kMaximumCoordinate),
            ClampFinite(value.height, 0.0F, 0.0F, kMaximumCoordinate),
        };
    }

    [[nodiscard]] float Snap(const float value) const noexcept {
        return std::round(value * options_.pixelScale) / options_.pixelScale;
    }

    [[nodiscard]] Rect SnapRect(const Rect value) const noexcept {
        const auto left = Snap(value.x);
        const auto top = Snap(value.y);
        const auto right = Snap(value.x + value.width);
        const auto bottom = Snap(value.y + value.height);
        return {left, top, std::max(0.0F, right - left), std::max(0.0F, bottom - top)};
    }

    void AddIssue(
        const std::string_view id,
        const std::string_view code,
        const std::string_view message,
        const LayoutIssueSeverity severity) {
        const auto key = std::string{id} + "\n" + std::string{code} + "\n" + std::string{message};
        if (!issueKeys_.insert(key).second) return;
        result_.issues.push_back({
            severity,
            std::string{id},
            std::string{code},
            std::string{message},
        });
    }

    const IntrinsicMeasureCallback& measureIntrinsic_;
    LayoutOptions options_;
    LayoutResult result_;
    std::set<std::string, std::less<>> issueKeys_;
    Rect viewport_;
};

} // namespace

BoxSpacing BoxSpacing::One(const float all) noexcept {
    return {{all, 0.0F, 0.0F, 0.0F}, 1};
}

BoxSpacing BoxSpacing::Two(const float vertical, const float horizontal) noexcept {
    return {{vertical, horizontal, 0.0F, 0.0F}, 2};
}

BoxSpacing BoxSpacing::Three(
    const float top,
    const float horizontal,
    const float bottom) noexcept {
    return {{top, horizontal, bottom, 0.0F}, 3};
}

BoxSpacing BoxSpacing::Four(
    const float top,
    const float right,
    const float bottom,
    const float left) noexcept {
    return {{top, right, bottom, left}, 4};
}

bool LayoutResult::valid() const noexcept {
    return std::none_of(issues.begin(), issues.end(), [](const LayoutIssue& issue) {
        return issue.severity == LayoutIssueSeverity::Error;
    });
}

const LayoutBox* LayoutResult::Find(const std::string_view stableId) const noexcept {
    const auto found = boxes.find(stableId);
    return found == boxes.end() ? nullptr : &found->second;
}

LayoutResult ComputeLayout(
    const LayoutElement& root,
    const Rect viewport,
    const IntrinsicMeasureCallback& measureIntrinsic,
    const LayoutOptions options) {
    return Engine{measureIntrinsic, options, viewport}.Run(root);
}

} // namespace gba::declarative
