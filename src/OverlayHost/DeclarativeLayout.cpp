#include "DeclarativeLayout.h"
#include "TaffyLayoutBridge.h"
#include "WidgetProtocolPresentationContract.generated.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <set>
#include <utility>

namespace widgetrail::declarative {
namespace {

constexpr float kMaximumCoordinate = 1'000'000.0F;
constexpr float kMaximumSpacing = 256.0F;
constexpr float kMaximumFlex = 8.0F;
constexpr float kMinimumRatio = 0.2F;
constexpr float kMaximumRatio = 5.0F;
constexpr float kEpsilon = 0.0001F;
constexpr std::size_t kMaximumNodes = 4096;
constexpr std::size_t kMaximumDepth = 64;
constexpr float kMinimumGridColumnWidth =
    static_cast<float>(protocol_contract::MinimumGridColumnWidth);
constexpr float kMaximumGridColumnWidth =
    static_cast<float>(protocol_contract::MaximumGridColumnWidth);
constexpr std::size_t kMaximumGridColumns = protocol_contract::MaximumGridColumns;

struct Edges {
    float top{};
    float right{};
    float bottom{};
    float left{};
};

struct RawBox {
    Rect border;
    Rect content;
    Rect descendants;
    bool hasDescendants{};
    bool overflowX{};
    bool overflowY{};
    float maximumScrollOffset{};
    float admittedScrollOffset{};
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

} // namespace

struct LayoutSession::Impl {
    struct Entry {
        std::uint64_t key{}, revision{}, sourceRevision{};
        float maximumWidth{};
        bool compact{};
        bool hasMeasure{}, authoredHeight{};
    };
    WidgetRailTaffySession* handle{wrail_taffy_session_create()};
    std::map<std::string, Entry, std::less<>> entries;
    std::uint64_t nextKey{}, nextRevision{};
    ~Impl() { wrail_taffy_session_destroy(handle); }
};

LayoutSession::LayoutSession() : impl_(std::make_unique<Impl>()) {}
LayoutSession::~LayoutSession() = default;
void LayoutSession::Reset() { impl_ = std::make_unique<Impl>(); }

class LayoutEngine final {
public:
    LayoutEngine(
        const IntrinsicMeasureCallback& measureIntrinsic,
        LayoutOptions options,
        const Rect viewport)
        : measureIntrinsic_(measureIntrinsic),
          options_(options),
          viewport_(SanitizeViewport(viewport)) {
        if (!std::isfinite(options_.pixelScale) ||
            options_.pixelScale < 0.25F ||
            options_.pixelScale > 8.0F) {
            AddIssue({}, "invalid_pixel_scale", "Pixel scale was replaced with 1.",
                LayoutIssueSeverity::Warning);
            options_.pixelScale = 1.0F;
        }
        options_.compactWidth = ClampFinite(
            options_.compactWidth, 960.0F, 1.0F, kMaximumCoordinate);
        options_.compactHeight = ClampFinite(
            options_.compactHeight, 540.0F, 1.0F, kMaximumCoordinate);
        const auto responsiveWidth = options_.responsiveViewport
            ? ClampFinite(options_.responsiveViewport->width, viewport_.width,
                0.0F, kMaximumCoordinate)
            : viewport_.width;
        const auto responsiveHeight = options_.responsiveViewport
            ? ClampFinite(options_.responsiveViewport->height, viewport_.height,
                0.0F, kMaximumCoordinate)
            : viewport_.height;
        result_.compactMode = responsiveWidth < options_.compactWidth ||
            responsiveHeight < options_.compactHeight;
    }

    [[nodiscard]] LayoutResult Run(const LayoutElement& root) {
        std::set<std::string, std::less<>> ids;
        std::size_t nodeCount{};
        (void)Preflight(root, 1, ids, nodeCount);
        if (!result_.valid()) return std::move(result_);
        if (wrail_taffy_abi_version() != WRAIL_TAFFY_ABI_VERSION) {
            AddIssue({}, "taffy_abi_mismatch",
                "The native host and Taffy static library use different ABI versions.",
                LayoutIssueSeverity::Error);
            return std::move(result_);
        }

        const auto rootMargin = ResolveSpacing(root.margin, root.id, true);
        const auto availableWidth = std::max(0.0F,
            viewport_.width - Horizontal(rootMargin));
        const auto availableHeight = std::max(0.0F,
            viewport_.height - Vertical(rootMargin));
        const auto rootIndex = Flatten(
            root, std::nullopt, ScrollAxis::None, availableWidth);
        auto& rootInput = inputs_[rootIndex];
        const bool fillAutoRootWidth =
            options_.fillAutoRootWidth.value_or(options_.fillAutoRoot);
        // Taffy's root has no parent node. Its authored percentage uses the
        // supplied viewport. Retained local roots are already pinned to their
        // parent-assigned absolute width by the renderer.
        if (rootInput.widthFraction.present) {
            rootInput.width = Present(std::min(availableWidth,
                rootInput.widthFraction.value * availableWidth));
            rootInput.widthFraction = {};
        }
        if (fillAutoRootWidth && !root.width && !root.widthFraction)
            rootInput.width = Present(availableWidth);
        else if (rootInput.width.present)
            rootInput.width.value = std::min(rootInput.width.value, availableWidth);
        if (options_.fillAutoRoot && !root.height)
            rootInput.height = Present(availableHeight);
        else if (rootInput.height.present)
            rootInput.height.value = std::min(rootInput.height.value, availableHeight);

        if (options_.session && options_.session->impl_->handle) {
            auto& state = *options_.session->impl_;
            std::set<std::string, std::less<>> admitted;
            for (std::size_t index = 0; index < elements_.size(); ++index) {
                const auto& element = *elements_[index];
                admitted.insert(element.id);
                auto& entry = state.entries[element.id];
                if (!entry.key) entry.key = ++state.nextKey;
                if (!element.measureRevision || entry.sourceRevision != element.measureRevision ||
                    entry.maximumWidth != measurementMaximumWidths_[index] || entry.compact != result_.compactMode ||
                    entry.hasMeasure != static_cast<bool>(measureIntrinsic_) || entry.authoredHeight != element.height.has_value()) {
                    entry.revision = ++state.nextRevision;
                    entry.sourceRevision = element.measureRevision;
                    entry.maximumWidth = measurementMaximumWidths_[index];
                    entry.compact = result_.compactMode;
                    entry.hasMeasure = static_cast<bool>(measureIntrinsic_);
                    entry.authoredHeight = element.height.has_value();
                }
                inputs_[index].stableKey = entry.key;
                inputs_[index].measureRevision = entry.revision;
            }
            std::erase_if(state.entries, [&](const auto& entry) { return !admitted.contains(entry.first); });
        }
        outputs_.resize(inputs_.size());
        const auto compute = [&]() {
            if (options_.session && options_.session->impl_->handle)
                return wrail_taffy_session_compute(options_.session->impl_->handle,
                    inputs_.data(), inputs_.size(), childIndices_.data(), childIndices_.size(),
                    rootIndex, availableWidth, availableHeight,
                    options_.intrinsicRootHeight ? WRAIL_TAFFY_AVAILABLE_MAX_CONTENT : WRAIL_TAFFY_AVAILABLE_DEFINITE,
                    &MeasureThunk, this, outputs_.data(), outputs_.size());
            return wrail_taffy_compute(
                inputs_.data(), inputs_.size(), childIndices_.data(),
                childIndices_.size(), rootIndex, availableWidth, availableHeight,
                options_.intrinsicRootHeight
                    ? WRAIL_TAFFY_AVAILABLE_MAX_CONTENT
                    : WRAIL_TAFFY_AVAILABLE_DEFINITE,
                &MeasureThunk, this, outputs_.data(), outputs_.size());
        };
        auto status = compute();
        if (status == WRAIL_TAFFY_OK && !fillAutoRootWidth && !root.width && !root.widthFraction &&
            root.layoutMode != LayoutMode::ResponsiveGrid) {
            const auto& first = outputs_[rootIndex];
            const auto shrinkWidth = std::clamp(
                first.contentWidth + first.padding.left + first.padding.right,
                0.0F, availableWidth);
            if (shrinkWidth > kEpsilon &&
                shrinkWidth < first.width - kEpsilon) {
                rootInput.width = Present(shrinkWidth);
                status = compute();
            }
        }
        if (status != WRAIL_TAFFY_OK) {
            AddIssue(root.id, "taffy_layout_failed",
                "Taffy rejected or failed the validated semantic layout tree (code " +
                    std::to_string(status) + ").",
                LayoutIssueSeverity::Error);
            return std::move(result_);
        }

        raw_.resize(inputs_.size());
        (void)BuildRaw(rootIndex, viewport_.x, viewport_.y);
        Publish(rootIndex, 0.0F, 0.0F, viewport_);
        if ((!result_.valid() || intrinsicMeasurementFailed_) && options_.session) options_.session->Reset();
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
            AddIssue({}, "invalid_viewport",
                "Non-finite or negative viewport values were safely clamped.",
                LayoutIssueSeverity::Warning);
        }
        return result;
    }

    [[nodiscard]] bool Preflight(
        const LayoutElement& element,
        const std::size_t depth,
        std::set<std::string, std::less<>>& ids,
        std::size_t& nodes) {
        bool containsVerticalScroll =
            element.scrollAxis == ScrollAxis::Vertical;
        if (++nodes > kMaximumNodes) {
            if (nodes == kMaximumNodes + 1) {
                AddIssue(element.id, "tree_too_large",
                    "Layout tree exceeds 4096 elements.",
                    LayoutIssueSeverity::Error);
            }
            return containsVerticalScroll;
        }
        if (depth > kMaximumDepth) {
            AddIssue(element.id, "tree_too_deep",
                "Layout tree exceeds 64 levels.", LayoutIssueSeverity::Error);
            return containsVerticalScroll;
        }
        if (element.id.empty() || element.id.size() > 128) {
            AddIssue(element.id, "invalid_id",
                "Every layout element requires a 1-128 byte stable ID.",
                LayoutIssueSeverity::Error);
        } else if (!ids.insert(element.id).second) {
            AddIssue(element.id, "duplicate_id",
                "Layout element IDs must be unique.", LayoutIssueSeverity::Error);
        }
        if ((element.scrollAxis == ScrollAxis::Vertical &&
             element.direction != LayoutDirection::Column) ||
            (element.scrollAxis == ScrollAxis::Horizontal &&
             element.direction != LayoutDirection::Row)) {
            AddIssue(element.id, "scroll_axis_direction_mismatch",
                "Scroll axis must match the container layout direction.",
                LayoutIssueSeverity::Error);
        }
        if (element.wrap == WrapBehavior::Wrap &&
            (element.direction != LayoutDirection::Row ||
             element.scrollAxis != ScrollAxis::None)) {
            AddIssue(element.id, "invalid_wrap_container",
                "Wrapping requires a non-scroll row container.",
                LayoutIssueSeverity::Error);
        }
        if (element.layoutMode == LayoutMode::ResponsiveGrid) {
            if (!element.gridMinimumColumnWidth ||
                !std::isfinite(*element.gridMinimumColumnWidth) ||
                *element.gridMinimumColumnWidth < kMinimumGridColumnWidth ||
                *element.gridMinimumColumnWidth > kMaximumGridColumnWidth) {
                AddIssue(element.id, "invalid_grid_minimum_column_width",
                    "Responsive grid minimum column width must be finite and between 44 and 1600 DIPs.",
                    LayoutIssueSeverity::Error);
            }
            if (element.gridMaximumColumns &&
                (*element.gridMaximumColumns < 1 ||
                 *element.gridMaximumColumns > kMaximumGridColumns)) {
                AddIssue(element.id, "invalid_grid_maximum_columns",
                    "Responsive grid maximum columns must be between 1 and 32.",
                    LayoutIssueSeverity::Error);
            }
            if (element.scrollAxis != ScrollAxis::None ||
                element.wrap != WrapBehavior::NoWrap) {
                AddIssue(element.id, "invalid_grid_container",
                    "Responsive grid owns row wrapping and cannot also scroll or use flex wrapping.",
                    LayoutIssueSeverity::Error);
            }
        } else if (element.layoutMode != LayoutMode::Flex ||
                   element.gridMinimumColumnWidth ||
                   element.gridMaximumColumns) {
            AddIssue(element.id, "grid_property_not_allowed",
                "Grid column properties require responsive-grid layout mode.",
                LayoutIssueSeverity::Error);
        }
        for (const auto& child : element.children) {
            containsVerticalScroll = Preflight(
                child, depth + 1, ids, nodes) || containsVerticalScroll;
        }
        if (containsVerticalScroll)
            verticalScrollAncestors_.insert(&element);
        return containsVerticalScroll;
    }

    [[nodiscard]] static WidgetRailTaffyOptionalFloat Present(const float value) noexcept {
        return {1U, value};
    }

    [[nodiscard]] WidgetRailTaffyOptionalFloat ResolveOptionalForBridge(
        const std::optional<float> value,
        const std::string_view id,
        const std::string_view name,
        const float minimum,
        const float maximum) {
        if (!value) return {};
        return Present(ResolveNumber(*value, id, name, minimum, maximum, minimum));
    }

    [[nodiscard]] static WidgetRailTaffyEdges ToBridge(const Edges value) noexcept {
        return {value.top, value.right, value.bottom, value.left};
    }

    [[nodiscard]] std::uint32_t Flatten(
        const LayoutElement& element,
        const std::optional<LayoutDirection> parentDirection,
        const ScrollAxis parentScrollAxis,
        const float inheritedMaximumWidth) {
        const auto index = static_cast<std::uint32_t>(inputs_.size());
        elements_.push_back(&element);
        inputs_.emplace_back();
        const auto resolvedPadding = ResolveSpacing(
            element.padding, element.id, false);
        auto ownMaximumWidth = element.estimatesOffWindowScrollExtent &&
                parentScrollAxis == ScrollAxis::Horizontal
            ? kMaximumCoordinate
            : inheritedMaximumWidth;
        if (element.width)
            ownMaximumWidth = std::min(ownMaximumWidth, ClampFinite(
                *element.width, 0.0F, 0.0F, kMaximumCoordinate));
        if (element.maxWidth)
            ownMaximumWidth = std::min(ownMaximumWidth, ClampFinite(
                *element.maxWidth, 0.0F, 0.0F, kMaximumCoordinate));
        const auto contentMaximumWidth = std::max(
            0.0F, ownMaximumWidth - Horizontal(resolvedPadding));
        measurementMaximumWidths_.push_back(contentMaximumWidth);
        std::vector<std::uint32_t> directChildren;
        directChildren.reserve(element.children.size());
        for (const auto& child : element.children)
            directChildren.push_back(Flatten(
                child, element.direction, element.scrollAxis,
                contentMaximumWidth));

        auto& input = inputs_[index];
        input.childStart = static_cast<std::uint32_t>(childIndices_.size());
        input.childCount = static_cast<std::uint32_t>(directChildren.size());
        childIndices_.insert(
            childIndices_.end(), directChildren.begin(), directChildren.end());
        input.layoutMode = element.layoutMode == LayoutMode::ResponsiveGrid
            ? WRAIL_TAFFY_GRID : WRAIL_TAFFY_FLEX;
        input.direction = element.direction == LayoutDirection::Row
            ? WRAIL_TAFFY_ROW : WRAIL_TAFFY_COLUMN;
        input.wrap = element.wrap == WrapBehavior::Wrap
            ? WRAIL_TAFFY_WRAP : WRAIL_TAFFY_NO_WRAP;
        input.mainAlignment = static_cast<std::uint32_t>(element.mainAxisAlignment);
        input.crossAlignment = static_cast<std::uint32_t>(element.crossAxisAlignment);
        input.overflow = (element.overflow == OverflowBehavior::Clip ||
                          element.scrollAxis != ScrollAxis::None)
            ? WRAIL_TAFFY_OVERFLOW_CLIP : WRAIL_TAFFY_OVERFLOW_VISIBLE;
        input.width = ResolveOptionalForBridge(
            element.width, element.id, "width", 0.0F, kMaximumCoordinate);
        input.widthFraction = ResolveOptionalForBridge(
            element.widthFraction, element.id, "widthFraction", 0.0F, kMaximumCoordinate);
        input.height = ResolveOptionalForBridge(
            element.height, element.id, "height", 0.0F, kMaximumCoordinate);
        input.minWidth = ResolveOptionalForBridge(
            element.minWidth, element.id, "minWidth", 0.0F, kMaximumCoordinate);
        input.minHeight = ResolveOptionalForBridge(
            element.minHeight, element.id, "minHeight", 0.0F, kMaximumCoordinate);
        // The established native contract uses zero as the unauthored inline
        // flex/grid minimum. A vertical Scroll and its ordinary stack ancestors
        // also require a zero block-axis minimum so the nested viewport can
        // shrink within the admitted widget instead of expanding to its full
        // (possibly estimated virtual) content extent.
        if (!input.minWidth.present) input.minWidth = Present(0.0F);
        if (!input.minHeight.present &&
            verticalScrollAncestors_.contains(&element)) {
            input.minHeight = Present(0.0F);
        }
        input.maxWidth = ResolveOptionalForBridge(
            element.maxWidth, element.id, "maxWidth", 0.0F, kMaximumCoordinate);
        input.maxWidth = Present(ownMaximumWidth);
        input.maxHeight = ResolveOptionalForBridge(
            element.maxHeight, element.id, "maxHeight", 0.0F, kMaximumCoordinate);
        input.flexBasis = ResolveOptionalForBridge(
            element.flexBasis, element.id, "flexBasis", 0.0F, kMaximumCoordinate);
        input.aspectRatio = ResolveOptionalForBridge(
            element.aspectRatio, element.id, "aspectRatio", kMinimumRatio, kMaximumRatio);
        // Taffy applies aspect ratio from an authored main size, while the
        // existing contract allows flex-basis to supply that main size. Mirror
        // the typed basis into size only for this generic aspect-ratio case;
        // flexbox still owns final distribution.
        if (input.aspectRatio.present && input.flexBasis.present && parentDirection) {
            if (*parentDirection == LayoutDirection::Row && !input.width.present && !input.widthFraction.present)
                input.width = input.flexBasis;
            else if (*parentDirection == LayoutDirection::Column && !input.height.present)
                input.height = input.flexBasis;
        }
        input.padding = ToBridge(resolvedPadding);
        input.margin = ToBridge(ResolveSpacing(element.margin, element.id, true));
        const auto mainGap = ResolveNumber(
            element.gap, element.id, "gap", 0.0F, kMaximumSpacing, 0.0F);
        const auto crossGap = ResolveNumber(
            element.crossGap, element.id, "crossGap", 0.0F, kMaximumSpacing, 0.0F);
        if (element.layoutMode == LayoutMode::ResponsiveGrid ||
            element.direction == LayoutDirection::Row) {
            input.columnGap = mainGap;
            input.rowGap = crossGap;
        } else {
            input.columnGap = crossGap;
            input.rowGap = mainGap;
        }
        input.flexGrow = ResolveNumber(
            element.flexGrow, element.id, "flexGrow", 0.0F, kMaximumFlex, 0.0F);
        input.flexShrink = ResolveNumber(
            element.flexShrink, element.id, "flexShrink", 0.0F, kMaximumFlex, 1.0F);
        if (parentScrollAxis != ScrollAxis::None) {
            // Scroll content retains its authored/measured main extent; the
            // host scrolls overflow rather than asking flexbox to compress it
            // back into the viewport.
            input.flexShrink = 0.0F;
        }
        if (parentDirection == LayoutDirection::Column &&
            element.children.empty() && measureIntrinsic_ &&
            !element.height && !element.flexBasis) {
            // Vertical text/control content must scroll or clip rather than be
            // compressed below its measured paint/control height. Horizontal
            // text remains shrinkable so it can reflow at its final width.
            input.flexShrink = 0.0F;
        }
        input.gridMinimumColumnWidth = element.gridMinimumColumnWidth.value_or(0.0F);
        input.gridMaximumColumns = static_cast<std::uint32_t>(
            element.gridMaximumColumns.value_or(kMaximumGridColumns));
        input.gridStartIndex = element.gridStartIndex;
        input.stretchCrossAxis = element.stretchCrossAxis ? 1U : 0U;
        if (element.layoutMode == LayoutMode::ResponsiveGrid &&
            parentScrollAxis == ScrollAxis::Horizontal &&
            !element.width && !element.widthFraction) {
            input.flexGrow = std::max(1.0F, input.flexGrow);
            input.flexShrink = 0.0F;
        }
        return index;
    }

    [[nodiscard]] static WidgetRailTaffyMeasuredSize MeasureThunk(
        void* context,
        const std::uint32_t nodeIndex,
        const WidgetRailTaffyMeasureInput input) noexcept {
        return static_cast<LayoutEngine*>(context)->Measure(nodeIndex, input);
    }

    [[nodiscard]] WidgetRailTaffyMeasuredSize Measure(
        const std::uint32_t nodeIndex,
        const WidgetRailTaffyMeasureInput input) noexcept {
        if (nodeIndex >= elements_.size()) return {};
        const auto& element = *elements_[nodeIndex];
        if (!element.children.empty() || !measureIntrinsic_) return {};
        const auto maximumWidth = input.knownWidth.present
            ? input.knownWidth.value
            : (input.availableWidthMode == WRAIL_TAFFY_AVAILABLE_DEFINITE
                ? input.availableWidth : measurementMaximumWidths_[nodeIndex]);
        const auto maximumHeight =
            (input.knownHeight.present && element.height)
            ? input.knownHeight.value
            : (input.availableHeightMode == WRAIL_TAFFY_AVAILABLE_DEFINITE
                ? input.availableHeight : kMaximumCoordinate);
        try {
            ++result_.intrinsicMeasures;
            const auto measured = measureIntrinsic_(element, {
                std::max(0.0F, maximumWidth),
                std::max(0.0F, maximumHeight),
                result_.compactMode,
            });
            const auto width = input.knownWidth.present
                ? input.knownWidth.value
                : ClampFinite(measured.width, 0.0F, 0.0F, kMaximumCoordinate);
            const auto cleanMeasuredHeight = ClampFinite(
                measured.height, 0.0F, 0.0F, kMaximumCoordinate);
            const auto height = input.knownHeight.present
                ? (element.height
                    ? input.knownHeight.value
                    : std::max(input.knownHeight.value, cleanMeasuredHeight))
                : cleanMeasuredHeight;
            if ((!input.knownWidth.present && width != measured.width) ||
                (!input.knownHeight.present && height != measured.height)) {
                intrinsicMeasurementFailed_ = true;
                AddIssue(element.id, "invalid_intrinsic_size",
                    "Intrinsic measurement returned non-finite, negative, or excessive geometry.",
                    LayoutIssueSeverity::Warning);
            }
            return {width, height};
        } catch (...) {
            intrinsicMeasurementFailed_ = true;
            AddIssue(element.id, "intrinsic_measure_failed",
                "Intrinsic measurement failed and was replaced with an empty size.",
                LayoutIssueSeverity::Warning);
            return {};
        }
    }

    [[nodiscard]] Rect BuildRaw(
        const std::uint32_t index,
        const float parentX,
        const float parentY) {
        const auto& output = outputs_[index];
        auto& raw = raw_[index];
        raw.border = {
            parentX + output.x,
            parentY + output.y,
            std::max(0.0F, output.width),
            std::max(0.0F, output.height),
        };
        raw.content = {
            raw.border.x + output.padding.left,
            raw.border.y + output.padding.top,
            std::max(0.0F, raw.border.width - output.padding.left - output.padding.right),
            std::max(0.0F, raw.border.height - output.padding.top - output.padding.bottom),
        };

        const auto& input = inputs_[index];
        for (std::uint32_t offset = 0; offset < input.childCount; ++offset) {
            const auto childIndex = childIndices_[input.childStart + offset];
            const auto childBounds = BuildRaw(
                childIndex, raw.border.x, raw.border.y);
            raw.descendants = raw.hasDescendants
                ? Union(raw.descendants, childBounds) : childBounds;
            raw.hasDescendants = true;
        }

        const auto& element = *elements_[index];
        auto contentRight = raw.content.x + std::max(
            raw.content.width, output.contentWidth);
        auto contentBottom = raw.content.y + std::max(
            raw.content.height, output.contentHeight);
        if (raw.hasDescendants) {
            contentRight = std::max(
                contentRight, raw.descendants.x + raw.descendants.width);
            contentBottom = std::max(
                contentBottom, raw.descendants.y + raw.descendants.height);
        }
        raw.overflowX = contentRight > raw.content.x + raw.content.width + kEpsilon ||
            (raw.hasDescendants && raw.descendants.x < raw.content.x - kEpsilon);
        raw.overflowY = contentBottom > raw.content.y + raw.content.height + kEpsilon ||
            (raw.hasDescendants && raw.descendants.y < raw.content.y - kEpsilon);
        if (element.scrollAxis == ScrollAxis::Horizontal) {
            raw.maximumScrollOffset = std::max(
                0.0F, contentRight - (raw.content.x + raw.content.width));
        } else if (element.scrollAxis == ScrollAxis::Vertical) {
            raw.maximumScrollOffset = std::max(
                0.0F, contentBottom - (raw.content.y + raw.content.height));
        }
        raw.admittedScrollOffset = std::clamp(
            ResolveNumber(element.scrollOffset, element.id, "scrollOffset",
                0.0F, kMaximumCoordinate, 0.0F),
            0.0F, raw.maximumScrollOffset);
        // Taffy's content extent can stop at the final border box. Preserve
        // authored trailing margin in the overflow passed to scroll ancestors;
        // it may reserve room for focus decoration or visual enlargement.
        // This changes scroll reach only, never the element's layout box.
        const Rect outerBounds{raw.border.x, raw.border.y,
            raw.border.width + std::max(0.0F, input.margin.right),
            raw.border.height + std::max(0.0F, input.margin.bottom)};
        return raw.hasDescendants ? Union(outerBounds, raw.descendants) : outerBounds;
    }

    void Publish(
        const std::uint32_t index,
        const float translatedX,
        const float translatedY,
        const Rect ancestorClip) {
        const auto& element = *elements_[index];
        const auto& raw = raw_[index];
        LayoutBox box;
        box.unscrolledBorderBox = raw.border;
        box.unscrolledContentBox = raw.content;
        box.clipsDescendants = element.overflow == OverflowBehavior::Clip || element.scrollAxis != ScrollAxis::None;
        ProjectLayoutBox(box, translatedX, translatedY, ancestorClip, options_.pixelScale);
        box.overflowX = raw.overflowX;
        box.overflowY = raw.overflowY;
        box.scrollAxis = element.scrollAxis;
        box.scrollOffset = raw.admittedScrollOffset;
        box.maximumScrollOffset = raw.maximumScrollOffset;
        result_.boxes[element.id] = box;

        auto childClip = ancestorClip;
        if (element.overflow == OverflowBehavior::Clip ||
            element.scrollAxis != ScrollAxis::None) {
            childClip = Intersect(ancestorClip, box.contentBox);
        }
        auto childTranslatedX = translatedX;
        auto childTranslatedY = translatedY;
        if (element.scrollAxis == ScrollAxis::Horizontal)
            childTranslatedX += raw.admittedScrollOffset;
        else if (element.scrollAxis == ScrollAxis::Vertical)
            childTranslatedY += raw.admittedScrollOffset;

        const auto& input = inputs_[index];
        for (std::uint32_t offset = 0; offset < input.childCount; ++offset) {
            Publish(childIndices_[input.childStart + offset],
                childTranslatedX, childTranslatedY, childClip);
        }
    }

    [[nodiscard]] Edges ResolveSpacing(
        const BoxSpacing& spacing,
        const std::string_view id,
        const bool allowNegative) {
        if (spacing.count > 4) {
            AddIssue(id, "invalid_spacing",
                "Spacing count was outside 0-4 and was ignored.",
                LayoutIssueSeverity::Warning);
            return {};
        }
        const auto minimum = allowNegative ? -kMaximumSpacing : 0.0F;
        std::array<float, 4> clean{};
        for (std::size_t index = 0; index < spacing.count; ++index) {
            clean[index] = ResolveNumber(spacing.values[index], id,
                allowNegative ? "margin" : "padding", minimum,
                kMaximumSpacing, 0.0F);
        }
        switch (spacing.count) {
        case 0: return {};
        case 1: return {clean[0], clean[0], clean[0], clean[0]};
        case 2: return {clean[0], clean[1], clean[0], clean[1]};
        case 3: return {clean[0], clean[1], clean[2], clean[1]};
        default: return {clean[0], clean[1], clean[2], clean[3]};
        }
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
                std::string{name} +
                    " was non-finite or outside its safe range.",
                LayoutIssueSeverity::Warning);
        }
        return result;
    }


    void AddIssue(
        const std::string_view id,
        const std::string_view code,
        const std::string_view message,
        const LayoutIssueSeverity severity) {
        const auto key = std::string{id} + "\n" + std::string{code} +
            "\n" + std::string{message};
        if (!issueKeys_.insert(key).second) return;
        result_.issues.push_back({severity, std::string{id},
            std::string{code}, std::string{message}});
    }

    const IntrinsicMeasureCallback& measureIntrinsic_;
    LayoutOptions options_;
    bool intrinsicMeasurementFailed_{};
    LayoutResult result_;
    std::set<std::string, std::less<>> issueKeys_;
    std::set<const LayoutElement*> verticalScrollAncestors_;
    Rect viewport_;
    std::vector<const LayoutElement*> elements_;
    std::vector<float> measurementMaximumWidths_;
    std::vector<WidgetRailTaffyNodeInput> inputs_;
    std::vector<std::uint32_t> childIndices_;
    std::vector<WidgetRailTaffyNodeOutput> outputs_;
    std::vector<RawBox> raw_;
};

BoxSpacing BoxSpacing::One(const float all) noexcept {
    return {{all, 0.0F, 0.0F, 0.0F}, 1};
}

BoxSpacing BoxSpacing::Two(
    const float vertical, const float horizontal) noexcept {
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
    return std::none_of(issues.begin(), issues.end(),
        [](const LayoutIssue& issue) {
            return issue.severity == LayoutIssueSeverity::Error;
        });
}

const LayoutBox* LayoutResult::Find(
    const std::string_view stableId) const noexcept {
    const auto found = boxes.find(stableId);
    return found == boxes.end() ? nullptr : &found->second;
}

void ProjectLayoutBox(LayoutBox& box, const float translatedX, const float translatedY,
    const Rect ancestorClip, const float pixelScale) noexcept {
    const float scale = std::isfinite(pixelScale) && pixelScale > 0 ? pixelScale : 1.0F;
    const auto pixel = [scale](float value) { return std::round(value * scale); };
    const auto project = [&](Rect raw) {
        // Subtract on the physical-pixel grid before converting back to DIPs.
        // Subtracting two large, separately divided float coordinates loses
        // precision deep into a collection and changes otherwise identical
        // capture bounds/clips at fractional scale.
        const auto left = pixel(raw.x), top = pixel(raw.y);
        return Rect{(left - pixel(translatedX)) / scale, (top - pixel(translatedY)) / scale,
            std::max(0.0F, pixel(raw.x + raw.width) - left) / scale,
            std::max(0.0F, pixel(raw.y + raw.height) - top) / scale};
    };
    box.unroundedBorderBox = box.unscrolledBorderBox;
    box.unroundedBorderBox.x -= translatedX;
    box.unroundedBorderBox.y -= translatedY;
    box.borderBox = project(box.unscrolledBorderBox);
    box.contentBox = project(box.unscrolledContentBox);
    box.visibleBox = Intersect(box.borderBox, ancestorClip);
    box.clippedByAncestor = !Contains(ancestorClip, box.borderBox);
}

LayoutResult ComputeLayout(
    const LayoutElement& root,
    const Rect viewport,
    const IntrinsicMeasureCallback& measureIntrinsic,
    const LayoutOptions options) {
    if (root.layoutMode == LayoutMode::ModalOverlay) {
        if (root.children.size() != 2U) {
            LayoutResult invalid;
            invalid.issues.push_back({LayoutIssueSeverity::Error, root.id,
                "invalid_modal_layer", "A modal requires background and dialog children."});
            return invalid;
        }
        auto modalOptions = options;
        modalOptions.session = nullptr;
        auto result = ComputeLayout(root.children[0], viewport, measureIntrinsic, modalOptions);
        const auto* background = result.Find(root.children[0].id);
        if (!background) return result;
        // Modal content never participates in the underlying page's measurement.
        const auto bounds = background->borderBox;
        result.boxes.emplace(root.id, *background);
        if (options.intrinsicRootHeight) return result;
        auto panel = root.children[1];
        const float inset = std::min(16.0F, std::min(bounds.width, bounds.height) / 8.0F);
        const auto dimension = [](const float preferred, const std::optional<float> maximum,
                                  const float available, const float fallback) {
            const float desired = std::isfinite(preferred) ? preferred : fallback;
            const float limit = maximum && std::isfinite(*maximum) ? *maximum : available;
            return std::max(0.0F, std::min({desired, limit, available}));
        };
        const float availableWidth = bounds.width - inset * 2;
        const float width = dimension(panel.widthFraction
                ? availableWidth * *panel.widthFraction : panel.width.value_or(760.0F),
            panel.maxWidth, availableWidth, 760.0F);
        const float height = dimension(panel.height.value_or(640.0F), panel.maxHeight,
            bounds.height - inset * 2, 640.0F);
        panel.width = panel.minWidth = panel.maxWidth = width;
        panel.height = panel.minHeight = panel.maxHeight = height;
        panel.widthFraction.reset();
        panel.margin = {};
        panel.overflow = OverflowBehavior::Clip;
        modalOptions.fillAutoRoot = true;
        modalOptions.fillAutoRootWidth = true;
        auto foreground = ComputeLayout(panel,
            {bounds.x + (bounds.width - width) / 2, bounds.y + (bounds.height - height) / 2, width, height},
            measureIntrinsic, modalOptions);
        result.boxes.insert(foreground.boxes.begin(), foreground.boxes.end());
        result.issues.insert(result.issues.end(), foreground.issues.begin(), foreground.issues.end());
        result.intrinsicMeasures += foreground.intrinsicMeasures;
        return result;
    }
    return LayoutEngine{measureIntrinsic, options, viewport}.Run(root);
}

} // namespace widgetrail::declarative
