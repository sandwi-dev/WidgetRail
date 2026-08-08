#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace gba::declarative {

struct Size {
    float width{};
    float height{};
};

struct Rect {
    float x{};
    float y{};
    float width{};
    float height{};
};

// CSS order: one=all; two=vertical,horizontal; three=top,horizontal,bottom;
// four=top,right,bottom,left.
struct BoxSpacing {
    std::array<float, 4> values{};
    std::uint8_t count{};

    [[nodiscard]] static BoxSpacing One(float all) noexcept;
    [[nodiscard]] static BoxSpacing Two(float vertical, float horizontal) noexcept;
    [[nodiscard]] static BoxSpacing Three(float top, float horizontal, float bottom) noexcept;
    [[nodiscard]] static BoxSpacing Four(float top, float right, float bottom, float left) noexcept;
};

enum class LayoutDirection {
    Row,
    Column,
};

enum class LayoutMode {
    Flex,
    ResponsiveGrid,
};

enum class WrapBehavior {
    NoWrap,
    Wrap,
};

enum class OverflowBehavior {
    Visible,
    Clip,
};

enum class ScrollAxis {
    None,
    Horizontal,
    Vertical,
};

enum class MainAxisAlignment {
    Start,
    Center,
    End,
    SpaceBetween,
    SpaceAround,
};

enum class CrossAxisAlignment {
    Start,
    Center,
    End,
    Stretch,
};

struct LayoutElement {
    std::string id;
    LayoutMode layoutMode{LayoutMode::Flex};
    LayoutDirection direction{LayoutDirection::Column};
    std::vector<LayoutElement> children;

    // Typed responsive-grid semantics. Values are logical DIPs. Grid derives
    // a stable row-major column count from its actual content width; gap is
    // the column gap and crossGap is the row gap.
    std::optional<float> gridMinimumColumnWidth;
    std::optional<std::size_t> gridMaximumColumns;

    std::optional<float> width;
    std::optional<float> height;
    std::optional<float> minWidth;
    std::optional<float> minHeight;
    std::optional<float> maxWidth;
    std::optional<float> maxHeight;
    BoxSpacing padding{};
    BoxSpacing margin{};
    float gap{};
    // Cross-axis spacing between wrapped row lines. Ordinary single-line
    // containers ignore this value.
    float crossGap{};
    WrapBehavior wrap{WrapBehavior::NoWrap};
    float flexGrow{};
    float flexShrink{1.0F};
    std::optional<float> flexBasis;
    std::optional<float> aspectRatio;
    bool stretchCrossAxis{true};
    MainAxisAlignment mainAxisAlignment{MainAxisAlignment::Start};
    CrossAxisAlignment crossAxisAlignment{CrossAxisAlignment::Stretch};
    OverflowBehavior overflow{OverflowBehavior::Visible};
    // Scroll offsets are host-owned logical DIPs. The engine clamps them to
    // the measured content extent and clips descendants to contentBox.
    ScrollAxis scrollAxis{ScrollAxis::None};
    float scrollOffset{};
};

struct MeasureConstraints {
    float maximumWidth{};
    float maximumHeight{};
    bool compactMode{};
};

using IntrinsicMeasureCallback =
    std::function<Size(const LayoutElement&, const MeasureConstraints&)>;

struct LayoutBox {
    Rect borderBox;
    Rect contentBox;
    Rect visibleBox;
    bool overflowX{};
    bool overflowY{};
    bool clippedByAncestor{};
    ScrollAxis scrollAxis{ScrollAxis::None};
    float scrollOffset{};
    float maximumScrollOffset{};
};

enum class LayoutIssueSeverity {
    Warning,
    Error,
};

struct LayoutIssue {
    LayoutIssueSeverity severity{LayoutIssueSeverity::Warning};
    std::string elementId;
    std::string code;
    std::string message;
};

struct LayoutOptions {
    // Logical-to-physical pixel multiplier. Rect edges are rounded to this grid.
    float pixelScale{1.0F};
    float compactWidth{960.0F};
    float compactHeight{540.0F};
    // Supply the full output viewport when the root rect is a smaller panel.
    std::optional<Size> responsiveViewport;
    bool fillAutoRoot{true};
};

struct LayoutResult {
    std::map<std::string, LayoutBox, std::less<>> boxes;
    std::vector<LayoutIssue> issues;
    bool compactMode{};

    [[nodiscard]] bool valid() const noexcept;
    [[nodiscard]] const LayoutBox* Find(std::string_view stableId) const noexcept;
};

// The viewport is renderer-neutral logical geometry. No Direct2D/WinUI types
// cross this boundary. The callback measures text/images or other intrinsic
// leaf content under the supplied maximums.
[[nodiscard]] LayoutResult ComputeLayout(
    const LayoutElement& root,
    Rect viewport,
    const IntrinsicMeasureCallback& measureIntrinsic = {},
    LayoutOptions options = {});

} // namespace gba::declarative
