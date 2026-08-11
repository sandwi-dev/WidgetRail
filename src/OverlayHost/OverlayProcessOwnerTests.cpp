#include "OverlayProcessOwner.h"

#include <Windows.h>

#include <atomic>
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

        const auto profile = UniqueProfile(L"hidden-visible");
        gba::process::OverlayProcessOwner owner;
        std::wstring error;
        Check(owner.Begin(profile, 3s, error) == gba::process::OwnershipResult::Owner,
              "first launch wins per-user profile ownership");
        auto hiddenClient = std::async(std::launch::async, Client, profile, 3s);
        Check(hiddenClient.get() == gba::process::OwnershipResult::ClientAcknowledged,
              "hidden no-HWND owner acknowledges one Show client");
        Check(owner.WaitForShow(0ms), "hidden owner retains the pending Show signal");
        owner.BindNotificationWindow(window, kShowMessage);
        PumpUntil(1);
        Check(showMessages.load() == 1,
              "binding the owner window forwards the pending Show exactly once");

        std::vector<std::future<gba::process::OwnershipResult>> clients;
        for (int index = 0; index < 4; ++index)
            clients.push_back(std::async(std::launch::async, Client, profile, 3s));
        for (auto& client : clients)
            Check(client.get() == gba::process::OwnershipResult::ClientAcknowledged,
                  "simultaneous later launch is only an acknowledged client");
        PumpUntil(5);
        Check(showMessages.load() == 5,
              "each simultaneous launch contributes exactly one bounded Show");

        const auto names = gba::process::OverlayProcessOwner::NamesForTests(profile);
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
        owner.Stop();
        owner.Stop();

        gba::process::OverlayProcessOwner replacement;
        Check(replacement.Begin(profile, 3s, error) ==
                  gba::process::OwnershipResult::Owner,
              "orderly shutdown releases ownership exactly once");
        replacement.Stop();

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

        DestroyWindow(window);
        std::cout << "OverlayProcessOwnerTests passed (" << checks << " checks)\n";
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "OverlayProcessOwnerTests failed after " << checks
                  << " checks: " << exception.what() << '\n';
        return 1;
    }
}
