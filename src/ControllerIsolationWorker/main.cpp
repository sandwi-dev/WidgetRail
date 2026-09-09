#include "../OverlayPlatformInterop/ControllerIsolationProcessOwner.h"

#if !defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
#include "../OverlayPlatformInterop/ViGEmOutputAdapter.h"
#endif

#include <windows.h>

#include <string>

namespace {

using namespace widgetrail::isolation;

#if defined(WRAIL_CONTROLLER_ISOLATION_FAKE_BACKEND)
constexpr wchar_t kExpectedParent[] = L"ControllerIsolationGuardianTestHost.exe";
#else
constexpr wchar_t kExpectedParent[] = L"ControllerIsolationGuardian.exe";
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
    // This is the first point at which the production worker may contact the
    // driver: inherited handle, nonce, parent and session admission have all
    // succeeded. No WidgetRail product path launches this dormant binary yet.
    ViGEmOutputAdapter output(OfficialViGEmApi());
    if (!output.Open()) {
        (void)channel->Reply(
            ControlMessageKind::Terminal, hello,
            static_cast<std::uint32_t>(output.status()));
        return ERROR_DEVICE_NOT_AVAILABLE;
    }
#endif

    if (!channel->Reply(
            ControlMessageKind::HelloAccepted, hello, 0,
            GetCurrentProcessId())) {
        return ERROR_BROKEN_PIPE;
    }

    for (;;) {
        ControlFrame request;
        const auto wait = channel->WaitForRequest(
            ControllerIsolationCommandTimeoutMilliseconds, request);
        if (wait == ChildWaitResult::Stop ||
            wait == ChildWaitResult::ParentExited) {
            return 0;
        }
        if (wait != ChildWaitResult::Request) return ERROR_INVALID_DATA;
        switch (request.kind) {
        case ControlMessageKind::Heartbeat:
            if (!channel->Reply(ControlMessageKind::Heartbeat, request))
                return ERROR_BROKEN_PIPE;
            break;
        case ControlMessageKind::Stop:
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
