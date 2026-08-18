#include "TextEntryModal.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <limits>

namespace gba::input {

SecureTextBuffer::SecureTextBuffer(std::vector<wchar_t>&& value) noexcept
    : value_(std::move(value)) {}

SecureTextBuffer::~SecureTextBuffer() { clear(); }

SecureTextBuffer::SecureTextBuffer(SecureTextBuffer&& other) noexcept
    : value_(std::move(other.value_)) {
    other.clear();
}

SecureTextBuffer& SecureTextBuffer::operator=(SecureTextBuffer&& other) noexcept {
    if (this != &other) {
        clear();
        value_ = std::move(other.value_);
        other.clear();
    }
    return *this;
}

void SecureTextBuffer::clear() noexcept {
    if (!value_.empty())
        SecureZeroMemory(value_.data(), value_.size() * sizeof(wchar_t));
    value_.clear();
}
namespace {

constexpr wchar_t kClassName[] = L"WidgetRail.TextEntryModal";
constexpr int kEditId = 100;
constexpr int kBackspaceId = 101;
constexpr int kClearId = 102;
constexpr int kCancelId = 103;
constexpr int kCommitId = 104;
constexpr int kCharacterBase = 1000;
constexpr std::wstring_view kCharacters = L"ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_";
static_assert(kCharacters.size() == TextEntryCharacterCount);
constexpr UINT kControllerMessage = WM_APP + 1;
constexpr int kBaseWidth = 760;
constexpr int kBaseHeight = 540;

RECT ScaleRect(const RECT value, const double scale) noexcept {
    return {
        static_cast<LONG>(std::lround(value.left * scale)),
        static_cast<LONG>(std::lround(value.top * scale)),
        static_cast<LONG>(std::lround(value.right * scale)),
        static_cast<LONG>(std::lround(value.bottom * scale)),
    };
}

bool Overlaps(const LONG firstStart, const LONG firstEnd,
              const LONG secondStart, const LONG secondEnd) noexcept {
    return firstStart < secondEnd && secondStart < firstEnd;
}

} // namespace

TextEntryModalLayout CalculateTextEntryModalLayout(
    RECT workArea,
    const UINT dpi) noexcept {
    if (workArea.right <= workArea.left || workArea.bottom <= workArea.top)
        workArea = {0, 0, kBaseWidth, kBaseHeight};
    const auto requestedScale = static_cast<double>(dpi == 0 ? 96U : dpi) / 96.0;
    const auto availableWidth = static_cast<double>(workArea.right - workArea.left);
    const auto availableHeight = static_cast<double>(workArea.bottom - workArea.top);
    const auto scale = std::max(0.25, std::min({
        requestedScale,
        availableWidth / kBaseWidth,
        availableHeight / kBaseHeight,
    }));
    const int width = static_cast<int>(std::lround(kBaseWidth * scale));
    const int height = static_cast<int>(std::lround(kBaseHeight * scale));
    const LONG x = workArea.left +
        std::max<LONG>(0, (workArea.right - workArea.left - width) / 2);
    const LONG y = workArea.top +
        std::max<LONG>(0, (workArea.bottom - workArea.top - height) / 2);

    TextEntryModalLayout result{};
    result.windowBounds = {x, y, x + width, y + height};
    result.scale = scale;
    result.editBounds = ScaleRect({24, 24, 724, 66}, scale);
    constexpr int columns = 10;
    for (std::size_t index = 0; index < result.characterBounds.size(); ++index) {
        const int row = static_cast<int>(index) / columns;
        const int column = static_cast<int>(index) % columns;
        result.characterBounds[index] = ScaleRect({
            24 + column * 70, 86 + row * 56,
            88 + column * 70, 134 + row * 56,
        }, scale);
    }
    for (std::size_t index = 0; index < result.actionBounds.size(); ++index) {
        const int left = 24 + static_cast<int>(index) * 176;
        result.actionBounds[index] = ScaleRect({left, 430, left + 164, 478}, scale);
    }
    return result;
}

TextEntryModalResult TextEntryModal::Show(
    HINSTANCE instance,
    HWND owner,
    const std::wstring_view value,
    const std::wstring_view placeholder,
    const std::size_t maximumLength,
    const bool password) {
    if (active() || !instance || !owner || maximumLength == 0 ||
        maximumLength > MaximumLength || value.size() > maximumLength ||
        placeholder.size() > MaximumLength || (password && !value.empty()))
        return {};

    WNDCLASSEXW type{sizeof(type)};
    type.hInstance = instance;
    type.lpfnWndProc = WindowProc;
    type.lpszClassName = kClassName;
    type.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    type.hbrBackground = GetSysColorBrush(COLOR_WINDOW);
    if (!RegisterClassExW(&type) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
        return {};

    instance_ = instance;
    owner_ = owner;
    initialValue_ = value;
    placeholder_ = placeholder;
    maximumLength_ = maximumLength;
    password_ = password;
    const auto ownerDpi = GetDpiForWindow(owner);
    dpi_ = ownerDpi == 0 ? 96U : ownerDpi;
    result_.reset();
    outcome_ = TextEntryModalOutcome::Failed;
    completed_ = false;
    focusTargets_.clear();
    focusIndex_ = 0;

    RECT workArea{};
    MONITORINFO monitorInfo{sizeof(monitorInfo)};
    const auto monitor = MonitorFromWindow(owner, MONITOR_DEFAULTTONEAREST);
    if (monitor && GetMonitorInfoW(monitor, &monitorInfo)) {
        workArea = monitorInfo.rcWork;
    } else {
        GetWindowRect(owner, &workArea);
    }
    layout_ = CalculateTextEntryModalLayout(workArea, dpi_);
    const int width = layout_.windowBounds.right - layout_.windowBounds.left;
    const int height = layout_.windowBounds.bottom - layout_.windowBounds.top;
    window_ = CreateWindowExW(
        WS_EX_DLGMODALFRAME | WS_EX_CONTROLPARENT,
        kClassName,
        placeholder_.empty() ? L"Enter text" : placeholder_.c_str(),
        WS_POPUP | WS_CAPTION | WS_SYSMENU,
        layout_.windowBounds.left, layout_.windowBounds.top,
        width, height, owner, nullptr, instance, this);
    if (!window_) return {};

    outcome_ = TextEntryModalOutcome::Closed;

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
    priorEditWindowProc_ = nullptr;
    TextEntryModalResult result{outcome_, std::move(result_)};
    result_.reset();
    if (!initialValue_.empty())
        SecureZeroMemory(initialValue_.data(), initialValue_.size() * sizeof(wchar_t));
    initialValue_.clear();
    placeholder_.clear();
    password_ = false;
    return result;
}

void TextEntryModal::CreateControls() {
    const auto createBounds = [](const RECT bounds) {
        return std::array{
            bounds.left, bounds.top,
            bounds.right - bounds.left, bounds.bottom - bounds.top,
        };
    };
    const auto editBounds = createBounds(layout_.editBounds);
    edit_ = CreateWindowExW(
        WS_EX_CLIENTEDGE, L"EDIT", initialValue_.c_str(),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL |
            (password_ ? ES_PASSWORD : 0),
        editBounds[0], editBounds[1], editBounds[2], editBounds[3],
        window_, reinterpret_cast<HMENU>(
            static_cast<INT_PTR>(kEditId)), instance_, nullptr);
    if (password_) {
        SetWindowLongPtrW(edit_, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(this));
        priorEditWindowProc_ = reinterpret_cast<WNDPROC>(SetWindowLongPtrW(
            edit_, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(EditWindowProc)));
        SendMessageW(edit_, EM_SETPASSWORDCHAR, static_cast<WPARAM>(L'●'), 0);
    }
    SendMessageW(edit_, EM_SETLIMITTEXT, static_cast<WPARAM>(maximumLength_), 0);
    focusTargets_.push_back({edit_, layout_.editBounds});

    for (std::size_t index = 0; index < kCharacters.size(); ++index) {
        const wchar_t label[2]{kCharacters[index], L'\0'};
        const auto bounds = createBounds(layout_.characterBounds[index]);
        HWND button = CreateWindowExW(
            0, L"BUTTON", label, WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON,
            bounds[0], bounds[1], bounds[2], bounds[3],
            window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(
                kCharacterBase + index)), instance_, nullptr);
        focusTargets_.push_back({button, layout_.characterBounds[index]});
    }
    const std::array controls{
        std::pair{kBackspaceId, L"Backspace"}, std::pair{kClearId, L"Clear"},
        std::pair{kCancelId, L"Cancel"}, std::pair{kCommitId, L"Commit"},
    };
    for (std::size_t index = 0; index < controls.size(); ++index) {
        const auto bounds = createBounds(layout_.actionBounds[index]);
        HWND button = CreateWindowExW(
            0, L"BUTTON", controls[index].second,
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON,
            bounds[0], bounds[1], bounds[2], bounds[3],
            window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(
                controls[index].first)), instance_, nullptr);
        focusTargets_.push_back({button, layout_.actionBounds[index]});
    }
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

void TextEntryModal::Complete(const TextEntryModalOutcome outcome) {
    if (completed_) return;
    completed_ = true;
    outcome_ = outcome;
    if (outcome == TextEntryModalOutcome::Committed) {
        const int length = std::clamp(GetWindowTextLengthW(edit_), 0,
            static_cast<int>(maximumLength_));
        std::vector<wchar_t> value(static_cast<std::size_t>(length) + 1, L'\0');
        if (length != 0) GetWindowTextW(edit_, value.data(), length + 1);
        value.resize(static_cast<std::size_t>(length));
        result_.emplace(std::move(value));
    }
    if (edit_) SetWindowTextW(edit_, L"");
    HWND closing = window_;
    if (closing) DestroyWindow(closing);
    window_ = nullptr;
}

void TextEntryModal::MoveFocus(const Direction direction) {
    if (focusTargets_.empty()) return;
    const auto current = GetFocus();
    const auto found = std::find_if(
        focusTargets_.begin(), focusTargets_.end(),
        [&](const FocusTarget& target) { return target.window == current; });
    focusIndex_ = found == focusTargets_.end()
        ? 0
        : static_cast<std::size_t>(found - focusTargets_.begin());
    const RECT source = focusTargets_[focusIndex_].bounds;
    std::size_t best = focusIndex_;
    long long bestScore = std::numeric_limits<long long>::max();
    for (std::size_t index = 0; index < focusTargets_.size(); ++index) {
        if (index == focusIndex_) continue;
        const RECT candidate = focusTargets_[index].bounds;
        LONG primary{};
        LONG secondary{};
        bool eligible{};
        if (direction == Direction::Left || direction == Direction::Right) {
            const bool overlap = Overlaps(
                source.top, source.bottom, candidate.top, candidate.bottom);
            if (direction == Direction::Left && candidate.right <= source.left) {
                primary = source.left - candidate.right;
                eligible = overlap;
            } else if (direction == Direction::Right && candidate.left >= source.right) {
                primary = candidate.left - source.right;
                eligible = overlap;
            }
            secondary = std::abs(
                (source.top + source.bottom) - (candidate.top + candidate.bottom));
        } else {
            const bool overlap = Overlaps(
                source.left, source.right, candidate.left, candidate.right);
            if (direction == Direction::Up && candidate.bottom <= source.top) {
                primary = source.top - candidate.bottom;
                eligible = overlap;
            } else if (direction == Direction::Down && candidate.top >= source.bottom) {
                primary = candidate.top - source.bottom;
                eligible = overlap;
            }
            secondary = std::abs(
                (source.left + source.right) - (candidate.left + candidate.right));
        }
        if (!eligible) continue;
        const long long score = static_cast<long long>(primary) * 10'000 + secondary;
        if (score < bestScore) {
            bestScore = score;
            best = index;
        }
    }
    if (best != focusIndex_) {
        focusIndex_ = best;
        SetFocus(focusTargets_[focusIndex_].window);
    }
}

void TextEntryModal::HandleController(const std::wstring_view button) noexcept {
    if (!window_) return;
    if (button == L"B") Complete(TextEntryModalOutcome::Cancelled);
    else if (button == L"A") {
        HWND focused = GetFocus();
        if (focused == edit_) MoveFocus(Direction::Down);
        else if (focused) SendMessageW(focused, BM_CLICK, 0, 0);
    } else if (button == L"X") Backspace();
    else if (button == L"DPadLeft") MoveFocus(Direction::Left);
    else if (button == L"DPadRight") MoveFocus(Direction::Right);
    else if (button == L"DPadUp") MoveFocus(Direction::Up);
    else if (button == L"DPadDown") MoveFocus(Direction::Down);
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

LRESULT CALLBACK TextEntryModal::EditWindowProc(
    const HWND window, const UINT message, const WPARAM wParam, const LPARAM lParam) {
    auto* self = reinterpret_cast<TextEntryModal*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (!self || !self->priorEditWindowProc_)
        return DefWindowProcW(window, message, wParam, lParam);
    if (message == WM_COPY || message == WM_CUT || message == WM_PASTE ||
        message == WM_CONTEXTMENU) return 0;
    return CallWindowProcW(self->priorEditWindowProc_, window, message, wParam, lParam);
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
    case WM_CLOSE: Complete(TextEntryModalOutcome::Closed); return 0;
    case WM_COMMAND: {
        const int id = LOWORD(wParam);
        if (id >= kCharacterBase &&
            id < kCharacterBase + static_cast<int>(kCharacters.size()))
            Append(kCharacters[static_cast<std::size_t>(id - kCharacterBase)]);
        else if (id == kBackspaceId) Backspace();
        else if (id == kClearId) SetWindowTextW(edit_, L"");
        else if (id == kCancelId) Complete(TextEntryModalOutcome::Cancelled);
        else if (id == kCommitId) Complete(TextEntryModalOutcome::Committed);
        return 0;
    }
    case WM_KEYDOWN:
        if (wParam == VK_ESCAPE) Complete(TextEntryModalOutcome::Cancelled);
        else if (wParam == VK_RETURN && GetFocus() != edit_) {
            HWND focused = GetFocus();
            if (focused) SendMessageW(focused, BM_CLICK, 0, 0);
        }
        return 0;
    default: return DefWindowProcW(window_, message, wParam, lParam);
    }
}

} // namespace gba::input
