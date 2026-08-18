#pragma once

#include "AccessibilityTree.h"
#include "TrayLayout.h"

#include <string>
#include <vector>

namespace widgetrail::accessibility {

struct TrayItem final {
    std::wstring widgetId;
    std::wstring name;
    bool enabled{true};
};

struct DashboardSemantics final {
    std::wstring title;
    declarative::Rect titleBounds;
    std::wstring help;
    declarative::Rect helpBounds;
    std::wstring status;
    declarative::Rect statusBounds;
};

struct OpenWidgetSemantics final {
    std::wstring title;
    HostAction backAction{HostAction::None};
    std::wstring backTargetId;
    declarative::Rect backBounds;
    declarative::Rect closeBounds;
    std::wstring help;
    declarative::Rect helpBounds;
    std::wstring status;
    declarative::Rect statusBounds;
};

/// Mirrors managed open-widget B resolution for the exact active scope and
/// current focus. The root surface's host-owned Back behavior is intentionally
/// handled separately.
[[nodiscard]] bool HasActiveScopeBackShortcut(
    const WidgetSnapshot& snapshot,
    std::wstring_view focusedElementId) noexcept;

/// Revalidates a published Back command against the current immutable
/// snapshot. No Back action falls through to a different scope behavior.
[[nodiscard]] bool IsCurrentBackAction(
    HostAction action,
    std::wstring_view targetScopeId,
    const WidgetSnapshot& snapshot,
    std::wstring_view focusedElementId) noexcept;

[[nodiscard]] long long ComputeTraySemanticRevision(
    const std::vector<TrayItem>& items,
    const DashboardSemantics* dashboard = nullptr) noexcept;

[[nodiscard]] long long ComputeOpenWidgetSemanticRevision(
    const std::vector<TrayItem>& items,
    const OpenWidgetSemantics& semantics) noexcept;

[[nodiscard]] Tree BuildTrayTree(
    const std::vector<TrayItem>& items,
    const shell::TrayLayout& layout,
    std::size_t selectedSlot,
    long long sequence,
    const DashboardSemantics* dashboard = nullptr);

/// Composes the cached widget projection with host-owned footer commands and
/// the visible tray. Widget identities remain unchanged; host actions stay
/// closed and typed, and exactly one focus owner is published.
[[nodiscard]] Tree BuildOpenWidgetTree(
    Tree widgetTree,
    const std::vector<TrayItem>& items,
    const shell::TrayLayout& layout,
    std::size_t selectedSlot,
    bool trayFocused,
    const OpenWidgetSemantics& semantics);

} // namespace widgetrail::accessibility
