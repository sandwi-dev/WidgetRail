#include "TextEntryModal.h"

#include <algorithm>
#include <array>

namespace gba::input {
namespace {

constexpr wchar_t kClassName[] = L"GameBarAlternative.TextEntryModal";
constexpr int kEditId = 100;
constexpr int kBackspaceId = 101;
constexpr int kClearId = 102;
constexpr int kCancelId = 103;
constexpr int kCommitId = 104;
constexpr int kCharacterBase = 1000;
constexpr std::wstring_view kCharacters = L"ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_";
constexpr UINT kControllerMessage = WM_APP + 1;

} // namespace

std::optional<std::wstring> TextEntryModal::Show(
    HINSTANCE instance,
    HWND owner,
    const std::wstring_view value,
    const std::wstring_view placeholder,
    const std::size_t maximumLength) {
    if (active() || !instance || !owner || maximumLength == 0 ||
        maximumLength > MaximumLength || value.size() > maximumLength ||
        placeholder.size() > MaximumLength) return std::nullopt;

    WNDCLASSEXW type{sizeof(type)};
    type.hInstance = instance;
    type.lpfnWndProc = WindowProc;
    type.lpszClassName = kClassName;
    type.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    type.hbrBackground = GetSysColorBrush(COLOR_WINDOW);
    if (!RegisterClassExW(&type) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
        return std::nullopt;

    instance_ = instance;
    owner_ = owner;
    initialValue_ = value;
    placeholder_ = placeholder;
    maximumLength_ = maximumLength;
    const auto ownerDpi = GetDpiForWindow(owner);
    dpi_ = ownerDpi == 0 ? 96U : ownerDpi;
    result_.reset();
    completed_ = false;
    focusTargets_.clear();
    focusIndex_ = 0;

    RECT ownerBounds{};
    GetWindowRect(owner, &ownerBounds);
    const int width = Scale(760);
    const int height = Scale(520);
    const int x = ownerBounds.left + std::max(0L, (ownerBounds.right - ownerBounds.left - width) / 2);
    const int y = ownerBounds.top + std::max(0L, (ownerBounds.bottom - ownerBounds.top - height) / 2);
    window_ = CreateWindowExW(
        WS_EX_DLGMODALFRAME | WS_EX_CONTROLPARENT,
        kClassName,
        placeholder_.empty() ? L"Enter text" : placeholder_.c_str(),
        WS_POPUP | WS_CAPTION | WS_SYSMENU,
        x, y, width, height, owner, nullptr, instance, this);
    if (!window_) return std::nullopt;

    EnableWindow(owner, FALSE);
    ShowWindow(window_, SW_SHOW);
    UpdateWindow(window_);
    SetFocus(edit_);

    MSG message{};
    BOOL messageResult{};
    while (window_ && (messageResult = GetMessageW(&message, nullptr, 0, 0)) > 0) {
        if (!IsDialogMessageW(window_, &message)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
    if (messageResult == 0) PostQuitMessage(static_cast<int>(message.wParam));
    if (IsWindow(owner)) {
        EnableWindow(owner, TRUE);
        SetActiveWindow(owner);
        SetFocus(owner);
    }
    window_ = nullptr;
    edit_ = nullptr;
    focusTargets_.clear();
    return result_;
}

void TextEntryModal::CreateControls() {
    edit_ = CreateWindowExW(
        WS_EX_CLIENTEDGE, L"EDIT", initialValue_.c_str(),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
        Scale(24), Scale(24), Scale(700), Scale(42), window_, reinterpret_cast<HMENU>(
            static_cast<INT_PTR>(kEditId)), instance_, nullptr);
    SendMessageW(edit_, EM_SETLIMITTEXT, static_cast<WPARAM>(maximumLength_), 0);
    focusTargets_.push_back(edit_);

    constexpr int columns = 10;
    constexpr int keyWidth = 64;
    constexpr int keyHeight = 48;
    for (std::size_t index = 0; index < kCharacters.size(); ++index) {
        const wchar_t label[2]{kCharacters[index], L'\0'};
        const int row = static_cast<int>(index) / columns;
        const int column = static_cast<int>(index) % columns;
        HWND button = CreateWindowExW(
            0, L"BUTTON", label, WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON,
            Scale(24 + column * 70), Scale(86 + row * 56),
            Scale(keyWidth), Scale(keyHeight),
            window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(
                kCharacterBase + index)), instance_, nullptr);
        focusTargets_.push_back(button);
    }
    const std::array controls{
        std::pair{kBackspaceId, L"Backspace"}, std::pair{kClearId, L"Clear"},
        std::pair{kCancelId, L"Cancel"}, std::pair{kCommitId, L"Commit"},
    };
    for (std::size_t index = 0; index < controls.size(); ++index) {
        HWND button = CreateWindowExW(
            0, L"BUTTON", controls[index].second,
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON,
            Scale(24 + static_cast<int>(index) * 176), Scale(430),
            Scale(164), Scale(48),
            window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(
                controls[index].first)), instance_, nullptr);
        focusTargets_.push_back(button);
    }
}

int TextEntryModal::Scale(const int value) const noexcept {
    return MulDiv(value, static_cast<int>(dpi_), 96);
}

void TextEntryModal::Append(const wchar_t value) {
    const int length = GetWindowTextLengthW(edit_);
    if (length < 0 || static_cast<std::size_t>(length) >= maximumLength_) return;
    SendMessageW(edit_, EM_SETSEL, static_cast<WPARAM>(length), static_cast<LPARAM>(length));
    const wchar_t text[2]{value, L'\0'};
    SendMessageW(edit_, EM_REPLACESEL, TRUE, reinterpret_cast<LPARAM>(text));
}

void TextEntryModal::Backspace() {
    const int length = GetWindowTextLengthW(edit_);
    if (length <= 0) return;
    SendMessageW(edit_, EM_SETSEL, length - 1, length);
    SendMessageW(edit_, EM_REPLACESEL, TRUE, reinterpret_cast<LPARAM>(L""));
}

void TextEntryModal::Complete(const bool commit) {
    if (completed_) return;
    completed_ = true;
    if (commit) {
        const int length = std::clamp(GetWindowTextLengthW(edit_), 0,
            static_cast<int>(maximumLength_));
        std::wstring value(static_cast<std::size_t>(length) + 1, L'\0');
        if (length != 0) GetWindowTextW(edit_, value.data(), length + 1);
        value.resize(static_cast<std::size_t>(length));
        result_ = std::move(value);
    }
    HWND closing = window_;
    if (closing) DestroyWindow(closing);
    window_ = nullptr;
}

void TextEntryModal::MoveFocus(const int delta) {
    if (focusTargets_.empty()) return;
    const auto current = GetFocus();
    const auto found = std::find(focusTargets_.begin(), focusTargets_.end(), current);
    focusIndex_ = found == focusTargets_.end()
        ? 0
        : static_cast<std::size_t>(found - focusTargets_.begin());
    const auto count = static_cast<int>(focusTargets_.size());
    focusIndex_ = static_cast<std::size_t>(
        (static_cast<int>(focusIndex_) + delta + count) % count);
    SetFocus(focusTargets_[focusIndex_]);
}

void TextEntryModal::HandleController(const std::wstring_view button) noexcept {
    if (!window_) return;
    if (button == L"B") Complete(false);
    else if (button == L"A") {
        HWND focused = GetFocus();
        if (focused == edit_) MoveFocus(1);
        else if (focused) SendMessageW(focused, BM_CLICK, 0, 0);
    } else if (button == L"X") Backspace();
    else if (button == L"DPadLeft" || button == L"DPadUp") MoveFocus(-1);
    else if (button == L"DPadRight" || button == L"DPadDown") MoveFocus(1);
}

bool TextEntryModal::PostController(const std::wstring_view button) noexcept {
    if (!window_) return false;
    const WPARAM command = button == L"A" ? 1 : button == L"B" ? 2 :
        button == L"X" ? 3 : button == L"DPadLeft" ? 4 :
        button == L"DPadRight" ? 5 : button == L"DPadUp" ? 6 :
        button == L"DPadDown" ? 7 : 0;
    return command != 0 && PostMessageW(window_, kControllerMessage, command, 0) != FALSE;
}

LRESULT CALLBACK TextEntryModal::WindowProc(
    const HWND window, const UINT message, const WPARAM wParam, const LPARAM lParam) {
    auto* self = reinterpret_cast<TextEntryModal*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        const auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
        self = static_cast<TextEntryModal*>(create->lpCreateParams);
        self->window_ = window;
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
    }
    return self ? self->HandleMessage(message, wParam, lParam)
                : DefWindowProcW(window, message, wParam, lParam);
}

LRESULT TextEntryModal::HandleMessage(
    const UINT message, const WPARAM wParam, const LPARAM lParam) {
    switch (message) {
    case kControllerMessage:
        HandleController(wParam == 1 ? L"A" : wParam == 2 ? L"B" :
            wParam == 3 ? L"X" : wParam == 4 ? L"DPadLeft" :
            wParam == 5 ? L"DPadRight" : wParam == 6 ? L"DPadUp" : L"DPadDown");
        return 0;
    case WM_CREATE: CreateControls(); return 0;
    case WM_CLOSE: Complete(false); return 0;
    case WM_COMMAND: {
        const int id = LOWORD(wParam);
        if (id >= kCharacterBase &&
            id < kCharacterBase + static_cast<int>(kCharacters.size()))
            Append(kCharacters[static_cast<std::size_t>(id - kCharacterBase)]);
        else if (id == kBackspaceId) Backspace();
        else if (id == kClearId) SetWindowTextW(edit_, L"");
        else if (id == kCancelId) Complete(false);
        else if (id == kCommitId) Complete(true);
        return 0;
    }
    case WM_KEYDOWN:
        if (wParam == VK_ESCAPE) Complete(false);
        else if (wParam == VK_RETURN && GetFocus() != edit_) {
            HWND focused = GetFocus();
            if (focused) SendMessageW(focused, BM_CLICK, 0, 0);
        }
        return 0;
    default: return DefWindowProcW(window_, message, wParam, lParam);
    }
}

} // namespace gba::input
