#pragma once

#include <cstddef>
#include <cstdint>

// Stable, product-owned C ABI. The Rust implementation is pinned and built as
// a static library; no Rust types or allocator ownership cross this boundary.

#define GBA_TAFFY_ABI_VERSION 2U

enum GbaTaffyResult : std::int32_t {
    GBA_TAFFY_OK = 0,
    GBA_TAFFY_INVALID_ARGUMENT = 1,
    GBA_TAFFY_INVALID_TREE = 2,
    GBA_TAFFY_LAYOUT_ERROR = 3,
    GBA_TAFFY_PANIC = 4,
};

enum GbaTaffyLayoutMode : std::uint32_t {
    GBA_TAFFY_FLEX = 0,
    GBA_TAFFY_GRID = 1,
};

enum GbaTaffyDirection : std::uint32_t {
    GBA_TAFFY_ROW = 0,
    GBA_TAFFY_COLUMN = 1,
};

enum GbaTaffyWrap : std::uint32_t {
    GBA_TAFFY_NO_WRAP = 0,
    GBA_TAFFY_WRAP = 1,
};

enum GbaTaffyMainAlignment : std::uint32_t {
    GBA_TAFFY_MAIN_START = 0,
    GBA_TAFFY_MAIN_CENTER = 1,
    GBA_TAFFY_MAIN_END = 2,
    GBA_TAFFY_MAIN_SPACE_BETWEEN = 3,
    GBA_TAFFY_MAIN_SPACE_AROUND = 4,
};

enum GbaTaffyCrossAlignment : std::uint32_t {
    GBA_TAFFY_CROSS_START = 0,
    GBA_TAFFY_CROSS_CENTER = 1,
    GBA_TAFFY_CROSS_END = 2,
    GBA_TAFFY_CROSS_STRETCH = 3,
};

enum GbaTaffyOverflow : std::uint32_t {
    GBA_TAFFY_OVERFLOW_VISIBLE = 0,
    GBA_TAFFY_OVERFLOW_CLIP = 1,
};

enum GbaTaffyAvailableMode : std::uint32_t {
    GBA_TAFFY_AVAILABLE_DEFINITE = 0,
    GBA_TAFFY_AVAILABLE_MIN_CONTENT = 1,
    GBA_TAFFY_AVAILABLE_MAX_CONTENT = 2,
};

struct GbaTaffyOptionalFloat {
    std::uint32_t present{};
    float value{};
};

struct GbaTaffyEdges {
    float top{};
    float right{};
    float bottom{};
    float left{};
};

struct GbaTaffyNodeInput {
    std::uint32_t childStart{};
    std::uint32_t childCount{};
    std::uint32_t layoutMode{};
    std::uint32_t direction{};
    std::uint32_t wrap{};
    std::uint32_t mainAlignment{};
    std::uint32_t crossAlignment{};
    std::uint32_t overflow{};
    GbaTaffyOptionalFloat width;
    GbaTaffyOptionalFloat height;
    GbaTaffyOptionalFloat minWidth;
    GbaTaffyOptionalFloat minHeight;
    GbaTaffyOptionalFloat maxWidth;
    GbaTaffyOptionalFloat maxHeight;
    GbaTaffyOptionalFloat flexBasis;
    GbaTaffyOptionalFloat aspectRatio;
    GbaTaffyEdges padding;
    GbaTaffyEdges margin;
    float columnGap{};
    float rowGap{};
    float flexGrow{};
    float flexShrink{1.0F};
    float gridMinimumColumnWidth{};
    std::uint32_t gridMaximumColumns{};
    std::uint32_t stretchCrossAxis{1};
};

struct GbaTaffyMeasureInput {
    GbaTaffyOptionalFloat knownWidth;
    GbaTaffyOptionalFloat knownHeight;
    float availableWidth{};
    float availableHeight{};
    std::uint32_t availableWidthMode{};
    std::uint32_t availableHeightMode{};
};

struct GbaTaffyMeasuredSize {
    float width{};
    float height{};
};

using GbaTaffyMeasureCallback = GbaTaffyMeasuredSize (*)(
    void* context,
    std::uint32_t nodeIndex,
    GbaTaffyMeasureInput input);

struct GbaTaffyNodeOutput {
    float x{};
    float y{};
    float width{};
    float height{};
    float contentWidth{};
    float contentHeight{};
    GbaTaffyEdges padding;
};

static_assert(sizeof(GbaTaffyOptionalFloat) == 8);
static_assert(sizeof(GbaTaffyEdges) == 16);
static_assert(sizeof(GbaTaffyNodeInput) == 156);
static_assert(sizeof(GbaTaffyMeasureInput) == 32);
static_assert(sizeof(GbaTaffyMeasuredSize) == 8);
static_assert(sizeof(GbaTaffyNodeOutput) == 40);

extern "C" {
[[nodiscard]] std::uint32_t gba_taffy_abi_version() noexcept;

[[nodiscard]] std::int32_t gba_taffy_compute(
    const GbaTaffyNodeInput* nodes,
    std::size_t nodeCount,
    const std::uint32_t* children,
    std::size_t childCount,
    std::uint32_t rootIndex,
    float availableWidth,
    float availableHeight,
    std::uint32_t availableHeightMode,
    GbaTaffyMeasureCallback measure,
    void* measureContext,
    GbaTaffyNodeOutput* outputs,
    std::size_t outputCount) noexcept;
}
