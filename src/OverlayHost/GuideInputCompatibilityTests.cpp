#include "GuideInputCompatibility.h"
#include "ControllerGuide.h"

#include <array>
#include <cstdlib>
#include <iostream>
#include <utility>

namespace {

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

} // namespace

int main() {
    widgetrail::input::GuideEdgeTracker tracker;
    std::array<bool, XUSER_MAX_COUNT> state{};
    Check(tracker.Update(state) == 0, "initial neutral state is only a baseline");
    state[0] = true;
    Check(tracker.Update(state) == 0x01, "slot zero rising edge is reported");
    Check(tracker.Update(state) == 0, "held Guide does not repeat");
    state[0] = false;
    Check(tracker.Update(state) == 0, "release does not toggle");
    state[1] = true;
    state[3] = true;
    Check(tracker.Update(state) == 0x0A, "simultaneous slots retain identity");
    state = {};
    Check(tracker.Update(state) == 0, "multi-slot release is quiet");

    widgetrail::input::GuideCompatibilityActivation activation;
    widgetrail::input::GuideCompatibilityActivation::DeviceId first{};
    widgetrail::input::GuideCompatibilityActivation::DeviceId second{};
    first[0] = 1;
    second[0] = 2;
    Check(!activation.active() && activation.deviceCount() == 0,
          "compatibility polling starts dormant");
    Check(activation.Update(first, true) && activation.active(),
          "first legacy connection activates compatibility polling");
    Check(!activation.Update(first, true) && activation.deviceCount() == 1,
          "duplicate connection notification is idempotent");
    Check(!activation.Update(second, true) && activation.deviceCount() == 2,
          "additional legacy devices retain one active timer");
    Check(!activation.Update(first, false) && activation.active(),
          "disconnecting one of two devices retains polling");
    Check(activation.Update(second, false) && !activation.active(),
          "last legacy disconnect disables polling");
    Check(!activation.Update(first, false),
          "duplicate disconnect notification is idempotent");
    Check(activation.Update(first, true),
          "compatibility polling can reactivate after reconnect");
    activation.Reset();
    Check(!activation.active() && activation.deviceCount() == 0,
          "reset clears tracked devices");

    widgetrail::WidgetSnapshot snapshot;
    snapshot.activeInputScopeId = L"root";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"root";
    widgetrail::WidgetNode select;
    select.id = L"density";
    select.kind = L"button";
    select.isSelect = true;
    select.text = L"Density";
    widgetrail::WidgetSelectOption compact;
    compact.id = L"compact";
    compact.label = L"Compact";
    compact.actionId = L"density.compact";
    compact.isSelected = true;
    select.selectOptions.push_back(compact);
    snapshot.root.children.push_back(std::move(select));
    const auto selectAuthority =
        widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density");
    Check(selectAuthority.focusedActivation,
          "focused Select publishes one exact activation guide authority");
    const auto selectLine = widgetrail::guide::BuildOpenWidgetLine(
        widgetrail::ControllerGuideDensity::Full, selectAuthority, true, 500.0F,
        [](const std::wstring_view value) {
            return std::optional<float>{static_cast<float>(value.size() * 7U)};
        });
    Check(selectLine.contextual == L"A Select" &&
              selectLine.host == L"B Back   Guide Close" &&
              selectLine.accessible == L"A Select   B Back   Guide Close",
          "focused Select guide and accessibility text retain direct A activation");

    snapshot.root.children[0].selectOptions[0].isDisabled = true;
    const auto disabledSelectAuthority =
        widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density");
    const auto disabledSelectLine = widgetrail::guide::BuildOpenWidgetLine(
        widgetrail::ControllerGuideDensity::Full, disabledSelectAuthority, true,
        500.0F, [](const std::wstring_view value) {
            return std::optional<float>{static_cast<float>(value.size() * 7U)};
        });
    Check(!disabledSelectAuthority.focusedActivation &&
              disabledSelectLine.contextual.empty() &&
              disabledSelectLine.accessible == L"B Back   Guide Close",
          "all-disabled Select options publish no A Select guide authority");

    snapshot.root.children[0].selectOptions[0].isDisabled = false;
    snapshot.root.children[0].selectOptions[0].isBusy = true;
    const auto busySelectAuthority =
        widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density");
    const auto busySelectLine = widgetrail::guide::BuildOpenWidgetLine(
        widgetrail::ControllerGuideDensity::Full, busySelectAuthority, true,
        500.0F, [](const std::wstring_view value) {
            return std::optional<float>{static_cast<float>(value.size() * 7U)};
        });
    Check(!busySelectAuthority.focusedActivation &&
              busySelectLine.contextual.empty() &&
              busySelectLine.accessible == L"B Back   Guide Close",
          "all-busy Select options publish no A Select guide authority");

    snapshot.root.children[0].selectOptions[0].isBusy = false;
    snapshot.root.text = L"Root command";
    snapshot.root.shortcuts.push_back({L"x", L"root.command", L"pressed"});
    snapshot.root.isDisabled = true;
    Check(widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density")
              .actions.empty(),
          "a disabled ancestor owner is not advertised by the guide");
    snapshot.root.isDisabled = false;
    snapshot.root.isBusy = true;
    Check(widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density")
              .actions.empty(),
          "a busy ancestor owner is not advertised by the guide");
    snapshot.root.isBusy = false;
    const auto enabledAncestor =
        widgetrail::guide::ResolveOpenWidgetAuthority(snapshot, L"density");
    Check(enabledAncestor.actions.size() == 1 &&
              enabledAncestor.actions[0].button == L"x" &&
              enabledAncestor.actions[0].label == L"Root command",
          "the guide advertises the exact enabled ancestor owner");

    std::cout << "GuideInputCompatibilityTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
