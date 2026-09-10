#include "../../src/OverlayPlatformInterop/HidHideConfigurationAdapter.h"

#include <array>
#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>

namespace {
using namespace widgetrail::isolation;

int checks{};
void Check(const bool value, const char* message) {
    ++checks;
    if (!value) {
        std::cerr << "FAILED: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

struct FakeTransaction final {
    HidHideSnapshot state;
    std::vector<std::string> calls;
    std::string failAt;
};

bool Read(void* context, HidHideSnapshot& state, std::uint32_t&) noexcept {
    auto& fake = *static_cast<FakeTransaction*>(context);
    fake.calls.emplace_back("read");
    state = fake.state;
    return fake.failAt != "read";
}
bool WriteApplications(
    void* context, const std::set<std::wstring>& values,
    std::uint32_t&) noexcept {
    auto& fake = *static_cast<FakeTransaction*>(context);
    fake.calls.emplace_back("applications");
    if (fake.failAt == "applications") return false;
    fake.state.applicationPaths = values;
    return true;
}
bool WriteDevices(
    void* context, const std::set<std::wstring>& values,
    std::uint32_t&) noexcept {
    auto& fake = *static_cast<FakeTransaction*>(context);
    fake.calls.emplace_back("devices");
    if (fake.failAt == "devices") return false;
    fake.state.deviceInstanceIds = values;
    return true;
}
bool WriteActive(void* context, const bool value, std::uint32_t&) noexcept {
    auto& fake = *static_cast<FakeTransaction*>(context);
    fake.calls.emplace_back("active");
    if (fake.failAt == "active") return false;
    fake.state.active = value;
    return true;
}
HidHideSnapshotTransaction Api(FakeTransaction& fake) {
    return {&fake, Read, WriteApplications, WriteDevices, WriteActive};
}

void DriverEmptyMultiStringIsAcceptedWithoutWeakeningShape() {
    std::set<std::wstring> values{L"stale"};
    constexpr std::array empty{L'\0'};
    Check(ParseHidHideMultiString(empty, values) && values.empty(),
          "the driver's one-NUL empty multi-string is accepted exactly");

    constexpr std::array ordinary{L'A', L'\0', L'B', L'\0', L'\0'};
    Check(ParseHidHideMultiString(ordinary, values) &&
              values == std::set<std::wstring>{L"A", L"B"},
          "ordinary double-terminated entries retain exact set semantics");

    constexpr std::array missingTerminal{L'A', L'\0'};
    constexpr std::array dataAfterEmpty{L'\0', L'A'};
    constexpr std::array duplicate{L'A', L'\0', L'A', L'\0', L'\0'};
    Check(!ParseHidHideMultiString({}, values) &&
              !ParseHidHideMultiString(missingTerminal, values) &&
              !ParseHidHideMultiString(dataAfterEmpty, values) &&
              !ParseHidHideMultiString(duplicate, values),
          "missing termination, trailing data, and duplicate entries still fail closed");
}

void ForeignDriftCausesNoMutation() {
    HidHideSnapshot expected{{L"existing.exe"}, {}, false, false};
    FakeTransaction fake{{{L"existing.exe", L"foreign.exe"}, {}, false, false}};
    HidHideSnapshot desired = expected;
    desired.applicationPaths.insert(L"worker.exe");
    desired.deviceInstanceIds.insert(L"device");
    desired.active = true;
    HidHideSnapshot observed;
    std::uint32_t error{};
    Check(ApplyHidHideSnapshotTransaction(
              Api(fake), expected, desired, observed, error) ==
              HidHideConfigurationStatus::ConcurrentPolicyDrift &&
              fake.calls == std::vector<std::string>{"read"} &&
              observed == fake.state,
          "foreign drift between planning and transaction performs zero writes");
}

void SameOwnerPerformsExactReadback() {
    HidHideSnapshot before{{L"existing.exe"}, {}, false, false};
    HidHideSnapshot desired{
        {L"existing.exe", L"worker.exe"}, {L"physical"}, true, false};
    FakeTransaction fake{before};
    HidHideSnapshot observed;
    std::uint32_t error{};
    Check(ApplyHidHideSnapshotTransaction(
              Api(fake), before, desired, observed, error) ==
              HidHideConfigurationStatus::Ready &&
              observed == desired && fake.state == desired &&
              fake.calls == std::vector<std::string>{
                  "read", "applications", "devices", "active", "read"},
          "one transaction owns expected-before, writes, and exact readback");
}

void PartialWriteRetainsObservedRecoveryEvidence() {
    HidHideSnapshot before{{L"existing.exe"}, {}, false, false};
    HidHideSnapshot desired{
        {L"existing.exe", L"worker.exe"}, {L"physical"}, true, false};
    FakeTransaction fake{before};
    fake.failAt = "devices";
    HidHideSnapshot observed;
    std::uint32_t error{};
    Check(ApplyHidHideSnapshotTransaction(
              Api(fake), before, desired, observed, error) ==
              HidHideConfigurationStatus::PartialMutation &&
              observed.applicationPaths == desired.applicationPaths &&
              observed.deviceInstanceIds.empty() && !observed.active &&
              fake.calls == std::vector<std::string>{
                  "read", "applications", "devices", "read"},
          "partial write is reread and retained for journal recovery");
}
}

int main() {
    DriverEmptyMultiStringIsAcceptedWithoutWeakeningShape();
    ForeignDriftCausesNoMutation();
    SameOwnerPerformsExactReadback();
    PartialWriteRetainsObservedRecoveryEvidence();
    std::cout << "HidHideConfigurationAdapterTests passed (" << checks
              << " checks)\n";
}
