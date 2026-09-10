#include "ControllerIsolationHostSession.h"

#include "ControllerIsolationInputTransport.h"
#include "ControllerIsolationReader.h"
#include "LocalControllerPolicy.h"
#include "ControllerIsolationRoutingSession.h"
#include "HidHideConfigurationAdapter.h"
#include "ViGEmOutputAdapter.h"

#include <windows.h>
#include <shlobj.h>

#include <array>
#include <condition_variable>
#include <deque>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <set>
#include <stop_token>
#include <thread>
#include <utility>
#include <vector>

namespace widgetrail::isolation {
namespace {

constexpr std::uint64_t kNeutralDwellInputMilliseconds = 10;

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

class LocalQueues final : public ControllerIsolationGuideSink,
                          public ControllerIsolationHostInputSink {
public:
    void SetNotify(ControllerIsolationHostSession::Notify notify, void* context) noexcept {
        notify_ = notify; context_ = context;
    }
    bool BeginInteraction() noexcept { std::scoped_lock l(mutex_); return input_.BeginInteraction(); }
    void RetireInteraction() noexcept { std::scoped_lock l(mutex_); input_.RetireInteraction(); }
    bool PublishInput(const GamepadState& state, std::uint64_t ordinal) noexcept override {
        std::scoped_lock l(mutex_); return input_.Push(state, ordinal);
    }
    bool PublishGuide(bool pressed, std::uint64_t, std::uint64_t ordinal) noexcept override {
        {
            std::scoped_lock lock(mutex_);
            if (guides_.size() == 32) return false;
            guides_.push_back(ordinal | (pressed ? 0ULL : (1ULL << 63)));
        }
        if (notify_) notify_(context_);
        return true;
    }
    bool Pop(GamepadState& state, std::uint32_t& remaining) noexcept {
        std::scoped_lock l(mutex_);
        const auto next = input_.TakeNext();
        if (!next) return false;
        state = *next;
        remaining = static_cast<std::uint32_t>(input_.size()); return true;
    }
    std::uint64_t TakeGuide() noexcept {
        std::scoped_lock l(mutex_);
        if (guides_.empty()) return 0;
        const auto result = guides_.front(); guides_.pop_front(); return result;
    }
private:
    std::mutex mutex_;
    ControllerIsolationInputEventQueue input_;
    std::deque<std::uint64_t> guides_;
    ControllerIsolationHostSession::Notify notify_{};
    void* context_{};
};

[[nodiscard]] LocalControllerProgress ConvertProgress(ControllerIsolationRoutingState state) {
    switch (state) {
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
    bool startupDone{}, desiredOverlay{};
    std::uint64_t desiredGeneration{}, appliedGeneration{};

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
            RunOwned(stop, cleanupFailure);
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

    void RunOwned(std::stop_token stop, std::wstring& cleanupFailure) {
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
            test ? (descriptor = test->descriptor, SelectedControllerDiscoveryStatus::Ready) :
#endif
            DiscoverCurrentPhysicalController(GetTickCount64() | 1, descriptor);
        if (discovery !=
            SelectedControllerDiscoveryStatus::Ready) {
            FinishStartup(L"Controller isolation requires exactly one known physical controller."); return;
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
        const auto plan = PlanHidHideApply(before, executableDevicePath,
            {std::wstring(descriptor.deviceInstanceId.view())});
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
        auto ownedSource =
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
            test ? std::unique_ptr<SelectedControllerSource>{} :
#endif
            CreateGameInputSelectedControllerReader();
        LocalOutput nativeOutput;
        auto* source =
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
            test ? test->source :
#endif
            ownedSource.get();
        ControllerIsolationOutput& output =
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
            test ? *test->output :
#endif
            nativeOutput;
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
        if (test && test->throwAfterApply) throw std::runtime_error("injected post-apply allocation failure");
#endif
        if (!source) { FinishStartup(L"Controller isolation reader allocation failed."); return; }
        ControllerIsolationRoutingSession routing(*source, output, queues, {}, &queues);
        const RoutingAuthority authority{1, 1, 1, GetTickCount64() | 1};
        auto result = routing.PrepareSession(authority, descriptor.enrollment, GetTickCount64());
        if (result != ControllerIsolationRoutingResult::Applied) {
            FinishStartup(L"Controller isolation reader/output prepare failed result=" +
                std::to_wstring(static_cast<unsigned>(result))); return;
        }
        result = routing.CommitPlaying(authority, kNeutralDwellInputMilliseconds, GetTickCount64());
        const auto startupAt = GetTickCount64();
        while (!stop.stop_requested() && routing.state() != ControllerIsolationRoutingState::Playing &&
               result != ControllerIsolationRoutingResult::Faulted &&
               GetTickCount64() - startupAt < 2'000) {
            result = routing.Pump(GetTickCount64()); Sleep(1);
        }
        if (routing.state() != ControllerIsolationRoutingState::Playing) {
            FinishStartup(L"Controller isolation startup neutral barrier failed."); return;
        }
        { std::scoped_lock lock(mutex); progress = LocalControllerProgress::Playing;
          startupDone = true; changed.notify_all(); }
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
            result = routing.Pump(GetTickCount64());
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
                firstFailure = L"Controller isolation routing fault=" +
                    std::to_wstring(static_cast<unsigned>(routing.fault())); break;
            }
            Sleep(1);
        }
        queues.RetireInteraction();
        if (routing.state() != ControllerIsolationRoutingState::Disabled) (void)routing.Stop(authority);
        (void)cleanup.Finish();
        std::scoped_lock lock(mutex);
        if (!firstFailure.empty()) {
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
    Notify notify, void* context) noexcept {
    if (!enabled) return false;
    try {
        if (!impl_) impl_ = std::make_unique<Impl>();
        if (impl_->thread.joinable()) { diagnostic = L"Controller isolation already configured."; return false; }
        impl_->queues.SetNotify(notify, context);
        impl_->thread = std::jthread([this](std::stop_token stop) { impl_->Run(stop); });
    } catch (...) {
        diagnostic = L"Controller isolation thread/allocation failed.";
        return false;
    }
    std::unique_lock lock(impl_->mutex);
    impl_->changed.wait(lock, [this] { return impl_->startupDone; });
    diagnostic = impl_->diagnostic;
    return impl_->progress == LocalControllerProgress::Playing;
}

bool ControllerIsolationHostSession::PrepareOverlay(std::wstring& diagnostic) noexcept {
    if (!impl_) { diagnostic = L"Controller isolation is not configured."; return false; }
    std::unique_lock lock(impl_->mutex);
    if (impl_->progress == LocalControllerProgress::Fault) { diagnostic = impl_->diagnostic; return false; }
    impl_->desiredOverlay = true;
    const auto requestedGeneration = ++impl_->desiredGeneration;
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
    (void)impl_->queues.Pop(reading.state, reading.remainingInputStates);
    reading.guideEvent = impl_->queues.TakeGuide(); diagnostic = impl_->diagnostic;
    return impl_->progress != LocalControllerProgress::Fault;
}

bool ControllerIsolationHostSession::PollGuide(std::uint64_t& guideEvent,
                                                std::wstring& diagnostic) noexcept {
    guideEvent = 0;
    if (!impl_) return false;
    std::scoped_lock lock(impl_->mutex);
    guideEvent = impl_->queues.TakeGuide(); diagnostic = impl_->diagnostic;
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

} // namespace widgetrail::isolation
