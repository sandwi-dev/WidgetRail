#include "CollectionLayoutState.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <stdexcept>
#include <unordered_set>
#include <utility>

namespace widgetrail::collection {
namespace {
bool ValidExtent(double value) noexcept {
    return std::isfinite(value) && value >= CollectionLayoutState::MinimumItemExtent &&
        value <= CollectionLayoutState::MaximumItemExtent;
}
void ValidateViewport(double offset, double extent) {
    if (!std::isfinite(offset) || !std::isfinite(extent) || extent < 0)
        throw std::invalid_argument("Collection viewport must be finite and its extent nonnegative.");
}
std::size_t LowBit(std::size_t value) noexcept { return value & (~value + 1); }
}

bool CollectionLayoutState::Replace(Identity identity, Layout layout,
    std::vector<Item> items, bool hasBefore, bool hasAfter) {
    if (identity.scope.empty() || identity.scope.size() > MaximumScopeLength ||
        layout.columns == 0 || layout.columns > MaximumColumns ||
        !std::isfinite(layout.lineGap) || layout.lineGap < 0 || layout.lineGap > MaximumItemExtent ||
        items.size() > MaximumItems || generation_ == std::numeric_limits<std::uint64_t>::max() ||
        geometryRevision_ == std::numeric_limits<std::uint64_t>::max()) return false;

    // Build separately so duplicate keys, invalid extents or allocation failure
    // cannot partially replace the currently admitted logical window.
    CollectionLayoutState next;
    next.identity_ = std::move(identity);
    next.layout_ = layout;
    next.hasBefore_ = hasBefore;
    next.hasAfter_ = hasAfter;
    next.generation_ = generation_ + 1;
    next.geometryRevision_ = geometryRevision_ + 1;
    next.items_.reserve(items.size());
    next.byKey_.reserve(items.size());
    const bool reuse = next.identity_ == identity_ && layout.measurementContext != 0 &&
        layout.measurementContext == layout_.measurementContext && layout.columns == layout_.columns;
    for (auto& item : items) {
        if (item.key.empty() || item.key.size() > MaximumKeyLength || !ValidExtent(item.estimatedExtent) ||
            !next.byKey_.emplace(item.key, next.items_.size()).second) return false;
        Entry entry{std::move(item)};
        entry.extent = entry.item.estimatedExtent;
        if (reuse && entry.item.measurementRevision != 0) {
            const auto old = byKey_.find(entry.item.key);
            if (old != byKey_.end()) {
                const auto& previous = items_[old->second];
                if (previous.measured && previous.item.measurementRevision == entry.item.measurementRevision) {
                    entry.extent = previous.extent;
                    entry.measured = true;
                }
            }
        }
        next.items_.push_back(std::move(entry));
    }
    next.BuildExtents();
    *this = std::move(next);
    return true;
}

std::size_t CollectionLayoutState::LeadingColumns() const noexcept {
    const auto columns = static_cast<std::int64_t>(layout_.columns);
    const auto remainder = layout_.startIndex % columns;
    return static_cast<std::size_t>(remainder < 0 ? remainder + columns : remainder);
}
std::size_t CollectionLayoutState::LineOf(std::size_t index) const noexcept {
    return (LeadingColumns() + index) / layout_.columns;
}
std::size_t CollectionLayoutState::LineBegin(std::size_t line) const noexcept {
    const auto start = line * layout_.columns;
    return start > LeadingColumns() ? start - LeadingColumns() : 0;
}
std::size_t CollectionLayoutState::LineEnd(std::size_t line) const noexcept {
    return std::min(items_.size(), (line + 1) * layout_.columns - LeadingColumns());
}
double CollectionLayoutState::LineExtent(std::size_t line) const noexcept {
    double result = 0;
    for (auto i = LineBegin(line); i < LineEnd(line); ++i) result = std::max(result, items_[i].extent);
    return result;
}
void CollectionLayoutState::BuildExtents() {
    const auto count = items_.empty() ? 0 : LineOf(items_.size() - 1) + 1;
    lines_.resize(count);
    prefixTree_.assign(count + 1, 0);
    // Linear Fenwick construction; no O(n log n) rebuild on page admission.
    for (std::size_t line = 0; line < count; ++line) {
        lines_[line] = LineExtent(line);
        const auto i = line + 1;
        prefixTree_[i] += lines_[line] + layout_.lineGap;
        const auto parent = i + LowBit(i);
        if (parent <= count) prefixTree_[parent] += prefixTree_[i];
    }
}
void CollectionLayoutState::UpdateLine(std::size_t line, double extent) noexcept {
    const auto delta = extent - lines_[line];
    lines_[line] = extent;
    for (auto i = line + 1; i < prefixTree_.size(); i += LowBit(i)) prefixTree_[i] += delta;
}
double CollectionLayoutState::Prefix(std::size_t count) const noexcept {
    double result = 0;
    for (auto i = count; i != 0; i -= LowBit(i)) result += prefixTree_[i];
    return result;
}
double CollectionLayoutState::Extent() const noexcept {
    return lines_.empty() ? 0 : Prefix(lines_.size()) - layout_.lineGap;
}
std::size_t CollectionLayoutState::FindLine(double offset) const noexcept {
    // Largest prefix <= offset, so exact line boundaries select the next line.
    std::size_t index = 0, step = 1;
    while (step <= lines_.size() / 2) step *= 2;
    double consumed = 0;
    for (; step != 0; step /= 2) {
        const auto candidate = index + step;
        if (candidate < prefixTree_.size() && consumed + prefixTree_[candidate] <= offset) {
            index = candidate;
            consumed += prefixTree_[candidate];
        }
    }
    return lines_.empty() ? 0 : std::min(index, lines_.size() - 1);
}

bool CollectionLayoutState::Measure(std::uint64_t generation, std::span<const Measurement> batch) {
    if (generation == 0 || generation != generation_ || batch.size() > items_.size() ||
        geometryRevision_ == std::numeric_limits<std::uint64_t>::max()) return false;
    std::unordered_set<std::size_t> unique;
    std::vector<std::size_t> dirtyLines;
    unique.reserve(batch.size());
    dirtyLines.reserve(batch.size());
    for (const auto& measurement : batch) {
        if (measurement.index >= items_.size() || !ValidExtent(measurement.extent) ||
            !unique.insert(measurement.index).second) return false;
        dirtyLines.push_back(LineOf(measurement.index));
    }
    // Finish all allocation/validation before changing state.
    std::sort(dirtyLines.begin(), dirtyLines.end());
    dirtyLines.erase(std::unique(dirtyLines.begin(), dirtyLines.end()), dirtyLines.end());
    for (const auto& measurement : batch) {
        auto& entry = items_[measurement.index];
        entry.extent = measurement.extent;
        entry.measured = true;
    }
    for (const auto line : dirtyLines) UpdateLine(line, LineExtent(line));
    if (!batch.empty()) ++geometryRevision_;
    return true;
}

std::optional<std::size_t> CollectionLayoutState::Find(const std::wstring& key) const {
    const auto found = byKey_.find(key);
    return found == byKey_.end() ? std::nullopt : std::optional{found->second};
}
Geometry CollectionLayoutState::At(std::size_t index) const {
    const auto& entry = items_.at(index);
    const auto line = LineOf(index);
    return {Prefix(line), entry.extent, line, (LeadingColumns() + index) % layout_.columns};
}
bool CollectionLayoutState::IsMeasured(std::size_t index) const { return items_.at(index).measured; }

Demand CollectionLayoutState::Plan(double offset, double viewportExtent, std::size_t bufferLines,
    std::span<const std::wstring> protectedKeys, std::size_t budget) const {
    ValidateViewport(offset, viewportExtent);
    Demand result;
    result.geometryRevision = geometryRevision_;
    if (!items_.empty() && viewportExtent > 0) {
        offset = std::clamp(offset, 0.0, std::max(0.0, Extent() - viewportExtent));
        const auto end = offset + std::min(viewportExtent, Extent());
        auto first = FindLine(offset);
        // An offset inside the gap must not count the previous row as visible.
        if (Prefix(first) + lines_[first] <= offset) ++first;
        auto last = FindLine(end);
        if (Prefix(last) < end) ++last;
        last = std::max(first, std::min(last, lines_.size()));
        result.visibleBegin = first < lines_.size() ? LineBegin(first) : items_.size();
        result.visibleEnd = last > first ? LineEnd(last - 1) : result.visibleBegin;
        const auto bufferedFirst = first > bufferLines ? first - bufferLines : 0;
        const auto bufferedLast = last + std::min(bufferLines, lines_.size() - last);
        result.bufferedBegin = bufferedFirst < lines_.size() ? LineBegin(bufferedFirst) : items_.size();
        result.bufferedEnd = bufferedLast > bufferedFirst ? LineEnd(bufferedLast - 1) : result.bufferedBegin;
    }
    std::unordered_set<std::size_t> protectedIndices;
    for (const auto& key : protectedKeys) {
        const auto index = Find(key);
        if (index && (*index < result.bufferedBegin || *index >= result.bufferedEnd) &&
            protectedIndices.insert(*index).second)
            result.protectedItems.push_back(*index);
    }
    std::sort(result.protectedItems.begin(), result.protectedItems.end());
    result.requiredCount = result.bufferedEnd - result.bufferedBegin + result.protectedItems.size();
    result.exceedsBudget = result.requiredCount > budget;
    return result;
}

std::optional<Anchor> CollectionLayoutState::CaptureAnchor(double offset) const {
    ValidateViewport(offset, 0);
    if (items_.empty()) return std::nullopt;
    offset = std::clamp(offset, 0.0, Extent());
    auto line = FindLine(offset);
    if (Prefix(line) + lines_[line] <= offset && line + 1 < lines_.size()) ++line;
    const auto index = LineBegin(line);
    return Anchor{identity_, items_[index].item.key, Prefix(line) - offset};
}
std::optional<double> CollectionLayoutState::RestoreAnchor(const Anchor& anchor, double viewportExtent) const {
    ValidateViewport(anchor.viewportOffset, viewportExtent);
    if (anchor.identity != identity_) return std::nullopt;
    const auto index = Find(anchor.key);
    if (!index) return std::nullopt;
    return std::clamp(At(*index).offset - anchor.viewportOffset, 0.0, std::max(0.0, Extent() - viewportExtent));
}

Target CollectionLayoutState::Navigate(const std::wstring& origin, Direction direction) const {
    const auto index = Find(origin);
    if (!index) return {TargetKind::MissingOrigin};
    const auto before = [&] { return Target{hasBefore_ ? TargetKind::NeedBefore : TargetKind::Boundary}; };
    const auto after = [&] { return Target{hasAfter_ ? TargetKind::NeedAfter : TargetKind::Boundary}; };
    switch (direction) {
    case Direction::Previous:
        return *index == 0 ? before() : Target{TargetKind::Item, *index - 1};
    case Direction::Next:
        return *index + 1 == items_.size() ? after() : Target{TargetKind::Item, *index + 1};
    case Direction::PreviousLine:
        if (*index >= layout_.columns) return {TargetKind::Item, *index - layout_.columns};
        if (!hasBefore_ && LineOf(*index) > 0) return {TargetKind::Item, 0};
        return before();
    case Direction::NextLine:
        if (*index + layout_.columns < items_.size()) return {TargetKind::Item, *index + layout_.columns};
        // At a provider boundary, request the missing same-column item rather
        // than changing columns before its descriptor has arrived.
        if (hasAfter_) return after();
        if (LineOf(*index) < LineOf(items_.size() - 1)) return {TargetKind::Item, items_.size() - 1};
        return after();
    }
    return {TargetKind::Boundary};
}

} // namespace widgetrail::collection
