#include "TextEntryActionAdmission.h"
#include "TextEntryModal.h"
#include "AccessibilityProvider.h"

#include <Windows.h>
#include <Unknwn.h>
#include <UIAutomation.h>

#include <chrono>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

namespace {
int failures{};
void Check(const bool value, const char* message) {
    if (!value) { ++failures; std::cerr << "FAIL: " << message << '\n'; }
}

bool IsInside(const RECT inner, const RECT outer) {
    return inner.left >= outer.left && inner.top >= outer.top &&
        inner.right <= outer.right && inner.bottom <= outer.bottom;
}

HWND CurrentFocus(const HWND modalWindow) {
    GUITHREADINFO info{sizeof(info)};
    const auto thread = GetWindowThreadProcessId(modalWindow, nullptr);
    return GetGUIThreadInfo(thread, &info) ? info.hwndFocus : nullptr;
}

std::wstring WindowText(const HWND window) {
    const int length = GetWindowTextLengthW(window);
    std::wstring result(static_cast<std::size_t>(std::max(0, length)) + 1, L'\0');
    if (length > 0) GetWindowTextW(window, result.data(), length + 1);
    result.resize(static_cast<std::size_t>(std::max(0, length)));
    return result;
}

bool WaitUntil(const auto& predicate) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (std::chrono::steady_clock::now() < deadline) {
        if (predicate()) return true;
        SwitchToThread();
    }
    return predicate();
}

widgetrail::WidgetSnapshot TextEntrySnapshot() {
    widgetrail::WidgetNode entry{};
    entry.id = L"search";
    entry.kind = L"textEntry";
    entry.actionId = L"search.commit";
    entry.textEntryValue = L"game";
    entry.textEntryPlaceholder = L"Search installed games";
    entry.textEntryMaximumLength = 64;
    entry.isTextEntry = true;
    widgetrail::WidgetSnapshot snapshot{};
    snapshot.sequence = 17;
    snapshot.activeInputScopeId = L"root";
    snapshot.root.id = L"root";
    snapshot.root.inputScopeId = L"root";
    snapshot.root.children.push_back(std::move(entry));
    return snapshot;
}

void CheckAdmission() {
    auto snapshot = TextEntrySnapshot();
    const auto request = widgetrail::input::CaptureTextEntryActionRequest(
        L"game-launcher", L"generation-a", snapshot, L"search");
    Check(request.has_value(), "current text entry captures bounded immutable authority");
    if (!request) return;
    const auto current = widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"generation-a", snapshot);
    Check(current && current->actionId == L"search.commit" &&
        current->sourceElementId == L"search" &&
        current->activeInputScopeId == L"root",
        "unchanged current request re-resolves one exact action");
    const widgetrail::accessibility::ActionRequest focusRequest{
        widgetrail::accessibility::ActionKind::Focus,
        L"game-launcher", L"generation-a", snapshot.sequence,
        snapshot.activeInputScopeId,
        widgetrail::accessibility::ElementDomain::Widget,
        L"search", L"search.commit",
    };
    const auto focused = widgetrail::accessibility::ResolveActionRequest(
        focusRequest, L"game-launcher", L"generation-a", snapshot);
    Check(focused && focused->kind == widgetrail::accessibility::ActionKind::Focus &&
        focused->nodeId == L"search",
        "current TextEntry admits exact UIA focus through the existing action owner");
    auto invokeRequest = focusRequest;
    invokeRequest.kind = widgetrail::accessibility::ActionKind::Invoke;
    const auto invoked = widgetrail::accessibility::ResolveActionRequest(
        invokeRequest, L"game-launcher", L"generation-a", snapshot);
    Check(invoked && invoked->protocolButton == L"A" &&
        invoked->nodeId == L"search",
        "current TextEntry Invoke resolves to the existing modal-opening A action");
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, false, L"game-launcher", L"generation-a", snapshot),
        "hidden or inactive widget rejects modal commit");
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"replacement", L"generation-a", snapshot),
        "active widget replacement rejects modal commit");
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"generation-b", snapshot),
        "runtime replacement rejects modal commit");

    auto changed = snapshot;
    ++changed.sequence;
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"generation-a", changed),
        "snapshot refresh rejects authority retained across the modal loop");
    changed = snapshot;
    changed.activeInputScopeId = L"replacement-scope";
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"generation-a", changed),
        "input-scope replacement rejects modal commit");
    changed = snapshot;
    changed.root.children.clear();
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"generation-a", changed),
        "removed source node rejects modal commit");
    changed = snapshot;
    changed.root.children[0].isDisabled = true;
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"generation-a", changed),
        "disabled source node rejects modal commit");
    changed = snapshot;
    changed.root.children[0].actionId = L"replacement.action";
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"generation-a", changed),
        "replaced source action rejects modal commit");
}

void CheckLayout(const RECT workArea, const UINT dpi, const char* name) {
    const auto layout = widgetrail::input::CalculateTextEntryModalLayout(workArea, dpi);
    Check(IsInside(layout.windowBounds, workArea), name);
    const RECT client{0, 0,
        layout.windowBounds.right - layout.windowBounds.left,
        layout.windowBounds.bottom - layout.windowBounds.top};
    Check(IsInside(layout.editBounds, client), "edit remains inside modal bounds");
    for (const auto bounds : layout.characterBounds)
        Check(IsInside(bounds, client), "keyboard key remains inside modal bounds");
    for (const auto bounds : layout.actionBounds)
        Check(IsInside(bounds, client), "action row remains inside modal bounds");
}

BOOL CALLBACK CollectChildren(const HWND child, const LPARAM value) {
    reinterpret_cast<std::vector<HWND>*>(value)->push_back(child);
    return TRUE;
}
}

int wmain() {
    Check(widgetrail::input::TextEntryModal::MaximumLength == 96,
        "host text entry uses the protocol bound");
    CheckAdmission();
    CheckLayout({0, 0, 640, 480}, 96, "compact modal fits the active work area");
    CheckLayout({120, 80, 1120, 780}, 96, "standard modal fits an offset work area");
    CheckLayout({0, 0, 1920, 1080}, 144, "150-percent modal fits the active work area");

    std::vector<wchar_t> ownedSecret(12);
    for (std::size_t index = 0; index < ownedSecret.size(); ++index)
        ownedSecret[index] = static_cast<wchar_t>(L'!' + index);
    widgetrail::input::SecureTextBuffer secureBuffer(std::move(ownedSecret));
    Check(secureBuffer.view().size() == 12,
        "secure modal result owns one bounded mutable character buffer");
    secureBuffer.clear();
    Check(secureBuffer.empty(), "secure modal result clears its terminal buffer");

    widgetrail::input::TextEntryModal modal;
    Check(!modal.active(), "modal starts inactive");
    Check(modal.Show(GetModuleHandleW(nullptr), nullptr, L"", L"Search", 96).outcome ==
            widgetrail::input::TextEntryModalOutcome::Failed,
        "missing owner fails closed without entering a modal loop");
    Check(modal.Show(GetModuleHandleW(nullptr), GetDesktopWindow(), L"", L"Search", 97)
            .outcome == widgetrail::input::TextEntryModalOutcome::Failed,
        "oversized maximum fails closed");

    const auto owner = CreateWindowExW(
        0, L"STATIC", L"Text entry fixture", WS_OVERLAPPED,
        0, 0, 800, 600, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    Check(owner != nullptr, "fixture owner window is available");
    std::thread driver([&] {
        Check(WaitUntil([&] { return modal.active(); }),
            "host modal reaches active controller admission");
        HWND window{};
        HWND edit{};
        Check(WaitUntil([&] {
            window = FindWindowW(
                L"WidgetRail.TextEntryModal", L"Search installed games");
            edit = window ? FindWindowExW(window, nullptr, L"EDIT", nullptr) : nullptr;
            return window && edit;
        }), "modal controls complete creation before controller input");
        Check(window != nullptr, "modal owns one discoverable native window");
        Check(edit != nullptr, "modal exposes a native text-edit accessibility control");

        RECT modalBounds{};
        GetWindowRect(window, &modalBounds);
        MONITORINFO monitorInfo{sizeof(monitorInfo)};
        GetMonitorInfoW(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &monitorInfo);
        Check(IsInside(modalBounds, monitorInfo.rcWork),
            "production modal remains inside its monitor work area");
        std::vector<HWND> children;
        Check(WaitUntil([&] {
            children.clear();
            EnumChildWindows(window, CollectChildren, reinterpret_cast<LPARAM>(&children));
            return children.size() == 44;
        }), "all modal controls complete creation before accessibility inspection");
        Check(children.size() == 44, "edit, 39 keys, and four actions are all present");
        for (const auto child : children) {
            RECT childBounds{};
            Check(GetWindowRect(child, &childBounds) && IsInside(childBounds, modalBounds),
                "every native focus target remains on screen inside the modal");
            IRawElementProviderSimple* provider{};
            Check(SUCCEEDED(UiaHostProviderFromHwnd(child, &provider)) && provider,
                "every native focus target supplies a UI Automation provider");
            if (provider) provider->Release();
            Check(!WindowText(child).empty() || child == edit,
                "keyboard and action controls expose accessible names");
        }

        Check(modal.PostController(L"A"), "controller enters the spatial keyboard");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"F"; }),
            "edit Down chooses the nearest keyboard key");
        for (const wchar_t expected : std::wstring_view(L"GHIJ")) {
            Check(modal.PostController(L"DPadRight"), "controller moves right in one row");
            Check(WaitUntil([&] {
                return WindowText(CurrentFocus(window)) == std::wstring(1, expected);
            }), "right navigation follows the keyboard row");
        }
        Check(modal.PostController(L"DPadRight"), "right edge input is admitted");
        Check(modal.PostController(L"DPadLeft"), "left input follows the edge probe");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"I"; }),
            "right edge does not wrap into an unrelated row");
        Check(modal.PostController(L"A"), "controller activates the selected key");
        for (int step = 0; step < 4; ++step) {
            const auto prior = CurrentFocus(window);
            Check(modal.PostController(L"DPadDown"), "controller moves down spatially");
            Check(WaitUntil([&] { return CurrentFocus(window) != prior; }),
                "down navigation advances to the next spatial row");
        }
        Check(WindowText(CurrentFocus(window)) == L"Commit",
            "spatial Down reaches the aligned Commit action");
        Check(modal.PostController(L"A"), "controller commits the final value");
    });
    const auto committed = modal.Show(
        GetModuleHandleW(nullptr), owner, L"A", L"Search installed games", 8);
    driver.join();
    const auto expectedCommitted = std::wstring_view(L"AI");
    Check(committed.outcome == widgetrail::input::TextEntryModalOutcome::Committed &&
        committed.committedText && std::equal(
        committed.committedText->view().begin(), committed.committedText->view().end(),
        expectedCommitted.begin(), expectedCommitted.end()),
        "on-screen key and commit publish one bounded final value");
    Check(!modal.active(), "committed modal releases its window");

    std::thread actionDriver([&] {
        Check(WaitUntil([&] { return modal.active(); }), "action-row modal becomes active");
        HWND window{};
        Check(WaitUntil([&] {
            window = FindWindowW(
                L"WidgetRail.TextEntryModal", L"Edit installed games");
            return window && FindWindowExW(window, nullptr, L"EDIT", nullptr);
        }), "action-row controls finish creation");
        Check(modal.PostController(L"A"), "controller enters the keyboard for action-row test");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"F"; }),
            "keyboard entry remains spatial");
        for (int step = 0; step < 4; ++step) {
            const auto prior = CurrentFocus(window);
            Check(modal.PostController(L"DPadDown"), "controller descends toward actions");
            Check(WaitUntil([&] { return CurrentFocus(window) != prior; }),
                "controller reaches the next action-row direction");
        }
        Check(WindowText(CurrentFocus(window)) == L"Cancel",
            "aligned Down reaches Cancel");
        Check(modal.PostController(L"DPadLeft"), "controller moves to Clear");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"Clear"; }),
            "Clear is spatially reachable");
        Check(modal.PostController(L"DPadLeft"), "controller moves to Backspace");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"Backspace"; }),
            "Backspace is spatially reachable");
        Check(modal.PostController(L"DPadRight"), "controller returns to Clear");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"Clear"; }),
            "Clear regains focus");
        Check(modal.PostController(L"A"), "controller activates Clear");
        Check(modal.PostController(L"DPadRight"), "controller moves to Cancel");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"Cancel"; }),
            "Cancel is spatially reachable");
        Check(modal.PostController(L"DPadRight"), "controller moves to Commit");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"Commit"; }),
            "Commit is spatially reachable from the action row");
        Check(modal.PostController(L"A"), "controller commits the cleared value");
    });
    const auto cleared = modal.Show(
        GetModuleHandleW(nullptr), owner, L"retained", L"Edit installed games", 8);
    actionDriver.join();
    Check(cleared.outcome == widgetrail::input::TextEntryModalOutcome::Committed &&
        cleared.committedText && cleared.committedText->empty(),
        "Clear and Commit produce one empty value");

    std::thread cancelDriver([&] {
        Check(WaitUntil([&] { return modal.active(); }), "cancel modal becomes active");
        Check(modal.PostController(L"B"), "controller B cancels the modal");
    });
    const auto cancelled = modal.Show(
        GetModuleHandleW(nullptr), owner, L"retained", L"Search installed games", 8);
    cancelDriver.join();
    Check(cancelled.outcome == widgetrail::input::TextEntryModalOutcome::Cancelled &&
        !cancelled.committedText,
        "controller cancel returns one typed outcome and no committed text");
    Check(GetFocus() == owner, "modal restores focus to its owner after cancellation");
    Check(!modal.active(), "cancelled modal releases its window");

    std::thread closeDriver([&] {
        Check(WaitUntil([&] { return modal.active(); }), "close modal becomes active");
        HWND window{};
        Check(WaitUntil([&] {
            window = FindWindowW(
                L"WidgetRail.TextEntryModal", L"Search installed games");
            return window != nullptr;
        }), "close modal window completes creation");
        Check(PostMessageW(window, WM_CLOSE, 0, 0) != FALSE,
            "native window close enters the modal terminal path");
    });
    const auto closed = modal.Show(
        GetModuleHandleW(nullptr), owner, L"retained", L"Search installed games", 8);
    closeDriver.join();
    Check(closed.outcome == widgetrail::input::TextEntryModalOutcome::Closed &&
        !closed.committedText,
        "window close remains distinct from controller cancel and commit");
    Check(GetFocus() == owner, "window close restores focus to its owner");

    std::thread passwordDriver([&] {
        const auto comResult = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        Check(WaitUntil([&] { return modal.active(); }), "password modal becomes active");
        HWND window{};
        HWND edit{};
        Check(WaitUntil([&] {
            window = FindWindowW(
                L"WidgetRail.TextEntryModal", L"Password for test network");
            edit = window ? FindWindowExW(window, nullptr, L"EDIT", nullptr) : nullptr;
            return window && edit;
        }), "password modal exposes one native edit control");
        Check((GetWindowLongPtrW(edit, GWL_STYLE) & ES_PASSWORD) != 0,
            "password modal uses native password semantics");
        IUIAutomation* automation{};
        IUIAutomationElement* element{};
        Check(SUCCEEDED(CoCreateInstance(
                CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(&automation))) && automation &&
                SUCCEEDED(automation->ElementFromHandle(edit, &element)) && element,
            "password edit exposes a UI Automation element");
        if (element) {
            VARIANT isPassword{};
            VariantInit(&isPassword);
            Check(SUCCEEDED(element->GetCurrentPropertyValue(
                        UIA_IsPasswordPropertyId, &isPassword)) &&
                    isPassword.vt == VT_BOOL && isPassword.boolVal == VARIANT_TRUE,
                "UI Automation marks the protected edit as a password");
            VariantClear(&isPassword);
            element->Release();
        }
        if (automation) automation->Release();
        SendMessageW(edit, WM_PASTE, 0, 0);
        Check(WindowText(edit).empty(), "clipboard paste is suppressed for protected input");
        Check(modal.PostController(L"B"), "controller B cancels protected input");
        if (SUCCEEDED(comResult)) CoUninitialize();
    });
    const auto protectedCancelled = modal.Show(
        GetModuleHandleW(nullptr), owner, L"", L"Password for test network", 63, true);
    passwordDriver.join();
    Check(protectedCancelled.outcome == widgetrail::input::TextEntryModalOutcome::Cancelled &&
        !protectedCancelled.committedText,
        "cancelled protected input publishes no secret");
    Check(!modal.active(), "protected modal releases its window");
    if (owner) DestroyWindow(owner);
    if (failures == 0) std::cout << "TextEntryModalTests passed\n";
    return failures == 0 ? 0 : 1;
}
