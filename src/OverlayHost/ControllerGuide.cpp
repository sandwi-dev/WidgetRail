#include "ControllerGuide.h"

#include <algorithm>

namespace widgetrail::guide {
namespace {

const WidgetNode* FindScopeRoot(
    const WidgetNode& node,
    const std::wstring_view scopeId,
    const bool documentRoot = true) noexcept {
    if ((documentRoot || !node.inputScopeId.empty()) &&
        (node.inputScopeId.empty() ? std::wstring_view{node.id}
                                   : std::wstring_view{node.inputScopeId}) == scopeId)
        return &node;
    for (const auto& child : node.children) {
        if (const auto* found = FindScopeRoot(child, scopeId, false)) return found;
    }
    return nullptr;
}

bool FindPathInScope(
    const WidgetNode& node,
    const std::wstring_view targetId,
    const bool scopeRoot,
    std::vector<const WidgetNode*>& path) {
    if (!scopeRoot && !node.inputScopeId.empty()) return false;
    path.push_back(&node);
    if (node.id == targetId) return true;
    for (const auto& child : node.children) {
        if (FindPathInScope(child, targetId, false, path)) return true;
    }
    path.pop_back();
    return false;
}

std::wstring_view QuickActionLabel(
    const WidgetSnapshot& snapshot,
    const WidgetShortcut& shortcut) noexcept {
    const auto match = std::find_if(
        snapshot.quickActions.begin(), snapshot.quickActions.end(),
        [&](const WidgetQuickAction& action) {
            return action.button == shortcut.button &&
                action.actionId == shortcut.actionId && !action.label.empty();
        });
    return match == snapshot.quickActions.end()
        ? std::wstring_view{}
        : std::wstring_view{match->label};
}

int ButtonPriority(const std::wstring_view button) noexcept {
    if (button == L"x") return 0;
    if (button == L"leftTrigger") return 1;
    if (button == L"rightTrigger") return 2;
    if (button == L"leftBumper") return 3;
    if (button == L"rightBumper") return 4;
    if (button == L"y") return 5;
    if (button == L"menu") return 6;
    if (button == L"view") return 7;
    if (button == L"leftStick") return 8;
    if (button == L"rightStick") return 9;
    return 10;
}

std::wstring_view DisplayButton(const std::wstring_view button) noexcept {
    if (button == L"leftBumper") return L"LB";
    if (button == L"rightBumper") return L"RB";
    if (button == L"leftTrigger") return L"LT";
    if (button == L"rightTrigger") return L"RT";
    if (button == L"leftStick") return L"LS";
    if (button == L"rightStick") return L"RS";
    if (button == L"a") return L"A";
    if (button == L"x") return L"X";
    if (button == L"y") return L"Y";
    if (button == L"menu") return L"Menu";
    if (button == L"view") return L"View";
    return button;
}

std::wstring Sanitize(
    const std::wstring_view value,
    const std::size_t limit) {
    std::wstring result;
    result.reserve(std::min(value.size(), limit));
    bool priorSpace = false;
    for (const wchar_t character : value) {
        const bool whitespace = character == L' ' || character == L'\t' ||
                                character == L'\r' || character == L'\n';
        if (whitespace) {
            if (!result.empty() && !priorSpace) result.push_back(L' ');
            priorSpace = true;
        } else {
            result.push_back(character);
            priorSpace = false;
        }
        if (result.size() >= limit) break;
    }
    while (!result.empty() && result.back() == L' ') result.pop_back();
    if (value.size() > result.size() && result.size() >= 2) result.back() = L'…';
    return result;
}

} // namespace

OpenWidgetAuthority ResolveOpenWidgetAuthority(
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId) {
    OpenWidgetAuthority result;
    if (snapshot.activeInputScopeId.empty()) return result;
    const auto* scopeRoot = FindScopeRoot(
        snapshot.root, snapshot.activeInputScopeId);
    if (!scopeRoot) return result;

    std::vector<const WidgetNode*> path;
    if (focusedElementId.empty()) {
        path.push_back(scopeRoot);
    } else if (!FindPathInScope(*scopeRoot, focusedElementId, true, path)) {
        return result;
    }

    const auto* focused = focusedElementId.empty() ? nullptr : path.back();
    result.focusedActivation = focused && !focused->isDisabled && !focused->isBusy &&
        !focused->actionId.empty() &&
        (focused->kind == L"button" || focused->kind == L"slider" ||
         focused->kind == L"actionSurface");

    std::vector<std::wstring> consumedButtons;
    for (auto cursor = path.rbegin(); cursor != path.rend(); ++cursor) {
        const auto& owner = **cursor;
        const bool availabilityGatesOwner =
            cursor == path.rbegin() || focusedElementId.empty();
        for (const auto& shortcut : owner.shortcuts) {
            if (shortcut.phase != L"pressed" || shortcut.actionId.empty() ||
                shortcut.button == L"a" || shortcut.button == L"b" ||
                shortcut.button == L"dPadLeft" || shortcut.button == L"dPadRight")
                continue;
            if (std::find(
                    consumedButtons.begin(), consumedButtons.end(),
                    shortcut.button) != consumedButtons.end())
                continue;
            // The first binding along the actual dispatch path owns the button,
            // even when unavailable or unlabeled; never advertise an ancestor's
            // different action as a fallback.
            consumedButtons.push_back(shortcut.button);
            if (availabilityGatesOwner && (owner.isDisabled || owner.isBusy))
                continue;
            const std::wstring_view label = !owner.text.empty()
                ? std::wstring_view{owner.text}
                : !owner.accessibilityLabel.empty()
                    ? std::wstring_view{owner.accessibilityLabel}
                    : QuickActionLabel(snapshot, shortcut);
            if (label.empty()) continue;
            result.actions.push_back({shortcut.button, std::wstring{label}});
        }
    }
    std::stable_sort(
        result.actions.begin(), result.actions.end(),
        [](const OpenWidgetAction& left, const OpenWidgetAction& right) {
            return ButtonPriority(left.button) < ButtonPriority(right.button);
        });
    return result;
}

OpenWidgetLine BuildOpenWidgetLine(
    const ControllerGuideDensity density,
    const OpenWidgetAuthority& authority,
    const bool hasBack) {
    OpenWidgetLine result;
    result.host = hasBack ? L"B Back   Guide Close" : L"Guide Close";
    const std::size_t characterBudget =
        density == ControllerGuideDensity::Full ? 92U :
        density == ControllerGuideDensity::Compact ? 78U : 28U;
    const std::size_t labelBudget =
        density == ControllerGuideDensity::Full ? 20U :
        density == ControllerGuideDensity::Compact ? 12U : 0U;
    const std::size_t actionBudget =
        density == ControllerGuideDensity::Full ? 4U :
        density == ControllerGuideDensity::Compact ? 3U : 0U;

    std::size_t accepted{};
    auto append = [&](const std::wstring_view button,
                      const std::wstring_view label) {
        if (accepted >= actionBudget) return;
        const auto displayButton = Sanitize(DisplayButton(button), 4U);
        const auto displayLabel = Sanitize(label, labelBudget);
        if (displayButton.empty() || displayLabel.empty()) return;
        const std::wstring segment = displayButton + L" " + displayLabel;
        const std::size_t projected = result.contextual.size() +
            (result.contextual.empty() ? 0U : 3U) + segment.size() + 3U +
            result.host.size();
        if (projected > characterBudget) return;
        if (!result.contextual.empty()) result.contextual += L"   ";
        result.contextual += segment;
        ++accepted;
    };
    for (const auto& action : authority.actions) append(action.button, action.label);
    if (authority.focusedActivation) append(L"a", L"Select");

    result.accessible = result.contextual;
    if (!result.accessible.empty()) result.accessible += L"   ";
    result.accessible += result.host;
    return result;
}

} // namespace widgetrail::guide
