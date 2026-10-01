#include "../../src/OverlayPlatformInterop/OverlayProcessInterop.h"
#include "../../src/OverlayPlatformInterop/OverlayProcessOwner.h"
#include <future>
#include <iostream>
#include <stdexcept>
#include <array>

namespace {
using namespace std::chrono_literals;
constexpr UINT message = WM_APP + 0x719;
int shown{}, checks{};
void Check(bool value, const char* reason) { ++checks; if (!value) throw std::runtime_error(reason); }
LRESULT CALLBACK WindowProc(HWND window, UINT id, WPARAM w, LPARAM l) {
    if (id == message) { ++shown; return 0; }
    return DefWindowProcW(window, id, w, l);
}
void Pump() {
    const auto deadline = GetTickCount64() + 2000;
    while (!shown && GetTickCount64() < deadline) {
        MSG event{};
        while (PeekMessageW(&event, nullptr, 0, 0, PM_REMOVE)) DispatchMessageW(&event);
        Sleep(5);
    }
}
WidgetRailProcessResult Begin(const std::wstring& profile, bool show, void** owner) {
    std::array<wchar_t, 512> error{};
    return WidgetRailProcessBegin(1, profile.c_str(), static_cast<std::uint32_t>(profile.size()), show, 3000, owner, error.data(), 512);
}
}
int main() {
    try {
        const auto profile = L"lifecycle-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64());
        WNDCLASSW type{}; type.lpfnWndProc = WindowProc; type.hInstance = GetModuleHandleW(nullptr); type.lpszClassName = L"LifecycleFixture";
        RegisterClassW(&type);
        const auto window = CreateWindowExW(0, type.lpszClassName, L"", 0, 0, 0, 1, 1, HWND_MESSAGE, nullptr, type.hInstance, nullptr);
        Check(window != nullptr, "message-only activation window creates without showing desktop UI");
        void* owner{};
        Check(Begin(profile, false, &owner) == WidgetRailProcessResult::Owner && owner, "quiet first launch elects owner");
        auto silent = std::async(std::launch::async, [&] { void* client{}; auto result = Begin(profile, false, &client); return result == WidgetRailProcessResult::AlreadyRunning && !client; });
        Check(silent.get(), "quiet duplicate exits without new owner");
        Check(WidgetRailProcessBindWindow(owner, reinterpret_cast<std::uintptr_t>(window), message) == WidgetRailProcessStatus::Ok, "owner binds native HWND");
        MSG pending{}; Check(!PeekMessageW(&pending, window, message, message, PM_REMOVE), "quiet duplicate never schedules Show");
        auto oldClient = std::async(std::launch::async, [&] {
            widgetrail::process::OverlayProcessOwner legacy; std::wstring error;
            return legacy.Begin(profile, 3s, error);
        });
        Check(oldClient.get() == widgetrail::process::OwnershipResult::ClientAcknowledged, "original native client redirects to WinUI owner protocol");
        Pump(); Check(shown == 1, "legacy redirect publishes one Show");
        auto wrongThread = std::async(std::launch::async, [&] { return WidgetRailProcessEnd(owner); });
        Check(wrongThread.get() == WidgetRailProcessStatus::WrongThread, "cross-thread release is rejected without destroying owner");
        Check(WidgetRailProcessEnd(owner) == WidgetRailProcessStatus::Ok, "electing thread releases owner"); owner = nullptr;
        shown = 0;
        Check(Begin(profile, true, &owner) == WidgetRailProcessResult::Owner, "new owner can register before its XAML window exists");
        auto early = std::async(std::launch::async, [&] { void* client{}; return Begin(profile, true, &client); });
        Check(early.get() == WidgetRailProcessResult::Redirected, "early Show is acknowledged without a bound HWND");
        Check(WidgetRailProcessBindWindow(owner, reinterpret_cast<std::uintptr_t>(window), message) == WidgetRailProcessStatus::Ok, "late HWND binding accepts retained activation");
        Pump(); Check(shown == 1, "retained early Show is delivered exactly once");
        Check(WidgetRailProcessEnd(owner) == WidgetRailProcessStatus::Ok, "early-activation owner cleans up"); owner = nullptr;
        widgetrail::process::OverlayProcessOwner legacyOwner; std::wstring error;
        Check(legacyOwner.Begin(profile, 3s, error) == widgetrail::process::OwnershipResult::Owner, "released lease admits original native owner");
        auto newClient = std::async(std::launch::async, [&] { void* client{}; return Begin(profile, true, &client); });
        Check(newClient.get() == WidgetRailProcessResult::Redirected && legacyOwner.WaitForShow(2s), "WinUI client redirects to original native owner");
        auto newQuiet = std::async(std::launch::async, [&] { void* client{}; return Begin(profile, false, &client); });
        Check(newQuiet.get() == WidgetRailProcessResult::AlreadyRunning, "quiet launch works with unchanged native Show-only protocol");
        void* isolated{};
        Check(Begin(profile + L"-other", true, &isolated) == WidgetRailProcessResult::Owner, "different profiles remain independent");
        Check(WidgetRailProcessEnd(isolated) == WidgetRailProcessStatus::Ok, "isolated owner cleans up");
        legacyOwner.Stop(); DestroyWindow(window);
        Check(Begin(L"bad/profile", true, &owner) == WidgetRailProcessResult::Failed && !owner, "bad profile cannot elect or leak handle");
        Check(WidgetRailProcessBegin(999, profile.c_str(), static_cast<std::uint32_t>(profile.size()), 1, 3000, &owner, nullptr, 0) == WidgetRailProcessResult::Failed && !owner, "ABI mismatch fails before ownership");
        std::cout << "ProcessLifecycleTests passed (" << checks << " checks)\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
