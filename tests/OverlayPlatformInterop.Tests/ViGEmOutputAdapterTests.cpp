#include "../../src/OverlayPlatformInterop/ViGEmOutputAdapter.h"

#include <algorithm>
#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>

namespace {

using namespace widgetrail::isolation;

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAILED: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

struct FakeBackend {
    static constexpr std::uint32_t Success = 0x20000000;
    static constexpr std::uint32_t Failure = 0xE0000001;
    std::vector<std::string> calls;
    std::vector<GamepadState> reports;
    std::string failAt;
    int peerTargets{3};
};

thread_local FakeBackend* activeBackend{};
int clientToken{};
int targetToken{};
const auto kClient = static_cast<ViGEmClientHandle>(&clientToken);
const auto kTarget = static_cast<ViGEmTargetHandle>(&targetToken);

bool Fails(const char* operation) {
    return activeBackend && activeBackend->failAt == operation;
}

ViGEmClientHandle AllocateClient() noexcept {
    activeBackend->calls.emplace_back("allocate-client");
    return Fails("allocate-client") ? nullptr : kClient;
}
void FreeClient(ViGEmClientHandle client) noexcept {
    Check(client == kClient, "only the allocated client is freed");
    activeBackend->calls.emplace_back("free-client");
}
std::uint32_t Connect(ViGEmClientHandle client) noexcept {
    Check(client == kClient, "connect uses the allocated client");
    activeBackend->calls.emplace_back("connect");
    return Fails("connect") ? FakeBackend::Failure : FakeBackend::Success;
}
void Disconnect(ViGEmClientHandle client) noexcept {
    Check(client == kClient, "disconnect uses the allocated client");
    activeBackend->calls.emplace_back("disconnect");
}
ViGEmTargetHandle AllocateTarget() noexcept {
    activeBackend->calls.emplace_back("allocate-target");
    return Fails("allocate-target") ? nullptr : kTarget;
}
void FreeTarget(ViGEmTargetHandle target) noexcept {
    Check(target == kTarget, "only the allocated target is freed");
    activeBackend->calls.emplace_back("free-target");
}
std::uint32_t AddTarget(
    ViGEmClientHandle client,
    ViGEmTargetHandle target) noexcept {
    Check(client == kClient && target == kTarget,
          "add owns the exact allocated target");
    activeBackend->calls.emplace_back("add-target");
    return Fails("add-target") ? FakeBackend::Failure : FakeBackend::Success;
}
std::uint32_t RemoveTarget(
    ViGEmClientHandle client,
    ViGEmTargetHandle target) noexcept {
    Check(client == kClient && target == kTarget,
          "remove owns the exact allocated target");
    activeBackend->calls.emplace_back("remove-target");
    return Fails("remove-target") ? FakeBackend::Failure
                                   : FakeBackend::Success;
}
std::uint32_t Update(
    ViGEmClientHandle client,
    ViGEmTargetHandle target,
    const GamepadState& state) noexcept {
    Check(client == kClient && target == kTarget,
          "update owns the exact allocated target");
    activeBackend->calls.emplace_back("update");
    activeBackend->reports.push_back(state);
    return Fails("update") ? FakeBackend::Failure : FakeBackend::Success;
}

ViGEmApi Api() {
    return {
        AllocateClient, FreeClient, Connect, Disconnect, AllocateTarget,
        FreeTarget, AddTarget, RemoveTarget, Update, FakeBackend::Success};
}

void ExactLifecycleAndReportMapping() {
    FakeBackend backend;
    activeBackend = &backend;
    const auto api = Api();
    ViGEmOutputAdapter adapter(api);
    Check(adapter.Open() && adapter.status() == ViGEmAdapterStatus::Ready &&
              adapter.targetOwned() && backend.reports.size() == 1 &&
              backend.reports.front() == GamepadState{},
          "open creates one exact target and explicitly submits neutral");
    GamepadState report;
    report.buttons = 0xA001;
    report.leftTrigger = 17;
    report.rightTrigger = 231;
    report.leftThumbX = -12'345;
    report.leftThumbY = 23'456;
    report.rightThumbX = 31'000;
    report.rightThumbY = -30'000;
    Check(adapter.Submit(report) && backend.reports.size() == 2 &&
              backend.reports.back() == report,
          "every fixed report field reaches the exact target unchanged");
    adapter.RemoveOwnedTarget();
    adapter.RemoveOwnedTarget();
    Check(adapter.status() == ViGEmAdapterStatus::Closed &&
              backend.calls == std::vector<std::string>{
            "allocate-client", "connect", "allocate-target", "add-target",
            "update", "update", "remove-target", "free-target",
            "disconnect", "free-client"} &&
              backend.peerTargets == 3,
          "normal and repeated teardown remove only one owned target once");
}

void FailuresCleanUpOnlyAcquiredOwnership() {
    for (const std::string failure : {
             "allocate-client", "connect", "allocate-target", "add-target"}) {
        FakeBackend backend;
        backend.failAt = failure;
        activeBackend = &backend;
        const auto api = Api();
        ViGEmOutputAdapter adapter(api);
        Check(!adapter.Open() && !adapter.targetOwned() &&
                  backend.peerTargets == 3,
              "partial open failure preserves peer targets");
    }

    FakeBackend neutralFailure;
    neutralFailure.failAt = "update";
    activeBackend = &neutralFailure;
    const auto neutralApi = Api();
    ViGEmOutputAdapter neutralAdapter(neutralApi);
    Check(!neutralAdapter.Open() &&
              neutralAdapter.status() ==
                  ViGEmAdapterStatus::InitialNeutralFailed &&
              !neutralAdapter.targetOwned() &&
              std::count(
                  neutralFailure.calls.begin(), neutralFailure.calls.end(),
                  "remove-target") == 1 &&
              neutralFailure.peerTargets == 3,
          "failed initial neutral retires the exact target and retains diagnostics");
}

void UpdateAndRemovalFailuresRemainBounded() {
    FakeBackend backend;
    activeBackend = &backend;
    const auto api = Api();
    ViGEmOutputAdapter adapter(api);
    Check(adapter.Open(), "failure test opens exact target");
    backend.failAt = "update";
    Check(!adapter.Submit(GamepadState{}) &&
              adapter.status() == ViGEmAdapterStatus::UpdateFailed,
          "update failure is terminal for further submissions");
    backend.failAt = "remove-target";
    adapter.RemoveOwnedTarget();
    adapter.RemoveOwnedTarget();
    Check(adapter.status() == ViGEmAdapterStatus::RemoveTargetFailed &&
              std::count(
                  backend.calls.begin(), backend.calls.end(),
                  "remove-target") == 1 &&
              backend.peerTargets == 3,
          "remove failure closes the client once without broad cleanup");
}

} // namespace

int main() {
    ExactLifecycleAndReportMapping();
    FailuresCleanUpOnlyAcquiredOwnership();
    UpdateAndRemovalFailuresRemainBounded();
    std::cout << "ViGEmOutputAdapterTests passed (" << checks << " checks)\n";
    return 0;
}
