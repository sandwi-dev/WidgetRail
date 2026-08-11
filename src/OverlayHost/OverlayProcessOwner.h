#pragma once

#include <Windows.h>

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace gba::process {

enum class OwnershipResult {
    Owner,
    ClientAcknowledged,
    ClientFailed,
};

class OverlayProcessOwner final {
public:
    OverlayProcessOwner() = default;
    ~OverlayProcessOwner();
    OverlayProcessOwner(const OverlayProcessOwner&) = delete;
    OverlayProcessOwner& operator=(const OverlayProcessOwner&) = delete;

    [[nodiscard]] OwnershipResult Begin(
        std::wstring_view profile,
        std::chrono::milliseconds clientTimeout,
        std::wstring& error);
    void BindNotificationWindow(HWND window, UINT message) noexcept;
    [[nodiscard]] bool WaitForShow(std::chrono::milliseconds timeout) const noexcept;
    void Stop() noexcept;

    [[nodiscard]] bool owner() const noexcept { return owner_; }
    [[nodiscard]] std::wstring_view profile() const noexcept { return profile_; }

#ifdef GBA_OVERLAY_PROCESS_OWNER_TESTING
    struct ObjectNamesForTests final {
        std::wstring mutex;
        std::wstring pipe;
    };
    [[nodiscard]] static ObjectNamesForTests NamesForTests(std::wstring_view profile);
    [[nodiscard]] std::wstring_view mutexNameForTests() const noexcept { return mutexName_; }
    [[nodiscard]] std::wstring_view pipeNameForTests() const noexcept { return pipeName_; }
#endif

private:
    void ServerLoop() noexcept;
    [[nodiscard]] bool StartServer(std::wstring& error);
    [[nodiscard]] bool SendShow(
        std::chrono::milliseconds timeout,
        std::wstring& error) const;
    [[nodiscard]] bool SameUserClient(HANDLE pipe) const noexcept;
    void SignalShow() noexcept;

    HANDLE mutex_{};
    HANDLE stopEvent_{};
    HANDLE readyEvent_{};
    HANDLE showEvent_{};
    std::thread server_;
    std::wstring profile_;
    std::wstring mutexName_;
    std::wstring pipeName_;
    std::vector<std::byte> userSid_;
    std::atomic<HWND> notificationWindow_{};
    std::atomic<UINT> notificationMessage_{};
    std::atomic<bool> serverReadyOk_{};
    bool owner_{};
};

[[nodiscard]] bool ValidProcessProfile(std::wstring_view profile) noexcept;

} // namespace gba::process
