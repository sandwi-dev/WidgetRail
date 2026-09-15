#include "WidgetLifecycle.h"

#ifdef NDEBUG
#undef NDEBUG
#endif
#include <cassert>
#include <iostream>

int main() {
    using widgetrail::DesiredWidgetLifecycle;
    using widgetrail::FocusRegion;
    using widgetrail::Surface;
    using widgetrail::WidgetLifecycleProtocolValue;
    using widgetrail::WidgetLifecycleState;

    assert(WidgetLifecycleProtocolValue(WidgetLifecycleState::Background) == L"background");
    assert(WidgetLifecycleProtocolValue(WidgetLifecycleState::Visible) == L"visible");
    assert(WidgetLifecycleProtocolValue(WidgetLifecycleState::Interactive) == L"interactive");

    assert(!DesiredWidgetLifecycle(Surface::Hidden, FocusRegion::Widget, L"music", L"music", true, true));
    assert(!DesiredWidgetLifecycle(Surface::Dashboard, FocusRegion::Tray, L"built-in", L"", false, false));
    assert(!DesiredWidgetLifecycle(Surface::Widget, FocusRegion::Widget, L"music", L"built-in", true, false));

    const auto visible = DesiredWidgetLifecycle(
        Surface::Dashboard, FocusRegion::Tray, L"music", L"", true, false);
    assert((visible == widgetrail::WidgetLifecycleTarget{
        L"music", WidgetLifecycleState::Visible}));

    const auto interactive = DesiredWidgetLifecycle(
        Surface::Widget, FocusRegion::Widget, L"music", L"music", true, true);
    assert((interactive == widgetrail::WidgetLifecycleTarget{
        L"music", WidgetLifecycleState::Interactive}));

    const auto visiblePanel = DesiredWidgetLifecycle(
        Surface::Widget, FocusRegion::Tray, L"music", L"music", true, true);
    assert((visiblePanel == widgetrail::WidgetLifecycleTarget{
        L"music", WidgetLifecycleState::Visible}));

    assert(!DesiredWidgetLifecycle(
        Surface::Dashboard, FocusRegion::Tray, L"music", L"", true, false, false));
    assert((DesiredWidgetLifecycle(
        Surface::Widget, FocusRegion::Tray, L"other", L"music", true, true, false) ==
        widgetrail::WidgetLifecycleTarget{L"music", WidgetLifecycleState::Visible}));
    std::cout << "WidgetLifecycleTests passed\n";
}
