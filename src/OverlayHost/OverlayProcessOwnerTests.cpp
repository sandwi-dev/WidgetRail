#include "OverlayProcessOwner.h"

#include <Windows.h>
#include <sddl.h>

#include <atomic>
#include <array>
#include <chrono>
#include <future>
#include <iostream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace {

using namespace std::chrono_literals;
constexpr UINT kShowMessage = WM_APP + 0x517;
std::atomic<int> showMessages{};
int checks{};

void Completed(const std::string_view scenario) {
    std::cerr << "OverlayProcessOwnerTests checkpoint completed=" << scenario << '\n';
}

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) throw std::runtime_error(std::string(message));
}

LRESULT CALLBACK TestWindowProc(
    const HWND window, const UINT message, const WPARAM wParam, const LPARAM lParam) {
    if (message == kShowMessage) {
        ++showMessages;
        return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

std::wstring UniqueProfile(const std::wstring_view label) {
    return L"dlv070-" + std::wstring(label) + L"-" +
        std::to_wstring(GetCurrentProcessId()) + L"-" +
        std::to_wstring(GetTickCount64());
}

gba::process::OwnershipResult Client(
    const std::wstring profile,
    const std::chrono::milliseconds timeout = 3s) {
    gba::process::OverlayProcessOwner client;
    std::wstring error;
    return client.Begin(profile, timeout, error);
}

void PumpUntil(const int expected, const std::chrono::milliseconds timeout = 3s) {
    const auto deadline = GetTickCount64() + timeout.count();
    while (showMessages.load() < expected && GetTickCount64() < deadline) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        std::this_thread::sleep_for(5ms);
    }
}

} // namespace

int main() {
    try {
        Check(!gba::process::ValidProcessProfile(L"") &&
                  !gba::process::ValidProcessProfile(L"bad/profile") &&
                  gba::process::ValidProcessProfile(L"fixture.Profile-1"),
              "process profiles are closed and path-free");

        WNDCLASSEXW windowClass{sizeof(windowClass)};
        windowClass.lpfnWndProc = TestWindowProc;
        windowClass.hInstance = GetModuleHandleW(nullptr);
        windowClass.lpszClassName = L"GbaOverlayProcessOwnerTests";
        Check(RegisterClassExW(&windowClass) != 0, "test window class registers");
        const HWND window = CreateWindowExW(
            0, windowClass.lpszClassName, L"", 0, 0, 0, 1, 1,
            HWND_MESSAGE, nullptr, windowClass.hInstance, nullptr);
        Check(window != nullptr, "message-only test window creates");
        Completed("profile-and-window-validation");

        const auto profile = UniqueProfile(L"hidden-visible");
        gba::process::OverlayProcessOwner owner;
        std::wstring error;
        const auto ownerBeginStarted = std::chrono::steady_clock::now();
        Check(owner.Begin(profile, 3s, error) == gba::process::OwnershipResult::Owner,
              "first launch wins per-user profile ownership");
        Check(std::chrono::steady_clock::now() - ownerBeginStarted < 3s,
              "first-owner startup returns inside its readiness bound");
        auto hiddenClient = std::async(std::launch::async, Client, profile, 3s);
        Check(hiddenClient.get() == gba::process::OwnershipResult::ClientAcknowledged,
              "hidden no-HWND owner acknowledges one Show client");
        Check(owner.WaitForShow(0ms), "hidden owner retains the pending Show signal");
        owner.BindNotificationWindow(window, kShowMessage);
        PumpUntil(1);
        Check(showMessages.load() == 1,
              "binding the owner window forwards the pending Show exactly once");
        Completed("hidden-owner-activation");

        std::vector<std::future<gba::process::OwnershipResult>> clients;
        for (int index = 0; index < 4; ++index)
            clients.push_back(std::async(std::launch::async, Client, profile, 3s));
        for (auto& client : clients)
            Check(client.get() == gba::process::OwnershipResult::ClientAcknowledged,
                  "simultaneous later launch is only an acknowledged client");
        PumpUntil(5);
        Check(showMessages.load() == 5,
              "each simultaneous launch contributes exactly one bounded Show");
        Completed("simultaneous-clients");

        const auto names = gba::process::OverlayProcessOwner::NamesForTests(profile);
        Check(names.mutex.starts_with(L"Global\\WidgetRail.OverlayHost.Owner.") &&
                  names.pipe.starts_with(L"\\\\.\\pipe\\WidgetRail.OverlayHost.Activation."),
              "the only live singleton and activation endpoints use WidgetRail identity");
        Check(names.mutex.find(L"GameBarAlternative") == std::wstring::npos &&
                  names.pipe.find(L"GameBarAlternative") == std::wstring::npos,
              "singleton and activation endpoints expose no legacy authority");
        struct BadRequest final { std::uint32_t magic{}; std::uint32_t payload{}; } bad;
        std::uint32_t reply = 1;
        DWORD read{};
        Check(CallNamedPipeW(
                  names.pipe.c_str(), &bad, sizeof(bad), &reply, sizeof(reply),
                  &read, 1000) && read == sizeof(reply) && reply == 0,
              "malformed client receives a bounded rejection");
        PumpUntil(6, 100ms);
        Check(showMessages.load() == 5,
              "rejected client cannot activate the owner window");
        Completed("malformed-client-rejection");

        const auto stalledReadProfile = UniqueProfile(L"stalled-read");
        gba::process::OverlayProcessOwner stalledReadOwner;
        Check(stalledReadOwner.Begin(stalledReadProfile, 3s, error) ==
                  gba::process::OwnershipResult::Owner,
              "stalled-read fixture owns its isolated profile");
        const auto stalledReadNames =
            gba::process::OverlayProcessOwner::NamesForTests(stalledReadProfile);
        HANDLE stalledReader = CreateFileW(
            stalledReadNames.pipe.c_str(), GENERIC_READ | GENERIC_WRITE, 0,
            nullptr, OPEN_EXISTING, 0, nullptr);
        Check(stalledReader != INVALID_HANDLE_VALUE,
              "stalled client connects without sending an activation frame");
        auto stoppedReadOwner = std::async(std::launch::async, [&] {
            stalledReadOwner.Stop();
        });
        Check(stoppedReadOwner.wait_for(2s) == std::future_status::ready,
              "owner stop cancels a pending client read within its bound");
        stoppedReadOwner.get();
        CloseHandle(stalledReader);
        Completed("stalled-server-read-stop");

        owner.Stop();
        owner.Stop();

        gba::process::OverlayProcessOwner replacement;
        Check(replacement.Begin(profile, 3s, error) ==
                  gba::process::OwnershipResult::Owner,
              "orderly shutdown releases ownership exactly once");
        replacement.Stop();
        Completed("orderly-owner-replacement");

        const auto squattedProfile = UniqueProfile(L"squatted");
        const auto squattedNames =
            gba::process::OverlayProcessOwner::NamesForTests(squattedProfile);
        HANDLE squatter = CreateNamedPipeW(
            squattedNames.pipe.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1, 64, 64, 100, nullptr);
        Check(squatter != INVALID_HANDLE_VALUE,
              "endpoint-squatter fixture claims only its isolated profile");
        gba::process::OverlayProcessOwner refusedOwner;
        Check(refusedOwner.Begin(squattedProfile, 3s, error) ==
                  gba::process::OwnershipResult::ClientFailed,
              "owner fails closed when its activation endpoint is already claimed");
        CloseHandle(squatter);
        Completed("endpoint-squatter-rejection");

        const auto stalledProfile = UniqueProfile(L"timeout");
        const auto stalledNames =
            gba::process::OverlayProcessOwner::NamesForTests(stalledProfile);
        std::promise<HANDLE> heldPromise;
        std::promise<void> releasePromise;
        auto release = releasePromise.get_future();
        std::thread holder([&] {
            HANDLE held = CreateMutexW(nullptr, TRUE, stalledNames.mutex.c_str());
            heldPromise.set_value(held);
            release.wait();
            ReleaseMutex(held);
            CloseHandle(held);
        });
        HANDLE held = heldPromise.get_future().get();
        Check(held != nullptr, "stalled-owner fixture owns the exact profile mutex");
        auto timedClient = std::async(std::launch::async, Client, stalledProfile, 150ms);
        Check(timedClient.get() == gba::process::OwnershipResult::ClientFailed,
              "client timeout is bounded when an owner has no activation transport");
        releasePromise.set_value();
        holder.join();
        Completed("stalled-owner-timeout");

        const auto replyProfile = UniqueProfile(L"stalled-reply");
        const auto replyNames = gba::process::OverlayProcessOwner::NamesForTests(replyProfile);
        std::promise<void> replyServerReadyPromise;
        auto replyServerReady = replyServerReadyPromise.get_future();
        std::promise<void> releaseReplyServerPromise;
        auto releaseReplyServer = releaseReplyServerPromise.get_future();
        std::thread replyServer([&] {
            HANDLE replyMutex = CreateMutexW(nullptr, TRUE, replyNames.mutex.c_str());
            PSECURITY_DESCRIPTOR replyDescriptor{};
            SECURITY_ATTRIBUTES replySecurity{sizeof(replySecurity)};
            if (ConvertStringSecurityDescriptorToSecurityDescriptorW(
                    L"D:P(A;;GA;;;WD)", SDDL_REVISION_1,
                    &replyDescriptor, nullptr)) {
                replySecurity.lpSecurityDescriptor = replyDescriptor;
            }
            HANDLE replyPipe = CreateNamedPipeW(
                replyNames.pipe.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
                PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT |
                    PIPE_REJECT_REMOTE_CLIENTS,
                1, 64, 64, 100,
                replySecurity.lpSecurityDescriptor ? &replySecurity : nullptr);
            if (replyDescriptor) LocalFree(replyDescriptor);
            replyServerReadyPromise.set_value();
            if (replyMutex && replyPipe != INVALID_HANDLE_VALUE) {
                const BOOL connected = ConnectNamedPipe(replyPipe, nullptr)
                    ? TRUE
                    : GetLastError() == ERROR_PIPE_CONNECTED;
                if (connected) {
                    std::array<std::byte, 8> request{};
                    DWORD readBytes{};
                    (void)ReadFile(
                        replyPipe, request.data(), static_cast<DWORD>(request.size()),
                        &readBytes, nullptr);
                    releaseReplyServer.wait();
                    DisconnectNamedPipe(replyPipe);
                }
            }
            if (replyPipe != INVALID_HANDLE_VALUE) CloseHandle(replyPipe);
            if (replyMutex) {
                ReleaseMutex(replyMutex);
                CloseHandle(replyMutex);
            }
        });
        replyServerReady.wait();
        gba::process::OverlayProcessOwner stalledReplyClient;
        const auto replyStarted = std::chrono::steady_clock::now();
        Check(stalledReplyClient.Begin(replyProfile, 150ms, error) ==
                  gba::process::OwnershipResult::ClientFailed,
              "client fails when the resident endpoint withholds its reply");
        Check(std::chrono::steady_clock::now() - replyStarted < 1s,
              "client request and reply share the advertised activation deadline");
        releaseReplyServerPromise.set_value();
        replyServer.join();
        Completed("stalled-owner-reply-timeout");

        const auto staleProfile = UniqueProfile(L"stale");
        const auto staleNames = gba::process::OverlayProcessOwner::NamesForTests(staleProfile);
        std::promise<HANDLE> abandonedPromise;
        std::thread abandoned([&] {
            HANDLE stale = CreateMutexW(nullptr, TRUE, staleNames.mutex.c_str());
            abandonedPromise.set_value(stale);
            // Thread exit abandons ownership while the process retains the handle.
        });
        HANDLE staleHandle = abandonedPromise.get_future().get();
        abandoned.join();
        gba::process::OverlayProcessOwner recovered;
        Check(recovered.Begin(staleProfile, 3s, error) ==
                  gba::process::OwnershipResult::Owner,
              "abandoned owner lease is recovered without destructive cleanup");
        recovered.Stop();
        CloseHandle(staleHandle);
        Completed("abandoned-owner-recovery");

        DestroyWindow(window);
        std::cout << "OverlayProcessOwnerTests passed (" << checks << " checks)\n";
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "OverlayProcessOwnerTests failed after " << checks
                  << " checks: " << exception.what() << '\n';
        return 1;
    }
}
