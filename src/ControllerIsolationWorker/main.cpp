#include "../OverlayPlatformInterop/ControllerIsolationProcessOwner.h"
#include "../OverlayPlatformInterop/ControllerIsolationInputTransport.h"

#if !defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
#include "../OverlayPlatformInterop/ControllerIsolationRoutingSession.h"
#include "../OverlayPlatformInterop/ViGEmOutputAdapter.h"
#endif

#include <windows.h>

#include <array>
#include <string>

namespace {

using namespace widgetrail::isolation;

#if defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
constexpr wchar_t kExpectedParent[] = L"ControllerIsolationGuardianTestHost.exe";
#else
constexpr wchar_t kExpectedParent[] = L"ControllerIsolationGuardian.exe";

class WorkerOutput final : public ControllerIsolationOutput {
public:
    WorkerOutput() noexcept : adapter_(OfficialViGEmApi()) {}

    bool OpenOwnedTarget() noexcept override { return adapter_.Open(); }
    bool Submit(const GamepadState& state) noexcept override {
        return adapter_.Submit(state);
    }
    bool TakeLatestFeedback(ControllerRumbleState& state) noexcept override {
        return adapter_.TakeLatestFeedback(state);
    }
    void RemoveOwnedTarget() noexcept override {
        adapter_.RemoveOwnedTarget();
    }
    [[nodiscard]] ViGEmAdapterStatus status() const noexcept {
        return adapter_.status();
    }

private:
    ViGEmOutputAdapter adapter_;
};

class PendingGuideSink final : public ControllerIsolationGuideSink {
public:
    bool PublishGuide(
        const bool pressed,
        const std::uint64_t sourceTimestampMicroseconds,
        const std::uint64_t ingressOrdinal) noexcept override {
        if (count_ == events_.size()) return false;
        events_[(head_ + count_) % events_.size()] = {
            pressed, sourceTimestampMicroseconds, ingressOrdinal};
        ++count_;
        return true;
    }

    [[nodiscard]] std::uint64_t TakeNext() noexcept {
        if (count_ == 0) return 0;
        const auto event = events_[head_];
        head_ = (head_ + 1) % events_.size();
        --count_;
        // High bit identifies release; zero is reserved for no event.
        return event.ingressOrdinal |
            (event.pressed ? 0ULL : (1ULL << 63));
    }

private:
    struct Event final {
        bool pressed{};
        std::uint64_t sourceTimestampMicroseconds{};
        std::uint64_t ingressOrdinal{};
    };
    std::array<Event, 32> events_{};
    std::size_t head_{};
    std::size_t count_{};
};

class PendingHostInputSink final : public ControllerIsolationHostInputSink {
public:
    [[nodiscard]] bool BeginInteraction() noexcept {
        return queue_.BeginInteraction();
    }

    void RetireInteraction() noexcept { queue_.RetireInteraction(); }

    bool PublishInput(
        const GamepadState& state,
        const std::uint64_t ingressOrdinal) noexcept override {
        return queue_.Push(state, ingressOrdinal);
    }

    [[nodiscard]] ControllerInputBatch TakeBatch() noexcept {
        return queue_.TakeBatch();
    }

private:
    ControllerIsolationInputEventQueue queue_;
};

[[nodiscard]] bool Applied(
    const ControllerIsolationRoutingResult result) noexcept {
    return result == ControllerIsolationRoutingResult::Applied;
}

[[nodiscard]] ControlProgress Progress(
    const ControllerIsolationRoutingState state,
    const ControllerIsolationRoutingResult result) noexcept {
    if (result == ControllerIsolationRoutingResult::Waiting &&
        state == ControllerIsolationRoutingState::Playing) {
        return ControlProgress::Queued;
    }
    switch (state) {
    case ControllerIsolationRoutingState::PreparedNeutral:
        return ControlProgress::PreparedNeutral;
    case ControllerIsolationRoutingState::AwaitingPlaying:
        return ControlProgress::AwaitingNeutral;
    case ControllerIsolationRoutingState::Playing:
        return ControlProgress::Playing;
    case ControllerIsolationRoutingState::OverlayInteraction:
        return ControlProgress::Contained;
    case ControllerIsolationRoutingState::Disabled:
    case ControllerIsolationRoutingState::Fault:
        return ControlProgress::Terminal;
    }
    return ControlProgress::Terminal;
}
#endif

} // namespace

int wmain(const int argumentCount, wchar_t** arguments) {
    std::wstring error;
    auto channel = ChildControlChannel::Open(
        argumentCount, arguments, kExpectedParent, error);
    if (!channel) return ERROR_ACCESS_DENIED;

    ControlFrame hello;
    if (channel->WaitForRequest(
            ControllerIsolationStartupTimeoutMilliseconds, hello) !=
            ChildWaitResult::Request ||
        hello.kind != ControlMessageKind::Hello) {
        return ERROR_INVALID_DATA;
    }

#if !defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
    // Construction is deliberately dormant. PrepareSession is the only
    // command allowed to acquire GameInput and then create a ViGEm target.
    auto source = CreateGameInputSelectedControllerReader();
    WorkerOutput output;
    PendingGuideSink guideSink;
    PendingHostInputSink hostInputSink;
    if (!source) return ERROR_NOT_ENOUGH_MEMORY;
    ControllerIsolationRoutingSession routing(
        *source, output, guideSink, {}, &hostInputSink);
#endif

    if (!channel->Reply(
            ControlMessageKind::HelloAccepted, hello, 0,
            GetCurrentProcessId())) {
        return ERROR_BROKEN_PIPE;
    }

    for (;;) {
#if !defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
        if (routing.state() != ControllerIsolationRoutingState::Disabled) {
            const auto serviced =
                ServiceControllerIsolationRoutingBeforeControlWait(
                    routing, GetTickCount64());
            if (serviced != ControllerIsolationRoutingResult::Applied &&
                serviced != ControllerIsolationRoutingResult::Waiting) {
                return ERROR_DEVICE_NOT_AVAILABLE;
            }
        }
#endif
        ControlFrame request;
        const auto timeout =
#if defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
            ControllerIsolationCommandTimeoutMilliseconds;
#else
            routing.state() == ControllerIsolationRoutingState::Disabled
                ? ControllerIsolationCommandTimeoutMilliseconds
                : 1U;
#endif
        const auto wait = channel->WaitForRequest(timeout, request);
        if (wait == ChildWaitResult::Stop ||
            wait == ChildWaitResult::ParentExited) {
#if !defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
            if (routing.state() != ControllerIsolationRoutingState::Disabled)
                (void)routing.Stop(channel->admission().authority);
#endif
            return 0;
        }
#if !defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
        if (wait == ChildWaitResult::TimedOut &&
            routing.state() != ControllerIsolationRoutingState::Disabled) {
            continue;
        }
#endif
        if (wait != ChildWaitResult::Request) return ERROR_INVALID_DATA;
        switch (request.kind) {
        case ControlMessageKind::Heartbeat: {
#if !defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
            if (routing.state() != ControllerIsolationRoutingState::Disabled &&
                !Applied(routing.Heartbeat(request.authority, GetTickCount64()))) {
                (void)channel->Reply(
                    ControlMessageKind::Terminal, request,
                    ERROR_INVALID_STATE);
                return ERROR_INVALID_STATE;
            }
#endif
            ControlProgress progress{ControlProgress::None};
#if !defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
            progress = Progress(
                routing.state(), ControllerIsolationRoutingResult::Applied);
#endif
            if (!channel->Reply(
                    ControlMessageKind::Heartbeat, request, 0,
                    GetCurrentProcessId(), progress,
#if defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
                    {}, 0, {}))
#else
                    routing.currentState(), guideSink.TakeNext(),
                    hostInputSink.TakeBatch()))
#endif
                return ERROR_BROKEN_PIPE;
            break;
        }
#if !defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
        case ControlMessageKind::PrepareSession: {
            hostInputSink.RetireInteraction();
            const auto result = routing.PrepareSession(
                request.authority, request.enrollment, GetTickCount64());
            if (!Applied(result)) {
                (void)channel->Reply(
                    ControlMessageKind::Terminal, request,
                    static_cast<std::uint32_t>(result) + 1);
                return ERROR_DEVICE_NOT_AVAILABLE;
            }
            if (!channel->Reply(
                    ControlMessageKind::PrepareSession, request, 0,
                    GetCurrentProcessId(), Progress(routing.state(), result),
                    {}, 0, hostInputSink.TakeBatch()))
                return ERROR_BROKEN_PIPE;
            break;
        }
        case ControlMessageKind::CommitPlaying: {
            hostInputSink.RetireInteraction();
            const auto result = routing.CommitPlaying(
                request.authority, request.observedAtMilliseconds,
                GetTickCount64());
            if (result != ControllerIsolationRoutingResult::Applied &&
                result != ControllerIsolationRoutingResult::Waiting) {
                (void)channel->Reply(
                    ControlMessageKind::Terminal, request,
                    static_cast<std::uint32_t>(result) + 1);
                return ERROR_INVALID_STATE;
            }
            if (!channel->Reply(
                    ControlMessageKind::CommitPlaying, request, 0,
                    GetCurrentProcessId(), Progress(routing.state(), result),
                    {}, 0, hostInputSink.TakeBatch()))
                return ERROR_BROKEN_PIPE;
            break;
        }
        case ControlMessageKind::EnterOverlay: {
            if (!hostInputSink.BeginInteraction()) {
                (void)channel->Reply(
                    ControlMessageKind::Terminal, request,
                    ERROR_ARITHMETIC_OVERFLOW);
                return ERROR_ARITHMETIC_OVERFLOW;
            }
            const auto result = routing.state() ==
                    ControllerIsolationRoutingState::OverlayInteraction
                ? ControllerIsolationRoutingResult::Applied
                : routing.EnterOverlay(request.authority, GetTickCount64());
            if (result != ControllerIsolationRoutingResult::Applied &&
                result != ControllerIsolationRoutingResult::Waiting) {
                hostInputSink.RetireInteraction();
                (void)channel->Reply(
                    ControlMessageKind::Terminal, request,
                    static_cast<std::uint32_t>(result) + 1);
                return ERROR_INVALID_STATE;
            }
            if (!channel->Reply(
                    ControlMessageKind::EnterOverlay, request, 0,
                    GetCurrentProcessId(), Progress(routing.state(), result),
                    {}, 0, hostInputSink.TakeBatch()))
                return ERROR_BROKEN_PIPE;
            break;
        }
        case ControlMessageKind::CloseOverlay: {
            hostInputSink.RetireInteraction();
            const auto result = routing.CloseOverlay(
                request.authority, request.observedAtMilliseconds,
                GetTickCount64());
            if (result != ControllerIsolationRoutingResult::Applied &&
                result != ControllerIsolationRoutingResult::Waiting) {
                (void)channel->Reply(
                    ControlMessageKind::Terminal, request,
                    static_cast<std::uint32_t>(result) + 1);
                return ERROR_INVALID_STATE;
            }
            if (!channel->Reply(
                    ControlMessageKind::CloseOverlay, request, 0,
                    GetCurrentProcessId(), Progress(routing.state(), result),
                    {}, 0, hostInputSink.TakeBatch()))
                return ERROR_BROKEN_PIPE;
            break;
        }
        case ControlMessageKind::HoldContained: {
            hostInputSink.RetireInteraction();
            const auto result = routing.HoldContained(
                request.authority, GetTickCount64());
            if (result != ControllerIsolationRoutingResult::Applied) {
                (void)channel->Reply(
                    ControlMessageKind::Terminal, request,
                    static_cast<std::uint32_t>(result) + 1);
                return ERROR_INVALID_STATE;
            }
            if (!channel->Reply(
                    ControlMessageKind::HoldContained, request, 0,
                    GetCurrentProcessId(), Progress(routing.state(), result),
                    {}, 0, hostInputSink.TakeBatch()))
                return ERROR_BROKEN_PIPE;
            break;
        }
#endif
        case ControlMessageKind::Stop:
#if !defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
            hostInputSink.RetireInteraction();
            if (routing.state() != ControllerIsolationRoutingState::Disabled)
                (void)routing.Stop(request.authority);
#endif
            (void)channel->Reply(ControlMessageKind::Terminal, request);
            return 0;
#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
        case ControlMessageKind::TestExit:
            return ERROR_PROCESS_ABORTED;
        case ControlMessageKind::TestHang:
            Sleep(INFINITE);
            return ERROR_TIMEOUT;
#endif
        default:
            (void)channel->Reply(
                ControlMessageKind::Terminal, request, ERROR_NOT_SUPPORTED);
            return ERROR_NOT_SUPPORTED;
        }
    }
}
