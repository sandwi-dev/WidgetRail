#include "TextEntryActionAdmission.h"
#include "TextEntryModal.h"
#include "AccessibilityProvider.h"

#include <Windows.h>
#include <Unknwn.h>
#include <UIAutomation.h>

#include <algorithm>
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

std::wstring WindowClass(const HWND window) {
    std::wstring result(64, L'\0');
    const int length = GetClassNameW(window, result.data(), static_cast<int>(result.size()));
    result.resize(static_cast<std::size_t>(std::max(0, length)));
    return result;
}

std::wstring ModalTitle(const std::wstring_view prompt) {
    return L"WidgetRail text entry - " + std::wstring(prompt);
}

std::pair<DWORD, DWORD> EditSelection(const HWND edit) {
    DWORD start{};
    DWORD end{};
    SendMessageW(edit, EM_GETSEL,
        reinterpret_cast<WPARAM>(&start), reinterpret_cast<LPARAM>(&end));
    return {start, end};
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
        L"game-launcher", L"runtime-a", L"presentation-a", snapshot, L"search");
    Check(request.has_value(), "current text entry captures bounded immutable authority");
    if (!request) return;
    const auto current = widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-a", snapshot);
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
        *request, false, L"game-launcher", L"runtime-a", L"presentation-a", snapshot),
        "hidden or inactive widget rejects modal commit");
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"replacement", L"runtime-a", L"presentation-a", snapshot),
        "active widget replacement rejects modal commit");
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-b", L"presentation-a", snapshot),
        "runtime replacement rejects modal commit");
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-b", snapshot),
        "presentation replacement rejects modal commit");

    auto changed = snapshot;
    ++changed.sequence;
    Check(widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-a", changed)
            .has_value(),
        "harmless higher-sequence refresh retains exact modal action authority");
    changed = snapshot;
    --changed.sequence;
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-a", changed),
        "regressive snapshot authority rejects modal commit");
    changed = snapshot;
    changed.activeInputScopeId = L"replacement-scope";
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-a", changed),
        "input-scope replacement rejects modal commit");
    changed = snapshot;
    changed.root.children.clear();
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-a", changed),
        "removed source node rejects modal commit");
    changed = snapshot;
    changed.root.children[0].isDisabled = true;
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-a", changed),
        "disabled source node rejects modal commit");
    changed = snapshot;
    changed.root.children[0].isBusy = true;
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-a", changed),
        "busy source node rejects modal commit");
    changed = snapshot;
    changed.root.children[0].actionId = L"replacement.action";
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-a", changed),
        "replaced source action rejects modal commit");
    changed = snapshot;
    changed.root.children[0].textEntryValue = L"replacement";
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-a", changed),
        "changed committed value rejects modal commit");
    changed = snapshot;
    changed.root.children[0].textEntryMaximumLength = 63;
    Check(!widgetrail::input::ResolveTextEntryActionTarget(
        *request, true, L"game-launcher", L"runtime-a", L"presentation-a", changed),
        "changed text-entry bound rejects modal commit");
}

void CheckLayout(const RECT workArea, const UINT dpi, const char* name) {
    const auto layout = widgetrail::input::CalculateTextEntryModalLayout(workArea, dpi);
    Check(IsInside(layout.windowBounds, workArea), name);
    const RECT client{0, 0,
        layout.windowBounds.right - layout.windowBounds.left,
        layout.windowBounds.bottom - layout.windowBounds.top};
    Check(IsInside(layout.editBounds, client), "edit remains inside modal bounds");
    Check(IsInside(layout.promptBounds, client), "prompt remains inside modal bounds");
    for (const auto bounds : layout.keyBounds)
        Check(IsInside(bounds, client), "keyboard key remains inside modal bounds");
    Check(IsInside(layout.legendBounds, client),
        "controller legend remains inside modal bounds");
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
    CheckLayout({0, 0, 1920, 1080}, 96, "wide modal fits the active work area");
    const auto scaled = widgetrail::input::CalculateTextEntryModalLayout(
        {0, 0, 1920, 1080}, 96, 1.5);
    Check(scaled.scale == 1.5,
        "interface scale is applied independently inside a sufficient work area");

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
    ShowWindow(owner, SW_SHOW);
    UpdateWindow(owner);
    SetFocus(owner);

    widgetrail::input::TextEntryModalTheme theme;
    theme.canvas = RGB(7, 11, 17);
    theme.panel = RGB(19, 23, 29);
    theme.control = RGB(31, 37, 43);
    theme.controlFocused = RGB(47, 53, 59);
    theme.text = RGB(229, 233, 239);
    theme.secondaryText = RGB(131, 137, 149);
    theme.focus = RGB(251, 193, 71);
    std::thread driver([&] {
        Check(WaitUntil([&] { return modal.active(); }),
            "host modal reaches active controller admission");
        HWND window{};
        HWND edit{};
        Check(WaitUntil([&] {
            window = FindWindowW(
                L"WidgetRail.TextEntryModal", ModalTitle(L"Search installed games").c_str());
            edit = window ? FindWindowExW(window, nullptr, L"EDIT", nullptr) : nullptr;
            return window && edit;
        }), "modal controls complete creation before controller input");
        Check(window != nullptr, "modal owns one discoverable native window");
        Check(edit != nullptr, "modal exposes a native text-edit accessibility control");
        Check(WaitUntil([&] { return !IsWindowEnabled(owner); }),
            "modal disables its owner for exclusive pointer and keyboard input");

        const auto style = static_cast<DWORD>(GetWindowLongPtrW(window, GWL_STYLE));
        const auto extendedStyle = static_cast<DWORD>(
            GetWindowLongPtrW(window, GWL_EXSTYLE));
        Check((style & WS_POPUP) != 0 &&
                (style & (WS_CAPTION | WS_THICKFRAME | WS_SYSMENU)) == 0,
            "modal has no caption, resize frame, or system-menu affordance");
        Check((extendedStyle & WS_EX_CONTROLPARENT) != 0 &&
                (extendedStyle & WS_EX_DLGMODALFRAME) == 0,
            "modal retains control navigation without the untinted system frame");

        RECT modalBounds{};
        RECT clientBounds{};
        GetWindowRect(window, &modalBounds);
        GetClientRect(window, &clientBounds);
        Check(modalBounds.right - modalBounds.left == clientBounds.right &&
                modalBounds.bottom - modalBounds.top == clientBounds.bottom,
            "borderless modal keeps the authored client geometry exact");
        MONITORINFO monitorInfo{sizeof(monitorInfo)};
        GetMonitorInfoW(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &monitorInfo);
        Check(IsInside(modalBounds, monitorInfo.rcWork),
            "production modal remains inside its monitor work area");
        std::vector<HWND> children;
        Check(WaitUntil([&] {
            children.clear();
            EnumChildWindows(window, CollectChildren, reinterpret_cast<LPARAM>(&children));
            return children.size() == 43;
        }), "all modal controls complete creation before accessibility inspection");
        Check(children.size() == 43,
            "prompt, edit, 40 keys, and controller legend are all present");
        const auto buttonCount = std::count_if(children.begin(), children.end(),
            [](const HWND child) { return WindowClass(child) == L"Button"; });
        Check(buttonCount == widgetrail::input::TextEntryKeyCount,
            "keyboard exposes exactly 40 focusable keys and no action row");
        for (const auto child : children) {
            RECT childBounds{};
            Check(GetWindowRect(child, &childBounds) && IsInside(childBounds, modalBounds),
                "every native focus target remains on screen inside the modal");
            IRawElementProviderSimple* provider{};
            Check(SUCCEEDED(UiaHostProviderFromHwnd(child, &provider)) && provider,
                "every native focus target supplies a UI Automation provider");
            if (provider) provider->Release();
            Check(!WindowText(child).empty(),
                "prompt, edit, keyboard, and legend expose accessible text");
            const auto text = WindowText(child);
            Check(text != L"Commit" && text != L"Clear" &&
                    text != L"Cancel" && text != L"Backspace",
                "removed action-row controls do not re-enter the focus graph");
        }

        const HWND prompt = GetDlgItem(window, 100);
        const HWND legend = GetDlgItem(window, 102);
        Check(prompt && WindowText(prompt) == L"Search installed games" &&
                WindowText(edit) == L"ab",
            "prompt guidance and committed edit value remain distinct");
        Check(legend && WindowText(legend).find(L"RT  Enter") != std::wstring::npos &&
                WindowText(legend).find(L"Commit") == std::wstring::npos,
            "controller legend exposes Enter without a focusable Commit action");

        HDC promptDc = GetDC(prompt);
        const auto panelBrush = reinterpret_cast<HBRUSH>(SendMessageW(
            window, WM_CTLCOLORSTATIC,
            reinterpret_cast<WPARAM>(promptDc), reinterpret_cast<LPARAM>(prompt)));
        LOGBRUSH panelDetails{};
        Check(panelBrush && GetObjectW(panelBrush, sizeof(panelDetails), &panelDetails) &&
                panelDetails.lbColor == theme.panel && GetTextColor(promptDc) == theme.text,
            "active modal theme owns prompt foreground and panel background");
        ReleaseDC(prompt, promptDc);
        HDC editDc = GetDC(edit);
        const auto controlBrush = reinterpret_cast<HBRUSH>(SendMessageW(
            window, WM_CTLCOLOREDIT,
            reinterpret_cast<WPARAM>(editDc), reinterpret_cast<LPARAM>(edit)));
        LOGBRUSH controlDetails{};
        Check(controlBrush && GetObjectW(controlBrush, sizeof(controlDetails), &controlDetails) &&
                controlDetails.lbColor == theme.control && GetTextColor(editDc) == theme.text,
            "active modal theme owns live-buffer foreground and control background");
        ReleaseDC(edit, editDc);

        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"q"; }),
            "modal opens with one keyboard key focused");
        Check(modal.PostController(L"DPadRight"), "D-pad moves key focus");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"w"; }),
            "D-pad reaches the adjacent key");
        Check(WindowText(edit) == L"ab",
            "moving key focus never inserts a character");
        const HWND focusedKey = CurrentFocus(window);
        SendMessageW(window, WM_COMMAND,
            MAKEWPARAM(GetDlgCtrlID(focusedKey), BN_SETFOCUS),
            reinterpret_cast<LPARAM>(focusedKey));
        Check(WindowText(edit) == L"ab",
            "BN_SETFOCUS never activates a key");
        SendMessageW(window, WM_COMMAND,
            MAKEWPARAM(GetDlgCtrlID(focusedKey), BN_CLICKED),
            reinterpret_cast<LPARAM>(focusedKey));
        Check(WindowText(edit) == L"abw",
            "one exact BN_CLICKED inserts the focused key exactly once");

        Check(modal.PostController(L"DPadLeft"), "controller returns to q");
        Check(modal.PostController(L"DPadDown"), "controller reaches a");
        Check(modal.PostController(L"DPadDown"), "controller reaches Shift");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"Shift"; }),
            "layer key is controller reachable");
        Check(modal.PostController(L"A"), "A changes the keyboard layer");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"ABC"; }),
            "uppercase layer is visibly named");
        Check(modal.PostController(L"DPadUp"), "controller returns to uppercase A");
        Check(WaitUntil([&] { return WindowText(CurrentFocus(window)) == L"A"; }),
            "uppercase key label updates in place");
        Check(modal.PostController(L"A"), "A inserts the uppercase key");
        Check(WaitUntil([&] { return WindowText(edit) == L"abwA"; }),
            "live edit buffer updates immediately after insertion");

        Check(modal.PostController(L"LB"), "LB moves the caret left");
        Check(WaitUntil([&] { return EditSelection(edit) == std::pair<DWORD, DWORD>{3, 3}; }),
            "LB changes only the visible caret position");
        Check(modal.PostController(L"X"), "X backspaces at the caret");
        Check(WaitUntil([&] { return WindowText(edit) == L"abA"; }),
            "Backspace removes the character before the live caret");
        modal.UpdateCaretRepeat(true, false, true, false, 1000);
        Check(EditSelection(edit) == std::pair<DWORD, DWORD>{1, 1},
            "new shoulder press moves the caret once");
        modal.UpdateCaretRepeat(true, false, false, false, 1300);
        Check(EditSelection(edit) == std::pair<DWORD, DWORD>{1, 1},
            "caret repeat waits for the bounded initial delay");
        modal.UpdateCaretRepeat(true, false, false, false, 1400);
        Check(EditSelection(edit) == std::pair<DWORD, DWORD>{0, 0},
            "held shoulder repeats after the bounded delay");
        modal.UpdateCaretRepeat(false, false, false, false, 1500);
        Check(modal.PostController(L"A"), "A inserts at the moved caret");
        Check(modal.PostController(L"RB"), "RB moves the caret right");
        Check(modal.PostController(L"A"), "A inserts again at the new caret");
        Check(WaitUntil([&] { return WindowText(edit) == L"AaAbA"; }),
            "middle insertion follows the visible caret without moving key focus");
        Check(modal.PostController(L"RT"), "RT enters the final value");
    });
    const auto committed = modal.Show(
        GetModuleHandleW(nullptr), owner, L"ab", L"Search installed games", 8,
        false, theme);
    driver.join();
    const auto expectedCommitted = std::wstring_view(L"AaAbA");
    Check(committed.outcome == widgetrail::input::TextEntryModalOutcome::Committed &&
        committed.committedText && std::equal(
        committed.committedText->view().begin(), committed.committedText->view().end(),
        expectedCommitted.begin(), expectedCommitted.end()),
        "controller keyboard and Enter publish one bounded final value");
    Check(!modal.active(), "committed modal releases its window");
    Check(IsWindowEnabled(owner) && GetFocus() == owner,
        "Enter re-enables the owner and restores its focus");

    std::thread hintDriver([&] {
        Check(WaitUntil([&] { return modal.active(); }), "hint modal becomes active");
        HWND window{};
        HWND edit{};
        Check(WaitUntil([&] {
            window = FindWindowW(
                L"WidgetRail.TextEntryModal", ModalTitle(L"Public client identifier").c_str());
            edit = window ? GetDlgItem(window, 101) : nullptr;
            return window && edit;
        }), "hint modal controls finish creation");
        Check(WindowText(GetDlgItem(window, 100)) == L"Public client identifier" &&
                WindowText(edit).empty(),
            "authored prompt and empty live buffer are distinct states");
        Check(modal.PostController(L"A"), "A inserts the initial q key");
        Check(WaitUntil([&] { return WindowText(edit) == L"q"; }),
            "live buffer displays inserted text immediately");
        Check(modal.PostController(L"X"), "X backspaces the live buffer");
        Check(WaitUntil([&] { return WindowText(edit).empty(); }),
            "live buffer displays the empty result immediately");
        Check(modal.PostController(L"B"), "B cancels only the modal");
    });
    const auto hintedCancel = modal.Show(
        GetModuleHandleW(nullptr), owner, L"", L"Public client identifier", 8);
    hintDriver.join();
    Check(hintedCancel.outcome == widgetrail::input::TextEntryModalOutcome::Cancelled &&
            !hintedCancel.committedText && IsWindow(owner) && IsWindowVisible(owner),
        "B cancels once without closing the owner or returning prompt text");

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
                L"WidgetRail.TextEntryModal", ModalTitle(L"Search installed games").c_str());
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

    std::thread maximumDriver([&] {
        Check(WaitUntil([&] { return modal.active(); }), "maximum-length modal becomes active");
        HWND edit{};
        Check(WaitUntil([&] {
            const HWND window = FindWindowW(
                L"WidgetRail.TextEntryModal", ModalTitle(L"Bounded value").c_str());
            edit = window ? GetDlgItem(window, 101) : nullptr;
            return edit != nullptr;
        }), "maximum-length edit is available");
        Check(modal.PostController(L"A"), "full buffer receives an insertion attempt");
        Check(WaitUntil([&] { return WindowText(edit) == L"12345678"; }),
            "maximum-length buffer rejects further insertion without truncation");
        Check(modal.PostController(L"RT"), "full buffer remains enterable");
    });
    const auto maximum = modal.Show(
        GetModuleHandleW(nullptr), owner, L"12345678", L"Bounded value", 8);
    maximumDriver.join();
    Check(maximum.outcome == widgetrail::input::TextEntryModalOutcome::Committed &&
            maximum.committedText &&
            std::wstring_view(maximum.committedText->view().data(),
                maximum.committedText->view().size()) == L"12345678",
        "maximum-length value commits unchanged after a rejected insertion");

    std::thread passwordDriver([&] {
        const auto comResult = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        Check(WaitUntil([&] { return modal.active(); }), "password modal becomes active");
        HWND window{};
        HWND edit{};
        Check(WaitUntil([&] {
            window = FindWindowW(
                L"WidgetRail.TextEntryModal", ModalTitle(L"Password for test network").c_str());
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
