#pragma once

#include "ControllerIsolationProtocol.h"

#include <windows.h>

#include <filesystem>
#include <optional>
#include <string>

namespace widgetrail::isolation {

inline constexpr DWORD ControllerIsolationStartupTimeoutMilliseconds = 2'000;
inline constexpr DWORD ControllerIsolationCommandTimeoutMilliseconds = 500;
inline constexpr DWORD ControllerIsolationShutdownTimeoutMilliseconds = 1'000;

struct ControlSharedMemory final {
    ProcessAdmission admission{};
    ControlFrame request{};
    ControlFrame response{};
};

static_assert(sizeof(ControlSharedMemory) <= 1'024);

enum class ChildWaitResult {
    Request,
    Stop,
    ParentExited,
    TimedOut,
    InvalidFrame,
    Failed,
};

class ChildControlChannel final {
public:
    ChildControlChannel() noexcept = default;
    ~ChildControlChannel();
    ChildControlChannel(const ChildControlChannel&) = delete;
    ChildControlChannel& operator=(const ChildControlChannel&) = delete;
    ChildControlChannel(ChildControlChannel&& other) noexcept;
    ChildControlChannel& operator=(ChildControlChannel&& other) noexcept;

    [[nodiscard]] static std::optional<ChildControlChannel> Open(
        int argumentCount,
        wchar_t** arguments,
        const wchar_t* expectedParentFileName,
        std::wstring& error) noexcept;

    [[nodiscard]] ChildWaitResult WaitForRequest(
        DWORD timeoutMilliseconds,
        ControlFrame& request) noexcept;
    [[nodiscard]] bool Reply(
        ControlMessageKind kind,
        const ControlFrame& request,
        std::uint32_t status = 0,
        std::uint32_t processId = 0) noexcept;

    [[nodiscard]] const ProcessAdmission& admission() const noexcept {
        return shared_->admission;
    }

private:
    void Close() noexcept;

    HANDLE mapping_{};
    HANDLE requestEvent_{};
    HANDLE responseEvent_{};
    HANDLE stopEvent_{};
    HANDLE parentProcess_{};
    ControlSharedMemory* shared_{};
    std::optional<ControlSessionGate> gate_;
};

class ControllerIsolationProcessOwner final {
public:
    ControllerIsolationProcessOwner() noexcept = default;
    ~ControllerIsolationProcessOwner();
    ControllerIsolationProcessOwner(const ControllerIsolationProcessOwner&) = delete;
    ControllerIsolationProcessOwner& operator=(
        const ControllerIsolationProcessOwner&) = delete;
    ControllerIsolationProcessOwner(
        ControllerIsolationProcessOwner&& other) noexcept;
    ControllerIsolationProcessOwner& operator=(
        ControllerIsolationProcessOwner&& other) noexcept;

    [[nodiscard]] bool Start(
        const std::filesystem::path& executable,
        const RoutingAuthority& authority,
        std::uint32_t activeProcessLimit,
        DWORD timeoutMilliseconds,
        std::wstring& error) noexcept;
    [[nodiscard]] bool Send(
        ControlMessageKind kind,
        DWORD timeoutMilliseconds,
        ControlFrame& response,
        std::wstring& error) noexcept;
    void Stop() noexcept;

    [[nodiscard]] DWORD processId() const noexcept { return processId_; }
    [[nodiscard]] const ControlFrame& startupResponse() const noexcept {
        return startupResponse_;
    }

#if defined(WRAIL_CONTROLLER_ISOLATION_TESTING)
    [[nodiscard]] bool TerminateExactForTest() noexcept;
    [[nodiscard]] ControlFrame NextFrameForTest(
        ControlMessageKind kind) const noexcept;
    [[nodiscard]] bool SendRawForTest(
        const ControlFrame& request,
        DWORD timeoutMilliseconds,
        ControlFrame& response,
        std::wstring& error) noexcept;
    [[nodiscard]] bool WaitForExitForTest(DWORD timeoutMilliseconds) noexcept;
#endif

private:
    [[nodiscard]] bool WaitForResponse(
        std::uint64_t sequence,
        DWORD timeoutMilliseconds,
        ControlFrame& response,
        std::wstring& error) noexcept;
    void CloseChannelHandles() noexcept;
    void CloseProcessHandles() noexcept;

    HANDLE mapping_{};
    HANDLE requestEvent_{};
    HANDLE responseEvent_{};
    HANDLE stopEvent_{};
    HANDLE childProcess_{};
    HANDLE job_{};
    ControlSharedMemory* shared_{};
    ControllerIsolationNonce nonce_{};
    RoutingAuthority authority_{};
    std::uint64_t nextSequence_{1};
    DWORD processId_{};
    ControlFrame startupResponse_{};
};

[[nodiscard]] std::uint64_t ProcessCreationTime(HANDLE process) noexcept;

} // namespace widgetrail::isolation
