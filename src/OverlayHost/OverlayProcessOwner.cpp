#include "OverlayProcessOwner.h"

#include <sddl.h>

#include <algorithm>
#include <array>
#include <cstring>
#include <iomanip>
#include <sstream>
#include <utility>
#include <vector>

namespace gba::process {
namespace {

constexpr std::uint32_t kRequestMagic = 0x41424747;
constexpr std::uint32_t kReplyAccepted = 0x59414B4F;
constexpr std::uint16_t kProtocolVersion = 1;
constexpr std::uint16_t kShowCommand = 1;
constexpr DWORD kPipeBufferBytes = 64;

struct Request final {
    std::uint32_t magic{kRequestMagic};
    std::uint16_t version{kProtocolVersion};
    std::uint16_t command{kShowCommand};
};

[[nodiscard]] std::uint64_t Hash(const std::wstring_view value) noexcept {
    std::uint64_t result = 1469598103934665603ULL;
    for (const wchar_t character : value) {
        result ^= static_cast<std::uint16_t>(character);
        result *= 1099511628211ULL;
    }
    return result;
}

[[nodiscard]] std::wstring Hex(const std::uint64_t value) {
    std::wostringstream output;
    output << std::hex << std::setw(16) << std::setfill(L'0') << value;
    return output.str();
}

[[nodiscard]] std::vector<std::byte> CurrentUserSid() {
    HANDLE token{};
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return {};
    DWORD size{};
    (void)GetTokenInformation(token, TokenUser, nullptr, 0, &size);
    std::vector<std::byte> buffer(size);
    if (size == 0 || !GetTokenInformation(
            token, TokenUser, buffer.data(), size, &size)) {
        CloseHandle(token);
        return {};
    }
    const auto* user = reinterpret_cast<const TOKEN_USER*>(buffer.data());
    const DWORD sidSize = GetLengthSid(user->User.Sid);
    std::vector<std::byte> sid(sidSize);
    if (!CopySid(sidSize, sid.data(), user->User.Sid)) sid.clear();
    CloseHandle(token);
    return sid;
}

[[nodiscard]] std::wstring SidText(const std::vector<std::byte>& sid) {
    LPWSTR text{};
    if (sid.empty() || !ConvertSidToStringSidW(
            const_cast<std::byte*>(sid.data()), &text)) return {};
    std::wstring result(text);
    LocalFree(text);
    return result;
}

[[nodiscard]] SECURITY_ATTRIBUTES UserOnlySecurity(
    const std::vector<std::byte>& sid,
    PSECURITY_DESCRIPTOR& descriptor) {
    const auto sidText = SidText(sid);
    if (sidText.empty()) return {};
    const std::wstring sddl = L"D:P(A;;GA;;;SY)(A;;GA;;;" + sidText + L")";
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
            sddl.c_str(), SDDL_REVISION_1, &descriptor, nullptr)) return {};
    SECURITY_ATTRIBUTES attributes{sizeof(attributes)};
    attributes.lpSecurityDescriptor = descriptor;
    return attributes;
}

} // namespace

bool ValidProcessProfile(const std::wstring_view profile) noexcept {
    return !profile.empty() && profile.size() <= 64 &&
        std::ranges::all_of(profile, [](const wchar_t value) {
            return (value >= L'a' && value <= L'z') ||
                   (value >= L'A' && value <= L'Z') ||
                   (value >= L'0' && value <= L'9') ||
                   value == L'.' || value == L'_' || value == L'-';
        });
}

#ifdef GBA_OVERLAY_PROCESS_OWNER_TESTING
OverlayProcessOwner::ObjectNamesForTests OverlayProcessOwner::NamesForTests(
    const std::wstring_view profile) {
    const auto sid = CurrentUserSid();
    const auto identity = Hex(Hash(SidText(sid))) + L"." + Hex(Hash(profile));
    return {
        L"Global\\GameBarAlternative.OverlayHost.Owner." + identity,
        L"\\\\.\\pipe\\GameBarAlternative.OverlayHost.Activation." + identity,
    };
}
#endif

OverlayProcessOwner::~OverlayProcessOwner() {
    Stop();
}

OwnershipResult OverlayProcessOwner::Begin(
    const std::wstring_view profile,
    const std::chrono::milliseconds clientTimeout,
    std::wstring& error) {
    if (mutex_ || owner_ || !ValidProcessProfile(profile) || clientTimeout.count() < 1 ||
        clientTimeout > std::chrono::seconds(10)) {
        error = L"Overlay process ownership options are invalid.";
        return OwnershipResult::ClientFailed;
    }
    userSid_ = CurrentUserSid();
    if (userSid_.empty()) {
        error = L"The current user identity could not be resolved.";
        return OwnershipResult::ClientFailed;
    }
    profile_ = profile;
    const auto identity = Hex(Hash(SidText(userSid_))) + L"." + Hex(Hash(profile_));
    mutexName_ = L"Global\\GameBarAlternative.OverlayHost.Owner." + identity;
    pipeName_ = L"\\\\.\\pipe\\GameBarAlternative.OverlayHost.Activation." + identity;

    PSECURITY_DESCRIPTOR descriptor{};
    auto security = UserOnlySecurity(userSid_, descriptor);
    if (!security.lpSecurityDescriptor) {
        error = L"Per-user process ownership security could not be created.";
        return OwnershipResult::ClientFailed;
    }
    mutex_ = CreateMutexW(&security, FALSE, mutexName_.c_str());
    LocalFree(descriptor);
    if (!mutex_) {
        error = L"The per-user OverlayHost ownership mutex could not be opened.";
        return OwnershipResult::ClientFailed;
    }
    const DWORD wait = WaitForSingleObject(mutex_, 0);
    if (wait == WAIT_OBJECT_0 || wait == WAIT_ABANDONED) {
        owner_ = true;
        if (!StartServer(error)) {
            Stop();
            return OwnershipResult::ClientFailed;
        }
        return OwnershipResult::Owner;
    }
    if (wait != WAIT_TIMEOUT) {
        error = L"The per-user OverlayHost ownership lease could not be resolved.";
        CloseHandle(mutex_);
        mutex_ = nullptr;
        return OwnershipResult::ClientFailed;
    }
    const bool acknowledged = SendShow(clientTimeout, error);
    CloseHandle(mutex_);
    mutex_ = nullptr;
    return acknowledged
        ? OwnershipResult::ClientAcknowledged
        : OwnershipResult::ClientFailed;
}

bool OverlayProcessOwner::StartServer(std::wstring& error) {
    stopEvent_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    readyEvent_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    showEvent_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!stopEvent_ || !readyEvent_ || !showEvent_) {
        error = L"OverlayHost activation events could not be created.";
        return false;
    }
    server_ = std::thread([this] { ServerLoop(); });
    if (WaitForSingleObject(readyEvent_, 2000) != WAIT_OBJECT_0) {
        error = L"OverlayHost activation transport did not become ready.";
        return false;
    }
    if (!serverReadyOk_.load()) {
        error = L"OverlayHost activation transport could not claim its per-user endpoint.";
        return false;
    }
    return true;
}

void OverlayProcessOwner::ServerLoop() noexcept {
    bool announced = false;
    while (WaitForSingleObject(stopEvent_, 0) != WAIT_OBJECT_0) {
        PSECURITY_DESCRIPTOR descriptor{};
        auto security = UserOnlySecurity(userSid_, descriptor);
        HANDLE pipe = CreateNamedPipeW(
            pipeName_.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1, kPipeBufferBytes, kPipeBufferBytes, 500, &security);
        if (descriptor) LocalFree(descriptor);
        if (pipe == INVALID_HANDLE_VALUE) {
            if (!announced) {
                serverReadyOk_.store(false);
                SetEvent(readyEvent_);
            }
            return;
        }
        if (!announced) {
            serverReadyOk_.store(true);
            SetEvent(readyEvent_);
            announced = true;
        }
        const BOOL connected = ConnectNamedPipe(pipe, nullptr)
            ? TRUE
            : GetLastError() == ERROR_PIPE_CONNECTED;
        if (connected) {
            Request request{};
            DWORD read{};
            const bool valid = ReadFile(pipe, &request, sizeof(request), &read, nullptr) &&
                read == sizeof(request) && request.magic == kRequestMagic &&
                request.version == kProtocolVersion && request.command == kShowCommand &&
                SameUserClient(pipe);
            const std::uint32_t reply = valid ? kReplyAccepted : 0U;
            DWORD written{};
            (void)WriteFile(pipe, &reply, sizeof(reply), &written, nullptr);
            (void)FlushFileBuffers(pipe);
            if (valid) SignalShow();
        }
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }
}

bool OverlayProcessOwner::SameUserClient(const HANDLE pipe) const noexcept {
    if (!ImpersonateNamedPipeClient(pipe)) return false;
    HANDLE token{};
    const bool opened = OpenThreadToken(GetCurrentThread(), TOKEN_QUERY, TRUE, &token) != FALSE;
    std::vector<std::byte> sid;
    if (opened) {
        DWORD size{};
        (void)GetTokenInformation(token, TokenUser, nullptr, 0, &size);
        std::vector<std::byte> buffer(size);
        if (size && GetTokenInformation(token, TokenUser, buffer.data(), size, &size)) {
            const auto* user = reinterpret_cast<const TOKEN_USER*>(buffer.data());
            const DWORD sidSize = GetLengthSid(user->User.Sid);
            sid.resize(sidSize);
            if (!CopySid(sidSize, sid.data(), user->User.Sid)) sid.clear();
        }
        CloseHandle(token);
    }
    RevertToSelf();
    return !sid.empty() && EqualSid(
        const_cast<std::byte*>(sid.data()),
        const_cast<std::byte*>(userSid_.data())) != FALSE;
}

void OverlayProcessOwner::SignalShow() noexcept {
    SetEvent(showEvent_);
    const HWND window = notificationWindow_.load();
    const UINT message = notificationMessage_.load();
    if (window && message >= WM_APP && IsWindow(window))
        (void)PostMessageW(window, message, 0, 0);
}

bool OverlayProcessOwner::SendShow(
    const std::chrono::milliseconds timeout,
    std::wstring& error) const {
    const ULONGLONG deadline = GetTickCount64() + static_cast<ULONGLONG>(timeout.count());
    do {
        const Request request{};
        DWORD read{};
        std::uint32_t reply{};
        const auto remaining = std::max<ULONGLONG>(1, deadline - std::min(deadline, GetTickCount64()));
        if (CallNamedPipeW(
                pipeName_.c_str(), const_cast<Request*>(&request), sizeof(request),
                &reply, sizeof(reply), &read,
                static_cast<DWORD>(std::min<ULONGLONG>(remaining, 250)))) {
            if (read == sizeof(reply) && reply == kReplyAccepted) {
                error.clear();
                return true;
            }
            error = L"The resident OverlayHost rejected the activation request.";
            return false;
        }
    } while (GetTickCount64() < deadline);
    error = L"The resident OverlayHost did not acknowledge activation in time.";
    return false;
}

void OverlayProcessOwner::BindNotificationWindow(
    const HWND window, const UINT message) noexcept {
    notificationMessage_.store(message);
    notificationWindow_.store(window);
    if (WaitForShow(std::chrono::milliseconds(0)) && window && IsWindow(window))
        (void)PostMessageW(window, message, 0, 0);
}

bool OverlayProcessOwner::WaitForShow(
    const std::chrono::milliseconds timeout) const noexcept {
    return showEvent_ && WaitForSingleObject(
        showEvent_, static_cast<DWORD>(std::max<long long>(0, timeout.count()))) ==
        WAIT_OBJECT_0;
}

void OverlayProcessOwner::Stop() noexcept {
    if (stopEvent_) SetEvent(stopEvent_);
    if (server_.joinable()) {
        // Wake a blocking ConnectNamedPipe without sending an accepted frame.
        HANDLE wake = CreateFileW(pipeName_.c_str(), GENERIC_READ | GENERIC_WRITE, 0,
                                  nullptr, OPEN_EXISTING, 0, nullptr);
        if (wake != INVALID_HANDLE_VALUE) CloseHandle(wake);
        server_.join();
    }
    notificationWindow_.store(nullptr);
    notificationMessage_.store(0);
    if (showEvent_) CloseHandle(showEvent_);
    if (readyEvent_) CloseHandle(readyEvent_);
    if (stopEvent_) CloseHandle(stopEvent_);
    showEvent_ = readyEvent_ = stopEvent_ = nullptr;
    if (mutex_) {
        if (owner_) ReleaseMutex(mutex_);
        CloseHandle(mutex_);
        mutex_ = nullptr;
    }
    owner_ = false;
    serverReadyOk_.store(false);
}

} // namespace gba::process
