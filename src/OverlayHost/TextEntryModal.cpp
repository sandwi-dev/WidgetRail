#include "TextEntryModal.h"

#include <CommCtrl.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cwctype>
#include <utility>

namespace widgetrail::input {

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
constexpr int kPromptId = 100;
constexpr int kEditId = 101;
constexpr int kLegendId = 102;
constexpr int kKeyBase = 1000;
constexpr UINT kControllerMessage = WM_APP + 1;
constexpr int kBaseWidth = 760;
constexpr int kBaseHeight = 560;
constexpr int kColumns = 10;
constexpr unsigned long long kControllerRepeatDelayMilliseconds = 400;
constexpr unsigned long long kControllerRepeatIntervalMilliseconds = 90;

constexpr std::wstring_view kDigits = L"1234567890";
constexpr std::wstring_view kTopLetters = L"qwertyuiop";
constexpr std::wstring_view kMiddleLetters = L"asdfghjkl";
constexpr std::wstring_view kBottomLetters = L"zxcvbnm";
constexpr std::array<wchar_t, 38> kSymbols{
    L'!', L'@', L'#', L'$', L'%', L'^', L'&', L'*', L'(', L')',
    L'-', L'_', L'=', L'+', L'[', L']', L'{', L'}', L'\\', L'|',
    L';', L':', L'\'', L'"', L',', L'.', L'<', L'>', L'/', L'?',
    L'`', L'~', L'?', L':', L'+', L'=', L'_', L' ',
};

RECT ScaleRect(const RECT value, const double scale) noexcept {
    return {
        static_cast<LONG>(std::lround(value.left * scale)),
        static_cast<LONG>(std::lround(value.top * scale)),
        static_cast<LONG>(std::lround(value.right * scale)),
        static_cast<LONG>(std::lround(value.bottom * scale)),
    };
}

int PixelHeight(const double dip, const double scale, const double textScale) noexcept {
    return std::max(10, static_cast<int>(std::lround(dip * scale * textScale)));
}

bool IsPasteGesture(const WPARAM key) noexcept {
    return (key == L'V' && (GetKeyState(VK_CONTROL) & 0x8000) != 0) ||
        (key == VK_INSERT && (GetKeyState(VK_SHIFT) & 0x8000) != 0);
}

} // namespace

TextEntryModalLayout CalculateTextEntryModalLayout(
    RECT workArea,
    const UINT dpi,
    const double interfaceScale) noexcept {
    if (workArea.right <= workArea.left || workArea.bottom <= workArea.top)
        workArea = {0, 0, kBaseWidth, kBaseHeight};
    const auto boundedInterfaceScale = std::isfinite(interfaceScale)
        ? std::clamp(interfaceScale, 0.85, 1.5)
        : 1.0;
    const auto requestedScale =
        static_cast<double>(dpi == 0 ? 96U : dpi) / 96.0 * boundedInterfaceScale;
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
    result.promptBounds = ScaleRect({28, 24, 732, 58}, scale);
    result.editBounds = ScaleRect({44, 87, 716, 121}, scale);
    constexpr int keyWidth = 64;
    constexpr int keyHeight = 52;
    constexpr int keyGap = 8;
    for (std::size_t index = 0; index < TextEntryCharacterKeyCount; ++index) {
        const int row = static_cast<int>(index) / kColumns;
        const int column = static_cast<int>(index) % kColumns;
        const int left = 24 + column * (keyWidth + keyGap);
        const int top = 148 + row * (keyHeight + keyGap);
        result.keyBounds[index] = ScaleRect(
            {left, top, left + keyWidth, top + keyHeight}, scale);
    }
    for (std::size_t action = 0; action < 4; ++action) {
        const int left = 24 + static_cast<int>(action) * 180;
        result.keyBounds[TextEntryCharacterKeyCount + action] =
            ScaleRect({left, 408, left + 172, 460}, scale);
    }
    result.legendBounds = ScaleRect({28, 484, 732, 540}, scale);
    return result;
}

bool TextEntryModal::Begin(
    HINSTANCE instance,
    HWND owner,
    const std::wstring_view value,
    const std::wstring_view placeholder,
    const std::size_t maximumLength,
    const bool password,
    TextEntryModalTheme theme) {
    if (active() || completed_ || !instance || !owner || maximumLength == 0 ||
        maximumLength > MaximumLength || value.size() > maximumLength ||
        placeholder.size() > MaximumLength || (password && !value.empty()))
        return {};

    WNDCLASSEXW type{sizeof(type)};
    type.hInstance = instance;
    type.lpfnWndProc = WindowProc;
    type.lpszClassName = kClassName;
    type.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    if (!RegisterClassExW(&type) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
        return {};

    instance_ = instance;
    owner_ = owner;
    initialValue_ = value;
    placeholder_ = placeholder;
    maximumLength_ = maximumLength;
    password_ = password;
    theme_ = std::move(theme);
    theme_.interfaceScale = std::isfinite(theme_.interfaceScale)
        ? std::clamp(theme_.interfaceScale, 0.85, 1.5) : 1.0;
    theme_.textScale = std::isfinite(theme_.textScale)
        ? std::clamp(theme_.textScale, 0.85, 1.5) : 1.0;
    const auto ownerDpi = GetDpiForWindow(owner);
    dpi_ = ownerDpi == 0 ? 96U : ownerDpi;
    result_.reset();
    outcome_ = TextEntryModalOutcome::Failed;
    completed_ = false;
    visible_ = false;
    resumeFocus_ = nullptr;
    keys_.fill(nullptr);
    focusIndex_ = 10;
    layer_ = Layer::Lowercase;
    ResetControllerRepeat();

    RECT workArea{};
    MONITORINFO monitorInfo{sizeof(monitorInfo)};
    const auto monitor = MonitorFromWindow(owner, MONITOR_DEFAULTTONEAREST);
    if (monitor && GetMonitorInfoW(monitor, &monitorInfo)) {
        workArea = monitorInfo.rcWork;
    } else {
        GetWindowRect(owner, &workArea);
    }
    layout_ = CalculateTextEntryModalLayout(workArea, dpi_, theme_.interfaceScale);
    const int width = layout_.windowBounds.right - layout_.windowBounds.left;
    const int height = layout_.windowBounds.bottom - layout_.windowBounds.top;
    const std::wstring title = placeholder_.empty()
        ? L"WidgetRail text entry"
        : L"WidgetRail text entry - " + placeholder_;
    window_ = CreateWindowExW(
        WS_EX_CONTROLPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED,
        kClassName,
        title.c_str(),
        WS_POPUP | WS_CLIPCHILDREN,
        layout_.windowBounds.left, layout_.windowBounds.top,
        width, height, owner, nullptr, instance, this);
    if (!window_) {
        ReleaseThemeResources();
        return {};
    }

    outcome_ = TextEntryModalOutcome::Closed;
    SetOpacity(1.0F);
    SetVisible(true);
    return true;
}

TextEntryModal::~TextEntryModal() {
    owner_ = nullptr;
    Close();
    (void)TakeResult();
    ReleaseThemeResources();
}

std::optional<TextEntryModalResult> TextEntryModal::TakeResult() {
    if (!completed_) return std::nullopt;
    completed_ = false;
    window_ = nullptr;
    prompt_ = nullptr;
    edit_ = nullptr;
    legend_ = nullptr;
    keys_.fill(nullptr);
    priorEditWindowProc_ = nullptr;
    priorKeyWindowProc_ = nullptr;
    ReleaseThemeResources();
    TextEntryModalResult result{outcome_, std::move(result_)};
    result_.reset();
    if (!initialValue_.empty())
        SecureZeroMemory(initialValue_.data(), initialValue_.size() * sizeof(wchar_t));
    initialValue_.clear();
    placeholder_.clear();
    password_ = false;
    return result;
}

void TextEntryModal::SetVisible(const bool visible) noexcept {
    if (!window_ || visible_ == visible) return;
    if (!visible && IsChild(window_, GetFocus())) resumeFocus_ = GetFocus();
    visible_ = visible;
    ResetControllerRepeat();
    ShowWindow(window_, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
    if (visible) {
        Raise();
        Focus();
    }
}

D2D1_COLOR_F DrawingColor(const COLORREF color) noexcept {
    return D2D1::ColorF(GetRValue(color) / 255.0F,
        GetGValue(color) / 255.0F, GetBValue(color) / 255.0F);
}

void TextEntryModal::Focus() noexcept {
    if (!window_ || !visible_) return;
    if (IsChild(window_, GetFocus())) return;
    if (resumeFocus_ && IsChild(window_, resumeFocus_)) SetFocus(resumeFocus_);
    else SetKeyboardFocus(focusIndex_);
}

void TextEntryModal::SetOpacity(const float opacity) noexcept {
    if (window_) SetLayeredWindowAttributes(window_, 0,
        static_cast<BYTE>(std::lround(std::clamp(opacity, 0.0F, 1.0F) * 255.0F)), LWA_ALPHA);
}

void TextEntryModal::Raise() noexcept {
    if (window_ && visible_) SetWindowPos(window_, HWND_TOPMOST, 0, 0, 0, 0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}

void TextEntryModal::CreateThemeResources() {
    ReleaseThemeResources();
    canvasBrush_ = CreateSolidBrush(theme_.canvas);
    panelBrush_ = CreateSolidBrush(theme_.panel);
    controlBrush_ = CreateSolidBrush(theme_.control);
    const wchar_t* family = theme_.fontFamily.empty()
        ? L"Segoe UI" : theme_.fontFamily.c_str();
    bodyFont_ = CreateFontW(
        -PixelHeight(20.0, layout_.scale, theme_.textScale), 0, 0, 0,
        FW_SEMIBOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
        CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, family);
    keyFont_ = CreateFontW(
        -PixelHeight(17.0, layout_.scale, theme_.textScale), 0, 0, 0,
        FW_SEMIBOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
        CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, family);
    legendFont_ = CreateFontW(
        -PixelHeight(13.0, layout_.scale, theme_.textScale), 0, 0, 0,
        FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
        CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, family);
}

void TextEntryModal::ReleaseThemeResources() noexcept {
    drawingBrush_.Reset();
    drawingTarget_.Reset();
    keyTextFormat_.Reset();
    if (canvasBrush_) DeleteObject(std::exchange(canvasBrush_, nullptr));
    if (panelBrush_) DeleteObject(std::exchange(panelBrush_, nullptr));
    if (controlBrush_) DeleteObject(std::exchange(controlBrush_, nullptr));
    if (bodyFont_) DeleteObject(std::exchange(bodyFont_, nullptr));
    if (keyFont_) DeleteObject(std::exchange(keyFont_, nullptr));
    if (legendFont_) DeleteObject(std::exchange(legendFont_, nullptr));
}

bool TextEntryModal::EnsureDrawingResources() {
    if (!drawingFactory_ && FAILED(D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED, drawingFactory_.GetAddressOf()))) return false;
    if (!textFactory_ && FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,
            __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(textFactory_.GetAddressOf()))))
        return false;
    if (!keyTextFormat_) {
        const auto* family = theme_.fontFamily.empty() ? L"Segoe UI" : theme_.fontFamily.c_str();
        if (FAILED(textFactory_->CreateTextFormat(family, nullptr,
                DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL,
                static_cast<float>(PixelHeight(17.0, layout_.scale, theme_.textScale)), L"",
                keyTextFormat_.GetAddressOf()))) return false;
        keyTextFormat_->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
        keyTextFormat_->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        keyTextFormat_->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
        Microsoft::WRL::ComPtr<IDWriteInlineObject> ellipsis;
        if (SUCCEEDED(textFactory_->CreateEllipsisTrimmingSign(keyTextFormat_.Get(), &ellipsis))) {
            const DWRITE_TRIMMING trimming{DWRITE_TRIMMING_GRANULARITY_CHARACTER, 0, 0};
            keyTextFormat_->SetTrimming(&trimming, ellipsis.Get());
        }
    }
    if (!drawingTarget_) {
        // Native owner-draw supplies a DC. Render at physical-pixel DPI because
        // layout and font sizes already include monitor and interface scaling.
        const auto properties = D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_SOFTWARE,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_IGNORE), 96.0F, 96.0F);
        if (FAILED(drawingFactory_->CreateDCRenderTarget(&properties, &drawingTarget_))) return false;
        drawingTarget_->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        // Grayscale text stays smooth when the owned overlay surface fades.
        drawingTarget_->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
    }
    if (!drawingBrush_ && FAILED(drawingTarget_->CreateSolidColorBrush(
            DrawingColor(theme_.text), &drawingBrush_))) return false;
    return true;
}

bool TextEntryModal::PaintSurface(HDC dc, const RECT& bounds,
    const std::optional<std::size_t> key, const bool focused) {
    if (!dc || bounds.right <= bounds.left || bounds.bottom <= bounds.top) return false;
    // Recreate a discarded target once in this paint; repeated failure uses
    // the native fallback so the keyboard remains usable without graphics.
    for (int attempt = 0; attempt < 2; ++attempt) {
        if (!EnsureDrawingResources() || FAILED(drawingTarget_->BindDC(dc, &bounds))) {
            drawingBrush_.Reset();
            drawingTarget_.Reset();
            continue;
        }
        const float width = static_cast<float>(bounds.right - bounds.left);
        const float height = static_cast<float>(bounds.bottom - bounds.top);
        const float scale = static_cast<float>(layout_.scale);
        const float inset = key ? std::max(2.0F, 2.0F * scale) : 0.0F;
        const float radius = key ? std::max(2.0F, 5.0F * scale) : std::max(4.0F, 6.0F * scale);
        const auto rounded = D2D1::RoundedRect(
            D2D1::RectF(inset, inset, width - inset, height - inset), radius, radius);
        drawingTarget_->BeginDraw();
        drawingTarget_->Clear(DrawingColor(theme_.panel));
        drawingBrush_->SetColor(DrawingColor(focused ? theme_.controlFocused : theme_.control));
        drawingTarget_->FillRoundedRectangle(rounded, drawingBrush_.Get());
        if (key) {
            drawingBrush_->SetColor(DrawingColor(focused ? theme_.focus : theme_.control));
            drawingTarget_->DrawRoundedRectangle(rounded, drawingBrush_.Get(),
                std::max(1.0F, (focused ? 3.0F : 1.0F) * scale));
            drawingBrush_->SetColor(DrawingColor(theme_.text));
            const auto label = KeyLabel(*key);
            drawingTarget_->DrawText(label.data(), static_cast<UINT32>(label.size()),
                keyTextFormat_.Get(), D2D1::RectF(inset, inset, width - inset, height - inset),
                drawingBrush_.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
        }
        const auto result = drawingTarget_->EndDraw();
        if (SUCCEEDED(result)) return true;
        drawingBrush_.Reset();
        drawingTarget_.Reset();
        if (result != D2DERR_RECREATE_TARGET) break;
    }
    return false;
}

void TextEntryModal::CreateControls() {
    CreateThemeResources();
    const auto createBounds = [](const RECT bounds) {
        return std::array{bounds.left, bounds.top,
            bounds.right - bounds.left, bounds.bottom - bounds.top};
    };
    const auto promptBounds = createBounds(layout_.promptBounds);
    const std::wstring promptText = placeholder_.empty() ? L"Enter text" : placeholder_;
    prompt_ = CreateWindowExW(
        0, L"STATIC", promptText.c_str(), WS_CHILD | WS_VISIBLE | SS_LEFT | SS_NOPREFIX,
        promptBounds[0], promptBounds[1], promptBounds[2], promptBounds[3],
        window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kPromptId)), instance_, nullptr);

    const auto editBounds = createBounds(layout_.editBounds);
    edit_ = CreateWindowExW(
        0, L"EDIT", initialValue_.c_str(),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL | ES_NOHIDESEL |
            (password_ ? ES_PASSWORD : 0),
        editBounds[0], editBounds[1], editBounds[2], editBounds[3],
        window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kEditId)), instance_, nullptr);
    if (edit_) {
        SetWindowLongPtrW(edit_, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(this));
        priorEditWindowProc_ = reinterpret_cast<WNDPROC>(SetWindowLongPtrW(
            edit_, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(EditWindowProc)));
        if (password_)
            SendMessageW(edit_, EM_SETPASSWORDCHAR, static_cast<WPARAM>(L'\x25cf'), 0);
        SendMessageW(edit_, EM_SETLIMITTEXT, static_cast<WPARAM>(maximumLength_), 0);
        SendMessageW(edit_, EM_SETCUEBANNER, TRUE,
            reinterpret_cast<LPARAM>(L"Text will appear here"));
        const auto end = static_cast<LPARAM>(initialValue_.size());
        SendMessageW(edit_, EM_SETSEL, static_cast<WPARAM>(end), end);
    }

    for (std::size_t index = 0; index < keys_.size(); ++index) {
        const auto bounds = createBounds(layout_.keyBounds[index]);
        keys_[index] = CreateWindowExW(
            0, L"BUTTON", L"", WS_CHILD | WS_VISIBLE | WS_TABSTOP |
                BS_OWNERDRAW | BS_NOTIFY,
            bounds[0], bounds[1], bounds[2], bounds[3],
            window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kKeyBase + index)),
            instance_, nullptr);
        if (!keys_[index]) continue;
        SetWindowLongPtrW(keys_[index], GWLP_USERDATA, reinterpret_cast<LONG_PTR>(this));
        const auto prior = reinterpret_cast<WNDPROC>(SetWindowLongPtrW(
            keys_[index], GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(KeyWindowProc)));
        if (!priorKeyWindowProc_) priorKeyWindowProc_ = prior;
    }

    const auto legendBounds = createBounds(layout_.legendBounds);
    legend_ = CreateWindowExW(
        0, L"STATIC",
        L"A  Select     X  Backspace     Y  Clear     B  Cancel     RT  Done\r\n"
        L"LB / RB  Move caret     D-pad / Left stick  Move between keys",
        WS_CHILD | WS_VISIBLE | SS_LEFT | SS_NOPREFIX,
        legendBounds[0], legendBounds[1], legendBounds[2], legendBounds[3],
        window_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kLegendId)), instance_, nullptr);
    UpdateKeyLabels();
    ApplyLayout();
}

void TextEntryModal::ApplyLayout() {
    const auto move = [](const HWND child, const RECT bounds) {
        if (child) SetWindowPos(child, nullptr, bounds.left, bounds.top,
            bounds.right - bounds.left, bounds.bottom - bounds.top,
            SWP_NOACTIVATE | SWP_NOZORDER);
    };
    move(prompt_, layout_.promptBounds);
    move(edit_, layout_.editBounds);
    for (std::size_t index = 0; index < keys_.size(); ++index) {
        move(keys_[index], layout_.keyBounds[index]);
    }
    move(legend_, layout_.legendBounds);
    if (prompt_) SendMessageW(prompt_, WM_SETFONT, reinterpret_cast<WPARAM>(bodyFont_), TRUE);
    if (edit_) SendMessageW(edit_, WM_SETFONT, reinterpret_cast<WPARAM>(bodyFont_), TRUE);
    if (legend_) SendMessageW(legend_, WM_SETFONT, reinterpret_cast<WPARAM>(legendFont_), TRUE);
    for (const auto key : keys_)
        if (key) SendMessageW(key, WM_SETFONT, reinterpret_cast<WPARAM>(keyFont_), TRUE);
    if (window_) {
        const int radius = std::max(8, static_cast<int>(std::lround(20.0 * layout_.scale)));
        const int width = layout_.windowBounds.right - layout_.windowBounds.left;
        const int height = layout_.windowBounds.bottom - layout_.windowBounds.top;
        const HRGN region = CreateRoundRectRgn(
            0, 0, width + 1, height + 1, radius, radius);
        if (region && SetWindowRgn(window_, region, TRUE) == 0)
            DeleteObject(region);
        InvalidateRect(window_, nullptr, TRUE);
    }
}

std::optional<wchar_t> TextEntryModal::KeyValue(const std::size_t index) const noexcept {
    if (index >= TextEntryCharacterKeyCount || index == 30 || index == 39) return std::nullopt;
    if (layer_ == Layer::Symbols) {
        const auto symbolIndex = index < 30 ? index : index - 1;
        return kSymbols[symbolIndex];
    }
    wchar_t value{};
    if (index < 10) value = kDigits[index];
    else if (index < 20) value = kTopLetters[index - 10];
    else if (index < 29) value = kMiddleLetters[index - 20];
    else if (index == 29) value = L'\'';
    else if (index < 38) value = kBottomLetters[index - 31];
    else value = L' ';
    if (layer_ == Layer::Uppercase && std::iswalpha(value))
        value = static_cast<wchar_t>(std::towupper(value));
    return value;
}

std::wstring TextEntryModal::KeyLabel(const std::size_t index) const {
    if (index == 40) return L"Clear";
    if (index == 41) return L"Backspace";
    if (index == 42) return L"Cancel";
    if (index == 43) return L"Done";
    if (index == 30)
        return layer_ == Layer::Symbols ? L"abc" :
            layer_ == Layer::Uppercase ? L"ABC" : L"Shift";
    if (index == 39) return layer_ == Layer::Symbols ? L"abc" : L"#+=";
    const auto value = KeyValue(index);
    if (!value) return {};
    return *value == L' ' ? L"Space" : std::wstring(1, *value);
}

void TextEntryModal::UpdateKeyLabels() {
    for (std::size_t index = 0; index < keys_.size(); ++index) {
        if (!keys_[index]) continue;
        const auto label = KeyLabel(index);
        SetWindowTextW(keys_[index], label.c_str());
        InvalidateRect(keys_[index], nullptr, TRUE);
    }
}

void TextEntryModal::Insert(const wchar_t value) {
    if (!edit_ || value < 0x20 || value == 0x7f) return;
    const int length = GetWindowTextLengthW(edit_);
    DWORD selectionStart{};
    DWORD selectionEnd{};
    SendMessageW(edit_, EM_GETSEL,
        reinterpret_cast<WPARAM>(&selectionStart), reinterpret_cast<LPARAM>(&selectionEnd));
    const auto selected = selectionEnd >= selectionStart ? selectionEnd - selectionStart : 0;
    if (length < 0 || static_cast<std::size_t>(length - static_cast<int>(selected)) >=
            maximumLength_) return;
    const wchar_t text[2]{value, L'\0'};
    SendMessageW(edit_, EM_REPLACESEL, TRUE, reinterpret_cast<LPARAM>(text));
    InvalidateRect(edit_, nullptr, TRUE);
}

bool TextEntryModal::PasteClipboard() {
    // Clipboard contents are read only for an explicit paste directed at the
    // focused edit control. Sensitive entry retains the same bounded scan and
    // native password buffer; outbound clipboard operations remain blocked.
    if (!window_ || !edit_ || GetFocus() != edit_) return false;

    const int length = GetWindowTextLengthW(edit_);
    if (length < 0 || static_cast<std::size_t>(length) > maximumLength_)
        return false;
    DWORD selectionStart{};
    DWORD selectionEnd{};
    SendMessageW(edit_, EM_GETSEL,
        reinterpret_cast<WPARAM>(&selectionStart), reinterpret_cast<LPARAM>(&selectionEnd));
    const auto boundedStart = std::min<std::size_t>(selectionStart, length);
    const auto boundedEnd = std::min<std::size_t>(selectionEnd, length);
    const auto selected = boundedEnd >= boundedStart
        ? boundedEnd - boundedStart : 0;
    const auto retained = static_cast<std::size_t>(length) - selected;
    if (retained > maximumLength_) return false;
    const auto remaining = maximumLength_ - retained;

    if (!IsClipboardFormatAvailable(CF_UNICODETEXT) ||
        !OpenClipboard(window_)) return false;
    bool admitted{};
    const HANDLE handle = GetClipboardData(CF_UNICODETEXT);
    if (handle) {
        const SIZE_T units = GlobalSize(handle) / sizeof(wchar_t);
        const auto* text = static_cast<const wchar_t*>(GlobalLock(handle));
        if (text && units != 0) {
            const auto scanLimit = std::min<std::size_t>(units, remaining + 1);
            std::size_t count{};
            bool valid = scanLimit != 0;
            while (valid && count < scanLimit && text[count] != L'\0') {
                const wchar_t value = text[count];
                if (value < 0x20 || value == 0x7f) valid = false;
                ++count;
            }
            // The complete clipboard value, including its terminator, must fit
            // inside the bounded scan and the exact remaining edit capacity.
            valid = valid && count < scanLimit && text[count] == L'\0' &&
                count != 0 && count <= remaining;
            for (std::size_t index = 0; valid && index < count; ++index) {
                const auto value = static_cast<unsigned int>(text[index]);
                if (value >= 0xD800 && value <= 0xDBFF) {
                    if (++index >= count) {
                        valid = false;
                    } else {
                        const auto trailing = static_cast<unsigned int>(text[index]);
                        valid = trailing >= 0xDC00 && trailing <= 0xDFFF;
                    }
                } else if (value >= 0xDC00 && value <= 0xDFFF) {
                    valid = false;
                }
            }
            if (valid) {
                SendMessageW(edit_, EM_REPLACESEL, TRUE,
                    reinterpret_cast<LPARAM>(text));
                admitted = true;
            }
        }
        if (text) GlobalUnlock(handle);
    }
    CloseClipboard();
    if (admitted) InvalidateRect(edit_, nullptr, TRUE);
    return admitted;
}

void TextEntryModal::Clear() {
    if (!edit_) return;
    ResetControllerRepeat();
    SetWindowTextW(edit_, L"");
    SendMessageW(edit_, EM_SETSEL, 0, 0);
    InvalidateRect(edit_, nullptr, TRUE);
}

void TextEntryModal::Backspace() {
    if (!edit_) return;
    DWORD selectionStart{};
    DWORD selectionEnd{};
    SendMessageW(edit_, EM_GETSEL,
        reinterpret_cast<WPARAM>(&selectionStart), reinterpret_cast<LPARAM>(&selectionEnd));
    if (selectionStart == selectionEnd) {
        if (selectionStart == 0) return;
        --selectionStart;
    }
    SendMessageW(edit_, EM_SETSEL, selectionStart, selectionEnd);
    SendMessageW(edit_, EM_REPLACESEL, TRUE, reinterpret_cast<LPARAM>(L""));
    InvalidateRect(edit_, nullptr, TRUE);
}

void TextEntryModal::MoveCaret(const int delta) {
    if (!edit_ || delta == 0) return;
    DWORD selectionStart{};
    DWORD selectionEnd{};
    SendMessageW(edit_, EM_GETSEL,
        reinterpret_cast<WPARAM>(&selectionStart), reinterpret_cast<LPARAM>(&selectionEnd));
    const int length = std::max(0, GetWindowTextLengthW(edit_));
    const auto current = delta < 0 ? selectionStart : selectionEnd;
    const auto next = static_cast<DWORD>(std::clamp(
        static_cast<long long>(current) + delta, 0LL, static_cast<long long>(length)));
    SendMessageW(edit_, EM_SETSEL, next, next);
    SendMessageW(edit_, EM_SCROLLCARET, 0, 0);
    InvalidateRect(edit_, nullptr, TRUE);
}

void TextEntryModal::ActivateFocusedKey() {
    if (focusIndex_ >= keys_.size()) return;
    if (focusIndex_ == 40) { Clear(); return; }
    if (focusIndex_ == 41) { Backspace(); return; }
    if (focusIndex_ == 42) { Complete(TextEntryModalOutcome::Cancelled); return; }
    if (focusIndex_ == 43) { Complete(TextEntryModalOutcome::Committed); return; }
    if (focusIndex_ == 30) {
        layer_ = layer_ == Layer::Symbols ? Layer::Lowercase :
            layer_ == Layer::Lowercase ? Layer::Uppercase : Layer::Lowercase;
        UpdateKeyLabels();
        return;
    }
    if (focusIndex_ == 39) {
        layer_ = layer_ == Layer::Symbols ? Layer::Lowercase : Layer::Symbols;
        UpdateKeyLabels();
        return;
    }
    if (const auto value = KeyValue(focusIndex_)) Insert(*value);
}

void TextEntryModal::Complete(const TextEntryModalOutcome outcome) {
    if (completed_) return;
    ResetControllerRepeat();
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
    const HWND closing = window_;
    if (closing) DestroyWindow(closing);
    window_ = nullptr;
    visible_ = false;
    if (owner_) PostMessageW(owner_, TextEntryCompletedMessage, 0, 0);
}

void TextEntryModal::SetKeyboardFocus(const std::size_t index) {
    if (index >= keys_.size() || !keys_[index]) return;
    if (index != focusIndex_ &&
        controllerRepeatAction_ == RepeatAction::ActivateKey)
        ResetControllerRepeat();
    const auto prior = focusIndex_ < keys_.size() ? keys_[focusIndex_] : nullptr;
    focusIndex_ = index;
    SetFocus(keys_[focusIndex_]);
    if (prior) InvalidateRect(prior, nullptr, TRUE);
    InvalidateRect(keys_[focusIndex_], nullptr, TRUE);
    if (edit_) InvalidateRect(edit_, nullptr, TRUE);
}

void TextEntryModal::MoveFocus(const Direction direction) {
    const bool actionRow = focusIndex_ >= TextEntryCharacterKeyCount;
    int row = actionRow ? 4 : static_cast<int>(focusIndex_) / kColumns;
    int column = actionRow ? static_cast<int>(focusIndex_ - TextEntryCharacterKeyCount)
                           : static_cast<int>(focusIndex_) % kColumns;
    if (direction == Direction::Left || direction == Direction::Right) {
        const int count = actionRow ? 4 : kColumns;
        column = (column + count + (direction == Direction::Left ? -1 : 1)) % count;
    } else {
        row = (row + 5 + (direction == Direction::Up ? -1 : 1)) % 5;
        if (actionRow) column = std::min(9, (column * 2 + 1) * 10 / 8);
        else if (row == 4) column = std::min(3, (column * 2 + 1) * 4 / 20);
    }
    SetKeyboardFocus(row == 4 ? TextEntryCharacterKeyCount + column
                             : static_cast<std::size_t>(row * kColumns + column));
}

void TextEntryModal::HandleController(const std::wstring_view button) noexcept {
    if (!window_ || !visible_) return;
    if (button == L"B") Complete(TextEntryModalOutcome::Cancelled);
    else if (button == L"A") ActivateFocusedKey();
    else if (button == L"X") Backspace();
    else if (button == L"Y") Clear();
    else if (button == L"RT") Complete(TextEntryModalOutcome::Committed);
    else if (button == L"LB") MoveCaret(-1);
    else if (button == L"RB") MoveCaret(1);
    else if (button == L"DPadLeft") MoveFocus(Direction::Left);
    else if (button == L"DPadRight") MoveFocus(Direction::Right);
    else if (button == L"DPadUp") MoveFocus(Direction::Up);
    else if (button == L"DPadDown") MoveFocus(Direction::Down);
}

void TextEntryModal::Close() noexcept {
    if (window_) Complete(TextEntryModalOutcome::Closed);
}

void TextEntryModal::InvokeRepeatAction(const RepeatAction action) {
    switch (action) {
    case RepeatAction::ActivateKey: {
        const auto key = KeyValue(focusIndex_);
        if (focusIndex_ == controllerRepeatFocusIndex_ && key &&
            *key == controllerRepeatKey_)
            Insert(controllerRepeatKey_);
        else
            ResetControllerRepeat();
        break;
    }
    case RepeatAction::Backspace: Backspace(); break;
    case RepeatAction::CaretLeft: MoveCaret(-1); break;
    case RepeatAction::CaretRight: MoveCaret(1); break;
    case RepeatAction::None: break;
    }
}

void TextEntryModal::UpdateControllerRepeat(
    const TextEntryControllerRepeatSample& sample,
    const unsigned long long now) noexcept {
    // Translate the modal's raw button state into one logical immediate action
    // and a bounded repeated stream. Directional navigation keeps its existing
    // platform repeat owner; B and RT remain edge-triggered outside this owner.
    const std::array<std::pair<RepeatAction, bool>, 4> held{{
        {RepeatAction::ActivateKey, sample.activateDown},
        {RepeatAction::Backspace, sample.backspaceDown},
        {RepeatAction::CaretLeft, sample.caretLeftDown},
        {RepeatAction::CaretRight, sample.caretRightDown},
    }};
    RepeatAction requested = RepeatAction::None;
    unsigned int heldCount{};
    for (const auto& [action, down] : held) {
        if (!down) continue;
        requested = action;
        ++heldCount;
    }
    if (!window_ || !visible_ || heldCount != 1) {
        ResetControllerRepeat();
        return;
    }
    const bool pressed = requested == RepeatAction::ActivateKey
        ? sample.activatePressed
        : requested == RepeatAction::Backspace
            ? sample.backspacePressed
            : requested == RepeatAction::CaretLeft
                ? sample.caretLeftPressed
                : sample.caretRightPressed;
    if (controllerRepeatAction_ == RepeatAction::None) {
        if (!pressed) return;
        if (requested == RepeatAction::ActivateKey) {
            const auto key = KeyValue(focusIndex_);
            if (!key) {
                ActivateFocusedKey();
                return;
            }
            controllerRepeatFocusIndex_ = focusIndex_;
            controllerRepeatKey_ = *key;
        }
        controllerRepeatAction_ = requested;
        InvokeRepeatAction(requested);
        if (controllerRepeatAction_ != RepeatAction::None)
            controllerRepeatAt_ = now + kControllerRepeatDelayMilliseconds;
        return;
    }
    if (requested != controllerRepeatAction_) {
        ResetControllerRepeat();
        return;
    }
    if (controllerRepeatAt_ != 0 && now >= controllerRepeatAt_) {
        InvokeRepeatAction(requested);
        if (controllerRepeatAction_ != RepeatAction::None)
            controllerRepeatAt_ = now + kControllerRepeatIntervalMilliseconds;
    }
}

void TextEntryModal::ResetControllerRepeat() noexcept {
    controllerRepeatAt_ = 0;
    controllerRepeatAction_ = RepeatAction::None;
    controllerRepeatFocusIndex_ = 0;
    controllerRepeatKey_ = L'\0';
}

bool TextEntryModal::PostController(const std::wstring_view button) noexcept {
    if (!window_) return false;
    const WPARAM command = button == L"A" ? 1 : button == L"B" ? 2 :
        button == L"X" ? 3 : button == L"DPadLeft" ? 4 :
        button == L"DPadRight" ? 5 : button == L"DPadUp" ? 6 :
        button == L"DPadDown" ? 7 : button == L"LB" ? 8 :
        button == L"RB" ? 9 : button == L"RT" ? 10 : button == L"Y" ? 11 : 0;
    return command != 0 && PostMessageW(window_, kControllerMessage, command, 0) != FALSE;
}

void TextEntryModal::HandlePhysicalCharacter(const wchar_t value) {
    if (value >= 0x20 && value != 0x7f) Insert(value);
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
    if (message == WM_PASTE) {
        (void)self->PasteClipboard();
        return 0;
    }
    if (self->password_ && (message == WM_COPY || message == WM_CUT ||
        message == WM_CONTEXTMENU)) return 0;
    if (message == WM_GETDLGCODE)
        return DLGC_WANTARROWS | DLGC_WANTCHARS | DLGC_WANTALLKEYS;
    if (message == WM_KEYDOWN) {
        if (IsPasteGesture(wParam)) (void)self->PasteClipboard();
        else if (wParam == VK_ESCAPE) self->Complete(TextEntryModalOutcome::Cancelled);
        else if (wParam == VK_RETURN) self->Complete(TextEntryModalOutcome::Committed);
        else if (wParam == VK_BACK) self->Backspace();
        else if (wParam == VK_LEFT) self->MoveCaret(-1);
        else if (wParam == VK_RIGHT) self->MoveCaret(1);
        else if (wParam == VK_UP) self->MoveFocus(Direction::Up);
        else if (wParam == VK_DOWN) self->MoveFocus(Direction::Down);
        else if (wParam == VK_TAB) self->SetKeyboardFocus(
            (GetKeyState(VK_SHIFT) & 0x8000) != 0
                ? TextEntryKeyCount - 1 : 0);
        else return CallWindowProcW(self->priorEditWindowProc_, window, message, wParam, lParam);
        return 0;
    }
    if (message == WM_CHAR) {
        if (wParam != VK_BACK && wParam != VK_RETURN && wParam != VK_ESCAPE)
            self->HandlePhysicalCharacter(static_cast<wchar_t>(wParam));
        return 0;
    }
    const auto result = CallWindowProcW(
        self->priorEditWindowProc_, window, message, wParam, lParam);
    if (message == WM_PAINT && GetFocus() != window) {
        if (const auto caret = self->CaretBounds()) {
            HDC dc = GetDC(window);
            if (dc) {
                const auto brush = CreateSolidBrush(self->theme_.focus);
                FillRect(dc, &*caret, brush);
                DeleteObject(brush);
                ReleaseDC(window, dc);
            }
        }
    }
    return result;
}

std::optional<RECT> TextEntryModal::CaretBounds() const noexcept {
    if (!edit_) return std::nullopt;
    DWORD start{}, end{};
    SendMessageW(edit_, EM_GETSEL, reinterpret_cast<WPARAM>(&start), reinterpret_cast<LPARAM>(&end));
    if (start != end) return std::nullopt;
    const int length = GetWindowTextLengthW(edit_);
    const auto index = std::min<DWORD>(end, static_cast<DWORD>(std::max(0, length)));
    RECT client{};
    GetClientRect(edit_, &client);
    HDC dc = GetDC(edit_);
    if (!dc) return std::nullopt;
    const auto priorFont = SelectObject(dc, bodyFont_);
    TEXTMETRICW metrics{};
    GetTextMetricsW(dc, &metrics);
    const auto margins = SendMessageW(edit_, EM_GETMARGINS, 0, 0);
    int x = LOWORD(margins);
    int y = 0;
    // EM_POSFROMCHAR rejects the insertion point AFTER the last character.
    // Measure the final displayed glyph from its valid start position instead.
    if (length > 0) {
        const auto probe = std::min<DWORD>(index, length - 1);
        const auto position = SendMessageW(edit_, EM_POSFROMCHAR, probe, 0);
        if (position != -1) {
            x = static_cast<short>(LOWORD(position));
            y = static_cast<short>(HIWORD(position));
            if (index == static_cast<DWORD>(length)) {
                std::array<wchar_t, MaximumLength + 1> text{};
                GetWindowTextW(edit_, text.data(), static_cast<int>(text.size()));
                const wchar_t glyph = password_ ? L'\x25cf' : text[probe];
                SIZE advance{};
                GetTextExtentPoint32W(dc, &glyph, 1, &advance);
                x += advance.cx;
                SecureZeroMemory(text.data(), sizeof(text));
            }
        }
    }
    SelectObject(dc, priorFont);
    ReleaseDC(edit_, dc);
    const int width = std::max(2, static_cast<int>(std::lround(2.0 * layout_.scale)));
    x = std::clamp(x, 0, static_cast<int>(std::max(0L, client.right - width)));
    return RECT{x, std::max(0, y), x + width, std::min(client.bottom, y + metrics.tmHeight)};
}

LRESULT CALLBACK TextEntryModal::KeyWindowProc(
    const HWND window, const UINT message, const WPARAM wParam, const LPARAM lParam) {
    auto* self = reinterpret_cast<TextEntryModal*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (!self || !self->priorKeyWindowProc_)
        return DefWindowProcW(window, message, wParam, lParam);
    const auto found = std::find(self->keys_.begin(), self->keys_.end(), window);
    if (message == WM_GETDLGCODE)
        return DLGC_WANTARROWS | DLGC_WANTCHARS | DLGC_WANTALLKEYS;
    if (message == WM_SETFOCUS && found != self->keys_.end()) {
        const auto index = static_cast<std::size_t>(found - self->keys_.begin());
        if (index != self->focusIndex_ &&
            self->controllerRepeatAction_ == RepeatAction::ActivateKey)
            self->ResetControllerRepeat();
        self->focusIndex_ = index;
        InvalidateRect(window, nullptr, TRUE);
        if (self->edit_) InvalidateRect(self->edit_, nullptr, TRUE);
    } else if (message == WM_KILLFOCUS) {
        if (self->controllerRepeatAction_ == RepeatAction::ActivateKey)
            self->ResetControllerRepeat();
        InvalidateRect(window, nullptr, TRUE);
    }
    if (message == WM_KEYDOWN) {
        if (IsPasteGesture(wParam)) {
            // The modal starts in its controller keyboard. Transfer this
            // explicit paste gesture to the modal-owned edit so it owns focus
            // before the existing clipboard admission gate runs.
            if (self->edit_) SetFocus(self->edit_);
            (void)self->PasteClipboard();
        }
        else if (wParam == VK_SPACE) SendMessageW(window, BM_CLICK, 0, 0);
        else if (wParam == VK_ESCAPE) self->Complete(TextEntryModalOutcome::Cancelled);
        else if (wParam == VK_RETURN) self->Complete(TextEntryModalOutcome::Committed);
        else if (wParam == VK_BACK) self->Backspace();
        else if (wParam == VK_LEFT) self->MoveCaret(-1);
        else if (wParam == VK_RIGHT) self->MoveCaret(1);
        else if (wParam == VK_UP) self->MoveFocus(Direction::Up);
        else if (wParam == VK_DOWN) self->MoveFocus(Direction::Down);
        else if (wParam == VK_TAB) {
            const bool reverse = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
            if ((!reverse && self->focusIndex_ + 1 >= TextEntryKeyCount) ||
                (reverse && self->focusIndex_ == 0)) {
                SetFocus(self->edit_);
            } else {
                self->SetKeyboardFocus(reverse
                    ? self->focusIndex_ - 1 : self->focusIndex_ + 1);
            }
        }
        else return CallWindowProcW(self->priorKeyWindowProc_, window, message, wParam, lParam);
        return 0;
    }
    if (message == WM_CHAR) {
        if (wParam != VK_BACK && wParam != VK_RETURN && wParam != VK_ESCAPE && wParam != L' ')
            self->HandlePhysicalCharacter(static_cast<wchar_t>(wParam));
        return 0;
    }
    return CallWindowProcW(self->priorKeyWindowProc_, window, message, wParam, lParam);
}

LRESULT TextEntryModal::HandleMessage(
    const UINT message, const WPARAM wParam, const LPARAM lParam) {
    switch (message) {
    case kControllerMessage:
        HandleController(wParam == 1 ? L"A" : wParam == 2 ? L"B" :
            wParam == 3 ? L"X" : wParam == 4 ? L"DPadLeft" :
            wParam == 5 ? L"DPadRight" : wParam == 6 ? L"DPadUp" :
            wParam == 7 ? L"DPadDown" : wParam == 8 ? L"LB" :
            wParam == 9 ? L"RB" : wParam == 11 ? L"Y" : L"RT");
        return 0;
    case WM_CREATE:
        CreateControls();
        return edit_ && std::all_of(keys_.begin(), keys_.end(), [](const HWND key) {
            return key != nullptr;
        }) ? 0 : -1;
    case WM_CLOSE: Complete(TextEntryModalOutcome::Closed); return 0;
    case WM_ACTIVATE:
        if (LOWORD(wParam) == WA_INACTIVE) ResetControllerRepeat();
        return DefWindowProcW(window_, message, wParam, lParam);
    case WM_COMMAND: {
        const int id = LOWORD(wParam);
        if (id >= kKeyBase && id < kKeyBase + static_cast<int>(keys_.size())) {
            const auto index = static_cast<std::size_t>(id - kKeyBase);
            const auto source = reinterpret_cast<HWND>(lParam);
            if (HIWORD(wParam) == BN_CLICKED && source == keys_[index]) {
                if (index != focusIndex_ &&
                    controllerRepeatAction_ == RepeatAction::ActivateKey)
                    ResetControllerRepeat();
                focusIndex_ = index;
                ActivateFocusedKey();
            }
        }
        return 0;
    }
    case WM_DRAWITEM: {
        auto* item = reinterpret_cast<DRAWITEMSTRUCT*>(lParam);
        if (!item || item->CtlID < kKeyBase ||
            item->CtlID >= kKeyBase + static_cast<UINT>(keys_.size())) return FALSE;
        const auto index = static_cast<std::size_t>(item->CtlID - kKeyBase);
        const bool focused = (item->itemState & ODS_FOCUS) != 0 || index == focusIndex_;
        if (PaintSurface(item->hDC, item->rcItem, index, focused)) return TRUE;
        // Emergency native painting is only used when Direct2D is unavailable.
        const COLORREF fill = focused ? theme_.controlFocused : theme_.control;
        HBRUSH brush = CreateSolidBrush(fill);
        HPEN pen = CreatePen(PS_SOLID,
            std::max(1, static_cast<int>(std::lround((focused ? 3.0 : 1.0) * layout_.scale))),
            focused ? theme_.focus : theme_.control);
        const auto priorBrush = SelectObject(item->hDC, brush);
        const auto priorPen = SelectObject(item->hDC, pen);
        const int radius = std::max(4, static_cast<int>(std::lround(10.0 * layout_.scale)));
        FillRect(item->hDC, &item->rcItem, panelBrush_);
        const int inset = std::max(2, static_cast<int>(std::lround(2.0 * layout_.scale)));
        RoundRect(item->hDC, item->rcItem.left + inset, item->rcItem.top + inset,
            item->rcItem.right - inset, item->rcItem.bottom - inset, radius, radius);
        SelectObject(item->hDC, priorPen);
        SelectObject(item->hDC, priorBrush);
        DeleteObject(pen);
        DeleteObject(brush);
        SetBkMode(item->hDC, TRANSPARENT);
        SetTextColor(item->hDC, theme_.text);
        const auto priorFont = SelectObject(item->hDC, keyFont_);
        auto label = KeyLabel(index);
        RECT textBounds = item->rcItem;
        DrawTextW(item->hDC, label.data(), static_cast<int>(label.size()), &textBounds,
            DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS);
        SelectObject(item->hDC, priorFont);
        return TRUE;
    }
    case WM_CTLCOLORSTATIC: {
        const HDC dc = reinterpret_cast<HDC>(wParam);
        SetTextColor(dc, reinterpret_cast<HWND>(lParam) == legend_
            ? theme_.secondaryText : theme_.text);
        SetBkColor(dc, theme_.panel);
        return reinterpret_cast<LRESULT>(panelBrush_);
    }
    case WM_CTLCOLOREDIT: {
        const HDC dc = reinterpret_cast<HDC>(wParam);
        SetTextColor(dc, theme_.text);
        SetBkColor(dc, theme_.control);
        return reinterpret_cast<LRESULT>(controlBrush_);
    }
    case WM_ERASEBKGND: return 1;
    case WM_PAINT: {
        PAINTSTRUCT paint{};
        const HDC dc = BeginPaint(window_, &paint);
        RECT client{};
        GetClientRect(window_, &client);
        FillRect(dc, &client, canvasBrush_);
        RECT panel = client;
        const int inset = std::max(1, static_cast<int>(std::lround(8.0 * layout_.scale)));
        InflateRect(&panel, -inset, -inset);
        FillRect(dc, &panel, panelBrush_);
        RECT input = ScaleRect({28, 72, 732, 136}, layout_.scale);
        if (PaintSurface(dc, input, std::nullopt)) {
            EndPaint(window_, &paint);
            return 0;
        }
        const auto priorBrush = SelectObject(dc, controlBrush_);
        const auto priorPen = SelectObject(dc, GetStockObject(NULL_PEN));
        const int radius = std::max(8, static_cast<int>(std::lround(12 * layout_.scale)));
        RoundRect(dc, input.left, input.top, input.right, input.bottom, radius, radius);
        SelectObject(dc, priorPen);
        SelectObject(dc, priorBrush);
        EndPaint(window_, &paint);
        return 0;
    }
    case WM_DPICHANGED: {
        dpi_ = HIWORD(wParam);
        const auto* suggested = reinterpret_cast<const RECT*>(lParam);
        MONITORINFO monitorInfo{sizeof(monitorInfo)};
        RECT workArea = suggested ? *suggested : layout_.windowBounds;
        const auto monitor = MonitorFromRect(&workArea, MONITOR_DEFAULTTONEAREST);
        if (monitor && GetMonitorInfoW(monitor, &monitorInfo)) workArea = monitorInfo.rcWork;
        layout_ = CalculateTextEntryModalLayout(workArea, dpi_, theme_.interfaceScale);
        const int width = layout_.windowBounds.right - layout_.windowBounds.left;
        const int height = layout_.windowBounds.bottom - layout_.windowBounds.top;
        SetWindowPos(window_, nullptr, layout_.windowBounds.left, layout_.windowBounds.top,
            width, height, SWP_NOACTIVATE | SWP_NOZORDER);
        CreateThemeResources();
        ApplyLayout();
        return 0;
    }
    default: return DefWindowProcW(window_, message, wParam, lParam);
    }
}

} // namespace widgetrail::input
