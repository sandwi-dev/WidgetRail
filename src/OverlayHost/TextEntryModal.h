#pragma once

#include <Windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <array>
#include <cstddef>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace widgetrail::input {

class SecureTextBuffer final {
public:
    SecureTextBuffer() = default;
    explicit SecureTextBuffer(std::vector<wchar_t>&& value) noexcept;
    ~SecureTextBuffer();
    SecureTextBuffer(const SecureTextBuffer&) = delete;
    SecureTextBuffer& operator=(const SecureTextBuffer&) = delete;
    SecureTextBuffer(SecureTextBuffer&& other) noexcept;
    SecureTextBuffer& operator=(SecureTextBuffer&& other) noexcept;

    [[nodiscard]] std::span<const wchar_t> view() const noexcept { return value_; }
    [[nodiscard]] bool empty() const noexcept { return value_.empty(); }
    void clear() noexcept;

private:
    std::vector<wchar_t> value_;
};

inline constexpr std::size_t TextEntryCharacterKeyCount = 40;
inline constexpr std::size_t TextEntryKeyCount = 46;
inline constexpr UINT TextEntryCompletedMessage = WM_APP + 0x270;

struct TextEntryModalTheme final {
    COLORREF canvas{RGB(16, 19, 26)};
    COLORREF panel{RGB(27, 31, 41)};
    COLORREF control{RGB(36, 42, 55)};
    COLORREF controlFocused{RGB(252, 63, 108)};
    COLORREF text{RGB(247, 247, 250)};
    COLORREF secondaryText{RGB(155, 163, 179)};
    COLORREF focus{RGB(255, 255, 255)};
    double interfaceScale{1.0};
    double textScale{1.0};
    std::wstring fontFamily{L"Segoe UI"};
};

struct TextEntryControllerRepeatSample final {
    bool activateDown{};
    bool activatePressed{};
    bool backspaceDown{};
    bool backspacePressed{};
    bool caretLeftDown{};
    bool caretLeftPressed{};
    bool caretRightDown{};
    bool caretRightPressed{};
};

enum class TextEntryModalOutcome {
    Failed,
    Cancelled,
    Closed,
    Committed,
};

struct TextEntryModalResult final {
    TextEntryModalOutcome outcome{TextEntryModalOutcome::Failed};
    std::optional<SecureTextBuffer> committedText;
};

struct TextEntryModalLayout final {
    RECT windowBounds{};
    RECT promptBounds{};
    RECT editBounds{};
    std::array<RECT, TextEntryKeyCount> keyBounds{};
    double scale{1.0};
};

[[nodiscard]] TextEntryModalLayout CalculateTextEntryModalLayout(
    RECT workArea,
    UINT dpi,
    double interfaceScale = 1.0) noexcept;

class TextEntryModal final {
public:
    static constexpr std::size_t MaximumLength = 96;

    TextEntryModal() = default;
    ~TextEntryModal();
    TextEntryModal(const TextEntryModal&) = delete;
    TextEntryModal& operator=(const TextEntryModal&) = delete;

    [[nodiscard]] bool Begin(
        HINSTANCE instance,
        HWND owner,
        std::wstring_view value,
        std::wstring_view placeholder,
        std::size_t maximumLength,
        bool password = false,
        TextEntryModalTheme theme = {});

    [[nodiscard]] bool active() const noexcept { return window_ != nullptr; }
    [[nodiscard]] std::optional<TextEntryModalResult> TakeResult();
    void SetVisible(bool visible) noexcept;
    void SetOpacity(float opacity) noexcept;
    void Raise() noexcept;
    void Focus() noexcept;
    [[nodiscard]] std::optional<RECT> CaretBounds() const noexcept;
    void Close() noexcept;
    void HandleController(std::wstring_view button) noexcept;
    void UpdateControllerRepeat(
        const TextEntryControllerRepeatSample& sample,
        unsigned long long now) noexcept;
    [[nodiscard]] bool PostController(std::wstring_view button) noexcept;

private:
    enum class Direction { Left, Right, Up, Down };
    enum class Layer { Lowercase, Uppercase, Symbols };
    enum class RepeatAction { None, ActivateKey, Backspace, CaretLeft, CaretRight };

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    static LRESULT CALLBACK EditWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    static LRESULT CALLBACK KeyWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam);
    void CreateControls();
    void CreateThemeResources();
    void ReleaseThemeResources() noexcept;
    [[nodiscard]] bool EnsureDrawingResources();
    [[nodiscard]] bool PaintSurface(HDC dc, const RECT& bounds,
        std::optional<std::size_t> key, bool focused = false);
    void ApplyLayout();
    void UpdateKeyLabels();
    void Insert(wchar_t value);
    [[nodiscard]] bool PasteClipboard();
    void Backspace();
    void Clear();
    void ToggleShift();
    void ResetControllerRepeat() noexcept;
    void InvokeRepeatAction(RepeatAction action);
    void MoveCaret(int delta);
    void ActivateFocusedKey();
    [[nodiscard]] std::optional<wchar_t> KeyValue(std::size_t index) const noexcept;
    [[nodiscard]] std::wstring KeyLabel(std::size_t index) const;
    [[nodiscard]] std::wstring_view ControllerHint(std::size_t index, bool focused) const noexcept;
    void Complete(TextEntryModalOutcome outcome);
    void MoveFocus(Direction direction);
    void SetKeyboardFocus(std::size_t index);
    void HandlePhysicalCharacter(wchar_t value);

    HINSTANCE instance_{};
    HWND owner_{};
    HWND window_{};
    HWND prompt_{};
    HWND edit_{};
    HWND resumeFocus_{};
    std::array<HWND, TextEntryKeyCount> keys_{};
    WNDPROC priorEditWindowProc_{};
    WNDPROC priorKeyWindowProc_{};
    std::wstring initialValue_;
    std::wstring placeholder_;
    std::optional<SecureTextBuffer> result_;
    std::size_t maximumLength_{};
    UINT dpi_{96};
    TextEntryModalLayout layout_{};
    std::size_t focusIndex_{};
    Layer layer_{Layer::Lowercase};
    TextEntryModalTheme theme_{};
    HBRUSH canvasBrush_{};
    HBRUSH panelBrush_{};
    HBRUSH controlBrush_{};
    HFONT bodyFont_{};
    HFONT keyFont_{};
    Microsoft::WRL::ComPtr<ID2D1Factory> drawingFactory_;
    Microsoft::WRL::ComPtr<IDWriteFactory> textFactory_;
    Microsoft::WRL::ComPtr<ID2D1DCRenderTarget> drawingTarget_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> drawingBrush_;
    Microsoft::WRL::ComPtr<IDWriteTextFormat> keyTextFormat_;
    Microsoft::WRL::ComPtr<IDWriteTextFormat> hintTextFormat_;
    unsigned long long controllerRepeatAt_{};
    RepeatAction controllerRepeatAction_{RepeatAction::None};
    std::size_t controllerRepeatFocusIndex_{};
    wchar_t controllerRepeatKey_{};
    TextEntryModalOutcome outcome_{TextEntryModalOutcome::Failed};
    bool completed_{};
    bool password_{};
    bool visible_{};
};

} // namespace widgetrail::input
