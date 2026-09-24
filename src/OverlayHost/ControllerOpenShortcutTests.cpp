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

    using widgetrail::input::OpenShortcutSample;
    using widgetrail::input::OpenShortcutSampling;
    constexpr auto nativeId = static_cast<std::uintptr_t>(-1);
    const auto poll = [&](std::optional<int> native,
        std::initializer_list<OpenShortcutSample> shared,
        OpenShortcutSampling sampling = OpenShortcutSampling::AllControllers) {
        return widgetrail::input::PollViewMenuSources(tracker,
            native ? std::optional{OpenShortcutSample{nativeId, static_cast<std::uint8_t>(*native)}} : std::nullopt,
            sampling, [&](std::span<OpenShortcutSample> destination) {
                check(destination.size() >= shared.size(), "all public reader samples fit");
                check(sampling == OpenShortcutSampling::AllControllers,
                    "authoritative isolation never polls public readers");
                std::copy(shared.begin(), shared.end(), destination.begin());
                return shared.size();
            });
    };
    // Exercise each Xbox slot and a GameInput identity beside idle native HID.
    for (const std::uintptr_t xbox : {1U, 2U, 3U, 4U, 0x100U}) {
        tracker.Reset();
        check(!poll(0, {{xbox,0}}), "idle Xbox and DualSense arm together");
        check(poll(0, {{xbox,3}}), "idle DualSense cannot mask an Xbox shortcut");
        check(!poll(0, {{xbox,3}}), "shared Xbox hold does not toggle twice");
        check(!poll(0, {{xbox,0}}), "shared controllers release");
        check(poll(3, {{xbox,0}}), "native DualSense shortcut also works beside Xbox");
    }
    tracker.Reset();
    check(!poll(0, {{1,0},{0x100,0}}), "duplicate public readers start neutral");
    check(poll(0, {{1,3},{0x100,3}}), "XInput and GameInput duplicate chord toggles once");
    check(!poll(0, {{1,0},{0x100,3}}), "lagging duplicate remains consumed");
    check(!poll(0, {{1,0},{0x100,0}}), "duplicate readers release");
    check(!poll(1, {{1,2}}), "native View and Xbox Menu cannot form a chord");
    check(!poll(0, {{1,0}}), "split chord releases");
    check(!poll(std::nullopt, {{1,0}}), "native disconnect preserves neutral Xbox");
    check(poll(std::nullopt, {{1,3}}), "Xbox still works after native disconnect");

    tracker.Reset();
    check(!poll(0, {{1,3}}, OpenShortcutSampling::NativeOnly), "isolated native ignores public chord");
    check(poll(3, {{1,0}}, OpenShortcutSampling::NativeOnly), "isolated native chord still works");
    check(!poll(3, {{1,3}}, OpenShortcutSampling::NativeOnly), "isolated hold cannot repeat");
    check(!poll(std::nullopt, {{1,3}}, OpenShortcutSampling::NativeOnly), "missing authoritative input cannot admit public chord");
    check(!poll(3, {{1,0}}, OpenShortcutSampling::NativeOnly), "reconnected native chord requires release");
    check(!poll(0, {{1,3}}), "returning public reader requires release");
    check(!poll(0, {{1,0}}) && poll(0, {{1,3}}), "public shortcut rearms after leaving isolation");

    tracker.Reset();
    std::array<OpenShortcutSample,20> full{};
    for (std::size_t index = 0; index < full.size(); ++index) full[index] = {index+1,0};
    const auto capacityPoll = [&] {
        return widgetrail::input::PollViewMenuSources(tracker, OpenShortcutSample{nativeId,0},
            OpenShortcutSampling::AllControllers, [&](std::span<OpenShortcutSample> destination) {
                check(destination.size() == full.size(), "native input reserves a separate bounded slot");
                std::copy(full.begin(), full.end(), destination.begin());
                return full.size();
            });
    };
    check(!capacityPoll(), "maximum connected reader set arms");
    full.back().buttons = 3;
    check(capacityPoll(), "last public reader works alongside native at maximum capacity");
    std::cout << "ControllerOpenShortcutTests: " << checks << " checks passed\n";
}
