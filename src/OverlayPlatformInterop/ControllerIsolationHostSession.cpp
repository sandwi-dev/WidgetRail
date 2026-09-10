#include "ControllerIsolationHostSession.h"

#include "ControllerIsolationInputTransport.h"
#include "ControllerIsolationReader.h"
#include "LocalControllerPolicy.h"
#include "ControllerIsolationRoutingSession.h"
#include "HidHideConfigurationAdapter.h"
#include "ViGEmOutputAdapter.h"
#include "../OverlayHost/GuideInputCompatibility.h"

#include <windows.h>
#include <shlobj.h>

#include <array>
#include <condition_variable>
#include <deque>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <optional>
#include <set>
#include <stop_token>
#include <thread>
#include <utility>
#include <vector>

namespace widgetrail::isolation {
namespace {

constexpr std::uint64_t kNeutralDwellInputMilliseconds = 10;
constexpr std::uint64_t kGuideCompatibilityPollMilliseconds = 25;

[[nodiscard]] std::filesystem::path CurrentExecutable() {
    std::array<wchar_t, 32'768> path{};
    const auto length = GetModuleFileNameW(nullptr, path.data(),
                                           static_cast<DWORD>(path.size()));
    return length == 0 || length >= path.size()
        ? std::filesystem::path{}
        : std::filesystem::path(std::wstring_view(path.data(), length));
}

class LocalOutput final : public ControllerIsolationOutput {
public:
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
    LocalOutput() noexcept : adapter_(testApi_) {}
#else
    LocalOutput() noexcept : adapter_(OfficialViGEmApi()) {}
#endif
    bool OpenOwnedTarget() noexcept override { return adapter_.Open(); }
    bool Submit(const GamepadState& state) noexcept override { return adapter_.Submit(state); }
    bool TakeLatestFeedback(ControllerRumbleState& state) noexcept override {
        return adapter_.TakeLatestFeedback(state);
    }
    void RemoveOwnedTarget() noexcept override { adapter_.RemoveOwnedTarget(); }
private:
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
    inline static const ViGEmApi testApi_{};
#endif
    ViGEmOutputAdapter adapter_;
};

// A selected-reader session may retire while the enabled host keeps one game
// device. Every handoff neutralizes and discards old feedback before reuse.
class RetainedControllerOutput final : public ControllerIsolationOutput {
public:
    explicit RetainedControllerOutput(ControllerIsolationOutput& output) : output_(output) {}
    ~RetainedControllerOutput() override { if (open_) output_.RemoveOwnedTarget(); }
    bool OpenOwnedTarget() noexcept override {
        if (!open_) open_ = output_.OpenOwnedTarget();
        ControllerRumbleState ignored;
        (void)output_.TakeLatestFeedback(ignored);
        return open_;
    }
    bool Submit(const GamepadState& state) noexcept override { return open_ && output_.Submit(state); }
    bool TakeLatestFeedback(ControllerRumbleState& state) noexcept override { return output_.TakeLatestFeedback(state); }
    void RemoveOwnedTarget() noexcept override {
        if (open_ && !output_.Submit({})) { output_.RemoveOwnedTarget(); open_ = false; }
        ControllerRumbleState ignored;
        (void)output_.TakeLatestFeedback(ignored);
    }
private:
    ControllerIsolationOutput& output_;
    bool open_{};
};

class LocalQueues final : public ControllerIsolationGuideSink,
                          public ControllerIsolationHostInputSink {
public:
    void SetNotify(ControllerIsolationHostSession::Notify notify, void* context) noexcept {
        notify_ = notify; context_ = context;
    }
    void NotifyPlatform() const noexcept { if (notify_) notify_(context_); }
    bool BeginInteraction() noexcept {
        std::scoped_lock l(mutex_);
        latestInput_.reset();
        return input_.BeginInteraction();
    }
    void RetireInteraction() noexcept {
        std::scoped_lock l(mutex_);
        input_.RetireInteraction();
        latestInput_.reset();
    }
    bool PublishInput(const GamepadState& state, std::uint64_t ordinal) noexcept override {
        std::scoped_lock l(mutex_);
        if (!input_.Push(state, ordinal)) return false;
        latestInput_ = state;
        return true;
    }
    bool PublishGuide(bool pressed, std::uint64_t, std::uint64_t ordinal) noexcept override {
        {
            std::scoped_lock lock(mutex_);
            if (guides_.size() == 32) return false;
            guides_.push_back(ordinal | (pressed ? 0ULL : (1ULL << 63)));
        }
        NotifyPlatform();
        return true;
    }
    bool Pop(GamepadState& state, std::uint32_t& remaining) noexcept {
        std::scoped_lock l(mutex_);
        const auto next = input_.TakeNext();
        if (!next) {
            // Published history and its current state share this lock, so a
            // just-consumed release cannot fall back to an older routing snapshot.
            if (latestInput_) state = *latestInput_;
            return false;
        }
        state = *next;
        remaining = static_cast<std::uint32_t>(input_.size()); return true;
    }
    std::uint64_t TakeGuide() noexcept {
        std::scoped_lock l(mutex_);
        if (guides_.empty()) return 0;
        const auto result = guides_.front(); guides_.pop_front();
        return result;
    }
private:
    mutable std::mutex mutex_;
    ControllerIsolationInputEventQueue input_;
    std::optional<GamepadState> latestInput_;
    std::deque<std::uint64_t> guides_;
    ControllerIsolationHostSession::Notify notify_{};
    void* context_{};
};

[[nodiscard]] LocalControllerProgress ConvertProgress(ControllerIsolationRoutingState state) {
    switch (state) {
    case ControllerIsolationRoutingState::WaitingForInitialNeutral: return LocalControllerProgress::AwaitingNeutral;
    case ControllerIsolationRoutingState::PreparedNeutral: return LocalControllerProgress::Preparing;
    case ControllerIsolationRoutingState::AwaitingPlaying: return LocalControllerProgress::AwaitingNeutral;
    case ControllerIsolationRoutingState::Playing: return LocalControllerProgress::Playing;
    case ControllerIsolationRoutingState::OverlayInteraction: return LocalControllerProgress::Contained;
    case ControllerIsolationRoutingState::Fault: return LocalControllerProgress::Fault;
    case ControllerIsolationRoutingState::Disabled: return LocalControllerProgress::Disabled;
    }
    return LocalControllerProgress::Fault;
}

} // namespace

struct ControllerIsolationHostSession::Impl final {
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
    std::optional<TestDependencies> test;
#endif
    std::mutex mutex;
    std::condition_variable changed;
    std::jthread thread;
    LocalQueues queues;
    LocalControllerProgress progress{LocalControllerProgress::Disabled};
    GamepadState latest{};
    std::wstring diagnostic;
    std::deque<std::wstring> pendingDiagnostics;
    bool startupDone{}, desiredOverlay{};
    bool retryDiscovery{};
    bool preparingInput{};
    std::uint64_t sessionGeneration{};
    std::uint64_t desiredGeneration{}, appliedGeneration{};

    void QueueDiagnostic(std::wstring message) {
        {
            std::scoped_lock lock(mutex);
            if (pendingDiagnostics.size() >= 8) return;
            pendingDiagnostics.push_back(std::move(message));
        }
        queues.NotifyPlatform();
    }

    [[nodiscard]] std::wstring TakeDiagnosticLocked() {
        if (!pendingDiagnostics.empty()) {
            auto result = std::move(pendingDiagnostics.front());
            pendingDiagnostics.pop_front();
            return result;
        }
        return progress == LocalControllerProgress::Fault ? diagnostic : L"";
    }

    void FinishStartup(const std::wstring& message) noexcept {
        std::scoped_lock lock(mutex);
        diagnostic = message.empty() ? L"Controller isolation operation failed." : message; progress = LocalControllerProgress::Fault;
        startupDone = true; changed.notify_all();
    }

    void Run(std::stop_token stop) noexcept {
        std::wstring cleanupFailure;
        LocalOwnerLease owner;
        try {
            if (!owner.Acquire(cleanupFailure)) { FinishStartup(cleanupFailure); return; }
            LocalOutput nativeOutput;
            ControllerIsolationOutput& underlying =
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
                test ? *test->output :
#endif
                nativeOutput;
            RetainedControllerOutput output(underlying);
            do {
                retryDiscovery = false;
                RunOwned(stop, cleanupFailure, output);
                if (!cleanupFailure.empty() || !retryDiscovery || stop.stop_requested()) break;
                for (unsigned wait = 0; wait < 25 && !stop.stop_requested(); ++wait) Sleep(10);
            } while (!stop.stop_requested());
        } catch (const std::exception&) {
            FinishStartup(L"Controller isolation setup/routing exception.");
        } catch (...) {
            FinishStartup(L"Controller isolation unexpected setup/routing exception.");
        }
        if (!cleanupFailure.empty()) {
            std::scoped_lock lock(mutex);
            if (!diagnostic.empty()) diagnostic += L" ";
            diagnostic += cleanupFailure;
            progress = LocalControllerProgress::Fault;
            startupDone = true;
            changed.notify_all();
        }
    }

    void RunOwned(std::stop_token stop, std::wstring& cleanupFailure, ControllerIsolationOutput& output) {
        const auto journalPath =
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
            test ? test->journalPath :
#endif
            LocalControllerJournalPath();
        if (journalPath.empty()) { FinishStartup(L"Controller isolation LocalAppData unavailable."); return; }
        if (std::filesystem::exists(journalPath.parent_path() / L"session.bin")) {
            FinishStartup(L"A legacy controller-isolation journal requires original recovery."); return;
        }
        NativeLocalPolicyEffects nativeEffects;
        LocalPolicyEffects& hidhide =
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
            test ? *test->effects :
#endif
            nativeEffects;
        LocalPolicyRecord prior; bool priorFound{}; std::wstring failure;
        if (!LoadLocalPolicy(journalPath, prior, priorFound, failure) ||
            (priorFound && !RestoreLocalPolicy(journalPath, prior, hidhide, failure))) {
            FinishStartup(failure); return;
        }
        SelectedControllerDescriptor descriptor;
        const auto discovery =
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
            test ? (test->discover ? test->discover(test->discoveryContext, descriptor) :
                (descriptor = test->descriptor, SelectedControllerDiscoveryStatus::Ready)) :
#endif
            DiscoverCurrentPhysicalController(GetTickCount64() | 1, descriptor);
        if (discovery !=
            SelectedControllerDiscoveryStatus::Ready) {
            if (discovery == SelectedControllerDiscoveryStatus::Unavailable) {
                retryDiscovery = true;
                std::scoped_lock lock(mutex);
                latest = {}; progress = LocalControllerProgress::WaitingForController;
                startupDone = true; changed.notify_all();
                return;
            }
            FinishStartup(L"No eligible physical controller could be selected safely."); return;
        }
        HidHideSnapshot before; std::uint32_t error{}; std::wstring executableDevicePath;
        const bool pathResolved =
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
            test ? (executableDevicePath = L"test-reader", true) :
#endif
            ControllerIsolationDosDevicePath(CurrentExecutable().wstring(), executableDevicePath, error);
        if (!pathResolved || !hidhide.Read(before, error)) {
            FinishStartup(L"Controller isolation HidHide preflight error=" + std::to_wstring(error)); return;
        }
        std::set<std::wstring> hideTargets{std::wstring(descriptor.deviceInstanceId.view())};
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
        if (test) hideTargets.insert(test->additionalHideTargets.begin(), test->additionalHideTargets.end());
        else
#endif
        if (!DiscoverSelectedControllerHideTargets(descriptor.deviceInstanceId, hideTargets)) {
            FinishStartup(L"Selected controller gamepad interfaces could not be identified safely."); return;
        }
        const auto plan = PlanHidHideApply(before, executableDevicePath, hideTargets);
        LocalPolicyRecord record;
        if (!plan.plan || !SaveLocalPolicy(journalPath, plan.plan->journal, record, failure)) {
            FinishStartup(failure.empty() ? L"Controller isolation policy plan rejected." : failure); return;
        }
        // Every return and exception after publication goes through exact-owned
        // restoration. Reader/output objects are declared later and retire first.
        LocalPolicyCleanup cleanup(journalPath, record, hidhide, cleanupFailure);
        HidHideSnapshot observed;
        if (!hidhide.Apply(before, plan.plan->desired, observed, error)) {
            FinishStartup(L"Controller isolation HidHide apply error=" + std::to_wstring(error)); return;
        }
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
        const bool guideCompatibilityAvailable =
            test && test->pollGuideCompatibility;
#else
        widgetrail::input::XInputGuideCompatibility guideCompatibility;
        const bool guideCompatibilityAvailable = guideCompatibility.Initialize();
#endif
        auto ownedSource =
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
            test ? std::unique_ptr<SelectedControllerSource>{} :
#endif
            CreateGameInputSelectedControllerReader();
        auto* source =
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
            test ? test->source :
#endif
            ownedSource.get();
        if (!source) { FinishStartup(L"Controller isolation reader allocation failed."); return; }
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
        if (test && test->throwAfterApply) throw std::runtime_error("injected post-apply allocation failure");
#endif
        ControllerIsolationRoutingSession routing(*source, output, queues, {}, &queues);
        if (sessionGeneration == std::numeric_limits<std::uint64_t>::max()) {
            FinishStartup(L"Controller session generation exhausted."); return;
        }
        const RoutingAuthority authority{++sessionGeneration, sessionGeneration, 1, GetTickCount64() | 1};
        auto result = routing.PrepareSession(authority, descriptor.enrollment, GetTickCount64(), true);
        if (result == ControllerIsolationRoutingResult::Waiting) {
            { std::scoped_lock lock(mutex); preparingInput = true; progress = LocalControllerProgress::AwaitingNeutral;
              latest = {}; startupDone = true; changed.notify_all(); }
            while (!stop.stop_requested() && result == ControllerIsolationRoutingResult::Waiting) {
                result = routing.Pump(GetTickCount64()); Sleep(1);
            }
            { std::scoped_lock lock(mutex); preparingInput = false; }
        }
        if (stop.stop_requested()) return;
        if (result != ControllerIsolationRoutingResult::Applied) {
            if (result == ControllerIsolationRoutingResult::ReaderUnavailable) {
                retryDiscovery = true;
                std::scoped_lock lock(mutex);
                latest = {}; progress = LocalControllerProgress::WaitingForController;
                startupDone = true; changed.notify_all();
                return;
            }
            FinishStartup(L"Controller isolation reader/output prepare failed result=" +
                std::to_wstring(static_cast<unsigned>(result))); return;
        }
        bool startContained{};
        { std::scoped_lock lock(mutex); startContained = desiredOverlay; }
        if (startContained) {
            if (!queues.BeginInteraction()) { FinishStartup(L"Controller interaction generation exhausted."); return; }
            result = routing.HoldContained(authority, GetTickCount64());
        } else {
            result = routing.CommitPlaying(authority, kNeutralDwellInputMilliseconds, GetTickCount64());
        }
        const auto startupAt = GetTickCount64();
        while (!stop.stop_requested() && !startContained && routing.state() != ControllerIsolationRoutingState::Playing &&
               result != ControllerIsolationRoutingResult::Faulted &&
               GetTickCount64() - startupAt < 2'000) {
            result = routing.Pump(GetTickCount64()); Sleep(1);
        }
        if (routing.state() != ControllerIsolationRoutingState::Playing &&
            routing.state() != ControllerIsolationRoutingState::OverlayInteraction) {
            if (routing.fault() == RoutingFault::DeviceDisconnected) {
                retryDiscovery = true;
                std::scoped_lock lock(mutex);
                latest = {}; progress = LocalControllerProgress::WaitingForController;
                startupDone = true; changed.notify_all();
                return;
            }
            FinishStartup(L"Controller isolation startup neutral barrier failed."); return;
        }
        { std::scoped_lock lock(mutex); progress = ConvertProgress(routing.state());
          startupDone = true; changed.notify_all(); }
        QueueDiagnostic(guideCompatibilityAvailable
            ? L"Controller isolation physical XInput Guide compatibility polling active cadence-ms=25"
            : L"Controller isolation physical XInput Guide compatibility polling unavailable");
        auto nextGuideCompatibilityPoll = GetTickCount64();
        std::uint64_t guideCompatibilityOrdinal{};
        std::wstring firstFailure;
        LocalControllerOwnerAction inFlight{LocalControllerOwnerAction::None};
        while (!stop.stop_requested()) {
            bool wantsOverlay{};
            std::uint64_t generation{};
            { std::scoped_lock lock(mutex); wantsOverlay = desiredOverlay; generation = desiredGeneration; }
            const auto action = inFlight == LocalControllerOwnerAction::None
                ? DecideLocalControllerOwnerAction(ConvertProgress(routing.state()), wantsOverlay)
                : LocalControllerOwnerAction::None;
            result = ControllerIsolationRoutingResult::Applied;
            if (action == LocalControllerOwnerAction::Enter || action == LocalControllerOwnerAction::Recontain) {
                if (!queues.BeginInteraction()) {
                    firstFailure = L"Controller isolation interaction generation exhausted."; break;
                }
                result = action == LocalControllerOwnerAction::Enter
                    ? routing.EnterOverlay(authority, GetTickCount64())
                    : routing.HoldContained(authority, GetTickCount64());
            } else if (action == LocalControllerOwnerAction::Close) {
                queues.RetireInteraction();
                result = routing.CloseOverlay(authority, kNeutralDwellInputMilliseconds, GetTickCount64());
            }
            if (result != ControllerIsolationRoutingResult::Applied &&
                result != ControllerIsolationRoutingResult::Waiting) {
                firstFailure = L"Controller isolation local transition rejected result=" +
                    std::to_wstring(static_cast<unsigned>(result)); break;
            }
            if (action != LocalControllerOwnerAction::None &&
                result == ControllerIsolationRoutingResult::Waiting)
                inFlight = action;
            const auto now = GetTickCount64();
            result = routing.Pump(now);
            if (guideCompatibilityAvailable &&
                now >= nextGuideCompatibilityPoll) {
                nextGuideCompatibilityPoll =
                    now + kGuideCompatibilityPollMilliseconds;
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
                const auto slots = test->pollGuideCompatibility(
                    test->guideCompatibilityContext);
#else
                const auto slots = guideCompatibility.PollRisingEdges();
#endif
                if (slots != 0) {
                    (void)queues.PublishGuide(
                        true, now * 1'000, ++guideCompatibilityOrdinal);
                }
            }
            const bool transitionComplete =
                ((inFlight == LocalControllerOwnerAction::Enter ||
                  inFlight == LocalControllerOwnerAction::Recontain) &&
                 routing.state() == ControllerIsolationRoutingState::OverlayInteraction) ||
                (inFlight == LocalControllerOwnerAction::Close &&
                 (routing.state() == ControllerIsolationRoutingState::AwaitingPlaying ||
                  routing.state() == ControllerIsolationRoutingState::Playing));
            if (transitionComplete || result != ControllerIsolationRoutingResult::Waiting)
                inFlight = LocalControllerOwnerAction::None;
            { std::scoped_lock lock(mutex); progress = ConvertProgress(routing.state());
              latest = routing.currentState();
              const bool desiredApplied = wantsOverlay
                  ? routing.state() == ControllerIsolationRoutingState::OverlayInteraction
                  : routing.state() == ControllerIsolationRoutingState::Playing ||
                    routing.state() == ControllerIsolationRoutingState::AwaitingPlaying;
              if (inFlight == LocalControllerOwnerAction::None && desiredApplied &&
                  generation == desiredGeneration)
                  appliedGeneration = generation;
              changed.notify_all(); }
            if (result == ControllerIsolationRoutingResult::Faulted) {
                retryDiscovery = routing.fault() == RoutingFault::DeviceDisconnected;
                firstFailure = L"Controller isolation routing fault=" +
                    std::to_wstring(static_cast<unsigned>(routing.fault())); break;
            }
            Sleep(1);
        }
        queues.RetireInteraction();
        if (routing.state() != ControllerIsolationRoutingState::Disabled) (void)routing.Stop(authority);
        (void)cleanup.Finish();
        std::scoped_lock lock(mutex);
        if (retryDiscovery && cleanupFailure.empty()) {
            latest = {}; progress = LocalControllerProgress::WaitingForController;
        } else if (!firstFailure.empty()) {
            diagnostic = firstFailure;
            progress = LocalControllerProgress::Fault;
        } else {
            progress = stop.stop_requested() ? LocalControllerProgress::Disabled : LocalControllerProgress::Fault;
        }
        changed.notify_all();
    }
};

ControllerIsolationHostSession::ControllerIsolationHostSession() noexcept = default;
ControllerIsolationHostSession::~ControllerIsolationHostSession() { Stop(); }
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
ControllerIsolationHostSession::ControllerIsolationHostSession(TestDependencies dependencies)
    : impl_(std::make_unique<Impl>()) { impl_->test = std::move(dependencies); }
#endif

bool ControllerIsolationHostSession::Start(bool enabled, std::wstring& diagnostic,
    Notify notify, void* context, bool overlayVisible) noexcept {
    if (!enabled) return false;
    try {
        if (!impl_) impl_ = std::make_unique<Impl>();
        if (impl_->thread.joinable()) { diagnostic = L"Controller isolation already configured."; return false; }
        impl_->queues.SetNotify(notify, context);
        impl_->desiredOverlay = overlayVisible;
        impl_->thread = std::jthread([this](std::stop_token stop) { impl_->Run(stop); });
    } catch (...) {
        diagnostic = L"Controller isolation thread/allocation failed.";
        return false;
    }
    std::unique_lock lock(impl_->mutex);
    impl_->changed.wait(lock, [this] { return impl_->startupDone; });
    diagnostic = impl_->diagnostic;
    return impl_->progress != LocalControllerProgress::Fault &&
        impl_->progress != LocalControllerProgress::Disabled;
}

bool ControllerIsolationHostSession::PrepareOverlay(std::wstring& diagnostic) noexcept {
    if (!impl_) { diagnostic = L"Controller isolation is not configured."; return false; }
    std::unique_lock lock(impl_->mutex);
    if (impl_->progress == LocalControllerProgress::Fault) { diagnostic = impl_->diagnostic; return false; }
    impl_->desiredOverlay = true;
    const auto requestedGeneration = ++impl_->desiredGeneration;
    if (impl_->progress == LocalControllerProgress::WaitingForController ||
        impl_->preparingInput)
        return true;
    (void)impl_->changed.wait_for(lock, std::chrono::milliseconds(500), [this, requestedGeneration] {
        return (impl_->progress == LocalControllerProgress::Contained &&
                impl_->appliedGeneration == requestedGeneration) ||
               impl_->progress == LocalControllerProgress::Fault;
    });
    const bool contained = impl_->progress == LocalControllerProgress::Contained &&
        impl_->appliedGeneration == requestedGeneration && impl_->desiredOverlay;
    if (!contained) {
        impl_->desiredOverlay = false;
        ++impl_->desiredGeneration;
    }
    diagnostic = impl_->diagnostic;
    if (!contained && diagnostic.empty()) diagnostic = L"Controller isolation containment did not complete; open cancelled.";
    return contained;
}

void ControllerIsolationHostSession::CloseOverlay() noexcept {
    if (!impl_) return;
    std::scoped_lock lock(impl_->mutex);
    impl_->desiredOverlay = false;
    ++impl_->desiredGeneration;
}

bool ControllerIsolationHostSession::Poll(ControllerIsolationHostReading& reading,
                                           std::wstring& diagnostic) noexcept {
    reading = {};
    if (!impl_) return false;
    std::scoped_lock lock(impl_->mutex);
    reading.progress = impl_->progress; reading.state = impl_->latest;
    reading.queuedInput = impl_->queues.Pop(reading.state, reading.remainingInputStates);
    reading.guideEvent = impl_->queues.TakeGuide();
    diagnostic = impl_->TakeDiagnosticLocked();
    return impl_->progress != LocalControllerProgress::Fault;
}

bool ControllerIsolationHostSession::PollGuide(std::uint64_t& guideEvent,
                                                std::wstring& diagnostic) noexcept {
    guideEvent = 0;
    if (!impl_) return false;
    std::scoped_lock lock(impl_->mutex);
    guideEvent = impl_->queues.TakeGuide();
    diagnostic = impl_->TakeDiagnosticLocked();
    return impl_->progress != LocalControllerProgress::Fault;
}

bool ControllerIsolationHostSession::active() const noexcept {
    if (!impl_) return false;
    std::scoped_lock lock(impl_->mutex);
    return LocalControllerIsolationConfigured(impl_->progress);
}

void ControllerIsolationHostSession::Stop() noexcept {
    if (!impl_) return;
    if (impl_->thread.joinable()) { impl_->thread.request_stop(); impl_->thread.join(); }
}

void ControllerIsolationHostSession::Reset() noexcept {
    Stop();
    impl_.reset();
}

LocalControllerProgress ControllerIsolationHostSession::progress() const noexcept {
    if (!impl_) return LocalControllerProgress::Disabled;
    std::scoped_lock lock(impl_->mutex);
    return impl_->progress;
}

} // namespace widgetrail::isolation
