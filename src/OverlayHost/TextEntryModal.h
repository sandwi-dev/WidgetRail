#pragma once

#include <Windows.h>

#include <array>
#include <cstddef>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace gba::input {

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

inline constexpr std::size_t TextEntryCharacterCount = 39;
inline constexpr std::size_t TextEntryActionCount = 4;

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
    RECT editBounds{};
    std::array<RECT, TextEntryCharacterCount> characterBounds{};
    std::array<RECT, TextEntryActionCount> actionBounds{};
    double scale{1.0};
};

[[nodiscard]] TextEntryModalLayout CalculateTextEntryModalLayout(
    RECT workArea,
    UINT dpi) noexcept;

class TextEntryModal final {
public:
    static constexpr std::size_t MaximumLength = 96;

    TextEntryModal() = default;
    TextEntryModal(const TextEntryModal&) = delete;
    TextEntryModal& operator=(const TextEntryModal&) = delete;

    [[nodiscard]] TextEntryModalResult Show(
        HINSTANCE instance,
        HWND owner,
        std::wstring_view value,
        std::wstring_view placeholder,
        std::size_t maximumLength,
        bool password = false);

    [[nodiscard]] bool active() const noexcept { return window_ != nullptr; }
    void HandleController(std::wstring_view button) noexcept;
    [[nodiscard]] bool PostController(std::wstring_view button) noexcept;

private:
    enum class Direction { Left, Right, Up, Down };
    struct FocusTarget final { HWND window{}; RECT bounds{}; };

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    static LRESULT CALLBACK EditWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam);
    void CreateControls();
    void Append(wchar_t value);
    void Backspace();
    void Complete(TextEntryModalOutcome outcome);
    void MoveFocus(Direction direction);

    HINSTANCE instance_{};
    HWND owner_{};
    HWND window_{};
    HWND edit_{};
    WNDPROC priorEditWindowProc_{};
    std::vector<FocusTarget> focusTargets_;
    std::wstring initialValue_;
    std::wstring placeholder_;
    std::optional<SecureTextBuffer> result_;
    std::size_t maximumLength_{};
    UINT dpi_{96};
    TextEntryModalLayout layout_{};
    std::size_t focusIndex_{};
    TextEntryModalOutcome outcome_{TextEntryModalOutcome::Failed};
    bool completed_{};
    bool password_{};
};

} // namespace gba::input
