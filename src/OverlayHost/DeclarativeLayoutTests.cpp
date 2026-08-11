#include "DeclarativeLayout.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <string_view>

namespace {

using gba::declarative::BoxSpacing;
using gba::declarative::ComputeLayout;
using gba::declarative::CrossAxisAlignment;
using gba::declarative::IntrinsicMeasureCallback;
using gba::declarative::LayoutDirection;
using gba::declarative::LayoutElement;
using gba::declarative::LayoutIssueSeverity;
using gba::declarative::LayoutMode;
using gba::declarative::MainAxisAlignment;
using gba::declarative::OverflowBehavior;
using gba::declarative::Rect;
using gba::declarative::Size;
using gba::declarative::ScrollAxis;
using gba::declarative::WrapBehavior;

int checks = 0;

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Near(const float actual, const float expected, const std::string_view message) {
    Check(std::abs(actual - expected) <= 0.01F, message);
}

LayoutElement Element(const std::string_view id, const LayoutDirection direction = LayoutDirection::Column) {
    LayoutElement result;
    result.id = id;
    result.direction = direction;
    return result;
}

void MediaLayout1080p() {
    auto header = Element("header");
    header.height = 48.0F;
    header.flexShrink = 0.0F;

    auto artwork = Element("artwork");
    artwork.flexBasis = 184.0F;
    artwork.flexShrink = 0.0F;
    artwork.aspectRatio = 1.0F;

    auto details = Element("details");
    details.flexBasis = 0.0F;
    details.flexGrow = 1.0F;

    auto body = Element("body", LayoutDirection::Row);
    body.flexBasis = 0.0F;
    body.flexGrow = 1.0F;
    body.gap = 20.0F;
    body.children = {artwork, details};

    auto prompts = Element("prompts");
    prompts.height = 44.0F;
    prompts.flexShrink = 0.0F;

    auto root = Element("media");
    root.padding = BoxSpacing::One(24.0F);
    root.gap = 16.0F;
    root.children = {header, body, prompts};

    const auto result = ComputeLayout(root, {0.0F, 0.0F, 880.0F, 520.0F});
    Check(result.valid(), "1080p media layout is valid");
    Near(result.Find("media")->contentBox.width, 832.0F, "media padding resolves");
    Near(result.Find("header")->borderBox.height, 48.0F, "fixed header height");
    Near(result.Find("body")->borderBox.height, 348.0F, "body receives remaining column space");
    Near(result.Find("artwork")->borderBox.width, 184.0F, "art basis remains fixed");
    Near(result.Find("artwork")->borderBox.height, 184.0F, "art aspect ratio is square");
    Near(result.Find("details")->borderBox.x, 228.0F, "details follows art and gap");
    Near(result.Find("details")->borderBox.width, 628.0F, "details grows into remaining width");
    Near(result.Find("prompts")->borderBox.y, 452.0F, "prompt strip follows body gap");
}

void FlexShrinkAndMinimums() {
    auto first = Element("first");
    first.flexBasis = 200.0F;
    first.flexShrink = 1.0F;
    first.minWidth = 120.0F;
    auto second = first;
    second.id = "second";

    auto root = Element("row", LayoutDirection::Row);
    root.gap = 16.0F;
    root.children = {first, second};
    const auto result = ComputeLayout(root, {0.0F, 0.0F, 300.0F, 100.0F});
    Check(result.valid(), "shrink layout valid");
    Near(result.Find("first")->borderBox.width, 142.0F, "first shrinks proportionally");
    Near(result.Find("second")->borderBox.width, 142.0F, "second shrinks proportionally");
    Near(result.Find("second")->borderBox.x, 158.0F, "gap remains fixed while items shrink");
}

void FlexGrowHonorsMaximum() {
    auto capped = Element("capped");
    capped.flexBasis = 100.0F;
    capped.flexGrow = 1.0F;
    capped.maxWidth = 120.0F;
    auto flexible = Element("flexible");
    flexible.flexBasis = 100.0F;
    flexible.flexGrow = 1.0F;

    auto root = Element("row", LayoutDirection::Row);
    root.children = {capped, flexible};
    const auto result = ComputeLayout(root, {0.0F, 0.0F, 500.0F, 80.0F});
    Near(result.Find("capped")->borderBox.width, 120.0F, "grow respects max width");
    Near(result.Find("flexible")->borderBox.width, 380.0F, "remaining grow space is redistributed");
}

void FlexedRowRemeasuresWrappedCrossSize() {
    auto text = Element("header-text");
    text.flexGrow = 1.0F;
    text.flexShrink = 1.0F;
    text.minWidth = 0.0F;

    auto trailing = Element("header-action");
    trailing.width = 96.0F;
    trailing.height = 44.0F;
    trailing.flexShrink = 0.0F;

    auto header = Element("header", LayoutDirection::Row);
    header.gap = 10.0F;
    header.crossAxisAlignment = CrossAxisAlignment::Center;
    header.overflow = OverflowBehavior::Clip;
    header.children = {text, trailing};

    auto root = Element("root");
    root.children = {header};
    const auto measure = [](const LayoutElement& element,
                            const gba::declarative::MeasureConstraints& constraints) {
        if (element.id == "header-text") {
            return Size{360.0F, constraints.maximumWidth < 300.0F ? 76.0F : 59.0F};
        }
        return Size{};
    };
    const auto result = ComputeLayout(
        root, {0.0F, 0.0F, 384.0F, 200.0F}, measure);
    Check(result.valid(), "final-width row remeasure is valid");
    Near(result.Find("header")->borderBox.height, 76.0F,
        "trailing action cannot retain the obsolete one-line header height");
    Near(result.Find("header-text")->borderBox.width, 278.0F,
        "header text receives the final width after the trailing action and gap");
    Near(result.Find("header-text")->borderBox.height, 76.0F,
        "header text receives its complete wrapped cross size");
    Near(result.Find("header-text")->visibleBox.height, 76.0F,
        "header clipping uses the final wrapped cross size");
}

void NestedPaddingAndMargins() {
    auto leaf = Element("leaf");
    leaf.width = 40.0F;
    leaf.height = 20.0F;
    leaf.margin = BoxSpacing::Four(1.0F, 2.0F, 3.0F, 4.0F);

    auto stack = Element("stack");
    stack.padding = BoxSpacing::Three(10.0F, 20.0F, 30.0F);
    stack.children = {leaf};
    const auto result = ComputeLayout(stack, {0.0F, 0.0F, 200.0F, 120.0F});
    const auto* rootBox = result.Find("stack");
    const auto* leafBox = result.Find("leaf");
    Near(rootBox->contentBox.x, 20.0F, "three-value padding left");
    Near(rootBox->contentBox.y, 10.0F, "three-value padding top");
    Near(rootBox->contentBox.height, 80.0F, "three-value padding bottom");
    Near(leafBox->borderBox.x, 24.0F, "four-value margin left");
    Near(leafBox->borderBox.y, 11.0F, "four-value margin top");
}

void OverflowClipping() {
    auto child = Element("too-tall");
    child.flexBasis = 150.0F;
    child.minHeight = 150.0F;
    child.flexShrink = 0.0F;

    auto root = Element("clip");
    root.overflow = OverflowBehavior::Clip;
    root.children = {child};
    const auto result = ComputeLayout(root, {0.0F, 0.0F, 100.0F, 100.0F});
    Check(result.Find("clip")->overflowY, "overflow is reported on container");
    Check(result.Find("too-tall")->clippedByAncestor, "child reports ancestor clipping");
    Near(result.Find("too-tall")->visibleBox.height, 100.0F, "visible box is intersected with clip");
}

void ScrollOffsetsAreBoundedAndClipped() {
    auto scroll = Element("sessions");
    scroll.scrollAxis = ScrollAxis::Vertical;
    scroll.gap = 4.0F;
    for (int index = 0; index < 8; ++index) {
        auto row = Element("session-" + std::to_string(index));
        row.height = 44.0F;
        row.minHeight = 44.0F;
        row.flexShrink = 0.0F;
        scroll.children.push_back(std::move(row));
    }

    const auto initial = ComputeLayout(scroll, {0, 0, 240, 100});
    Check(initial.valid(), "vertical scroll layout is valid");
    Near(initial.Find("sessions")->maximumScrollOffset, 280.0F,
         "content extent determines bounded maximum offset");
    Near(initial.Find("session-0")->borderBox.y, 0.0F,
         "initial scroll position starts at the leading edge");
    Check(initial.Find("session-3")->visibleBox.height == 0.0F,
          "offscreen descendants are fully clipped");

    scroll.scrollOffset = 999999.0F;
    const auto trailing = ComputeLayout(scroll, {0, 0, 240, 100});
    Near(trailing.Find("sessions")->scrollOffset, 280.0F,
         "oversized offset clamps to measured content extent");
    Near(trailing.Find("session-7")->borderBox.y, 56.0F,
         "clamped trailing offset reveals final row");
    Near(trailing.Find("session-7")->visibleBox.height, 44.0F,
         "final row remains wholly visible");

    scroll.scrollOffset = std::numeric_limits<float>::quiet_NaN();
    const auto invalid = ComputeLayout(scroll, {0, 0, 240, 100});
    Near(invalid.Find("sessions")->scrollOffset, 0.0F,
         "non-finite offset fails closed to the leading edge");
    Check(std::any_of(invalid.issues.begin(), invalid.issues.end(), [](const auto& issue) {
        return issue.code == "value_clamped";
    }), "invalid offset emits a bounded diagnostic");

    auto horizontal = Element("horizontal", LayoutDirection::Row);
    horizontal.scrollAxis = ScrollAxis::Horizontal;
    horizontal.scrollOffset = 999.0F;
    for (int index = 0; index < 4; ++index) {
        auto item = Element("horizontal-" + std::to_string(index));
        item.width = 44.0F;
        item.minWidth = 44.0F;
        item.flexShrink = 0.0F;
        horizontal.children.push_back(std::move(item));
    }
    const auto horizontalResult = ComputeLayout(horizontal, {0, 0, 100, 60});
    Near(horizontalResult.Find("horizontal")->scrollOffset, 76.0F,
         "horizontal offset uses the same bounded contract");
    Near(horizontalResult.Find("horizontal-3")->borderBox.x, 56.0F,
         "horizontal trailing item is fully revealed at the clamped edge");

    scroll.scrollOffset = 0.0F;
    scroll.direction = LayoutDirection::Row;
    const auto mismatched = ComputeLayout(scroll, {0, 0, 240, 100});
    Check(!mismatched.valid() && mismatched.boxes.empty(),
          "axis and direction mismatch fails closed without partial geometry");
}

void ResponsiveViewports() {
    auto rail = Element("rail", LayoutDirection::Row);
    rail.padding = BoxSpacing::Two(8.0F, 16.0F);
    auto card = Element("card");
    card.flexBasis = 408.0F;
    card.flexGrow = 1.0F;
    card.maxWidth = 544.0F;
    card.aspectRatio = 16.0F / 9.0F;
    auto info = Element("info");
    info.flexBasis = 248.0F;
    info.flexGrow = 1.0F;
    rail.gap = 16.0F;
    rail.children = {card, info};

    gba::declarative::LayoutOptions at720Options;
    at720Options.responsiveViewport = Size{1280.0F, 720.0F};
    gba::declarative::LayoutOptions at1080Options;
    at1080Options.responsiveViewport = Size{1920.0F, 1080.0F};
    gba::declarative::LayoutOptions ultrawideOptions;
    ultrawideOptions.responsiveViewport = Size{3440.0F, 1440.0F};
    const auto at720 = ComputeLayout(rail, {0.0F, 0.0F, 1280.0F, 230.0F}, {}, at720Options);
    const auto at1080 = ComputeLayout(rail, {0.0F, 0.0F, 1920.0F, 230.0F}, {}, at1080Options);
    const auto ultrawideStage = ComputeLayout(rail, {440.0F, 0.0F, 2560.0F, 307.0F}, {}, ultrawideOptions);
    Check(!at720.compactMode && !at1080.compactMode && !ultrawideStage.compactMode,
        "720p, 1080p, and centered ultrawide stage are standard mode");
    Check(at720.Find("card")->borderBox.width <= 544.0F, "720p respects card max");
    Near(at1080.Find("card")->borderBox.width, 544.0F, "1080p grow clamps card max");
    Near(ultrawideStage.Find("rail")->borderBox.x, 440.0F, "ultrawide stage origin is preserved");
    Check(ultrawideStage.Find("info")->borderBox.width > at1080.Find("info")->borderBox.width,
        "ultrawide stage allocates additional flexible detail width");
}

void ResponsiveRowsWrapAtStableItemBoundaries() {
    auto first = Element("wrap-first");
    first.width = 100.0F;
    first.height = 40.0F;
    first.flexShrink = 0.0F;
    auto second = first;
    second.id = "wrap-second";
    auto third = first;
    third.id = "wrap-third";

    auto row = Element("wrap-row", LayoutDirection::Row);
    row.wrap = WrapBehavior::Wrap;
    row.gap = 10.0F;
    row.crossGap = 6.0F;
    row.crossAxisAlignment = CrossAxisAlignment::Start;
    row.children = {first, second, third};

    gba::declarative::LayoutOptions measuredOptions;
    measuredOptions.fillAutoRoot = false;
    const auto narrow = ComputeLayout(
        row, {0.0F, 0.0F, 250.0F, 200.0F}, {}, measuredOptions);
    Check(narrow.valid(), "wrapped row remains valid at a narrow width");
    Near(narrow.Find("wrap-row")->borderBox.width, 210.0F,
         "auto-width wrapped row reports its widest line");
    Near(narrow.Find("wrap-row")->borderBox.height, 86.0F,
         "auto-height includes every row line and the cross gap");
    Near(narrow.Find("wrap-first")->borderBox.x, 0.0F,
         "first item starts the first line");
    Near(narrow.Find("wrap-second")->borderBox.x, 110.0F,
         "second item retains the main-axis gap");
    Near(narrow.Find("wrap-third")->borderBox.x, 0.0F,
         "third item starts a new line");
    Near(narrow.Find("wrap-third")->borderBox.y, 46.0F,
         "wrapped line uses the bounded cross-axis gap");

    const auto wide = ComputeLayout(
        row, {0.0F, 0.0F, 340.0F, 200.0F}, {}, measuredOptions);
    Near(wide.Find("wrap-row")->borderBox.width, 320.0F,
         "wider surface recomputes the natural single-line width");
    Near(wide.Find("wrap-row")->borderBox.height, 40.0F,
         "wider surface returns to one intrinsic line");
    Near(wide.Find("wrap-third")->borderBox.x, 220.0F,
         "third item rejoins the first line without widget branching");
    Near(wide.Find("wrap-third")->borderBox.y, 0.0F,
         "single-line layout removes the cross gap");

    auto invalid = Element("invalid-wrap");
    invalid.wrap = WrapBehavior::Wrap;
    invalid.children = {first};
    const auto rejected = ComputeLayout(invalid, {0, 0, 200, 100});
    Check(!rejected.valid() && rejected.boxes.empty(),
          "column wrapping fails closed before publishing partial geometry");
}

void ResponsiveGridUsesAvailableDipWidth() {
    auto grid = Element("responsive-grid");
    grid.layoutMode = LayoutMode::ResponsiveGrid;
    grid.gridMinimumColumnWidth = 100.0F;
    grid.gridMaximumColumns = 3;
    grid.gap = 10.0F;
    grid.crossGap = 8.0F;
    for (int index = 0; index < 5; ++index) {
        auto child = Element("grid-child-" + std::to_string(index));
        child.height = index == 1 ? 60.0F : (index == 4 ? 70.0F : 40.0F);
        grid.children.push_back(std::move(child));
    }

    const auto tiny = ComputeLayout(grid, {0, 0, 20, 500});
    Check(tiny.valid(), "grid gracefully falls back to one column below its minimum");
    Near(tiny.Find("grid-child-0")->borderBox.width, 20.0F,
         "tiny fallback uses the safe available width");
    Near(tiny.Find("grid-child-1")->borderBox.y, 48.0F,
         "single-column fallback preserves row order and row gap");

    // Layout consumes the snapped logical viewport. Stay more than half a DIP
    // below the boundary so default 1x pixel snapping cannot round into it.
    const auto belowBoundary = ComputeLayout(grid, {0, 0, 209.4F, 500});
    Near(belowBoundary.Find("grid-child-1")->borderBox.y, 48.0F,
         "width below the exact boundary remains one column");

    const auto exactBoundary = ComputeLayout(grid, {0, 0, 210, 500});
    Near(exactBoundary.Find("grid-child-0")->borderBox.width, 100.0F,
         "exact two-column boundary preserves the authored minimum");
    Near(exactBoundary.Find("grid-child-1")->borderBox.x, 110.0F,
         "column gap separates the second exact-boundary column");
    Near(exactBoundary.Find("grid-child-2")->borderBox.x, 0.0F,
         "third child wraps in stable row-major order");
    Near(exactBoundary.Find("grid-child-2")->borderBox.y, 68.0F,
         "next row follows the tallest intrinsic row and row gap");

    const auto wide = ComputeLayout(grid, {0, 0, 500, 500});
    Near(wide.Find("grid-child-0")->borderBox.width, 160.0F,
         "wide grid distributes remaining width equally across capped columns");
    Near(wide.Find("grid-child-1")->borderBox.x, 170.0F,
         "wide grid retains its authored column gap");
    Near(wide.Find("grid-child-2")->borderBox.x, 340.0F,
         "maximum-columns cap remains deterministic");
    Near(wide.Find("grid-child-3")->borderBox.y, 68.0F,
         "fourth child begins the next stable row");
}

void ResponsiveGridMeasuresMixedIntrinsicRows() {
    auto grid = Element("intrinsic-grid");
    grid.layoutMode = LayoutMode::ResponsiveGrid;
    grid.gridMinimumColumnWidth = 150.0F;
    grid.gridMaximumColumns = 4;
    grid.gap = 20.0F;
    grid.crossGap = 12.0F;
    grid.children = {
        Element("intrinsic-short"),
        Element("intrinsic-tall"),
        Element("intrinsic-wrapped"),
    };
    const IntrinsicMeasureCallback measure = [](
        const LayoutElement& element,
        const gba::declarative::MeasureConstraints& constraints) {
        if (element.id == "intrinsic-short") return Size{constraints.maximumWidth, 20.0F};
        if (element.id == "intrinsic-tall") return Size{constraints.maximumWidth, 50.0F};
        if (element.id == "intrinsic-wrapped")
            return Size{constraints.maximumWidth, constraints.maximumWidth <= 150.01F ? 60.0F : 30.0F};
        return Size{};
    };
    gba::declarative::LayoutOptions options;
    options.fillAutoRoot = false;
    const auto result = ComputeLayout(grid, {0, 0, 320, 500}, measure, options);
    Check(result.valid(), "mixed intrinsic grid remains valid");
    Near(result.Find("intrinsic-grid")->borderBox.height, 122.0F,
         "grid intrinsic height includes tallest rows and row gap");
    Near(result.Find("intrinsic-short")->borderBox.height, 50.0F,
         "default stretch gives peers the tallest row height");
    Near(result.Find("intrinsic-tall")->borderBox.height, 50.0F,
         "tall intrinsic child establishes first-row height");
    Near(result.Find("intrinsic-wrapped")->borderBox.y, 62.0F,
         "wrapped child starts after the complete first row");
    Near(result.Find("intrinsic-wrapped")->borderBox.height, 60.0F,
         "child is remeasured against its actual grid column width");
}

void ResponsiveGridHandlesEmptyLargeAndScaledSurfaces() {
    auto empty = Element("empty-grid");
    empty.layoutMode = LayoutMode::ResponsiveGrid;
    empty.gridMinimumColumnWidth = 120.0F;
    const auto emptyResult = ComputeLayout(empty, {0, 0, 300, 100});
    Check(emptyResult.valid() && emptyResult.boxes.size() == 1,
          "empty responsive grid is a valid bounded container");

    auto large = Element("large-grid");
    large.layoutMode = LayoutMode::ResponsiveGrid;
    large.gridMinimumColumnWidth = 80.0F;
    large.gridMaximumColumns = 4;
    large.gap = 4.0F;
    large.crossGap = 3.0F;
    for (int index = 0; index < 100; ++index) {
        auto child = Element("large-grid-" + std::to_string(index));
        child.height = 10.0F;
        large.children.push_back(std::move(child));
    }
    const auto largeResult = ComputeLayout(large, {0, 0, 500, 500});
    Check(largeResult.valid() && largeResult.boxes.size() == 101,
          "large child set retains every stable layout box");
    Near(largeResult.Find("large-grid-99")->borderBox.x, 378.0F,
         "last large-set child retains row-major column order");
    Near(largeResult.Find("large-grid-99")->borderBox.y, 312.0F,
         "last large-set child reaches the expected final row");

    auto scaled = Element("scaled-grid");
    scaled.layoutMode = LayoutMode::ResponsiveGrid;
    scaled.gridMinimumColumnWidth = 100.0F;
    scaled.gap = 10.0F;
    for (int index = 0; index < 3; ++index) {
        auto child = Element("scaled-grid-" + std::to_string(index));
        child.height = 44.0F;
        scaled.children.push_back(std::move(child));
    }
    gba::declarative::LayoutOptions oneX;
    oneX.pixelScale = 1.0F;
    gba::declarative::LayoutOptions twoX;
    twoX.pixelScale = 2.0F;
    const auto dipOne = ComputeLayout(scaled, {0, 0, 320, 100}, {}, oneX);
    const auto dipTwo = ComputeLayout(scaled, {0, 0, 320, 100}, {}, twoX);
    Near(dipOne.Find("scaled-grid-1")->borderBox.x,
         dipTwo.Find("scaled-grid-1")->borderBox.x,
         "grid column computation remains in DIPs across pixel scales");
    Near(dipOne.Find("scaled-grid-1")->borderBox.width,
         dipTwo.Find("scaled-grid-1")->borderBox.width,
         "DPI changes snapping, not responsive column semantics");
}

void ResponsiveGridInsideHorizontalScrollUsesViewportWidth() {
    auto grid = Element("scrolled-responsive-grid");
    grid.layoutMode = LayoutMode::ResponsiveGrid;
    grid.gridMinimumColumnWidth = 100.0F;
    grid.gridMaximumColumns = 3;
    grid.gap = 10.0F;
    for (int index = 0; index < 4; ++index) {
        auto child = Element("scrolled-grid-child-" + std::to_string(index));
        child.height = 44.0F;
        grid.children.push_back(std::move(child));
    }

    auto scroll = Element("horizontal-grid-scroll", LayoutDirection::Row);
    scroll.scrollAxis = ScrollAxis::Horizontal;
    scroll.children = {grid};
    const auto result = ComputeLayout(scroll, {0, 0, 320, 200});
    Check(result.valid(), "responsive grid remains valid inside horizontal scroll");
    Near(result.Find("scrolled-responsive-grid")->borderBox.width, 320.0F,
         "horizontal scroll constrains responsive grid to its viewport width");
    Near(result.Find("scrolled-grid-child-0")->borderBox.width, 100.0F,
         "viewport-constrained grid keeps its authored minimum at three columns");
    Near(result.Find("scrolled-grid-child-3")->borderBox.y, 44.0F,
         "fourth grid child wraps instead of expanding the horizontal extent");
    Check(result.Find("horizontal-grid-scroll")->maximumScrollOffset <= 0.01F,
          "responsive grid does not manufacture a bogus horizontal scroll extent");
}

void InvalidResponsiveGridFailsClosed() {
    auto missing = Element("missing-grid");
    missing.layoutMode = LayoutMode::ResponsiveGrid;
    const auto missingResult = ComputeLayout(missing, {0, 0, 300, 100});
    Check(!missingResult.valid() && missingResult.boxes.empty(),
          "grid without minimum column width fails closed");

    auto invalidMaximum = Element("invalid-grid-maximum");
    invalidMaximum.layoutMode = LayoutMode::ResponsiveGrid;
    invalidMaximum.gridMinimumColumnWidth = 100.0F;
    invalidMaximum.gridMaximumColumns = 33;
    const auto maximumResult = ComputeLayout(invalidMaximum, {0, 0, 300, 100});
    Check(!maximumResult.valid() && maximumResult.boxes.empty(),
          "unbounded grid column cap fails closed");

    auto flexWithGridProperties = Element("flex-grid-properties");
    flexWithGridProperties.gridMinimumColumnWidth = 100.0F;
    const auto flexResult = ComputeLayout(flexWithGridProperties, {0, 0, 300, 100});
    Check(!flexResult.valid() && flexResult.boxes.empty(),
          "grid semantics cannot leak onto flex containers");

    auto scrolling = Element("scrolling-grid");
    scrolling.layoutMode = LayoutMode::ResponsiveGrid;
    scrolling.gridMinimumColumnWidth = 100.0F;
    scrolling.scrollAxis = ScrollAxis::Vertical;
    const auto scrollingResult = ComputeLayout(scrolling, {0, 0, 300, 100});
    Check(!scrollingResult.valid() && scrollingResult.boxes.empty(),
          "grid cannot ambiguously own scrolling and responsive rows");
}

void IntrinsicAndCompactMode() {
    auto text = Element("text");
    text.flexGrow = 1.0F;
    auto root = Element("compact", LayoutDirection::Row);
    root.children = {text};
    bool sawCompact = false;
    const IntrinsicMeasureCallback measure =
        [&sawCompact](const LayoutElement& element, const gba::declarative::MeasureConstraints& constraints) {
            if (element.id == "text") {
                sawCompact |= constraints.compactMode;
                return Size{std::min(300.0F, constraints.maximumWidth), constraints.compactMode ? 48.0F : 24.0F};
            }
            return Size{};
        };
    const auto result = ComputeLayout(root, {0.0F, 0.0F, 800.0F, 500.0F}, measure);
    Check(result.compactMode && sawCompact, "compact mode reaches intrinsic callback");
    Near(result.Find("text")->borderBox.height, 500.0F, "default cross-axis stretch remains deterministic");
}

void WrappedIntrinsicLeavesDoNotCollapseOrOverlap() {
    auto title = Element("state-title");
    title.maxWidth = 160.0F;
    auto help = Element("state-help");
    help.maxWidth = 190.0F;
    auto action = Element("state-action");
    // A widget author commonly supplies 44 DIPs as the controller minimum.
    // That lower bound must not replace a taller intrinsic wrapped label.
    action.minHeight = 44.0F;

    auto state = Element("state-card");
    state.mainAxisAlignment = MainAxisAlignment::Center;
    state.gap = 8.0F;
    state.children = {title, help, action};

    const IntrinsicMeasureCallback measure = [](
        const LayoutElement& element,
        const gba::declarative::MeasureConstraints& constraints) {
        if (element.id == "state-title")
            return Size{constraints.maximumWidth,
                constraints.maximumWidth <= 160.01F ? 40.0F : 20.0F};
        if (element.id == "state-help")
            return Size{constraints.maximumWidth,
                constraints.maximumWidth <= 190.01F ? 60.0F : 20.0F};
        if (element.id == "state-action")
            return Size{std::min(150.0F, constraints.maximumWidth), 64.0F};
        return Size{};
    };

    // This is intentionally shorter than the wrapped content. Intrinsic leaf
    // boxes must retain their paint/control height and flow in order; clipping
    // belongs to the viewport/card, not to each individual line box.
    const auto result = ComputeLayout(
        state, {0.0F, 0.0F, 220.0F, 140.0F}, measure);
    Check(result.valid(), "constrained centered state remains valid");
    Near(result.Find("state-title")->borderBox.height, 40.0F,
         "wrapped title retains intrinsic line height");
    Near(result.Find("state-help")->borderBox.height, 60.0F,
         "wrapped help retains intrinsic paragraph height");
    Near(result.Find("state-action")->borderBox.height, 64.0F,
         "authored controller minimum does not crush taller intrinsic action");
    Check(result.Find("state-help")->borderBox.y >=
              result.Find("state-title")->borderBox.y +
                  result.Find("state-title")->borderBox.height + 7.99F,
          "wrapped help follows title without overlap");
    Check(result.Find("state-action")->borderBox.y >=
              result.Find("state-help")->borderBox.y +
                  result.Find("state-help")->borderBox.height + 7.99F,
          "action follows wrapped help without overlap");
    Check(result.Find("state-card")->overflowY,
          "constrained state reports honest overflow instead of crushing content");
}

void ScrollExtentIncludesWrappedIntrinsicParagraphs() {
    auto first = Element("paragraph-one");
    auto second = Element("paragraph-two");
    auto action = Element("scroll-action");

    auto scroll = Element("permission-scroll");
    scroll.scrollAxis = ScrollAxis::Vertical;
    scroll.gap = 6.0F;
    scroll.children = {first, second, action};

    const IntrinsicMeasureCallback measure = [](
        const LayoutElement& element,
        const gba::declarative::MeasureConstraints& constraints) {
        if (element.id == "paragraph-one")
            return Size{constraints.maximumWidth, 72.0F};
        if (element.id == "paragraph-two")
            return Size{constraints.maximumWidth, 96.0F};
        if (element.id == "scroll-action")
            return Size{std::min(150.0F, constraints.maximumWidth), 44.0F};
        return Size{};
    };

    const auto leading = ComputeLayout(
        scroll, {0.0F, 0.0F, 180.0F, 110.0F}, measure);
    Check(leading.valid(), "wrapped permission scroll remains valid");
    Near(leading.Find("permission-scroll")->maximumScrollOffset, 114.0F,
         "scroll extent includes every wrapped intrinsic line and gap");
    Check(leading.Find("scroll-action")->borderBox.y >= 180.0F,
          "action is placed after both complete paragraphs");

    scroll.scrollOffset = 999.0F;
    const auto trailing = ComputeLayout(
        scroll, {0.0F, 0.0F, 180.0F, 110.0F}, measure);
    Near(trailing.Find("permission-scroll")->scrollOffset, 114.0F,
         "wrapped permission scroll reaches its true trailing edge");
    Near(trailing.Find("scroll-action")->borderBox.y, 66.0F,
         "trailing action is fully revealed after wrapped content");
    Near(trailing.Find("scroll-action")->visibleBox.height, 44.0F,
         "trailing controller action remains wholly visible");
}

void TinyAndPortraitWidgetContainment() {
    auto title = Element("title");
    title.flexBasis = 40.0F;
    title.flexShrink = 0.0F;
    auto primary = Element("primary");
    primary.flexBasis = 220.0F;
    primary.minHeight = 120.0F;
    auto actions = Element("actions", LayoutDirection::Row);
    actions.flexBasis = 56.0F;
    actions.minHeight = 44.0F;
    actions.gap = 12.0F;
    for (int index = 0; index < 3; ++index) {
        auto action = Element("action-" + std::to_string(index));
        action.flexBasis = 64.0F;
        action.minWidth = 44.0F;
        actions.children.push_back(std::move(action));
    }

    auto root = Element("widget");
    root.padding = BoxSpacing::One(16.0F);
    root.gap = 12.0F;
    root.overflow = OverflowBehavior::Clip;
    root.children = {title, primary, actions};

    for (const auto viewport : {
             Rect{0, 0, 1, 1},
             Rect{0, 0, 238, 45},
             Rect{0, 0, 256, 545},
             Rect{0, 0, 718, 78},
             Rect{0, 0, 878, 446},
         }) {
        gba::declarative::LayoutOptions options;
        options.pixelScale = 1.875F;
        const float snapTolerance = 0.5F / options.pixelScale + 0.001F;
        const auto result = ComputeLayout(root, viewport, {}, options);
        Check(result.valid(), "tiny and portrait widget layout remains valid");
        Check(result.compactMode, "constrained widget viewport activates compact mode");
        for (const auto& [id, box] : result.boxes) {
            (void)id;
            Check(std::isfinite(box.visibleBox.x) && std::isfinite(box.visibleBox.y) &&
                  std::isfinite(box.visibleBox.width) && std::isfinite(box.visibleBox.height),
                  "constrained visible geometry remains finite");
            Check(box.visibleBox.width >= 0 && box.visibleBox.height >= 0,
                  "constrained visible geometry never inverts");
            const bool horizontalContainment = box.visibleBox.width <= 0.0F ||
                (box.visibleBox.x >= viewport.x - snapTolerance &&
                 box.visibleBox.x + box.visibleBox.width <=
                     viewport.x + viewport.width + snapTolerance);
            const bool verticalContainment = box.visibleBox.height <= 0.0F ||
                (box.visibleBox.y >= viewport.y - snapTolerance &&
                 box.visibleBox.y + box.visibleBox.height <=
                     viewport.y + viewport.height + snapTolerance);
            Check(horizontalContainment && verticalContainment,
                  "clipped widget geometry remains inside responsive viewport");
        }
    }
}

void AxisAlignment() {
    auto first = Element("first");
    first.width = 40.0F;
    first.height = 20.0F;
    auto second = Element("second");
    second.width = 40.0F;
    second.height = 20.0F;
    auto centered = Element("centered", LayoutDirection::Row);
    centered.mainAxisAlignment = MainAxisAlignment::Center;
    centered.crossAxisAlignment = CrossAxisAlignment::Center;
    centered.gap = 10.0F;
    centered.children = {first, second};
    const auto centerResult = ComputeLayout(centered, {0, 0, 200, 100});
    Near(centerResult.Find("first")->borderBox.x, 55.0F, "center justify adds leading space");
    Near(centerResult.Find("first")->borderBox.y, 40.0F, "center align positions cross axis");
    Near(centerResult.Find("second")->borderBox.x, 105.0F, "center justify retains authored gap");

    centered.mainAxisAlignment = MainAxisAlignment::SpaceBetween;
    centered.crossAxisAlignment = CrossAxisAlignment::End;
    const auto spread = ComputeLayout(centered, {0, 0, 200, 100});
    Near(spread.Find("first")->borderBox.x, 0.0F, "space-between starts at leading edge");
    Near(spread.Find("second")->borderBox.x, 160.0F, "space-between ends at trailing edge");
    Near(spread.Find("first")->borderBox.y, 80.0F, "end align positions cross axis");
}

void PixelSnapAndSafeMath() {
    auto child = Element("child");
    child.flexBasis = std::numeric_limits<float>::quiet_NaN();
    child.flexGrow = 99.0F;
    child.padding = BoxSpacing::One(std::numeric_limits<float>::infinity());
    auto root = Element("root", LayoutDirection::Row);
    root.children = {child};
    gba::declarative::LayoutOptions options;
    options.pixelScale = 2.0F;
    const auto result = ComputeLayout(root, {0.25F, 0.25F, 100.25F, 50.25F}, {}, options);
    Check(result.valid(), "bad numeric inputs degrade to warnings");
    Check(result.issues.size() >= 3, "unsafe values produce bounded diagnostics");
    const auto box = result.Find("root")->borderBox;
    Near(box.x, 0.5F, "left edge snaps to physical half-pixel grid");
    Check(std::isfinite(result.Find("child")->borderBox.width), "output width remains finite");
}

void DuplicateIdsFailClosed() {
    auto one = Element("same");
    auto two = Element("same");
    auto root = Element("root");
    root.children = {one, two};
    const auto result = ComputeLayout(root, {0.0F, 0.0F, 100.0F, 100.0F});
    Check(!result.valid(), "duplicate stable IDs fail layout");
    Check(result.boxes.empty(), "invalid tree publishes no partial rect map");
    Check(result.issues.size() == 1 &&
        result.issues[0].severity == LayoutIssueSeverity::Error &&
        result.issues[0].code == "duplicate_id", "duplicate diagnostic is deterministic");
}

void OutputIsDeterministicAndOrdinal() {
    auto zebra = Element("zebra");
    zebra.flexGrow = 1.0F;
    auto alpha = Element("alpha");
    alpha.flexGrow = 2.0F;
    auto root = Element("root", LayoutDirection::Row);
    root.gap = 3.0F;
    root.children = {zebra, alpha};
    gba::declarative::LayoutOptions options;
    options.pixelScale = 1.5F;
    const auto first = ComputeLayout(root, {1.0F, 2.0F, 601.0F, 101.0F}, {}, options);
    const auto second = ComputeLayout(root, {1.0F, 2.0F, 601.0F, 101.0F}, {}, options);
    Check(first.boxes.size() == second.boxes.size(), "repeat layout has same element count");
    auto firstItem = first.boxes.begin();
    auto secondItem = second.boxes.begin();
    Check(firstItem->first == "alpha", "output map enumerates IDs ordinally");
    for (; firstItem != first.boxes.end(); ++firstItem, ++secondItem) {
        Check(firstItem->first == secondItem->first, "repeat layout preserves ID order");
        Near(firstItem->second.borderBox.x, secondItem->second.borderBox.x, "repeat x is deterministic");
        Near(firstItem->second.borderBox.y, secondItem->second.borderBox.y, "repeat y is deterministic");
        Near(firstItem->second.borderBox.width, secondItem->second.borderBox.width, "repeat width is deterministic");
        Near(firstItem->second.borderBox.height, secondItem->second.borderBox.height, "repeat height is deterministic");
    }
}

} // namespace

int main() {
    MediaLayout1080p();
    FlexShrinkAndMinimums();
    FlexGrowHonorsMaximum();
    FlexedRowRemeasuresWrappedCrossSize();
    NestedPaddingAndMargins();
    OverflowClipping();
    ScrollOffsetsAreBoundedAndClipped();
    ResponsiveViewports();
    ResponsiveRowsWrapAtStableItemBoundaries();
    ResponsiveGridUsesAvailableDipWidth();
    ResponsiveGridMeasuresMixedIntrinsicRows();
    ResponsiveGridHandlesEmptyLargeAndScaledSurfaces();
    ResponsiveGridInsideHorizontalScrollUsesViewportWidth();
    InvalidResponsiveGridFailsClosed();
    IntrinsicAndCompactMode();
    WrappedIntrinsicLeavesDoNotCollapseOrOverlap();
    ScrollExtentIncludesWrappedIntrinsicParagraphs();
    TinyAndPortraitWidgetContainment();
    AxisAlignment();
    PixelSnapAndSafeMath();
    DuplicateIdsFailClosed();
    OutputIsDeterministicAndOrdinal();
    std::cout << "DeclarativeLayoutTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
