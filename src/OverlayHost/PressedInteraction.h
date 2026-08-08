#pragma once

#include "WidgetBridgeClient.h"

#include <string>
#include <string_view>

namespace gba::input {

/// Host-owned visual state for one physical controller press. It is keyed to
/// the immutable widget snapshot that resolved the action, so focus changes,
/// worker replacement, and newer snapshots fail closed instead of leaving a
/// stale control painted as pressed.
class PressedInteractionState final {
public:
    [[nodiscard]] bool Begin(
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId,
        std::wstring_view protocolButton) {
        const auto* node = FindInActiveScope(snapshot, focusedElementId);
        if (!IsActionableForButton(node, protocolButton)) return false;

        const bool changed = widgetInstanceId_ != snapshot.instanceId ||
            snapshotSequence_ != snapshot.sequence ||
            elementId_ != focusedElementId || button_ != protocolButton;
        widgetInstanceId_ = snapshot.instanceId;
        snapshotSequence_ = snapshot.sequence;
        elementId_ = focusedElementId;
        button_ = protocolButton;
        return changed;
    }

    /// Clears only the matching physical button release. Unrelated simultaneous
    /// controller input cannot cancel the active visual.
    [[nodiscard]] bool Release(const std::wstring_view protocolButton) noexcept {
        if (button_ != protocolButton) return false;
        return Clear();
    }

    /// Action dispatch failures use the same exact-button cancellation seam.
    [[nodiscard]] bool Cancel(const std::wstring_view protocolButton) noexcept {
        return Release(protocolButton);
    }

    /// Snapshot replacement and focus navigation are authoritative. The host
    /// calls this after either changes and repaints when it returns true.
    [[nodiscard]] bool Reconcile(
        const WidgetSnapshot& snapshot,
        const std::wstring_view focusedElementId) noexcept {
        if (elementId_.empty()) return false;
        if (widgetInstanceId_ == snapshot.instanceId &&
            snapshotSequence_ == snapshot.sequence &&
            elementId_ == focusedElementId &&
            IsActionableForButton(
                FindInActiveScope(snapshot, focusedElementId), button_)) {
            return false;
        }
        return Clear();
    }

    [[nodiscard]] bool Clear() noexcept {
        if (elementId_.empty()) return false;
        widgetInstanceId_.clear();
        snapshotSequence_ = 0;
        elementId_.clear();
        button_.clear();
        return true;
    }

    [[nodiscard]] std::wstring_view ActiveElementId(
        const WidgetSnapshot& snapshot,
        const std::wstring_view focusedElementId) const noexcept {
        return !elementId_.empty() && widgetInstanceId_ == snapshot.instanceId &&
                snapshotSequence_ == snapshot.sequence &&
                elementId_ == focusedElementId
            ? std::wstring_view(elementId_)
            : std::wstring_view{};
    }

    [[nodiscard]] bool active() const noexcept { return !elementId_.empty(); }

private:
    [[nodiscard]] static const WidgetNode* FindInActiveScope(
        const WidgetSnapshot& snapshot,
        const std::wstring_view target) noexcept {
        if (target.empty() || snapshot.activeInputScopeId.empty()) return nullptr;
        return Find(
            snapshot.root, target, snapshot.activeInputScopeId, std::wstring_view{});
    }

    [[nodiscard]] static const WidgetNode* Find(
        const WidgetNode& node,
        const std::wstring_view target,
        const std::wstring_view activeScope,
        const std::wstring_view inheritedScope) noexcept {
        const std::wstring_view scope = !node.inputScopeId.empty()
            ? std::wstring_view(node.inputScopeId)
            : inheritedScope.empty() ? std::wstring_view(node.id) : inheritedScope;
        if (node.id == target) return scope == activeScope ? &node : nullptr;
        for (const auto& child : node.children) {
            if (const auto* found = Find(child, target, activeScope, scope)) return found;
        }
        return nullptr;
    }

    [[nodiscard]] static bool IsActionableForButton(
        const WidgetNode* node,
        const std::wstring_view protocolButton) noexcept {
        if (!node || node->isDisabled || node->isBusy || protocolButton.empty()) return false;
        if (protocolButton == L"a") {
            return (node->kind == L"button" || node->kind == L"slider" ||
                    node->kind == L"actionSurface") &&
                !node->actionId.empty();
        }
        if (node->kind == L"slider" && node->hasSliderRange &&
            !node->valueChangedActionId.empty() &&
            (protocolButton == L"dPadLeft" || protocolButton == L"dPadRight")) {
            return true;
        }
        for (const auto& shortcut : node->shortcuts) {
            if (shortcut.button == protocolButton && shortcut.phase == L"pressed" &&
                !shortcut.actionId.empty()) return true;
        }
        return false;
    }

    std::wstring widgetInstanceId_;
    long long snapshotSequence_{};
    std::wstring elementId_;
    std::wstring button_;
};

} // namespace gba::input
