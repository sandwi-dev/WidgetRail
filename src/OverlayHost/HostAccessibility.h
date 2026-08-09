#pragma once

#include "AccessibilityTree.h"
#include "TrayLayout.h"

#include <string>
#include <vector>

namespace gba::accessibility {

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

[[nodiscard]] long long ComputeTraySemanticRevision(
    const std::vector<TrayItem>& items,
    const DashboardSemantics* dashboard = nullptr) noexcept;

[[nodiscard]] Tree BuildTrayTree(
    const std::vector<TrayItem>& items,
    const shell::TrayLayout& layout,
    std::size_t selectedSlot,
    long long sequence,
    const DashboardSemantics* dashboard = nullptr);

} // namespace gba::accessibility
