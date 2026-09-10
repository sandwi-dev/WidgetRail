#include "../../src/OverlayPlatformInterop/ControllerIsolationHostSession.h"
#include "../../src/OverlayPlatformInterop/LocalControllerPolicy.h"
#include <atomic>
#include <fstream>
#include <iostream>
#include <thread>
#include <mutex>
#include <stdexcept>
#include <functional>

using namespace widgetrail::isolation;
namespace widgetrail::isolation {
// Native discovery is never reached by the injected production owner in this suite.
std::unique_ptr<SelectedControllerSource> CreateGameInputSelectedControllerReader() noexcept { std::abort(); }
SelectedControllerDiscoveryStatus DiscoverCurrentPhysicalController(std::uint64_t, SelectedControllerDescriptor&) noexcept { std::abort(); }
}
namespace {
int checks{};
void Check(bool value, const char* name) {
    ++checks;
    if (!value) throw std::runtime_error(name);
}
struct Effects final : LocalPolicyEffects {
    HidHideSnapshot state{{L"other-app"}, {}, false, false};
    int writes{};
    bool failHide{}, failRestore{}, throwHide{};
    std::function<void()> duringRestore;
    bool Read(HidHideSnapshot& result, std::uint32_t&) override { result = state; return true; }
    bool Apply(const HidHideSnapshot& expected, const HidHideSnapshot& desired,
               HidHideSnapshot& observed, std::uint32_t& error) override {
        if (expected != state) { error = ERROR_REVISION_MISMATCH; return false; }
        ++writes;
        if (duringRestore && !desired.applicationPaths.contains(L"reader")) duringRestore();
        if (failRestore && !desired.active) { error = ERROR_ACCESS_DENIED; return false; }
        if ((failHide || throwHide) && desired.active) {
            state.applicationPaths = desired.applicationPaths;
            observed = state;
            if (throwHide) throw std::runtime_error("policy apply failure");
            error = ERROR_ACCESS_DENIED; return false;
        }
        state = desired; observed = state; return true;
    }
};
struct Source final : SelectedControllerSource {
    std::mutex mutex;
    ControllerIsolationReaderIngress* ingress{};
    GamepadState state{};
    std::uint64_t timestamp{100};
    std::atomic_bool blockSample{}, sampleEntered{};
    std::atomic_int stopped{};
    SelectedControllerPrepareStatus Prepare(const SelectedControllerEnrollment&, ControllerIsolationReaderIngress& value) noexcept override {
        std::scoped_lock lock(mutex); ingress = &value; return SelectedControllerPrepareStatus::Ready;
    }
    bool SampleCurrent(SelectedControllerCurrent& value) noexcept override {
        sampleEntered = true;
        while (blockSample) std::this_thread::yield();
        std::scoped_lock lock(mutex);
        value = {timestamp, GetTickCount64(), state, true}; return true;
    }
    bool Publish(ControllerReaderEventKind kind, GamepadState value = {}) {
        std::scoped_lock lock(mutex); state = value; ++timestamp;
        return ingress && ingress->Publish(kind, timestamp, GetTickCount64(), value,
            kind != ControllerReaderEventKind::Disconnected);
    }
    void PauseNextProducer() { ingress->PauseNextProducerForTest(); }
    bool ProducerPaused() { return ingress->ProducerPausedForTest(); }
    void ReleaseProducer() { ingress->ReleaseProducerForTest(); }
    void Stop() noexcept override { std::scoped_lock lock(mutex); ingress = nullptr; ++stopped; }
};
struct Output final : ControllerIsolationOutput {
    std::mutex mutex;
    GamepadState last{};
    int reports{};
    std::atomic_int removals{};
    bool OpenOwnedTarget() noexcept override { return true; }
    bool Submit(const GamepadState& value) noexcept override { std::scoped_lock lock(mutex); last = value; ++reports; return true; }
    void RemoveOwnedTarget() noexcept override { ++removals; }
    bool Is(GamepadState value) { std::scoped_lock lock(mutex); return last == value; }
};
template<class Predicate> bool Until(Predicate predicate) {
    const auto start = GetTickCount64();
    do { if (predicate()) return true; Sleep(1); } while (GetTickCount64() - start < 1500);
    return false;
}
ControllerIsolationHostSession::TestDependencies Dependencies(
    const std::filesystem::path& path, Effects& effects, Source* source, Output& output) {
    ControllerIsolationHostSession::TestDependencies value;
    value.effects = &effects; value.source = source; value.output = &output; value.journalPath = path;
    auto& enrollment = value.descriptor.enrollment;
    enrollment.enrollmentToken = 1; enrollment.deviceId[0] = 1; enrollment.deviceRootId[0] = 2;
    enrollment.containerId[0] = 3; enrollment.normalizedPnpPathDigest[0] = 4;
    enrollment.vendorId = 0x045e; enrollment.productId = 1; enrollment.deviceFamily = 1;
    enrollment.connected = true; enrollment.gamepadSupported = true;
    const std::wstring identity = L"selected-device";
    std::copy(identity.begin(), identity.end(), value.descriptor.deviceInstanceId.value.begin());
    value.descriptor.deviceInstanceId.length = identity.size();
    return value;
}
void Put(const std::filesystem::path& path, const std::string& text) {
    std::ofstream file(path, std::ios::binary | std::ios::trunc); file << text; file.close();
}
void ParserAndRecovery(const std::filesystem::path& root) {
    const auto path = root / L"local-session.v1";
    Effects effects; const auto before = effects.state;
    const auto plan = PlanHidHideApply(before, L"reader", {L"selected"});
    LocalPolicyRecord saved; std::wstring diagnostic;
    Check(SaveLocalPolicy(path, plan.plan->journal, saved, diagnostic), "save production local journal");
    LocalPolicyRecord loaded; bool found{};
    Check(LoadLocalPolicy(path, loaded, found, diagnostic) && found && loaded.encoded == saved.encoded,
        "production parser round trip");
    const std::string valid = saved.encoded;
    for (const auto& bad : {std::string("unknown-schema\n"), valid + "activated=1\n", valid.substr(0, valid.size()-1),
                            std::string(128 * 1024 + 1, 'a'), std::string("WidgetRailControllerIsolationLocal=1\n")}) {
        Put(path, bad); diagnostic.clear();
        Check(!LoadLocalPolicy(path, loaded, found, diagnostic) && !diagnostic.empty(), "malformed duplicate oversized or incomplete record rejected with diagnostic");
    }
    Put(path, valid + "owned-app=6100\n");
    effects.state = plan.plan->desired;
    Check(!RestoreLocalPolicy(path, saved, effects, diagnostic) && effects.writes == 0 && std::filesystem::exists(path),
        "replaced journal retained without policy write");
    Put(path, valid);
    effects.state.applicationPaths.erase(L"other-app");
    Check(!RestoreLocalPolicy(path, saved, effects, diagnostic) && effects.writes == 0 && std::filesystem::exists(path),
        "foreign policy removal retained");
    effects.state = plan.plan->desired;
    effects.state.applicationPaths.insert(L"new-other-app");
    bool replacementBlocked{};
    effects.duringRestore = [&] {
        HANDLE replacement = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        replacementBlocked = replacement == INVALID_HANDLE_VALUE && GetLastError() == ERROR_SHARING_VIOLATION;
        if (replacement != INVALID_HANDLE_VALUE) CloseHandle(replacement);
    };
    Check(RestoreLocalPolicy(path, saved, effects, diagnostic) && !std::filesystem::exists(path) &&
          effects.state.applicationPaths.contains(L"new-other-app") && !effects.state.applicationPaths.contains(L"reader"),
        "exact journal removed after only owned policy restore");
    Check(replacementBlocked, "journal cannot be replaced during policy restoration");
    Check(RecoverLocalControllerPolicy(path, effects, diagnostic) && effects.writes == 1,
        "recovery only never begins fresh hiding");
    LocalOwnerLease owner;
    Check(owner.Acquire(diagnostic, 0), "first lifecycle owner acquired");
    bool secondAccepted = true;
    std::thread second([&] { LocalOwnerLease other; std::wstring error; secondAccepted = other.Acquire(error, 20); });
    second.join(); Check(!secondAccepted, "actual mutex excludes concurrent owner");
}
void SetupCleanup(const std::filesystem::path& root) {
    for (int failure = 0; failure < 4; ++failure) {
        const auto path = root / (L"failure-" + std::to_wstring(failure)) / L"local-session.v1";
        Effects effects; const auto before = effects.state; Source source; Output output;
        effects.failHide = failure == 1; effects.throwHide = failure == 2;
        auto dependency = Dependencies(path, effects, failure == 0 ? nullptr : &source, output);
        dependency.throwAfterApply = failure == 3;
        ControllerIsolationHostSession owner(dependency); std::wstring error;
        Check(!owner.Start(true, error) && !error.empty(), "actual owner returns explicit setup failure");
        owner.Stop();
        Check(effects.state == before && !std::filesystem::exists(path), "allocation partial-apply or exception restores owned policy through production cleanup");
    }
    const auto path = root / L"restore-failure" / L"local-session.v1";
    Effects effects; effects.failRestore = true; Output output;
    ControllerIsolationHostSession owner(Dependencies(path, effects, nullptr, output));
    std::wstring error; Check(!owner.Start(true, error), "allocation failure fixture"); owner.Stop();
    ControllerIsolationHostReading reading; (void)owner.Poll(reading, error);
    Check(owner.active() && reading.progress == LocalControllerProgress::Fault &&
          error.find(L"allocation") != std::wstring::npos && error.find(L"recovery") != std::wstring::npos && std::filesystem::exists(path),
        "restore failure retains record and original fault");
}
void OwnerTransitions(const std::filesystem::path& root) {
    Effects effects; Source source; Output output;
    ControllerIsolationHostSession owner(Dependencies(root / L"owner" / L"local-session.v1", effects, &source, output));
    std::wstring error; Check(owner.Start(true, error), "actual routing thread starts");
    {
        Effects otherEffects; Source otherSource; Output otherOutput;
        ControllerIsolationHostSession second(Dependencies(root / L"owner" / L"local-session.v1", otherEffects, &otherSource, otherOutput));
        Check(!second.Start(true, error) && !error.empty() && otherEffects.writes == 0,
            "second actual owner cannot restore or replace active policy");
        second.Stop();
    }
    GamepadState down{}; down.buttons = 0x1000;
    Check(source.Publish(ControllerReaderEventKind::Reading, down) && Until([&] { return output.Is(down); }),
        "playing progresses without UI polling or rendering");
    Check(owner.PrepareOverlay(error) && output.Is({}), "actual enter neutral before admission");
    source.Publish(ControllerReaderEventKind::Reading, {});
    source.Publish(ControllerReaderEventKind::Reading, down);
    source.Publish(ControllerReaderEventKind::Reading, {});
    source.Publish(ControllerReaderEventKind::GuidePressed);
    source.Publish(ControllerReaderEventKind::GuideReleased);
    bool sawPress{}, sawRelease{}; unsigned guides{};
    Check(Until([&] {
        ControllerIsolationHostReading value; (void)owner.Poll(value, error);
        if (value.state == down) sawPress = true;
        if (sawPress && value.state == GamepadState{}) sawRelease = true;
        if (value.guideEvent) ++guides;
        return sawPress && sawRelease && guides == 2;
    }), "actual owner delivers short tap and Guide edges once");
    std::uint64_t guide{}; (void)owner.PollGuide(guide, error); Check(!guide, "Guide drained once");
    source.Publish(ControllerReaderEventKind::Reading, down);
    owner.CloseOverlay();
    Check(Until([&] { ControllerIsolationHostReading value; (void)owner.Poll(value, error); return value.progress == LocalControllerProgress::AwaitingNeutral; }), "held close awaits neutral");
    Check(owner.PrepareOverlay(error) && output.Is({}), "reopen while held recontains");
    source.sampleEntered = false; source.blockSample = true; owner.CloseOverlay();
    Check(Until([&] { return source.sampleEntered.load(); }), "owner paused at actual close sample");
    Check(!owner.PrepareOverlay(error) && !error.empty(), "actual paused owner open times out and cancels its generation");
    owner.CloseOverlay(); source.blockSample = false;
    Check(Until([&] { ControllerIsolationHostReading value; (void)owner.Poll(value, error); return value.progress == LocalControllerProgress::AwaitingNeutral; }), "hide cancels stale desired reopen");
    source.Publish(ControllerReaderEventKind::Reading, {});
    Check(Until([&] { ControllerIsolationHostReading value; (void)owner.Poll(value, error); return value.progress == LocalControllerProgress::Playing; }),
        "timeout and hide cannot apply stale enter after neutral");
    source.Publish(ControllerReaderEventKind::Disconnected);
    Check(Until([&] { ControllerIsolationHostReading value; (void)owner.Poll(value, error); return value.progress == LocalControllerProgress::Fault; }) && owner.active(),
        "actual runtime fault stays configured with no legacy fallback");
    owner.Stop(); Check(output.removals == 1 && source.stopped > 0, "actual owner shutdown retires source and exact target once");
}

void PendingTransitionsAreSingleFlight(const std::filesystem::path& root) {
    Effects effects; Source source; Output output;
    ControllerIsolationHostSession owner(Dependencies(
        root / L"single-flight" / L"local-session.v1", effects, &source, output));
    std::wstring error;
    Check(owner.Start(true, error), "single-flight actual owner starts");
    GamepadState held{}; held.buttons = 0x1000;

    source.PauseNextProducer();
    bool publishedEnter{};
    std::thread enterProducer([&] { publishedEnter = source.Publish(ControllerReaderEventKind::Reading, held); });
    Check(Until([&] { return source.ProducerPaused(); }), "Enter producer holds a real reserved ingress slot");
    std::atomic_bool enterDone{}; bool entered{};
    std::thread enter([&] { std::wstring message; entered = owner.PrepareOverlay(message); enterDone = true; });
    Sleep(25);
    Check(!enterDone && owner.active(), "pending Enter survives multiple owner iterations without resubmission fault");
    source.ReleaseProducer(); enterProducer.join(); enter.join();
    Check(publishedEnter && entered && output.Is({}), "pending Enter drains reserved report then neutralizes once");

    source.PauseNextProducer();
    bool publishedClose{};
    std::thread closeProducer([&] { publishedClose = source.Publish(ControllerReaderEventKind::Reading, held); });
    Check(Until([&] { return source.ProducerPaused(); }), "Close producer holds a real reserved ingress slot");
    owner.CloseOverlay();
    Sleep(25);
    Check(owner.active(), "pending Close survives multiple owner iterations without resubmission fault");
    source.ReleaseProducer(); closeProducer.join();
    Check(publishedClose && Until([&] {
        ControllerIsolationHostReading reading; (void)owner.Poll(reading, error);
        return reading.progress == LocalControllerProgress::AwaitingNeutral;
    }) && output.Is({}), "pending Close preserves neutral release barrier after reserved report");
    owner.Stop();
}
}
int main() {
    const auto root = std::filesystem::temp_directory_path() / (L"wrail-local-owner-" + std::to_wstring(GetCurrentProcessId()));
    std::filesystem::create_directories(root);
    try { ParserAndRecovery(root); SetupCleanup(root); OwnerTransitions(root);
          PendingTransitionsAreSingleFlight(root); }
    catch (const std::exception& error) { std::cerr << "FAILED: " << error.what() << "\nRetained: " << root << '\n'; return 1; }
    std::filesystem::remove_all(root);
    std::cout << "LocalControllerOwnerTests passed " << checks << " checks\n";
}
