#pragma once

#include "WidgetProtocolPresentationContract.generated.h"

#include <optional>
#include <string_view>
#include <vector>

namespace widgetrail::input {

enum class ControllerShortcutResolutionStatus {
    NoMatch,
    Resolved,
    OwnerUnavailable,
    FocusNotFound,
};

enum class AuthoredHeldActionDecisionPhase {
    Arm,
    InitialDispatch,
    Repeat,
};

enum class AuthoredHeldActionDisposition { Dispatch, Retire };

struct AuthoredHeldActionBindingView final {
    std::wstring_view focusElementId;
    std::wstring_view sourceElementId;
    std::wstring_view actionId;
};

struct AuthoredHeldActionDecision final {
    AuthoredHeldActionDisposition disposition{
        AuthoredHeldActionDisposition::Retire};
    std::optional<std::wstring_view> transportFocus;
    bool focusedNodeHandling{};
};

[[nodiscard]] inline AuthoredHeldActionDecision DecideAuthoredHeldAction(
    const AuthoredHeldActionDecisionPhase phase,
    const bool selectPopupOpen,
    const AuthoredHeldActionBindingView captured,
    const std::optional<AuthoredHeldActionBindingView> current = std::nullopt) {
    if (selectPopupOpen) return {};
    if (phase == AuthoredHeldActionDecisionPhase::Repeat &&
        (!current || current->focusElementId != captured.focusElementId ||
         current->sourceElementId != captured.sourceElementId ||
         current->actionId != captured.actionId))
        return {};
    return {
        AuthoredHeldActionDisposition::Dispatch,
        captured.focusElementId.empty()
            ? std::nullopt
            : std::optional<std::wstring_view>{captured.focusElementId},
        !captured.focusElementId.empty(),
    };
}

template <typename Node>
struct ControllerShortcutResolution final {
    ControllerShortcutResolutionStatus status{ControllerShortcutResolutionStatus::NoMatch};
    const Node* owner{};
    std::wstring_view actionId;
    std::wstring_view repeatPolicy;
};

template <typename Node>
[[nodiscard]] bool FindControllerShortcutPath(
    const Node& scopeRoot,
    const std::optional<std::wstring_view> focusedElementId,
    std::vector<const Node*>& path) {
    path.clear();
    if (!focusedElementId) {
        path.push_back(&scopeRoot);
        return true;
    }

    const auto findPath = [&](auto&& self, const Node& node,
                              const bool isScopeRoot) -> bool {
        if (!isScopeRoot && !node.inputScopeId.empty()) return false;
        path.push_back(&node);
        if (node.id == *focusedElementId) return true;
        for (const auto& child : node.children) {
            if (self(self, child, false)) return true;
        }
        path.pop_back();
        return false;
    };
    return findPath(findPath, scopeRoot, true);
}

/// Resolves one authored shortcut inside one admitted input scope. The nearest
/// declaring node owns the binding and its disabled/busy availability.
template <typename Node>
[[nodiscard]] ControllerShortcutResolution<Node> ResolveControllerShortcut(
    const Node& scopeRoot,
    const std::optional<std::wstring_view> focusedElementId,
    const std::wstring_view button,
    const std::wstring_view phase) {
    std::vector<const Node*> path;
    if (!FindControllerShortcutPath(scopeRoot, focusedElementId, path))
        return {ControllerShortcutResolutionStatus::FocusNotFound};

    for (auto cursor = path.rbegin(); cursor != path.rend(); ++cursor) {
        for (const auto& shortcut : (*cursor)->shortcuts) {
            if (!protocol_contract::ControllerShortcutMatches(
                    shortcut.button, shortcut.phase, shortcut.repeatPolicy,
                    button, phase)) continue;
            return protocol_contract::ControllerShortcutOwnerAvailable(
                       (*cursor)->isDisabled, (*cursor)->isBusy)
                ? ControllerShortcutResolution<Node>{
                      ControllerShortcutResolutionStatus::Resolved, *cursor,
                      shortcut.actionId, shortcut.repeatPolicy}
                : ControllerShortcutResolution<Node>{
                      ControllerShortcutResolutionStatus::OwnerUnavailable, *cursor,
                      shortcut.actionId, shortcut.repeatPolicy};
        }
    }
    return {ControllerShortcutResolutionStatus::NoMatch};
}

/// Resolves the host's exact raw focus authority. A genuinely empty focus may
/// consult only the scope root. A nonempty focus must still belong to the
/// active scope and remain the exact visible target; responsive fallback may
/// not silently rebind a shortcut to another node.
template <typename Node>
[[nodiscard]] ControllerShortcutResolution<Node> ResolveHostControllerShortcut(
    const Node& scopeRoot,
    const std::optional<std::wstring_view> focusedElementId,
    const std::optional<std::wstring_view> visibleFocusedElementId,
    const std::wstring_view button,
    const std::wstring_view phase) {
    if (focusedElementId) {
        std::vector<const Node*> path;
        if (!FindControllerShortcutPath(scopeRoot, focusedElementId, path) ||
            !visibleFocusedElementId ||
            *visibleFocusedElementId != *focusedElementId)
            return {ControllerShortcutResolutionStatus::FocusNotFound};
    }
    return ResolveControllerShortcut(
        scopeRoot, focusedElementId, button, phase);
}

template <typename Node>
[[nodiscard]] const Node* FindControllerShortcutScopeRoot(
    const Node& node,
    const std::wstring_view scopeId,
    const bool root = true) noexcept {
    if ((root || !node.inputScopeId.empty()) &&
        (node.inputScopeId.empty() ? std::wstring_view{node.id}
                                   : std::wstring_view{node.inputScopeId}) == scopeId)
        return &node;
    for (const auto& child : node.children) {
        if (const auto* found = FindControllerShortcutScopeRoot(child, scopeId, false))
            return found;
    }
    return nullptr;
}

} // namespace widgetrail::input
