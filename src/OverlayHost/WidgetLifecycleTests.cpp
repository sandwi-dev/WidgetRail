#include "WidgetLifecycle.h"

#include <cassert>
#include <iostream>

int main() {
    using gba::DesiredWidgetLifecycle;
    using gba::Surface;
    using gba::WidgetLifecycleProtocolValue;
    using gba::WidgetLifecycleState;

    assert(WidgetLifecycleProtocolValue(WidgetLifecycleState::Background) == L"background");
    assert(WidgetLifecycleProtocolValue(WidgetLifecycleState::Visible) == L"visible");
    assert(WidgetLifecycleProtocolValue(WidgetLifecycleState::Interactive) == L"interactive");

    assert(!DesiredWidgetLifecycle(Surface::Hidden, L"music", L"music", true, true));
    assert(!DesiredWidgetLifecycle(Surface::Dashboard, L"built-in", L"", false, false));
    assert(!DesiredWidgetLifecycle(Surface::Widget, L"music", L"built-in", true, false));

    const auto visible = DesiredWidgetLifecycle(
        Surface::Dashboard, L"music", L"", true, false);
    assert((visible == gba::WidgetLifecycleTarget{
        L"music", WidgetLifecycleState::Visible}));

    const auto interactive = DesiredWidgetLifecycle(
        Surface::Widget, L"music", L"music", true, true);
    assert((interactive == gba::WidgetLifecycleTarget{
        L"music", WidgetLifecycleState::Interactive}));

    std::cout << "WidgetLifecycleTests passed\n";
}
