#pragma once

#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>
#include <string>
#include <unordered_map>
#include <vector>

namespace widgetrail::collection {

// Logical descriptors survive unrealization. No action, WidgetNode, COM object,
// or provider callback is owned by this geometry/identity layer.
struct Item final {
    std::wstring key;
    // Host identity of content, resolved item style and intrinsic measure inputs.
    // Zero disables reuse across descriptor replacement.
    std::uint64_t measurementRevision{};
    double estimatedExtent{}; // Main-axis outer extent, excluding collection gap.
};

struct Layout final {
    std::size_t columns{1}; // One for a list; resolved adaptive count for a grid.
    std::int64_t startIndex{}; // Opaque cursor windows may have negative indices.
    double lineGap{};
    // Host revision for all measurement dependencies: actual item constraints,
    // inherited style, text/DPI scale and relevant intrinsic resource inputs.
    // Zero disables reuse across descriptor replacement.
    std::uint64_t measurementContext{};
};

struct Identity final {
    std::wstring scope;
    std::uint64_t resetGeneration{};
    bool operator==(const Identity&) const = default;
};

struct Measurement final { std::size_t index{}; double extent{}; };
struct Geometry final { double offset{}, extent{}; std::size_t line{}, column{}; };
struct Anchor final {
    Identity identity;
    std::wstring key;
    double viewportOffset{}; // Item offset minus scroll offset; may be negative.
};

struct Demand final {
    std::uint64_t geometryRevision{};
    // Half-open contiguous ranges; protected indices do not expand these ranges.
    std::size_t visibleBegin{}, visibleEnd{}, bufferedBegin{}, bufferedEnd{};
    std::vector<std::size_t> protectedItems;
    // Includes protected items only once. Never silently truncate visible demand.
    std::size_t requiredCount{};
    bool exceedsBudget{};
};

enum class Direction { Previous, Next, PreviousLine, NextLine };
enum class TargetKind { Item, NeedBefore, NeedAfter, Boundary, MissingOrigin };
struct Target final { TargetKind kind{TargetKind::Boundary}; std::size_t index{}; };

// Host-thread confined. Stage a copy when preparing a new presentation and
// publish it with its geometry/focus/paint transaction; this is not a concurrent
// scene publisher. Snapshot replacement is O(n); measurement/lookup is O(log n)
// for lists and O(columns + log lines) for grids.
class CollectionLayoutState final {
public:
    static constexpr std::size_t MaximumItems = 100'000;
    static constexpr std::size_t MaximumColumns = 128;
    static constexpr std::size_t MaximumKeyLength = 128;
    static constexpr std::size_t MaximumScopeLength = 512;
    static constexpr double MinimumItemExtent = 1.0 / 64.0;
    static constexpr double MaximumItemExtent = 1'000'000.0;

    // Invalid input leaves all prior state intact. Reuse requires matching
    // identity, key, item measurement revision and nonzero context, plus the
    // same column count. Caller resolves unknown provider prefix/suffix outside
    // this retained logical window; it is not fabricated as real item geometry.
    [[nodiscard]] bool Replace(Identity identity, Layout layout,
        std::vector<Item> items, bool hasBefore, bool hasAfter);
    // All-or-nothing validation. Generation rejects work from older snapshots,
    // including a removed/reinserted item with the same key and revision.
    [[nodiscard]] bool Measure(std::uint64_t generation, std::span<const Measurement> batch);
    [[nodiscard]] std::uint64_t Generation() const noexcept { return generation_; }
    [[nodiscard]] std::uint64_t GeometryRevision() const noexcept { return geometryRevision_; }
    [[nodiscard]] std::size_t Size() const noexcept { return items_.size(); }
    [[nodiscard]] double Extent() const noexcept;
    [[nodiscard]] std::optional<std::size_t> Find(const std::wstring& key) const;
    [[nodiscard]] Geometry At(std::size_t index) const;
    [[nodiscard]] bool IsMeasured(std::size_t index) const;
    [[nodiscard]] Demand Plan(double offset, double viewportExtent, std::size_t bufferLines,
        std::span<const std::wstring> protectedKeys, std::size_t budget) const;
    [[nodiscard]] std::optional<Anchor> CaptureAnchor(double offset) const;
    // Missing/reset anchors return nullopt. The caller decides explicit fallback
    // instead of transferring an old anchor to an unrelated logical item.
    [[nodiscard]] std::optional<double> RestoreAnchor(const Anchor& anchor, double viewportExtent) const;
    [[nodiscard]] Target Navigate(const std::wstring& origin, Direction direction) const;

private:
    struct Entry final { Item item; double extent{}; bool measured{}; };
    [[nodiscard]] std::size_t LeadingColumns() const noexcept;
    [[nodiscard]] std::size_t LineOf(std::size_t index) const noexcept;
    [[nodiscard]] std::size_t LineBegin(std::size_t line) const noexcept;
    [[nodiscard]] std::size_t LineEnd(std::size_t line) const noexcept;
    [[nodiscard]] double LineExtent(std::size_t line) const noexcept;
    [[nodiscard]] double Prefix(std::size_t count) const noexcept;
    [[nodiscard]] std::size_t FindLine(double offset) const noexcept;
    void BuildExtents();
    void UpdateLine(std::size_t line, double extent) noexcept;
    Identity identity_;
    Layout layout_;
    std::vector<Entry> items_;
    std::unordered_map<std::wstring, std::size_t> byKey_;
    std::vector<double> lines_;
    std::vector<double> prefixTree_;
    std::uint64_t generation_{}, geometryRevision_{};
    bool hasBefore_{}, hasAfter_{};
};

} // namespace widgetrail::collection
