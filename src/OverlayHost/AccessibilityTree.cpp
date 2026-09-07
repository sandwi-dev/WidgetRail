#include "AccessibilityTree.h"

#include <algorithm>
#include <climits>
#include <map>
#include <string_view>

namespace widgetrail::accessibility {
namespace {

Tree TreeMetadata(const Tree& source) {
    Tree result;
    result.widgetId = source.widgetId;
    result.runtimeGeneration = source.runtimeGeneration;
    result.snapshotSequence = source.snapshotSequence;
    result.activeInputScopeId = source.activeInputScopeId;
    result.name = source.name;
    return result;
}

std::optional<Role> ResolveRole(const WidgetNode& node) noexcept {
    if (node.isSelect) return Role::ComboBox;
    if (node.kind == L"button" || node.kind == L"actionSurface" ||
        node.kind == L"textEntry") return Role::Button;
    if (node.kind == L"slider") return Role::Slider;
    if (node.kind == L"text") return Role::Text;
    if (node.kind == L"image" || node.kind == L"icon" ||
        node.kind == L"mediaViewport") return Role::Image;
    if (node.kind == L"progress" || node.kind == L"loadingIndicator") return Role::Progress;
    return std::nullopt;
}

std::wstring_view AccessibleName(const WidgetNode& node) noexcept {
    if (!node.accessibilityLabel.empty()) return node.accessibilityLabel;
    return node.text;
}

} // namespace

WindowTreePartition PartitionForFixedChrome(
    const Tree& source,
    const float guideTop,
    const float chromeOriginX,
    const float chromeOriginY) {
    WindowTreePartition result{TreeMetadata(source), TreeMetadata(source)};
    std::vector<std::optional<std::size_t>> contentRemap(source.nodes.size());
    std::vector<std::optional<std::size_t>> chromeRemap(source.nodes.size());

    for (std::size_t index = 0; index < source.nodes.size(); ++index) {
        const auto& node = source.nodes[index];
        const bool chrome = node.domain == ElementDomain::Tray ||
            (node.domain == ElementDomain::HostShell && node.bounds.y >= guideTop);
        auto& tree = chrome ? result.chrome : result.content;
        auto& remap = chrome ? chromeRemap : contentRemap;
        remap[index] = tree.nodes.size();
        auto copy = node;
        copy.parent.reset();
        copy.children.clear();
        if (chrome) {
            copy.bounds.x -= chromeOriginX;
            copy.bounds.y -= chromeOriginY;
        }
        tree.nodes.push_back(std::move(copy));
    }

    const auto reconnect = [&](Tree& tree,
                               const std::vector<std::optional<std::size_t>>& remap) {
        for (std::size_t index = 0; index < source.nodes.size(); ++index) {
            if (!remap[index]) continue;
            auto& copy = tree.nodes[*remap[index]];
            const auto& original = source.nodes[index];
            if (original.parent && *original.parent < remap.size() &&
                remap[*original.parent]) {
                copy.parent = *remap[*original.parent];
            }
            for (const auto child : original.children) {
                if (child < remap.size() && remap[child])
                    copy.children.push_back(*remap[child]);
            }
        }
        if (source.focusedNode && *source.focusedNode < remap.size() &&
            remap[*source.focusedNode]) {
            tree.focusedNode = *remap[*source.focusedNode];
        }
    };
    reconnect(result.content, contentRemap);
    reconnect(result.chrome, chromeRemap);
    return result;
}

Tree BuildWidgetTree(
    std::wstring widgetId,
    std::wstring runtimeGeneration,
    const WidgetSnapshot& snapshot,
    const RenderResult& render,
    const std::wstring_view focusedElementId,
    const std::map<std::wstring, double, std::less<>>& presentedSliderValues,
    const SelectPopupAccessibility* selectPopup,
    const std::wstring_view activeSliderElementId) {
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
    struct VirtualSetPosition final {
        int position{};
        int size{};
    };
    std::map<const WidgetNode*, VirtualSetPosition> virtualPositions;
    const auto collectVirtualPositions = [&](const auto& self,
                                             const WidgetNode& node) -> void {
        if (node.virtualCollectionWindow &&
            node.virtualCollectionWindow->firstItemIndex) {
            std::vector<const WidgetNode*> items;
            const auto collectItems = [&](const auto& collect,
                                          const WidgetNode& current,
                                          const bool root) -> void {
                if (!root && current.kind == L"scroll") return;
                if (!current.collectionItemKey.empty()) {
                    items.push_back(&current);
                    return;
                }
                for (const auto& child : current.children)
                    collect(collect, child, false);
            };
            collectItems(collectItems, node, true);
            const auto first = *node.virtualCollectionWindow->firstItemIndex;
            const auto total = node.virtualCollectionWindow->totalItemCount.value_or(0);
            for (std::size_t index = 0; index < items.size(); ++index) {
                const auto position = first + index + 1U;
                if (position <= static_cast<std::uint64_t>(INT_MAX) &&
                    total <= static_cast<std::uint64_t>(INT_MAX)) {
                    virtualPositions.emplace(items[index], VirtualSetPosition{
                        static_cast<int>(position), static_cast<int>(total)});
                }
            }
        }
        for (const auto& child : node.children) self(self, child);
    };
    collectVirtualPositions(collectVirtualPositions, snapshot.root);

    const auto visit = [&](const auto& self,
                           const WidgetNode& source,
                           const std::wstring_view inheritedScope,
                           const std::optional<std::size_t> accessibleParent,
                           const std::optional<VirtualSetPosition> inheritedSet) -> void {
        const std::wstring_view scope = !source.inputScopeId.empty()
            ? std::wstring_view{source.inputScopeId}
            : inheritedScope.empty() ? std::wstring_view{source.id} : inheritedScope;
        const auto role = ResolveRole(source);
        const auto region = regions.find(source.id);
        const bool inActiveScope = scope == snapshot.activeInputScopeId;
        const bool exposed = inActiveScope && role && region != regions.end() &&
            !AccessibleName(source).empty();

        auto parent = accessibleParent;
        const auto ownSet = virtualPositions.find(&source);
        const auto setPosition = ownSet != virtualPositions.end()
            ? std::optional<VirtualSetPosition>{ownSet->second}
            : inheritedSet;
        if (exposed) {
            Node node;
            node.id = source.id;
            node.name = AccessibleName(source);
            node.value = source.accessibilityValue;
            if (source.kind == L"slider" && source.id == focusedElementId) {
                const auto instruction = source.id == activeSliderElementId
                    ? L"Adjustment active."
                    : source.sliderInteractionMode == L"activateToAdjust"
                        ? L"Press A to adjust."
                        : L"Adjustment active.";
                if (!node.value.empty()) node.value.append(L" ");
                node.value.append(instruction);
            }
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
            if (source.isSelect) {
                const bool hasAvailableOption = std::any_of(
                    source.selectOptions.begin(), source.selectOptions.end(),
                    [](const WidgetSelectOption& option) {
                        return !option.isDisabled && !option.isBusy;
                    });
                node.enabled = node.enabled && hasAvailableOption;
                const bool expanded = selectPopup &&
                    selectPopup->openerElementId == source.id;
                node.expanded = expanded && node.enabled;
                node.hostAction = !node.enabled ? HostAction::None
                    : node.expanded ? HostAction::CollapseSelect
                                    : HostAction::ExpandSelect;
                node.hostTargetId = source.id;
            }
            node.focused = source.id == focusedElementId && !node.expanded;
            if (setPosition) {
                node.positionInSet = setPosition->position;
                node.sizeOfSet = setPosition->size;
            }
            const auto index = tree.nodes.size();
            tree.nodes.push_back(std::move(node));
            if (accessibleParent) tree.nodes[*accessibleParent].children.push_back(index);
            if (tree.nodes[index].focused) tree.focusedNode = index;
            parent = index;

            if (source.isSelect) {
                const bool popupCurrent = tree.nodes[index].expanded && selectPopup &&
                    selectPopup->openerElementId == source.id;
                const auto& projectedOptions = popupCurrent
                    ? selectPopup->options : source.selectOptions;
                for (std::size_t optionIndexValue = 0;
                     optionIndexValue < projectedOptions.size();
                     ++optionIndexValue) {
                    const auto& option = projectedOptions[optionIndexValue];
                    if (!popupCurrent && !option.isSelected) continue;
                    std::optional<declarative::Rect> visibleBounds;
                    if (popupCurrent) {
                        const auto visible = std::find_if(
                            selectPopup->items.begin(), selectPopup->items.end(),
                            [&](const SelectPopupAccessibility::Item& item) {
                                return item.optionIndex == optionIndexValue;
                            });
                        if (visible != selectPopup->items.end())
                            visibleBounds = visible->bounds;
                    }
                    Node optionNode;
                    optionNode.id = L"select-option:" +
                        std::to_wstring(source.id.size()) + L":" + source.id +
                        std::to_wstring(option.id.size()) + L":" + option.id;
                    optionNode.name = option.accessibilityLabel.empty()
                        ? option.label : option.accessibilityLabel;
                    optionNode.value = option.label;
                    if (popupCurrent) optionNode.actionId = option.actionId;
                    optionNode.hostTargetId = source.id;
                    optionNode.bounds = visibleBounds.value_or(declarative::Rect{});
                    optionNode.role = Role::ListItem;
                    optionNode.domain = ElementDomain::WidgetOption;
                    optionNode.hostAction = popupCurrent
                        ? HostAction::CommitSelectOption : HostAction::None;
                    optionNode.parent = index;
                    optionNode.enabled = !option.isDisabled && !option.isBusy;
                    optionNode.selected = option.isSelected;
                    optionNode.focused = popupCurrent &&
                        source.id == focusedElementId &&
                        optionIndexValue == selectPopup->highlightedOption;
                    optionNode.offscreen = !visibleBounds.has_value();
                    optionNode.keyboardFocusable = popupCurrent && optionNode.enabled;
                    optionNode.positionInSet =
                        static_cast<int>(optionIndexValue + 1U);
                    optionNode.sizeOfSet =
                        static_cast<int>(projectedOptions.size());
                    const auto optionIndex = tree.nodes.size();
                    tree.nodes.push_back(std::move(optionNode));
                    tree.nodes[index].children.push_back(optionIndex);
                    if (tree.nodes[optionIndex].focused)
                        tree.focusedNode = optionIndex;
                }
            }
        }

        // An action surface is one semantic control. Its validated descendants
        // are visual content and must not be announced a second time.
        if (exposed && source.kind == L"actionSurface") return;
        const auto childSetPosition = exposed && setPosition
            ? std::optional<VirtualSetPosition>{}
            : setPosition;
        for (const auto& child : source.children)
            self(self, child, scope, parent, childSetPosition);
    };
    visit(visit, snapshot.root, std::wstring_view{}, std::nullopt, std::nullopt);
    return tree;
}

} // namespace widgetrail::accessibility
