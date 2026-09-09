#include "ControllerIsolationReconnect.h"
#include "ControllerIsolationProcessOwner.h"

#include <sddl.h>

#include <array>
#include <cstdlib>
#include <limits>
#include <vector>

namespace widgetrail::isolation {
namespace {

class LocalAllocation final {
public:
    ~LocalAllocation() { if (value_) LocalFree(value_); }
    void** address() noexcept { return &value_; }
    [[nodiscard]] void* get() const noexcept { return value_; }
private:
    void* value_{};
};

[[nodiscard]] bool CurrentUserSecurity(
    SECURITY_ATTRIBUTES& attributes,
    LocalAllocation& descriptor,
    std::uint32_t& error) noexcept {
    HANDLE token{};
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) {
        error = GetLastError();
        return false;
    }
    DWORD needed{};
    GetTokenInformation(token, TokenUser, nullptr, 0, &needed);
    std::vector<std::byte> storage(needed);
    if (needed == 0 || !GetTokenInformation(
            token, TokenUser, storage.data(), needed, &needed)) {
        error = GetLastError();
        CloseHandle(token);
        return false;
    }
    CloseHandle(token);
    auto* user = reinterpret_cast<TOKEN_USER*>(storage.data());
    LocalAllocation sidText;
    if (!ConvertSidToStringSidW(
            user->User.Sid,
            reinterpret_cast<LPWSTR*>(sidText.address()))) {
        error = GetLastError();
        return false;
    }
    const std::wstring sddl =
        L"D:P(A;;GA;;;SY)(A;;GA;;;" +
        std::wstring(static_cast<const wchar_t*>(sidText.get())) + L")";
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
            sddl.c_str(), SDDL_REVISION_1,
            descriptor.address(), nullptr)) {
        error = GetLastError();
        return false;
    }
    attributes = {
        sizeof(attributes), descriptor.get(), FALSE};
    return true;
}

[[nodiscard]] bool WaitOverlapped(
    const HANDLE object,
    OVERLAPPED& overlapped,
    const DWORD timeoutMilliseconds,
    std::uint32_t& error) noexcept {
    const auto wait = WaitForSingleObject(
        overlapped.hEvent, timeoutMilliseconds);
    if (wait != WAIT_OBJECT_0) {
        CancelIoEx(object, &overlapped);
        // The OVERLAPPED and frame buffer cannot escape while kernel I/O still
        // references them. Named-pipe cancellation is expected to complete;
        // if the kernel owner cannot acknowledge it inside this final bounded
        // window, retire only this guardian/client process. Its supervisor or
        // durable recovery journal remains the liveness boundary.
        if (WaitForSingleObject(overlapped.hEvent, 1'000) != WAIT_OBJECT_0) {
            TerminateProcess(GetCurrentProcess(), ERROR_OPERATION_ABORTED);
            std::abort();
        }
        error = wait == WAIT_TIMEOUT ? ERROR_TIMEOUT : GetLastError();
        return false;
    }
    DWORD transferred{};
    if (!GetOverlappedResult(object, &overlapped, &transferred, FALSE)) {
        error = GetLastError();
        return false;
    }
    return transferred == sizeof(ControlFrame);
}

[[nodiscard]] bool Transfer(
    const HANDLE pipe, const bool write, void* buffer,
    const DWORD timeoutMilliseconds,
    std::uint32_t& error) noexcept {
    OVERLAPPED overlapped{};
    overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!overlapped.hEvent) {
        error = GetLastError();
        return false;
    }
    DWORD transferred{};
    const auto started = write
        ? WriteFile(pipe, buffer, sizeof(ControlFrame), &transferred, &overlapped)
        : ReadFile(pipe, buffer, sizeof(ControlFrame), &transferred, &overlapped);
    bool succeeded{};
    if (started) {
        succeeded = transferred == sizeof(ControlFrame);
        if (!succeeded) error = ERROR_INVALID_DATA;
    } else if (GetLastError() == ERROR_IO_PENDING) {
        succeeded = WaitOverlapped(
            pipe, overlapped, timeoutMilliseconds, error);
    } else {
        error = GetLastError();
    }
    CloseHandle(overlapped.hEvent);
    return succeeded;
}

} // namespace

std::wstring ControllerIsolationPipeName(const RoutingAuthority& authority) {
    if (!authority.valid()) return {};
    return L"\\\\.\\pipe\\WidgetRail.ControllerIsolation." +
        std::to_wstring(authority.sessionGeneration) + L"." +
        std::to_wstring(authority.deviceGeneration) + L"." +
        std::to_wstring(authority.targetGeneration) + L"." +
        std::to_wstring(authority.leaseId);
}

std::wstring ControllerIsolationControlPipeName(
    const RoutingAuthority& authority) {
    const auto primary = ControllerIsolationPipeName(authority);
    return primary.empty() ? std::wstring{} : primary + L".control";
}

ControllerIsolationPipeServer::~ControllerIsolationPipeServer() {
    Disconnect();
    if (pipe_ != INVALID_HANDLE_VALUE) CloseHandle(pipe_);
}

bool ControllerIsolationPipeServer::Open(
    const std::wstring& pipeName, std::uint32_t& nativeError) noexcept {
    nativeError = ERROR_SUCCESS;
    if (pipe_ != INVALID_HANDLE_VALUE || pipeName.empty()) {
        nativeError = ERROR_INVALID_STATE;
        return false;
    }
    try {
        SECURITY_ATTRIBUTES security{};
        LocalAllocation descriptor;
        if (!CurrentUserSecurity(security, descriptor, nativeError)) return false;
        pipe_ = CreateNamedPipeW(
            pipeName.c_str(),
            PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT |
                PIPE_REJECT_REMOTE_CLIENTS,
            1, sizeof(ControlFrame), sizeof(ControlFrame), 0, &security);
        if (pipe_ == INVALID_HANDLE_VALUE) {
            nativeError = GetLastError();
            return false;
        }
        return true;
    } catch (...) {
        nativeError = ERROR_NOT_ENOUGH_MEMORY;
        return false;
    }
}

bool ControllerIsolationPipeServer::Accept(
    const DWORD timeoutMilliseconds, std::uint32_t& nativeError,
    const HANDLE acceptPostedEvent) noexcept {
    nativeError = ERROR_SUCCESS;
    if (pipe_ == INVALID_HANDLE_VALUE || connected_) {
        nativeError = ERROR_INVALID_STATE;
        return false;
    }
    OVERLAPPED overlapped{};
    overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!overlapped.hEvent) {
        nativeError = GetLastError();
        return false;
    }
    bool connected = ConnectNamedPipe(pipe_, &overlapped) != FALSE;
    if (!connected && GetLastError() == ERROR_PIPE_CONNECTED) connected = true;
    else if (!connected && GetLastError() == ERROR_IO_PENDING) {
        if (acceptPostedEvent) SetEvent(acceptPostedEvent);
        const auto wait = WaitForSingleObject(overlapped.hEvent, timeoutMilliseconds);
        if (wait == WAIT_OBJECT_0) connected = true;
        else {
            CancelIoEx(pipe_, &overlapped);
            if (WaitForSingleObject(overlapped.hEvent, 1'000) != WAIT_OBJECT_0) {
                TerminateProcess(GetCurrentProcess(), ERROR_OPERATION_ABORTED);
                std::abort();
            }
            nativeError = wait == WAIT_TIMEOUT ? ERROR_TIMEOUT : GetLastError();
        }
    } else if (!connected) nativeError = GetLastError();
    if (connected && acceptPostedEvent) SetEvent(acceptPostedEvent);
    CloseHandle(overlapped.hEvent);
    connected_ = connected;
    return connected;
}

bool ControllerIsolationPipeServer::Receive(
    ControlFrame& frame, const DWORD timeoutMilliseconds,
    std::uint32_t& nativeError) noexcept {
    frame = {};
    return connected_ && Transfer(
        pipe_, false, &frame, timeoutMilliseconds, nativeError);
}

bool ControllerIsolationPipeServer::Reply(
    const ControlFrame& frame, std::uint32_t& nativeError) noexcept {
    auto copy = frame;
    return connected_ && Transfer(
        pipe_, true, &copy, ControllerIsolationCommandTimeoutMilliseconds,
        nativeError);
}

std::optional<std::uint32_t>
ControllerIsolationPipeServer::clientProcessId() const noexcept {
    ULONG id{};
    if (!connected_ || !GetNamedPipeClientProcessId(pipe_, &id) || id == 0)
        return std::nullopt;
    return static_cast<std::uint32_t>(id);
}

void ControllerIsolationPipeServer::Disconnect() noexcept {
    if (pipe_ == INVALID_HANDLE_VALUE || !connected_) return;
    // Every protocol reply is already a bounded, completed WriteFile. A pipe
    // flush is not an acknowledgement and can wait forever for a client that
    // stopped reading, so teardown never depends on FlushFileBuffers.
    CancelIoEx(pipe_, nullptr);
    DisconnectNamedPipe(pipe_);
    connected_ = false;
}

ControllerIsolationPipeClient::~ControllerIsolationPipeClient() { Close(); }

bool ControllerIsolationPipeClient::Connect(
    const std::wstring& pipeName, const DWORD timeoutMilliseconds,
    std::uint32_t& nativeError) noexcept {
    nativeError = ERROR_SUCCESS;
    if (pipe_ != INVALID_HANDLE_VALUE || pipeName.empty()) {
        nativeError = ERROR_INVALID_STATE;
        return false;
    }
    const auto startedAt = GetTickCount64();
    std::uint32_t lastTransientError{ERROR_FILE_NOT_FOUND};
    for (;;) {
        const auto now = GetTickCount64();
        if (now < startedAt || now - startedAt >= timeoutMilliseconds) {
            nativeError = lastTransientError;
            return false;
        }
        const auto remaining = static_cast<DWORD>(
            timeoutMilliseconds - (now - startedAt));
        if (!WaitNamedPipeW(pipeName.c_str(), remaining)) {
            const auto error = GetLastError();
            if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PIPE_BUSY) {
                nativeError = error;
                return false;
            }
            lastTransientError = error;
            Sleep(1);
            continue;
        }
        pipe_ = CreateFileW(
            pipeName.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
            OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
        if (pipe_ != INVALID_HANDLE_VALUE) break;
        const auto error = GetLastError();
        if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PIPE_BUSY) {
            nativeError = error;
            return false;
        }
        lastTransientError = error;
        Sleep(1);
    }
    DWORD mode = PIPE_READMODE_MESSAGE;
    if (!SetNamedPipeHandleState(pipe_, &mode, nullptr, nullptr)) {
        nativeError = GetLastError();
        Close();
        return false;
    }
    return true;
}

bool ControllerIsolationPipeClient::Exchange(
    const ControlFrame& request, ControlFrame& response,
    const DWORD timeoutMilliseconds, std::uint32_t& nativeError) noexcept {
    response = {};
    auto copy = request;
    return pipe_ != INVALID_HANDLE_VALUE &&
        Transfer(pipe_, true, &copy, timeoutMilliseconds, nativeError) &&
        Transfer(pipe_, false, &response, timeoutMilliseconds, nativeError);
}

std::optional<std::uint32_t>
ControllerIsolationPipeClient::serverProcessId() const noexcept {
    ULONG id{};
    if (pipe_ == INVALID_HANDLE_VALUE ||
        !GetNamedPipeServerProcessId(pipe_, &id) || id == 0)
        return std::nullopt;
    return static_cast<std::uint32_t>(id);
}

void ControllerIsolationPipeClient::Close() noexcept {
    if (pipe_ != INVALID_HANDLE_VALUE) CloseHandle(pipe_);
    pipe_ = INVALID_HANDLE_VALUE;
}

} // namespace widgetrail::isolation
