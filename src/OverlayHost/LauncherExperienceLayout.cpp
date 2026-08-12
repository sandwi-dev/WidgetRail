#include "LauncherExperienceLayout.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <set>

namespace gba::launcher {
namespace {

using declarative::Rect;

constexpr std::array<Slot, 8> kSemanticOrder{
    Slot::CollectionTabs,
    Slot::GameRail,
    Slot::DetailsPanel,
    Slot::SourceStatus,
    Slot::OperationStatus,
    Slot::SystemStatus,
    Slot::ControllerHints,
    Slot::HeroBackground,
};
constexpr std::array<Slot, 4> kCritical{
    Slot::GameRail, Slot::DetailsPanel, Slot::SourceStatus, Slot::ControllerHints};

RecipeNode Leaf(
    const Slot slot,
    const NormalizedRect region,
    const std::optional<Orientation> orientation = {},
    const std::optional<Surface> surface = {}) {
    RecipeNode result;
    result.slot = slot;
    result.region = region;
    result.orientation = orientation;
    result.surface = surface;
    return result;
}

RecipeNode Overlay(std::initializer_list<RecipeNode> children) {
    RecipeNode result;
    result.type = Primitive::Overlay;
    result.children.assign(children);
    return result;
}

RecipeNode Profile(const Preset preset, const Branch branch) {
    const bool compact = branch == Branch::Compact;
    if (compact) {
        return Overlay({
            Leaf(Slot::HeroBackground, {0, 0, 1, 1}),
            Leaf(Slot::SourceStatus, {0.05F, 0.03F, 0.35F, 0.14F}),
            Leaf(Slot::CollectionTabs, {0.42F, 0.03F, 0.53F, 0.14F}),
            Leaf(Slot::DetailsPanel, {0.05F, 0.19F, 0.9F, 0.32F}),
            Leaf(Slot::GameRail, {0.05F, 0.54F, 0.9F, 0.27F},
                 preset == Preset::CoverWall || preset == Preset::CompactGrid
                     ? Orientation::Vertical
                     : Orientation::Horizontal),
            Leaf(Slot::OperationStatus, {0.05F, 0.83F, 0.42F, 0.15F}),
            Leaf(Slot::ControllerHints, {0.5F, 0.83F, 0.45F, 0.15F}),
        });
    }
    switch (preset) {
    case Preset::HeroRail:
        return Overlay({
            Leaf(Slot::HeroBackground, {0, 0, 1, 1}),
            Leaf(Slot::DetailsPanel, {0.07F, 0.08F, 0.54F, 0.4F}),
            Leaf(Slot::SourceStatus, {0.68F, 0.07F, 0.26F, 0.1F}),
            Leaf(Slot::CollectionTabs, {0.05F, 0.49F, 0.9F, 0.08F}),
            Leaf(Slot::GameRail, {0.06F, 0.58F, 0.88F, 0.28F},
                 Orientation::Horizontal),
            Leaf(Slot::OperationStatus, {0.05F, 0.88F, 0.42F, 0.09F}),
            Leaf(Slot::ControllerHints, {0.5F, 0.88F, 0.44F, 0.09F}),
        });
    case Preset::CoverWall:
        return Overlay({
            Leaf(Slot::HeroBackground, {0, 0, 1, 1}),
            Leaf(Slot::CollectionTabs, {0.04F, 0.03F, 0.62F, 0.09F}),
            Leaf(Slot::GameRail, {0.04F, 0.14F, 0.66F, 0.72F}, Orientation::Vertical),
            Leaf(Slot::DetailsPanel, {0.73F, 0.22F, 0.23F, 0.46F}),
            Leaf(Slot::SourceStatus, {0.73F, 0.08F, 0.23F, 0.1F}),
            Leaf(Slot::OperationStatus, {0.73F, 0.70F, 0.23F, 0.16F}),
            Leaf(Slot::ControllerHints, {0.52F, 0.89F, 0.44F, 0.08F}),
        });
    case Preset::Carousel:
        return Overlay({
            Leaf(Slot::HeroBackground, {0, 0, 1, 1}),
            Leaf(Slot::DetailsPanel, {0.1F, 0.08F, 0.54F, 0.32F}),
            Leaf(Slot::SourceStatus, {0.7F, 0.08F, 0.24F, 0.1F}),
            Leaf(Slot::CollectionTabs, {0.08F, 0.40F, 0.84F, 0.07F}),
            Leaf(Slot::GameRail, {0.07F, 0.48F, 0.86F, 0.34F}, Orientation::Horizontal),
            Leaf(Slot::OperationStatus, {0.07F, 0.84F, 0.44F, 0.13F}),
            Leaf(Slot::ControllerHints, {0.54F, 0.89F, 0.4F, 0.08F}),
        });
    case Preset::CompactGrid:
        return Overlay({
            Leaf(Slot::CollectionTabs, {0.04F, 0.03F, 0.62F, 0.09F}),
            Leaf(Slot::GameRail, {0.04F, 0.14F, 0.66F, 0.7F}, Orientation::Vertical),
            Leaf(Slot::DetailsPanel, {0.73F, 0.2F, 0.23F, 0.44F}),
            Leaf(Slot::SourceStatus, {0.73F, 0.07F, 0.23F, 0.09F}),
            Leaf(Slot::OperationStatus, {0.73F, 0.66F, 0.23F, 0.18F}),
            Leaf(Slot::ControllerHints, {0.5F, 0.89F, 0.46F, 0.08F}),
        });
    }
    return {};
}

Branch ChooseBranch(const Rect workArea) noexcept {
    if (workArea.width < 960.0F || workArea.height < 540.0F) return Branch::Compact;
    if (workArea.width >= 1280.0F && workArea.height >= 720.0F) return Branch::Wide;
    return Branch::Standard;
}

bool Finite(const float value) noexcept { return std::isfinite(value); }

bool ValidNormalized(const NormalizedRect& value) noexcept {
    return Finite(value.x) && Finite(value.y) && Finite(value.width) && Finite(value.height) &&
        value.x >= 0.0F && value.y >= 0.0F && value.width > 0.0F && value.height > 0.0F &&
        value.x + value.width <= 1.00001F && value.y + value.height <= 1.00001F;
}

Rect Compose(const Rect parent, const RecipeNode& node) noexcept {
    Rect result{
        parent.x + parent.width * node.region.x,
        parent.y + parent.height * node.region.y,
        parent.width * node.region.width,
        parent.height * node.region.height,
    };
    result.x += result.width * node.insets.left;
    result.y += result.height * node.insets.top;
    result.width *= 1.0F - node.insets.left - node.insets.right;
    result.height *= 1.0F - node.insets.top - node.insets.bottom;
    return result;
}

bool Contains(const Rect parent, const Rect child) noexcept {
    return child.x >= parent.x - 0.01F && child.y >= parent.y - 0.01F &&
        child.width > 0.0F && child.height > 0.0F &&
        child.x + child.width <= parent.x + parent.width + 0.01F &&
        child.y + child.height <= parent.y + parent.height + 0.01F;
}

bool Overlaps(const Rect left, const Rect right) noexcept {
    return left.x < right.x + right.width && right.x < left.x + left.width &&
        left.y < right.y + right.height && right.y < left.y + left.height;
}

bool IsInteractive(const Slot slot) noexcept { return slot != Slot::HeroBackground; }

bool MinimumExtent(const SlotPlacement& placement, const float textScale) noexcept {
    switch (placement.slot) {
    case Slot::GameRail:
        return placement.bounds.width >= 120.0F && placement.bounds.height >= 88.0F;
    case Slot::ControllerHints:
        return placement.bounds.width >= 120.0F * textScale &&
            placement.bounds.height >= 32.0F * textScale;
    case Slot::DetailsPanel:
        return placement.bounds.width >= 96.0F * textScale &&
            placement.bounds.height >= 72.0F * textScale;
    case Slot::SourceStatus:
        return placement.bounds.width >= 72.0F * textScale &&
            placement.bounds.height >= 30.0F * textScale;
    default: return placement.bounds.width > 0.0F && placement.bounds.height > 0.0F;
    }
}

LayoutResult TryResolve(
    const Recipe& recipe,
    const Preset preset,
    const Branch branch,
    const Rect workArea,
    const float textScale) {
    LayoutResult result;
    result.branch = branch;
    result.preset = preset;
    if (recipe.schemaVersion != 1) {
        result.diagnostics.push_back({"$.schemaVersion", "unsupported_version"});
        return result;
    }
    const auto selected = recipe.branches.find(branch);
    if (selected == recipe.branches.end()) {
        result.diagnostics.push_back({"$.branches", "missing_branch"});
        return result;
    }
    std::set<Slot> slots;
    std::size_t paintOrder{};
    const auto visit = [&](const auto& self, const RecipeNode& node, const Rect parent,
                           const std::string& path, const int depth) -> void {
        if (depth > 16 || result.paintPlacements.size() > 128) {
            result.diagnostics.push_back({path, "layout_bound"});
            return;
        }
        if (!ValidNormalized(node.region) || node.insets.left < 0 || node.insets.top < 0 ||
            node.insets.right < 0 || node.insets.bottom < 0 ||
            node.insets.left + node.insets.right >= 1.0F ||
            node.insets.top + node.insets.bottom >= 1.0F) {
            result.diagnostics.push_back({path + ".region", "out_of_bounds"});
            return;
        }
        if (node.type == Primitive::Grid &&
            (!node.rows || !node.columns || *node.rows < 1 || *node.rows > 12 ||
             *node.columns < 1 || *node.columns > 12))
            result.diagnostics.push_back({path, "invalid_grid"});
        if (node.type == Primitive::Inset && node.children.size() != 1)
            result.diagnostics.push_back({path, "invalid_inset"});
        if (node.children.size() > 32)
            result.diagnostics.push_back({path + ".children", "too_many_children"});
        const auto bounds = Compose(parent, node);
        if (!Contains(workArea, bounds)) result.diagnostics.push_back({path, "out_of_bounds"});
        if (node.slot) {
            if (!node.children.empty()) result.diagnostics.push_back({path, "slot_has_children"});
            if (!slots.insert(*node.slot).second) result.diagnostics.push_back({path, "slot_reuse"});
            result.paintPlacements.push_back(
                {*node.slot, bounds, node.orientation, node.surface, paintOrder++});
        } else if (node.children.empty()) {
            result.diagnostics.push_back({path, "empty_container"});
        }
        for (std::size_t index = 0; index < node.children.size(); ++index)
            self(self, node.children[index], bounds,
                 path + ".children[" + std::to_string(index) + "]", depth + 1);
    };
    visit(visit, selected->second, workArea, "$.branches.root", 0);
    for (const auto critical : kCritical)
        if (!slots.contains(critical))
            result.diagnostics.push_back({"$.branches", "missing_critical_slot"});
    for (const auto& placement : result.paintPlacements)
        if (!MinimumExtent(placement, textScale))
            result.diagnostics.push_back({std::string{"$.slots."} + std::string{SlotName(placement.slot)},
                                          "clipped_focus_extent"});
    for (std::size_t first = 0; first < result.paintPlacements.size(); ++first) {
        if (!IsInteractive(result.paintPlacements[first].slot)) continue;
        for (std::size_t second = first + 1; second < result.paintPlacements.size(); ++second) {
            if (IsInteractive(result.paintPlacements[second].slot) &&
                Overlaps(result.paintPlacements[first].bounds, result.paintPlacements[second].bounds))
                result.diagnostics.push_back({"$.slots", "invalid_overlap"});
        }
    }
    if (!result.diagnostics.empty()) return result;
    for (const auto slot : kSemanticOrder) {
        const auto found = std::find_if(result.paintPlacements.begin(), result.paintPlacements.end(),
            [slot](const SlotPlacement& item) { return item.slot == slot; });
        if (found != result.paintPlacements.end()) result.semanticPlacements.push_back(*found);
    }
    return result;
}

} // namespace

bool LayoutResult::valid() const noexcept { return diagnostics.empty(); }

const SlotPlacement* LayoutResult::Find(const Slot slot) const noexcept {
    const auto found = std::find_if(paintPlacements.begin(), paintPlacements.end(),
        [slot](const SlotPlacement& item) { return item.slot == slot; });
    return found == paintPlacements.end() ? nullptr : &*found;
}

Recipe BuiltInRecipe(const Preset preset) {
    Recipe recipe;
    recipe.branches.emplace(Branch::Compact, Profile(preset, Branch::Compact));
    recipe.branches.emplace(Branch::Standard, Profile(preset, Branch::Standard));
    recipe.branches.emplace(Branch::Wide, Profile(preset, Branch::Wide));
    return recipe;
}

LayoutResult ResolveLayout(
    const Recipe* recipe,
    const Preset preset,
    const Rect workArea,
    const float textScale) {
    const auto branch = ChooseBranch(workArea);
    if (recipe) {
        auto selected = TryResolve(*recipe, preset, branch, workArea, textScale);
        if (selected.valid()) return selected;
        auto rejected = std::move(selected.diagnostics);
        auto fallback = TryResolve(BuiltInRecipe(preset), preset, branch, workArea, textScale);
        fallback.usedFallback = true;
        fallback.fallbackDiagnostics = std::move(rejected);
        return fallback;
    }
    auto fallback = TryResolve(BuiltInRecipe(preset), preset, branch, workArea, textScale);
    return fallback;
}

std::string_view SlotName(const Slot slot) noexcept {
    switch (slot) {
    case Slot::HeroBackground: return "hero-background";
    case Slot::GameRail: return "game-rail";
    case Slot::DetailsPanel: return "details-panel";
    case Slot::CollectionTabs: return "collection-tabs";
    case Slot::SourceStatus: return "source-status";
    case Slot::OperationStatus: return "operation-status";
    case Slot::SystemStatus: return "system-status";
    case Slot::ControllerHints: return "controller-hints";
    }
    return "unknown";
}

} // namespace gba::launcher
