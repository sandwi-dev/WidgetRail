#pragma once

#include "OverlayState.h"

#include <optional>
#include <string>
#include <string_view>

namespace gba {

enum class WidgetLifecycleState {
    Background,
    Visible,
    Interactive,
};

struct WidgetLifecycleTarget final {
    std::wstring widgetId;
    WidgetLifecycleState state{WidgetLifecycleState::Background};

    bool operator==(const WidgetLifecycleTarget&) const = default;
};

[[nodiscard]] std::wstring_view WidgetLifecycleProtocolValue(
    WidgetLifecycleState state) noexcept;

/// Maps host presentation to the one bridge widget that should currently run.
/// Hidden and built-in surfaces intentionally return no target so the caller
/// can background only a worker it already tracks without launching another.
[[nodiscard]] std::optional<WidgetLifecycleTarget> DesiredWidgetLifecycle(
    Surface surface,
    FocusRegion focusRegion,
    std::wstring_view selectedWidget,
    std::wstring_view activeWidget,
    bool selectedWidgetIsBridge,
    bool activeWidgetIsBridge);

} // namespace gba
