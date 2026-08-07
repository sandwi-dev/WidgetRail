#include "WidgetLifecycle.h"

#include <cassert>
#include <iostream>

int main() {
    using gba::DesiredWidgetLifecycle;
    using gba::FocusRegion;
    using gba::Surface;
    using gba::WidgetLifecycleProtocolValue;
    using gba::WidgetLifecycleState;

    assert(WidgetLifecycleProtocolValue(WidgetLifecycleState::Background) == L"background");
    assert(WidgetLifecycleProtocolValue(WidgetLifecycleState::Visible) == L"visible");
    assert(WidgetLifecycleProtocolValue(WidgetLifecycleState::Interactive) == L"interactive");

    assert(!DesiredWidgetLifecycle(Surface::Hidden, FocusRegion::Widget, L"music", L"music", true, true));
    assert(!DesiredWidgetLifecycle(Surface::Dashboard, FocusRegion::Tray, L"built-in", L"", false, false));
    assert(!DesiredWidgetLifecycle(Surface::Widget, FocusRegion::Widget, L"music", L"built-in", true, false));

    const auto visible = DesiredWidgetLifecycle(
        Surface::Dashboard, FocusRegion::Tray, L"music", L"", true, false);
    assert((visible == gba::WidgetLifecycleTarget{
        L"music", WidgetLifecycleState::Visible}));

    const auto interactive = DesiredWidgetLifecycle(
        Surface::Widget, FocusRegion::Widget, L"music", L"music", true, true);
    assert((interactive == gba::WidgetLifecycleTarget{
        L"music", WidgetLifecycleState::Interactive}));

    const auto visiblePanel = DesiredWidgetLifecycle(
        Surface::Widget, FocusRegion::Tray, L"music", L"music", true, true);
    assert((visiblePanel == gba::WidgetLifecycleTarget{
        L"music", WidgetLifecycleState::Visible}));

    std::cout << "WidgetLifecycleTests passed\n";
}
