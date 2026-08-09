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

[[nodiscard]] Tree BuildTrayTree(
    const std::vector<TrayItem>& items,
    const shell::TrayLayout& layout,
    std::size_t selectedSlot,
    long long sequence);

} // namespace gba::accessibility
