#include "../OverlayPlatformInterop/ControllerIsolationProcessOwner.h"

#include <windows.h>

#include <array>
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
                    worker.processId())) {
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
