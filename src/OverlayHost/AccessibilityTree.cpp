#include "AccessibilityTree.h"

#include <algorithm>
#include <map>
#include <string_view>

namespace gba::accessibility {
namespace {

std::optional<Role> ResolveRole(const WidgetNode& node) noexcept {
    if (node.kind == L"button" || node.kind == L"actionSurface" ||
        node.kind == L"textEntry") return Role::Button;
    if (node.kind == L"slider") return Role::Slider;
    if (node.kind == L"text") return Role::Text;
    if (node.kind == L"image" || node.kind == L"icon") return Role::Image;
    if (node.kind == L"progress" || node.kind == L"loadingIndicator") return Role::Progress;
    return std::nullopt;
}

std::wstring_view AccessibleName(const WidgetNode& node) noexcept {
    if (!node.accessibilityLabel.empty()) return node.accessibilityLabel;
    return node.text;
}

} // namespace

Tree BuildWidgetTree(
    std::wstring widgetId,
    std::wstring runtimeGeneration,
    const WidgetSnapshot& snapshot,
    const RenderResult& render,
    const std::wstring_view focusedElementId,
    const std::map<std::wstring, double, std::less<>>& presentedSliderValues) {
    Tree tree{
        std::move(widgetId),
        std::move(runtimeGeneration),
        snapshot.sequence,
        snapshot.activeInputScopeId,
    };
    std::map<std::wstring_view, declarative::Rect, std::less<>> regions;
    for (const auto& region : render.accessibilityRegions) {
        if (!region.nodeId.empty() && region.rect.width > 0.5F && region.rect.height > 0.5F)
            regions.insert_or_assign(region.nodeId, region.rect);
    }

    const auto visit = [&](const auto& self,
                           const WidgetNode& source,
                           const std::wstring_view inheritedScope,
                           const std::optional<std::size_t> accessibleParent) -> void {
        const std::wstring_view scope = !source.inputScopeId.empty()
            ? std::wstring_view{source.inputScopeId}
            : inheritedScope.empty() ? std::wstring_view{source.id} : inheritedScope;
        const auto role = ResolveRole(source);
        const auto region = regions.find(source.id);
        const bool inActiveScope = scope == snapshot.activeInputScopeId;
        const bool exposed = inActiveScope && role && region != regions.end() &&
            !AccessibleName(source).empty();

        auto parent = accessibleParent;
        if (exposed) {
            Node node;
            node.id = source.id;
            node.name = AccessibleName(source);
            node.value = source.accessibilityValue;
            node.actionId = source.actionId;
            node.valueChangedActionId = source.valueChangedActionId;
            node.bounds = region->second;
            node.role = *role;
            node.parent = accessibleParent;
            const auto presentedValue = presentedSliderValues.find(source.id);
            node.rangeValue = source.kind == L"slider" &&
                    presentedValue != presentedSliderValues.end()
                ? presentedValue->second
                : source.value;
            node.rangeMinimum = source.minimum;
            node.rangeMaximum = source.maximum;
            node.rangeStep = source.step;
            node.enabled = !source.isDisabled && !source.isBusy;
            node.selected = source.isSelected;
            node.focused = source.id == focusedElementId;
            const auto index = tree.nodes.size();
            tree.nodes.push_back(std::move(node));
            if (accessibleParent) tree.nodes[*accessibleParent].children.push_back(index);
            if (tree.nodes[index].focused) tree.focusedNode = index;
            parent = index;
        }

        // An action surface is one semantic control. Its validated descendants
        // are visual content and must not be announced a second time.
        if (exposed && source.kind == L"actionSurface") return;
        for (const auto& child : source.children) self(self, child, scope, parent);
    };
    visit(visit, snapshot.root, std::wstring_view{}, std::nullopt);
    return tree;
}

} // namespace gba::accessibility
