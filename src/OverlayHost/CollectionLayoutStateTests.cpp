#include "CollectionLayoutState.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <random>
#include <stdexcept>
#include <string_view>
#include <unordered_map>

namespace {
using namespace widgetrail::collection;
std::size_t checks{};
void Check(bool condition, std::string_view message) {
    ++checks;
    if (!condition) { std::cerr << "FAIL: " << message << '\n'; std::exit(EXIT_FAILURE); }
}
void Near(double actual, double expected, std::string_view message) {
    Check(std::isfinite(actual) && std::abs(actual - expected) < 0.00001, message);
}
std::vector<Item> Items(std::size_t count, double extent = 100) {
    std::vector<Item> result;
    for (std::size_t i = 0; i < count; ++i) result.push_back({L"item-" + std::to_wstring(i), 1, extent});
    return result;
}
void Measure(CollectionLayoutState& state, std::initializer_list<Measurement> batch) {
    Check(state.Measure(state.Generation(), std::span{batch.begin(), batch.size()}), "accept valid measurement");
}

void TransactionAndReuse() {
    CollectionLayoutState state;
    auto items = Items(6);
    const Identity identity{L"widget/library/query", 1};
    Layout layout{1, 0, 10, 1};
    Check(state.Replace(identity, layout, items, false, true), "initial list accepted");
    Near(state.Extent(), 650, "estimated extent excludes trailing gap");
    Measure(state, {{1, 175}, {4, 50}});
    Near(state.Extent(), 675, "variable measurements update total");
    const auto oldGeneration = state.Generation();
    const auto oldGeometry = state.GeometryRevision();
    const auto original = items;
    items[2].key = items[0].key;
    Check(!state.Replace(identity, layout, items, true, true), "duplicate key rejected");
    Check(state.Generation() == oldGeneration && state.GeometryRevision() == oldGeometry, "bad snapshot is atomic");
    Near(state.Extent(), 675, "bad snapshot leaves extents intact");
    const Measurement bad[]{{0, 200}, {6, 120}};
    Check(!state.Measure(oldGeneration, bad), "invalid batch rejected");
    Near(state.At(0).extent, 100, "no partial measurement admission");
    Check(!state.IsMeasured(0), "bad measurement leaves measured bit intact");
    const Measurement duplicate[]{{0, 200}, {0, 300}};
    Check(!state.Measure(oldGeneration, duplicate), "ambiguous duplicate measurement rejected");
    items = original;
    std::swap(items[1], items[3]);
    Check(state.Replace(identity, layout, items, true, true), "reorder accepted");
    Check(state.IsMeasured(3), "measurement follows stable key");
    Near(state.At(3).extent, 175, "reorder retains valid item measurement");
    const Measurement stale[]{{3, 900}};
    Check(!state.Measure(oldGeneration, stale), "stale batch cannot update reordered item");
    items[3].measurementRevision++;
    Check(state.Replace(identity, layout, items, true, true), "item revision accepted");
    Check(!state.IsMeasured(3) && state.IsMeasured(4), "invalidate only changed item");
    Near(state.At(3).extent, 100, "changed item uses estimate until measured");
    layout.measurementContext++;
    Check(state.Replace(identity, layout, items, true, true), "changed constraints accepted");
    Check(!state.IsMeasured(4), "constraint revision invalidates measurement");
    Measure(state, {{4, 50}});
    Check(state.Replace({identity.scope, 2}, layout, items, true, true), "query reset accepted");
    Check(!state.IsMeasured(4), "reset cannot reuse old query measurements");
    Measure(state, {{4, 50}});
    layout.measurementContext = 0;
    Check(state.Replace({identity.scope, 2}, layout, items, true, true), "zero context allowed");
    Check(!state.IsMeasured(4), "zero context opts out of reuse");
    Measure(state, {{4, 50}});
    Check(state.Replace({identity.scope, 2}, layout, items, true, true), "same zero context allowed");
    Check(!state.IsMeasured(4), "zero is never a reusable context");
    layout.measurementContext = 7;
    items[4].measurementRevision = 0;
    Check(state.Replace(identity, layout, items, true, true), "zero item revision allowed");
    Measure(state, {{4, 50}});
    Check(state.Replace(identity, layout, items, true, true), "same zero item revision allowed");
    Check(!state.IsMeasured(4), "zero item revision never authorizes reuse");
}

void AnchorAndGrid() {
    CollectionLayoutState state;
    auto items = Items(20);
    const Identity identity{L"home", 7};
    Layout layout{3, -1, 8, 1};
    Check(state.Replace(identity, layout, items, true, true), "grid accepted");
    Check(state.At(0).column == 2 && state.At(1).column == 0, "negative window offset keeps partial row");
    Near(state.At(1).offset, 108, "next row starts after partial row");
    Measure(state, {{1, 150}, {2, 60}, {3, 80}});
    Near(state.At(4).offset, 266, "row takes maximum measured item extent");
    auto anchor = state.CaptureAnchor(280);
    Check(anchor && anchor->key == L"item-4", "anchor records logical visible row");
    Near(anchor->viewportOffset, -14, "anchor retains partial row position");
    Measure(state, {{0, 200}});
    Near(*state.RestoreAnchor(*anchor, 200), 380, "measurement before anchor preserves visual offset");
    items.insert(items.begin(), {L"prepended", 1, 100});
    layout.startIndex--;
    Check(state.Replace(identity, layout, items, true, true), "non-aligned prepend accepted");
    Near(*state.RestoreAnchor(*anchor, 200), 380, "prepend preserves same surviving row");
    layout.columns = 2;
    layout.measurementContext++;
    Check(state.Replace(identity, layout, items, true, true), "column change accepted");
    Near(*state.RestoreAnchor(*anchor, 200), state.At(*state.Find(L"item-4")).offset + 14,
        "column change preserves key-relative anchor");
    items.erase(items.begin() + static_cast<std::ptrdiff_t>(*state.Find(L"item-4")));
    Check(state.Replace(identity, layout, items, true, true), "anchor eviction accepted");
    Check(!state.RestoreAnchor(*anchor, 200), "removed anchor needs explicit fallback");
    anchor = state.CaptureAnchor(280);
    Check(state.Replace({L"other", 7}, layout, items, true, true), "scope replacement accepted");
    Check(!state.RestoreAnchor(*anchor, 200), "anchor never crosses input authority");
}

void DemandAndNavigation() {
    CollectionLayoutState state;
    Check(state.Replace({L"library", 1}, {3, 0, 10, 1}, Items(100'000), true, true), "large data window");
    const std::wstring protectedKeys[]{L"item-99999", L"item-99999", L"missing", L"item-33"};
    const auto plan = state.Plan(1100, 200, 1, protectedKeys, 20);
    Check(plan.visibleBegin == 30 && plan.visibleEnd == 36, "only intersecting lines are visible");
    Check(plan.bufferedBegin == 27 && plan.bufferedEnd == 39, "bounded adjacent line demand");
    Check(plan.protectedItems == std::vector<std::size_t>{99'999}, "distant focus does not realize intervening data");
    Check(plan.requiredCount == 13 && !plan.exceedsBudget, "deduplicate protected and visible demand");
    Check(state.Plan(1100, 200, 1, protectedKeys, 12).exceedsBudget, "budget exhaustion is explicit");
    const auto collapsed = state.Plan(1100, 0, 1, protectedKeys, 20);
    Check(collapsed.bufferedBegin == collapsed.bufferedEnd && collapsed.requiredCount == 2,
        "collapsed viewport requests only protected items");
    Check(state.Navigate(L"item-33", Direction::NextLine).index == 36, "logical grid navigation preserves column");
    Check(state.Navigate(L"item-0", Direction::PreviousLine).kind == TargetKind::NeedBefore, "previous provider boundary");
    Check(state.Navigate(L"item-99998", Direction::NextLine).kind == TargetKind::NeedAfter, "missing same-column item requests page");
    Check(state.Navigate(L"missing", Direction::Next).kind == TargetKind::MissingOrigin, "missing origin does not jump to first");
    Check(state.Replace({L"library", 1}, {3, 1, 10, 1}, Items(6), false, false), "terminal partial grid");
    Check(state.Navigate(L"item-2", Direction::PreviousLine).index == 0, "terminal partial previous row clamps column");
    Check(state.Navigate(L"item-4", Direction::NextLine).index == 5, "terminal partial next row clamps column");
    Check(state.Navigate(L"item-5", Direction::NextLine).kind == TargetKind::Boundary, "last row stops cleanly");
    Check(state.Replace({L"library", 1}, {1, 0, 10, 1}, Items(4), false, false), "gap test list");
    const auto gap = state.Plan(105, 2, 0, {}, 100);
    Check(gap.visibleBegin == gap.visibleEnd, "viewport entirely in a gap has no visible item");
    const auto boundary = state.Plan(0, 110, 0, {}, 100);
    Check(boundary.visibleBegin == 0 && boundary.visibleEnd == 1, "exact end boundary excludes next item");
}

void InvalidInput() {
    CollectionLayoutState state;
    const Identity identity{L"valid", 1};
    Check(state.Replace(identity, {}, {}, false, false), "empty collection accepted");
    Near(state.Extent(), 0, "empty extent");
    Check(!state.CaptureAnchor(0), "empty has no anchor");
    Check(state.Plan(0, 100, 1, {}, 100).requiredCount == 0, "empty has no demand");
    Check(!state.Replace({}, {}, Items(1), false, false), "empty authority rejected");
    Check(!state.Replace({std::wstring(513, L'x'), 1}, {}, Items(1), false, false), "scope memory bound enforced");
    Check(!state.Replace(identity, {}, {{std::wstring(129, L'x'), 1, 100}}, false, false), "key memory bound enforced");
    Check(!state.Replace(identity, {0}, Items(1), false, false), "zero columns rejected");
    Check(!state.Replace(identity, {129}, Items(1), false, false), "unbounded columns rejected");
    Check(!state.Replace(identity, {1, 0, -1}, Items(1), false, false), "negative gap rejected");
    for (auto extent : {0.0, -1.0, 1e-300, std::numeric_limits<double>::infinity(), std::nan(""), 1'000'001.0}) {
        Check(!state.Replace(identity, {}, Items(1, extent), false, false), "invalid estimated extent rejected");
    }
    Check(!state.Replace(identity, {}, Items(100'001), false, false), "logical metadata bound enforced");
    bool threw = false;
    try { (void)state.Plan(std::nan(""), 10, 0, {}, 1); }
    catch (const std::invalid_argument&) { threw = true; }
    Check(threw, "invalid viewport rejected before arithmetic");
    Check(state.Replace(identity, {3, std::numeric_limits<std::int64_t>::min(), 0, 1}, Items(5), false, false),
        "minimum signed window offset does not overflow");
    Check(state.At(0).column == 1, "floor modulo at signed minimum");
    const auto all = state.Plan(0, 10000, std::numeric_limits<std::size_t>::max(), {}, 100);
    Check(all.requiredCount == 5, "huge buffer request is bounded without overflow");
    const std::vector<std::wstring> repeatedProtection(100'000, L"item-4");
    const auto protectedOnly = state.Plan(0, 0, 0, repeatedProtection, 1);
    Check(protectedOnly.requiredCount == 1 && !protectedOnly.exceedsBudget, "repeated protection remains one item");
}

// Independent eager reference: materialize each row and scan every row for
// intersections. The production index must agree after arbitrary measurements.
void Differential() {
    std::mt19937 random{0x293};
    for (std::size_t trial = 0; trial < 100; ++trial) {
        CollectionLayoutState state;
        const auto count = 1 + random() % 1500;
        const auto columns = 1 + random() % 8;
        const auto start = static_cast<std::int64_t>(random() % 21) - 10;
        const double gap = static_cast<double>(random() % 40) / 4;
        const auto leading = static_cast<std::size_t>((start % columns + columns) % columns);
        auto items = Items(count);
        std::vector<double> extents;
        for (auto& item : items) {
            item.estimatedExtent = 10 + static_cast<double>(random() % 1200) / 4;
            extents.push_back(item.estimatedExtent);
        }
        Check(state.Replace({L"reference", trial}, {columns, start, gap, 1}, items, false, false), "random collection admitted");
        for (int iteration = 0; iteration < 12; ++iteration) {
            const auto index = random() % count;
            const auto measured = 5 + static_cast<double>(random() % 2000) / 4;
            Measure(state, {{index, measured}});
            extents[index] = measured;
            std::vector<double> heights((leading + count + columns - 1) / columns, 0);
            for (std::size_t i = 0; i < count; ++i)
                heights[(leading + i) / columns] = std::max(heights[(leading + i) / columns], extents[i]);
            std::vector<double> offsets;
            double extent = 0;
            for (const auto height : heights) { offsets.push_back(extent); extent += height + gap; }
            extent -= gap;
            Near(state.Extent(), extent, "incremental total matches eager reference");
            for (std::size_t i = 0; i < count; ++i) {
                const auto geometry = state.At(i);
                Near(geometry.offset, offsets[(leading + i) / columns], "indexed placement matches reference");
                Near(geometry.extent, extents[i], "item geometry matches reference");
            }
            const double viewport = 1 + random() % 800;
            const double offset = std::min(static_cast<double>(random() % 50000), std::max(0.0, extent - viewport));
            const auto plan = state.Plan(offset, viewport, 0, {}, count);
            for (std::size_t i = 0; i < count; ++i) {
                const auto row = (leading + i) / columns;
                const bool visible = offsets[row] < offset + viewport && offsets[row] + heights[row] > offset;
                Check(visible == (i >= plan.visibleBegin && i < plan.visibleEnd), "viewport demand matches eager reference");
            }
        }
    }
}

void MutationReference() {
    CollectionLayoutState state;
    auto items = Items(100);
    Identity identity{L"mutations", 1};
    Layout layout{4, -3, 7.3, 1};
    std::unordered_map<std::wstring, double> known;
    std::mt19937 random{0x293294};
    Check(state.Replace(identity, layout, items, true, true), "mutation reference initial state");
    for (std::size_t iteration = 0; iteration < 240; ++iteration) {
        const auto selected = static_cast<std::size_t>(random() % items.size());
        const auto measured = 17 + static_cast<double>(random() % 3000) / 10;
        Measure(state, {{selected, measured}});
        known[items[selected].key] = measured;
        const auto oldGeneration = state.Generation();
        const auto anchor = state.CaptureAnchor(std::min(750.0, state.Extent()));
        switch (iteration % 8) {
        case 0: // Insert a page fragment at the beginning.
            items.insert(items.begin(), {L"new-" + std::to_wstring(iteration), 1, 91.7});
            --layout.startIndex;
            break;
        case 1: // Evict an arbitrary existing item.
            known.erase(items[selected].key);
            items.erase(items.begin() + static_cast<std::ptrdiff_t>(selected));
            break;
        case 2: // Logical reorder keeps only valid per-key measurements.
            std::rotate(items.begin(), items.begin() + static_cast<std::ptrdiff_t>(selected), items.end());
            break;
        case 3: // Item text/style/intrinsic input changed.
            ++items[selected].measurementRevision;
            known.erase(items[selected].key);
            break;
        case 4: // DPI/width/text-scale change affects shared constraints.
            ++layout.measurementContext;
            layout.columns = 1 + random() % 7;
            known.clear();
            break;
        case 5: // Refresh uses same keys under new query authority.
            ++identity.resetGeneration;
            known.clear();
            break;
        case 6: // Data append at the trailing edge.
            items.push_back({L"appended-" + std::to_wstring(iteration), 1, 118.9});
            break;
        case 7: // Evict a provider prefix that is not aligned to grid rows.
            known.erase(items.front().key);
            items.erase(items.begin());
            ++layout.startIndex;
            break;
        }
        Check(state.Replace(identity, layout, items, true, true), "mixed mutation accepted");
        const Measurement stale[]{{0, 900}};
        Check(!state.Measure(oldGeneration, stale), "pending measurement cancelled by mixed mutation");
        const auto columns = static_cast<std::int64_t>(layout.columns);
        auto position = layout.startIndex;
        std::vector<double> offsets(items.size()), heights(items.size());
        double rowOffset = 0, rowHeight = 0;
        for (std::size_t i = 0; i < items.size(); ++i, ++position) {
            if (i != 0 && position % columns == 0) {
                rowOffset += rowHeight + layout.lineGap;
                rowHeight = 0;
            }
            const auto saved = known.find(items[i].key);
            const auto height = saved == known.end() ? items[i].estimatedExtent : saved->second;
            offsets[i] = rowOffset;
            heights[i] = height;
            rowHeight = std::max(rowHeight, height);
            Check(state.IsMeasured(i) == (saved != known.end()), "mixed mutation measurement reuse matches oracle");
            Near(state.At(i).offset, rowOffset, "mixed mutation placement matches linear oracle");
            Near(state.At(i).extent, height, "mixed mutation extent matches keyed oracle");
        }
        Near(state.Extent(), rowOffset + rowHeight, "mixed mutation total matches oracle");
        const auto restored = state.RestoreAnchor(*anchor, 150);
        const auto surviving = std::find_if(items.begin(), items.end(), [&](const Item& item) { return item.key == anchor->key; });
        if (anchor->identity != identity || surviving == items.end()) {
            Check(!restored, "reset or removed anchor is not implicitly reused");
        } else {
            const auto index = static_cast<std::size_t>(surviving - items.begin());
            Check(restored.has_value(), "surviving anchor restored");
            Near(*restored, std::clamp(offsets[index] - anchor->viewportOffset, 0.0,
                std::max(0.0, rowOffset + rowHeight - 150)), "mixed mutation anchor offset matches oracle");
        }
    }
}
}

int main() {
    TransactionAndReuse();
    AnchorAndGrid();
    DemandAndNavigation();
    InvalidInput();
    Differential();
    MutationReference();
    std::cout << "CollectionLayoutStateTests: " << checks << " checks passed.\n";
}
