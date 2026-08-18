#include "WidgetLifecycle.h"

namespace widgetrail {

std::wstring_view WidgetLifecycleProtocolValue(
    const WidgetLifecycleState state) noexcept {
    switch (state) {
    case WidgetLifecycleState::Background: return L"background";
    case WidgetLifecycleState::Visible: return L"visible";
    case WidgetLifecycleState::Interactive: return L"interactive";
    }
    return L"background";
}

std::optional<WidgetLifecycleTarget> DesiredWidgetLifecycle(
    const Surface surface,
    const FocusRegion focusRegion,
    const std::wstring_view selectedWidget,
    const std::wstring_view activeWidget,
    const bool selectedWidgetIsBridge,
    const bool activeWidgetIsBridge) {
    if (surface == Surface::Dashboard && selectedWidgetIsBridge &&
        !selectedWidget.empty()) {
        return WidgetLifecycleTarget{
            std::wstring(selectedWidget), WidgetLifecycleState::Visible};
    }
    if (surface == Surface::Widget && activeWidgetIsBridge &&
        !activeWidget.empty()) {
        return WidgetLifecycleTarget{
            std::wstring(activeWidget),
            focusRegion == FocusRegion::Widget
                ? WidgetLifecycleState::Interactive
                : WidgetLifecycleState::Visible};
    }
    return std::nullopt;
}

} // namespace widgetrail
