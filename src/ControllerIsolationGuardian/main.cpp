#include "../OverlayPlatformInterop/ControllerIsolationProcessOwner.h"
#include "../OverlayPlatformInterop/ControllerIsolationGuardianSession.h"

#include <windows.h>

#include <array>
#include <cerrno>
#include <cwchar>
#include <filesystem>
#include <string>
#include <string_view>

namespace {

using namespace widgetrail::isolation;

#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
constexpr wchar_t kExpectedParent[] = L"ControllerIsolationProcessTests.exe";
constexpr wchar_t kWorkerFileName[] = L"ControllerIsolationFakeWorker.exe";
#else
constexpr wchar_t kExpectedParent[] = L"OverlayHost.exe";
constexpr wchar_t kWorkerFileName[] = L"ControllerIsolationWorker.exe";
#endif

std::filesystem::path SiblingWorker() {
    std::array<wchar_t, 32'768> path{};
    const auto length = GetModuleFileNameW(
        nullptr, path.data(), static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size()) return {};
    return std::filesystem::path(
        std::wstring_view(path.data(), length)).parent_path() /
        kWorkerFileName;
}

bool ParseUInt64(const wchar_t* text, std::uint64_t& value) noexcept {
    if (!text || *text == L'\0' || *text == L'-') return false;
    errno = 0;
    wchar_t* end{};
    const auto parsed = std::wcstoull(text, &end, 10);
    if (errno != 0 || !end || *end != L'\0' || parsed == 0) return false;
    value = parsed;
    return true;
}

} // namespace

int wmain(const int argumentCount, wchar_t** arguments) {
#if !defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
    if (argumentCount == 8 && arguments[1] && arguments[2] && arguments[3] &&
        _wcsicmp(arguments[1], L"--controller-isolation-session") == 0 &&
        _wcsicmp(arguments[3], L"--expected-authority") == 0) {
        RoutingAuthority expected;
        if (!ParseUInt64(arguments[4], expected.sessionGeneration) ||
            !ParseUInt64(arguments[5], expected.deviceGeneration) ||
            !ParseUInt64(arguments[6], expected.targetGeneration) ||
            !ParseUInt64(arguments[7], expected.leaseId))
            return ERROR_INVALID_PARAMETER;
        return RunControllerIsolationGuardianSession(
            arguments[2], expected);
    }
#endif
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

    ControllerIsolationProcessOwner worker;
    if (!worker.Start(
            SiblingWorker(), channel->admission().authority,
            1,
            ControllerIsolationStartupTimeoutMilliseconds, error)) {
        (void)channel->Reply(
            ControlMessageKind::Terminal, hello, ERROR_PROCESS_ABORTED);
        return ERROR_PROCESS_ABORTED;
    }
    if (!channel->Reply(
            ControlMessageKind::HelloAccepted, hello, 0,
            worker.processId())) {
        return ERROR_BROKEN_PIPE;
    }

    for (;;) {
        ControlFrame request;
        const auto wait = channel->WaitForRequest(
            static_cast<DWORD>(RoutingBudgets{}.hostLeaseMilliseconds),
            request);
        if (wait == ChildWaitResult::Stop ||
            wait == ChildWaitResult::ParentExited) {
            return 0;
        }
        if (wait != ChildWaitResult::Request) return ERROR_INVALID_DATA;

        ControlFrame workerResponse;
        switch (request.kind) {
        case ControlMessageKind::Heartbeat:
            if (!worker.Send(
                    ControlMessageKind::Heartbeat,
                    ControllerIsolationCommandTimeoutMilliseconds,
                    workerResponse, error)) {
                worker.Stop();
                const auto remoteStatus = workerResponse.status != 0
                    ? workerResponse.status
                    : static_cast<std::uint32_t>(ERROR_TIMEOUT);
                (void)channel->Reply(
                    ControlMessageKind::Terminal, request, remoteStatus);
                return static_cast<int>(remoteStatus);
            }
            if (!channel->Reply(
                    ControlMessageKind::Heartbeat, request, 0,
                    worker.processId(),
                    static_cast<ControlProgress>(
                        workerResponse.observedAtMilliseconds),
                    workerResponse.state,
                    workerResponse.deviceEnrollmentToken,
                    workerResponse.inputBatch)) {
                return ERROR_BROKEN_PIPE;
            }
            break;
        case ControlMessageKind::PrepareSession:
        case ControlMessageKind::CommitPlaying:
        case ControlMessageKind::EnterOverlay:
        case ControlMessageKind::CloseOverlay:
        case ControlMessageKind::HoldContained:
            if (!worker.Send(
                    request, ControllerIsolationCommandTimeoutMilliseconds,
                    workerResponse, error)) {
                worker.Stop();
                const auto remoteStatus = workerResponse.status != 0
                    ? workerResponse.status
                    : static_cast<std::uint32_t>(ERROR_TIMEOUT);
                (void)channel->Reply(
                    ControlMessageKind::Terminal, request, remoteStatus);
                return static_cast<int>(remoteStatus);
            }
            if (!channel->Reply(
                    request.kind, request, 0, worker.processId(),
                    static_cast<ControlProgress>(
                        workerResponse.observedAtMilliseconds))) {
                return ERROR_BROKEN_PIPE;
            }
            break;
        case ControlMessageKind::Stop: {
            const auto stopped = worker.Send(
                ControlMessageKind::Stop,
                ControllerIsolationCommandTimeoutMilliseconds,
                workerResponse, error);
            worker.Stop();
            const auto stopStatus = stopped
                ? 0u
                : (workerResponse.status != 0
                       ? workerResponse.status
                       : static_cast<std::uint32_t>(ERROR_PROCESS_ABORTED));
            (void)channel->Reply(
                ControlMessageKind::Terminal, request, stopStatus);
            return static_cast<int>(stopStatus);
        }
#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
        case ControlMessageKind::TestWrongResponseKind:
            (void)channel->Reply(
                ControlMessageKind::HelloAccepted, request);
            return 0;
        case ControlMessageKind::TestUnknownResponseKind: {
            auto response = MakeControlFrame(
                static_cast<ControlMessageKind>(0x7FFF),
                channel->admission().nonce,
                channel->admission().authority,
                request.sequence);
            (void)channel->ReplyRawForTest(response);
            return 0;
        }
        case ControlMessageKind::TestNonzeroResponseStatus:
            (void)channel->Reply(
                ControlMessageKind::Heartbeat, request, ERROR_ACCESS_DENIED);
            return ERROR_ACCESS_DENIED;
        case ControlMessageKind::TestMalformedResponse: {
            auto response = MakeControlFrame(
                ControlMessageKind::Heartbeat,
                channel->admission().nonce,
                channel->admission().authority,
                request.sequence);
            response.size = 0;
            (void)channel->ReplyRawForTest(response);
            return ERROR_INVALID_DATA;
        }
        case ControlMessageKind::TestExit:
        case ControlMessageKind::TestHang: {
            const auto sent = worker.Send(
                request.kind, ControllerIsolationCommandTimeoutMilliseconds,
                workerResponse, error);
            worker.Stop();
            (void)channel->Reply(
                ControlMessageKind::Terminal, request,
                sent ? ERROR_INVALID_STATE :
                    (request.kind == ControlMessageKind::TestHang
                        ? ERROR_TIMEOUT
                        : ERROR_PROCESS_ABORTED));
            return 0;
        }
#endif
        default:
            worker.Stop();
            (void)channel->Reply(
                ControlMessageKind::Terminal, request, ERROR_NOT_SUPPORTED);
            return ERROR_NOT_SUPPORTED;
        }
    }
}
