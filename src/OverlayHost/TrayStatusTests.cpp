#include "TrayStatus.h"
#include <Windows.h>
#include <iostream>
#include <stdexcept>

int main() {
    using namespace widgetrail::shell;
    const auto check = [](bool value) { if (!value) throw std::runtime_error("Tray status contract failed"); };
    check(ResolveInternetStatus(0) == InternetStatus::Offline);
    check(ResolveInternetStatus(1) == InternetStatus::Limited);
    check(ResolveInternetStatus(2) == InternetStatus::Limited);
    check(ResolveInternetStatus(3) == InternetStatus::Online);
    check(ResolveInternetStatus(99) == InternetStatus::Unknown);
    TrayStatusSnapshot status{L"10:42 AM", L"9/12/2026", InternetStatus::Limited, WirelessStatus::Off};
    check(status.Description().find(L"sign-in required") != std::wstring::npos);
    check(status.Description().find(L"Bluetooth off") != std::wstring::npos);
    TrayStatusMonitor monitor;
    const auto started = GetTickCount64();
    auto observed = monitor.Read(true);
    check(GetTickCount64() - started < 1000); // OS discovery cannot block this call.
    check(!observed.time.empty() && !observed.date.empty());
    for (int i = 0; i < 50 && observed.internet == InternetStatus::Unknown; ++i) {
        Sleep(100);
        observed = monitor.Read(false);
    }
    std::wcout << L"TrayStatusTests passed (9 checks); " << observed.Description() << L"\n";
}
