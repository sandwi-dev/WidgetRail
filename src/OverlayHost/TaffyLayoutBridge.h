#pragma once

#include <cstddef>
#include <cstdint>

// Stable, product-owned C ABI. The Rust implementation is pinned and built as
// a static library; no Rust types or allocator ownership cross this boundary.

#define WRAIL_TAFFY_ABI_VERSION 2U

enum WidgetRailTaffyResult : std::int32_t {
    WRAIL_TAFFY_OK = 0,
    WRAIL_TAFFY_INVALID_ARGUMENT = 1,
    WRAIL_TAFFY_INVALID_TREE = 2,
    WRAIL_TAFFY_LAYOUT_ERROR = 3,
    WRAIL_TAFFY_PANIC = 4,
};

enum WidgetRailTaffyLayoutMode : std::uint32_t {
    WRAIL_TAFFY_FLEX = 0,
    WRAIL_TAFFY_GRID = 1,
};

enum WidgetRailTaffyDirection : std::uint32_t {
    WRAIL_TAFFY_ROW = 0,
    WRAIL_TAFFY_COLUMN = 1,
};

enum WidgetRailTaffyWrap : std::uint32_t {
    WRAIL_TAFFY_NO_WRAP = 0,
    WRAIL_TAFFY_WRAP = 1,
};

enum WidgetRailTaffyMainAlignment : std::uint32_t {
    WRAIL_TAFFY_MAIN_START = 0,
    WRAIL_TAFFY_MAIN_CENTER = 1,
    WRAIL_TAFFY_MAIN_END = 2,
    WRAIL_TAFFY_MAIN_SPACE_BETWEEN = 3,
    WRAIL_TAFFY_MAIN_SPACE_AROUND = 4,
};

enum WidgetRailTaffyCrossAlignment : std::uint32_t {
    WRAIL_TAFFY_CROSS_START = 0,
    WRAIL_TAFFY_CROSS_CENTER = 1,
    WRAIL_TAFFY_CROSS_END = 2,
    WRAIL_TAFFY_CROSS_STRETCH = 3,
};

enum WidgetRailTaffyOverflow : std::uint32_t {
    WRAIL_TAFFY_OVERFLOW_VISIBLE = 0,
    WRAIL_TAFFY_OVERFLOW_CLIP = 1,
};

enum WidgetRailTaffyAvailableMode : std::uint32_t {
    WRAIL_TAFFY_AVAILABLE_DEFINITE = 0,
    WRAIL_TAFFY_AVAILABLE_MIN_CONTENT = 1,
    WRAIL_TAFFY_AVAILABLE_MAX_CONTENT = 2,
};

struct WidgetRailTaffyOptionalFloat {
    std::uint32_t present{};
    float value{};
};

struct WidgetRailTaffyEdges {
    float top{};
    float right{};
    float bottom{};
    float left{};
};

struct WidgetRailTaffyNodeInput {
    std::uint32_t childStart{};
    std::uint32_t childCount{};
    std::uint32_t layoutMode{};
    std::uint32_t direction{};
    std::uint32_t wrap{};
    std::uint32_t mainAlignment{};
    std::uint32_t crossAlignment{};
    std::uint32_t overflow{};
    WidgetRailTaffyOptionalFloat width;
    WidgetRailTaffyOptionalFloat height;
    WidgetRailTaffyOptionalFloat minWidth;
    WidgetRailTaffyOptionalFloat minHeight;
    WidgetRailTaffyOptionalFloat maxWidth;
    WidgetRailTaffyOptionalFloat maxHeight;
    WidgetRailTaffyOptionalFloat flexBasis;
    WidgetRailTaffyOptionalFloat aspectRatio;
    WidgetRailTaffyEdges padding;
    WidgetRailTaffyEdges margin;
    float columnGap{};
    float rowGap{};
    float flexGrow{};
    float flexShrink{1.0F};
    float gridMinimumColumnWidth{};
    std::uint32_t gridMaximumColumns{};
    std::uint32_t stretchCrossAxis{1};
};

struct WidgetRailTaffyMeasureInput {
    WidgetRailTaffyOptionalFloat knownWidth;
    WidgetRailTaffyOptionalFloat knownHeight;
    float availableWidth{};
    float availableHeight{};
    std::uint32_t availableWidthMode{};
    std::uint32_t availableHeightMode{};
};

struct WidgetRailTaffyMeasuredSize {
    float width{};
    float height{};
};

using WidgetRailTaffyMeasureCallback = WidgetRailTaffyMeasuredSize (*)(
    void* context,
    std::uint32_t nodeIndex,
    WidgetRailTaffyMeasureInput input);

struct WidgetRailTaffyNodeOutput {
    float x{};
    float y{};
    float width{};
    float height{};
    float contentWidth{};
    float contentHeight{};
    WidgetRailTaffyEdges padding;
};

static_assert(sizeof(WidgetRailTaffyOptionalFloat) == 8);
static_assert(sizeof(WidgetRailTaffyEdges) == 16);
static_assert(sizeof(WidgetRailTaffyNodeInput) == 156);
static_assert(sizeof(WidgetRailTaffyMeasureInput) == 32);
static_assert(sizeof(WidgetRailTaffyMeasuredSize) == 8);
static_assert(sizeof(WidgetRailTaffyNodeOutput) == 40);

extern "C" {
[[nodiscard]] std::uint32_t wrail_taffy_abi_version() noexcept;

[[nodiscard]] std::int32_t wrail_taffy_compute(
    const WidgetRailTaffyNodeInput* nodes,
    std::size_t nodeCount,
    const std::uint32_t* children,
    std::size_t childCount,
    std::uint32_t rootIndex,
    float availableWidth,
    float availableHeight,
    std::uint32_t availableHeightMode,
    WidgetRailTaffyMeasureCallback measure,
    void* measureContext,
    WidgetRailTaffyNodeOutput* outputs,
    std::size_t outputCount) noexcept;
}
