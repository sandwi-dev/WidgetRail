#include "../../src/OverlayPlatformInterop/OverlayPlatformInterop.h"
#include <Windows.h>
#include <cstdlib>
#include <iostream>
#include <string_view>
#include <thread>

namespace {
int checks{};
void Check(bool value, const char* description) {
    ++checks;
    if (!value) { std::cerr << "FAIL: " << description << '\n'; std::exit(EXIT_FAILURE); }
}
}

// No Initialize call: these tests never create a GameInput/controller owner.
// Default execution uses hidden windows only and never changes foreground.
int main(int argc, char** argv) {
    using Status = WidgetRailOverlayPlatformStatus;
    Check(WidgetRailOverlayPlatformGetAbiVersion() == WRAIL_OVERLAY_PLATFORM_ABI_VERSION, "caller ABI matches DLL");
    std::uint32_t confirmed = 99;
    Check(WidgetRailOverlayPlatformAcquireForeground(nullptr, &confirmed) == Status::InvalidArgument && !confirmed,
        "null owner clears confirmation and is rejected");
    WidgetRailOverlayPlatformCreateOptions options;
    WidgetRailOverlayPlatformHandle* owner{};
    Check(WidgetRailOverlayPlatformCreate(&options, &owner) == Status::Ok && owner,
        "presentation owner can be created without hardware initialization");
    Check(WidgetRailOverlayPlatformAcquireForeground(owner, nullptr) == Status::InvalidArgument,
        "null output is rejected");
    Check(WidgetRailOverlayPlatformAcquireForeground(owner, &confirmed) == Status::InvalidArgument && !confirmed,
        "unregistered window is rejected");
    const auto assign = [&](HWND window) {
        Check(WidgetRailOverlayPlatformSetOwnedWindows(owner, reinterpret_cast<std::uintptr_t>(window), 0) == Status::Ok,
            "window registration succeeds");
    };
    assign(GetDesktopWindow());
    Check(WidgetRailOverlayPlatformAcquireForeground(owner, &confirmed) == Status::InvalidArgument && !confirmed,
        "foreign process window is rejected");
    const HWND window = CreateWindowExW(0, L"STATIC", L"WidgetRail foreground boundary test", WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, 320, 120, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    Check(window != nullptr, "owned hidden test window is created");
    assign(window);
    Check(WidgetRailOverlayPlatformAcquireForeground(owner, &confirmed) == Status::Ok && !confirmed,
        "hidden owned window does not attempt activation");
    Status otherThreadStatus{};
    std::uint32_t otherThreadConfirmed = 99;
    std::thread other([&] { otherThreadStatus = WidgetRailOverlayPlatformAcquireForeground(owner, &otherThreadConfirmed); });
    other.join();
    Check(otherThreadStatus == Status::InvalidArgument && !otherThreadConfirmed,
        "wrong UI thread is rejected before activation");

    // Explicit opt-in for a separately scheduled desktop check. Windows may
    // decline activation: the invariant is reporting actual process ownership,
    // not treating a successful API call as proof that activation was granted.
    if (argc > 1 && std::string_view(argv[1]) == "--activation") {
        ShowWindow(window, SW_SHOWNOACTIVATE);
        Check(WidgetRailOverlayPlatformAcquireForeground(owner, &confirmed) == Status::Ok,
            "visible owned acquisition executes");
        DWORD foregroundProcess{};
        (void)GetWindowThreadProcessId(GetForegroundWindow(), &foregroundProcess);
        Check((confirmed != 0) == (foregroundProcess == GetCurrentProcessId()),
            "confirmation reports actual foreground process");
        std::cout << "Foreground confirmed=" << confirmed << '\n';
    }
    DestroyWindow(window);
    confirmed = 99;
    Check(WidgetRailOverlayPlatformAcquireForeground(owner, &confirmed) == Status::InvalidArgument && !confirmed,
        "destroyed owned window is rejected");
    WidgetRailOverlayPlatformShutdown(owner);
    confirmed = 99;
    Check(WidgetRailOverlayPlatformAcquireForeground(owner, &confirmed) == Status::ShutDown && !confirmed,
        "retired owner cannot reactivate a window");
    WidgetRailOverlayPlatformDestroy(owner);
    std::cout << "ForegroundAcquisitionTests passed (" << checks << " checks)\n";
}
