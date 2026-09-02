#include "ControllerGuide.h"
#include "ControllerShortcutResolver.h"

#include <algorithm>
#include <cmath>

namespace widgetrail::guide {
namespace {

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

std::wstring Sanitize(const std::wstring_view value) {
    std::wstring result;
    result.reserve(value.size());
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
    }
    while (!result.empty() && result.back() == L' ') result.pop_back();
    return result;
}

std::wstring BuildContextual(
    const std::vector<OpenWidgetAction>& actions,
    const std::vector<std::wstring>& labels) {
    std::wstring result;
    for (std::size_t index = 0; index < actions.size(); ++index) {
        if (!result.empty()) result += L"   ";
        result += DisplayButton(actions[index].button);
        result += L' ';
        result += labels[index];
    }
    return result;
}

bool Fits(
    const std::wstring_view value,
    const float availableWidth,
    const MeasureOpenWidgetText& measureText) {
    if (value.empty()) return true;
    if (!std::isfinite(availableWidth) || availableWidth <= 0.0F || !measureText)
        return false;
    const auto measured = measureText(value);
    return measured && std::isfinite(*measured) && *measured <= availableWidth;
}

} // namespace

OpenWidgetAuthority ResolveOpenWidgetAuthority(
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId) {
    OpenWidgetAuthority result;
    if (snapshot.activeInputScopeId.empty()) return result;
    const auto* scopeRoot = widgetrail::input::FindControllerShortcutScopeRoot(
        snapshot.root, snapshot.activeInputScopeId);
    if (!scopeRoot) return result;

    std::vector<const WidgetNode*> path;
    const std::optional<std::wstring_view> shortcutFocus =
        focusedElementId.empty()
            ? std::nullopt
            : std::optional<std::wstring_view>{focusedElementId};
    if (!widgetrail::input::FindControllerShortcutPath(
            *scopeRoot, shortcutFocus, path)) {
        return result;
    }

    const auto* focused = focusedElementId.empty() ? nullptr : path.back();
    const bool selectOptionAvailable = focused && focused->isSelect &&
        std::ranges::any_of(focused->selectOptions, [](const auto& option) {
            return !option.isDisabled && !option.isBusy;
        });
    result.focusedActivation = focused && !focused->isDisabled && !focused->isBusy &&
        (!focused->actionId.empty() || selectOptionAvailable) &&
        (focused->kind == L"button" || focused->kind == L"slider" ||
         focused->kind == L"actionSurface");

    std::vector<std::wstring> consumedButtons;
    for (auto cursor = path.rbegin(); cursor != path.rend(); ++cursor) {
        for (const auto& shortcut : (*cursor)->shortcuts) {
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
            const auto resolved = widgetrail::input::ResolveControllerShortcut(
                *scopeRoot, shortcutFocus, shortcut.button, L"pressed");
            if (resolved.status != widgetrail::input::
                    ControllerShortcutResolutionStatus::Resolved ||
                !resolved.owner)
                continue;
            const auto& owner = *resolved.owner;
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
    const bool hasBack,
    const float availableWidth,
    const MeasureOpenWidgetText& measureText) {
    OpenWidgetLine result;
    result.host = hasBack ? L"B Back   Guide Close" : L"Guide Close";
    const auto reserved = measureText
        ? measureText(result.host + L"   ")
        : std::nullopt;
    const float contextualWidth = reserved && std::isfinite(*reserved)
        ? std::max(0.0F, availableWidth - *reserved)
        : 0.0F;
    const std::size_t actionBudget =
        density == ControllerGuideDensity::Full ? 4U :
        density == ControllerGuideDensity::Compact ? 3U : 0U;

    std::vector<OpenWidgetAction> actions;
    actions.reserve(actionBudget);
    for (const auto& action : authority.actions) {
        if (actions.size() >= actionBudget) break;
        const auto label = Sanitize(action.label);
        if (!label.empty()) actions.push_back({action.button, label});
    }
    std::vector<std::wstring> labels;
    labels.reserve(actions.size());
    for (const auto& action : actions) labels.push_back(action.label);

    while (!actions.empty()) {
        const auto line = BuildContextual(actions, labels);
        if (Fits(line, contextualWidth, measureText)) {
            result.contextual = line;
            break;
        }
        actions.pop_back();
        labels.pop_back();
    }

    if (authority.focusedActivation && actions.size() < actionBudget) {
        auto withActivation = result.contextual;
        if (!withActivation.empty()) withActivation += L"   ";
        withActivation += L"A Select";
        if (Fits(withActivation, contextualWidth, measureText))
            result.contextual = std::move(withActivation);
    }

    result.accessible = result.contextual;
    if (!result.accessible.empty()) result.accessible += L"   ";
    result.accessible += result.host;
    return result;
}

} // namespace widgetrail::guide
