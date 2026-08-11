#include "TextEntryModal.h"

#include <Windows.h>
#include <Unknwn.h>
#include <UIAutomation.h>

#include <iostream>
#include <thread>
#include <chrono>

namespace {
int failures{};
void Check(const bool value, const char* message) {
    if (!value) { ++failures; std::cerr << "FAIL: " << message << '\n'; }
}
}

int wmain() {
    Check(gba::input::TextEntryModal::MaximumLength == 96,
        "host text entry uses the protocol bound");
    gba::input::TextEntryModal modal;
    Check(!modal.active(), "modal starts inactive");
    Check(!modal.Show(GetModuleHandleW(nullptr), nullptr, L"", L"Search", 96),
        "missing owner fails closed without entering a modal loop");
    Check(!modal.Show(GetModuleHandleW(nullptr), GetDesktopWindow(), L"", L"Search", 97),
        "oversized maximum fails closed");
    Check(!modal.active(), "failed admission retains no modal window");

    const auto owner = CreateWindowExW(
        0, L"STATIC", L"Text entry fixture", WS_OVERLAPPED,
        0, 0, 800, 600, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    Check(owner != nullptr, "fixture owner window is available");
    std::thread driver([&] {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
        while (!modal.active() && std::chrono::steady_clock::now() < deadline)
            SwitchToThread();
        Check(modal.active(), "host modal reaches active controller admission");
        HWND window{};
        HWND edit{};
        while ((!window || !edit) && std::chrono::steady_clock::now() < deadline) {
            window = FindWindowW(
                L"GameBarAlternative.TextEntryModal", L"Search installed games");
            edit = window ? FindWindowExW(window, nullptr, L"EDIT", nullptr) : nullptr;
            if (!edit) SwitchToThread();
        }
        Check(window != nullptr, "modal owns one discoverable native window");
        Check(edit != nullptr, "modal exposes a native text-edit accessibility control");
        IRawElementProviderSimple* provider{};
        Check(edit && SUCCEEDED(UiaHostProviderFromHwnd(edit, &provider)) && provider,
            "native text edit supplies a UI Automation provider");
        if (provider) provider->Release();
        Check(modal.PostController(L"DPadRight"), "controller enters the keyboard");
        Check(modal.PostController(L"DPadRight"), "controller selects B");
        Check(modal.PostController(L"A"), "controller activates the selected key");
        Check(modal.PostController(L"DPadLeft"), "controller moves toward commit");
        Check(modal.PostController(L"DPadLeft"), "controller wraps from edit");
        Check(modal.PostController(L"DPadLeft"), "controller selects commit");
        Check(modal.PostController(L"A"), "controller commits the final value");
    });
    const auto committed = modal.Show(
        GetModuleHandleW(nullptr), owner, L"A", L"Search installed games", 8);
    driver.join();
    Check(committed && *committed == L"AB",
        "on-screen key and commit publish one bounded final value");
    Check(!modal.active(), "committed modal releases its window");

    std::thread cancelDriver([&] {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
        while (!modal.active() && std::chrono::steady_clock::now() < deadline)
            SwitchToThread();
        Check(modal.PostController(L"B"), "controller B cancels the modal");
    });
    const auto cancelled = modal.Show(
        GetModuleHandleW(nullptr), owner, L"retained", L"Search installed games", 8);
    cancelDriver.join();
    Check(!cancelled, "cancel returns no committed text");
    Check(GetFocus() == owner, "modal restores focus to its owner after cancellation");
    Check(!modal.active(), "cancelled modal releases its window");
    if (owner) DestroyWindow(owner);
    if (failures == 0) std::cout << "TextEntryModalTests passed\n";
    return failures == 0 ? 0 : 1;
}
