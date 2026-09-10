#include "ControllerOpenShortcut.h"
#include <cstdlib>
#include <initializer_list>
#include <iostream>

int main() {
    widgetrail::input::ViewMenuChordTracker tracker;
    int checks{};
    const auto check = [&](bool value, const char* message) {
        ++checks;
        if (!value) { std::cerr << "FAIL " << message << '\n'; std::exit(1); }
    };
    const auto read = [&](std::initializer_list<widgetrail::input::OpenShortcutSample> samples) {
        return tracker.Update(std::span(samples.begin(), samples.size()));
    };
    check(!read({{1,3}}), "held shortcut at startup cannot fire");
    check(!read({{1,1}}), "partial release does not arm startup chord");
    check(!read({{1,0}}), "neutral arms without firing");
    check(!read({{1,1}}), "View alone does not fire");
    check(read({{1,3}}), "Menu after View fires once");
    check(!read({{1,3}}), "holding cannot repeat");
    check(!read({{1,2}}), "partial release cannot repeat");
    check(!read({{1,3}}), "repressing one button cannot repeat");
    check(!read({{1,0},{2,3}}), "delayed virtual duplicate remains consumed");
    check(!read({{1,0},{2,0}}), "all sources must release before rearming");
    check(!read({{1,1},{2,2}}), "buttons on different controllers cannot form a chord");
    check(read({{1,3},{2,2}}), "one controller with both buttons fires");
    check(tracker.consumed(), "overlay can suppress chord side actions until release");
    check(!read({}), "disconnect clears held state without firing");
    check(!read({{1,3}}), "reconnect while held cannot fire");
    check(!read({{1,0}}) && read({{1,3}}), "reconnected controller works after release");
    tracker.Reset();
    check(!read({{1,3}}), "switching shortcut resets held input");
    check(!read({{1,0}}) && !read({{1,2}}) && read({{1,3}}), "View after Menu also fires");
    std::cout << "ControllerOpenShortcutTests: " << checks << " checks passed\n";
}
