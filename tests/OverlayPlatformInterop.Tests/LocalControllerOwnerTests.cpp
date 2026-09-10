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
SelectedControllerDiscoveryStatus DiscoverCurrentPhysicalController(std::uint64_t, SelectedControllerDescriptor&, const ControllerDeviceNodeIdentity*) noexcept { std::abort(); }
bool DiscoverSelectedControllerHideTargets(const ControllerDeviceNodeIdentity&, std::set<std::wstring>&) noexcept { std::abort(); }
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
    bool connected{true};
    std::uint64_t timestamp{100};
    std::atomic_bool blockSample{}, sampleEntered{};
    std::atomic_int stopped{};
    SelectedControllerPrepareStatus Prepare(
        const SelectedControllerEnrollment& enrollment,
        ControllerIsolationReaderIngress& value,
        SelectedControllerEnrollment& preparedEnrollment) noexcept override {
        std::scoped_lock lock(mutex);
        ingress = &value;
        preparedEnrollment = enrollment;
        return SelectedControllerPrepareStatus::Ready;
    }
    bool SampleCurrent(SelectedControllerCurrent& value) noexcept override {
        sampleEntered = true;
        while (blockSample) std::this_thread::yield();
        std::scoped_lock lock(mutex);
        value = {timestamp, GetTickCount64(), state, connected}; return connected;
    }
    bool Publish(ControllerReaderEventKind kind, GamepadState value = {}) {
        std::scoped_lock lock(mutex); state = value; ++timestamp;
        if (kind == ControllerReaderEventKind::Disconnected) connected = false;
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
    std::atomic_int opens{};
    bool failOpen{};
    bool OpenOwnedTarget() noexcept override { ++opens; return !failOpen; }
    bool Submit(const GamepadState& value) noexcept override { std::scoped_lock lock(mutex); last = value; ++reports; return true; }
    void RemoveOwnedTarget() noexcept override { ++removals; }
    bool Is(GamepadState value) { std::scoped_lock lock(mutex); return last == value; }
};
struct CompatibilityGuide final {
    std::atomic_uint8_t pending{};
    std::atomic_int polls{};
    static std::uint8_t Poll(void* context) noexcept {
        auto& self = *static_cast<CompatibilityGuide*>(context);
        ++self.polls;
        return self.pending.exchange(0);
    }
};
template<class Predicate> bool Until(Predicate predicate) {
    const auto start = GetTickCount64();
    do { if (predicate()) return true; Sleep(1); } while (GetTickCount64() - start < 1500);
    return false;
}
ControllerIsolationHostSession::TestDependencies Dependencies(
    const std::filesystem::path& path, Effects& effects, Source* source, Output& output,
    CompatibilityGuide* guide = nullptr) {
    ControllerIsolationHostSession::TestDependencies value;
    value.effects = &effects; value.source = source; value.output = &output; value.journalPath = path;
    if (guide) {
        value.pollGuideCompatibility = CompatibilityGuide::Poll;
        value.guideCompatibilityContext = guide;
    }
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
    for (int failure = 0; failure < 5; ++failure) {
        const auto path = root / (L"failure-" + std::to_wstring(failure)) / L"local-session.v1";
        Effects effects; const auto before = effects.state; Source source; Output output;
        effects.failHide = failure == 1; effects.throwHide = failure == 2;
        output.failOpen = failure == 4;
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
    Effects effects; Source source; Output output; CompatibilityGuide compatibilityGuide;
    ControllerIsolationHostSession owner(Dependencies(
        root / L"owner" / L"local-session.v1", effects, &source, output,
        &compatibilityGuide));
    std::wstring error; Check(owner.Start(true, error), "actual routing thread starts");
    Check(Until([&] { return compatibilityGuide.polls > 0; }),
        "routing thread owns physical XInput Guide polling");
    compatibilityGuide.pending = 1;
    std::uint64_t compatibilityEvent{};
    Check(Until([&] {
        return owner.PollGuide(compatibilityEvent, error) && compatibilityEvent != 0;
    }), "physical XInput Guide rising edge reaches the bounded local queue");
    Sleep(75);
    compatibilityEvent = 0;
    (void)owner.PollGuide(compatibilityEvent, error);
    Check(compatibilityEvent == 0,
        "physical XInput Guide edge is delivered once");
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
    bool sawPress{}, sawRelease{}, queuedPress{}, queuedRelease{}; unsigned guides{};
    Check(Until([&] {
        ControllerIsolationHostReading value; (void)owner.Poll(value, error);
        if (value.state == down) sawPress = true;
        if (sawPress && value.state == GamepadState{}) sawRelease = true;
        if (value.queuedInput && value.state == down) queuedPress = true;
        if (queuedPress && value.queuedInput && value.state == GamepadState{}) queuedRelease = true;
        if (value.guideEvent) ++guides;
        return sawPress && sawRelease && queuedPress && queuedRelease && guides == 2;
    }), "actual owner delivers short tap and Guide edges once");
    Check(queuedPress && queuedRelease, "actual owner marks preserved press/release as history");
    ControllerIsolationHostReading current;
    Check(Until([&] { (void)owner.Poll(current, error); return !current.queuedInput; }) &&
              current.remainingInputStates == 0 && current.state == GamepadState{},
          "drained history reconciles current neutral without resurrecting old input");
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
    Check(Until([&] { ControllerIsolationHostReading value; (void)owner.Poll(value, error); return value.progress == LocalControllerProgress::WaitingForController; }) && owner.active(),
        "selected-controller loss stays configured without legacy fallback");
    owner.Stop(); Check(output.removals == 1 && source.stopped > 0, "actual owner shutdown retires source and exact target once");
}

void VisibleStartupAndHeldDisconnect(const std::filesystem::path& root) {
    Effects effects; Source source; Output output;
    const auto path = root / L"visible-start" / L"local-session.v1";
    ControllerIsolationHostSession owner(Dependencies(path, effects, &source, output));
    std::wstring error;
    Check(owner.Start(true, error, nullptr, nullptr, true) && owner.progress() == LocalControllerProgress::Contained,
          "enable from visible overlay starts contained");
    GamepadState held{}; held.buttons = 0x1000;
    Check(source.Publish(ControllerReaderEventKind::Reading, held), "visible startup accepts physical input");
    Sleep(20);
    Check(output.Is({}), "visible startup never forwards held controls to game");
    owner.Stop();
    Check(output.removals == 1 && !effects.state.active && !std::filesystem::exists(path), "visible disable cleans owned policy");

    Effects heldEffects; Source heldSource; Output heldOutput;
    heldSource.state = held;
    ControllerIsolationHostSession pending(Dependencies(root / L"held-disconnect" / L"local-session.v1", heldEffects, &heldSource, heldOutput));
    Check(pending.Start(true, error) && pending.progress() == LocalControllerProgress::AwaitingNeutral,
          "held initial input starts cancellable neutral wait");
    Check(heldSource.Publish(ControllerReaderEventKind::Disconnected), "disconnect callback arrives during initial neutral wait");
    Check(Until([&] { return pending.progress() == LocalControllerProgress::WaitingForController; }),
          "disconnect during neutral wait returns to discovery");
    pending.Stop();
    Check(heldOutput.opens == 0 && !heldEffects.state.active, "disable during discovery restores access without a target");

    Effects racedEffects; Source racedSource; Output racedOutput;
    racedSource.connected = false;
    ControllerIsolationHostSession raced(Dependencies(root / L"discovery-race" / L"local-session.v1", racedEffects, &racedSource, racedOutput));
    Check(raced.Start(true, error) && raced.progress() == LocalControllerProgress::WaitingForController,
          "disconnect between discovery and first reading returns to waiting");
    raced.Stop();
    Check(racedOutput.opens == 0 && !racedEffects.state.active, "discovery race leaves no output or hidden device");
}

void AutomaticSelectionLifetime(const std::filesystem::path& root) {
    Effects effects; Source source; Output output;
    struct Discovery {
        std::atomic_bool available{};
        std::atomic_int calls{};
        SelectedControllerDescriptor descriptor;
        static SelectedControllerDiscoveryStatus Read(void* context, SelectedControllerDescriptor& result) noexcept {
            auto& self = *static_cast<Discovery*>(context); ++self.calls;
            if (!self.available.load()) return SelectedControllerDiscoveryStatus::Unavailable;
            result = self.descriptor; return SelectedControllerDiscoveryStatus::Ready;
        }
    } discovery;
    auto dependencies = Dependencies(root / L"automatic" / L"local-session.v1", effects, &source, output);
    discovery.descriptor = dependencies.descriptor;
    dependencies.discover = Discovery::Read; dependencies.discoveryContext = &discovery;
    ControllerIsolationHostSession owner(dependencies);
    std::wstring error;
    Check(owner.Start(true, error) && owner.progress() == LocalControllerProgress::WaitingForController && effects.writes == 0,
          "enabled without a controller waits without hiding devices");
    GamepadState held{}; held.buttons = 0x1000;
    { std::scoped_lock lock(source.mutex); source.state = held; }
    discovery.available = true;
    Check(Until([&] { return owner.progress() == LocalControllerProgress::AwaitingNeutral; }) && output.opens == 0,
          "newly connected held controller cannot create non-neutral output");
    Check(source.Publish(ControllerReaderEventKind::Reading, {}), "neutral replacement sample is admitted");
    Check(Until([&] { return owner.progress() == LocalControllerProgress::Playing; }) && output.opens == 1,
          "first controller becomes active after neutral");
    const auto selectedCalls = discovery.calls.load();
    discovery.available = false;
    Sleep(300);
    Check(discovery.calls == selectedCalls && owner.progress() == LocalControllerProgress::Playing,
          "unrelated catalogue changes do not interrupt the selected controller");
    Check(owner.PrepareOverlay(error), "automatic owner enters overlay before selected loss");
    Check(source.Publish(ControllerReaderEventKind::Disconnected), "selected loss is reported");
    Check(Until([&] { return owner.progress() == LocalControllerProgress::WaitingForController; }) &&
              output.opens == 1 && output.removals == 0 && output.Is({}),
          "selected loss retains one neutral virtual controller while waiting");
    discovery.descriptor.enrollment.enrollmentToken = 2;
    discovery.descriptor.enrollment.deviceId[0] = 2;
    discovery.descriptor.enrollment.containerId[0] = 4;
    { std::scoped_lock lock(source.mutex); source.connected = true; source.state = held; ++source.timestamp; }
    discovery.available = true;
    Check(Until([&] { return owner.progress() == LocalControllerProgress::AwaitingNeutral; }) && output.Is({}),
          "held replacement remains neutral before admission");
    Check(source.Publish(ControllerReaderEventKind::Reading, {}), "replacement neutral is published");
    Check(Until([&] { return owner.progress() == LocalControllerProgress::Contained; }) && output.opens == 1,
          "handoff preserves overlay containment and virtual identity");
    owner.Stop();
    Check(output.removals == 1 && !effects.state.active && effects.state.deviceInstanceIds.empty(),
          "disable retires the virtual device and restores exact owned policy");
}

void CompositeGamepadCleanup(const std::filesystem::path& root) {
    Effects effects; Source source; Output output;
    effects.state.active = true;
    effects.state.deviceInstanceIds.insert(L"HID\\other-controller");
    const auto before = effects.state;
    const auto path = root / L"composite" / L"local-session.v1";
    auto dependencies = Dependencies(path, effects, &source, output);
    dependencies.additionalHideTargets.insert(L"HID\\selected-gamepad-alias");
    ControllerIsolationHostSession owner(dependencies);
    std::wstring error;
    Check(owner.Start(true, error), "composite controller owner starts");
    Check(effects.state.deviceInstanceIds.size() == 3 &&
          effects.state.deviceInstanceIds.contains(L"HID\\selected-gamepad-alias") &&
          effects.state.deviceInstanceIds.contains(std::wstring(dependencies.descriptor.deviceInstanceId.view())),
          "both selected input identities are hidden without replacing unrelated policy");
    LocalPolicyRecord record; bool found{};
    Check(LoadLocalPolicy(path, record, found, error) && found &&
          record.policy.ownedDeviceInstanceIds.size() == 2 &&
          !record.policy.ownedDeviceInstanceIds.contains(L"HID\\other-controller"),
          "recovery journal owns every selected interface and excludes foreign devices");
    owner.Stop();
    Check(effects.state == before && !std::filesystem::exists(path) && output.removals == 1,
          "composite shutdown restores exactly the previous HidHide policy");
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
          PendingTransitionsAreSingleFlight(root); AutomaticSelectionLifetime(root); VisibleStartupAndHeldDisconnect(root); CompositeGamepadCleanup(root); }
    catch (const std::exception& error) { std::cerr << "FAILED: " << error.what() << "\nRetained: " << root << '\n'; return 1; }
    std::filesystem::remove_all(root);
    std::cout << "LocalControllerOwnerTests passed " << checks << " checks\n";
}
