#pragma once

#include <Windows.h>

#include <cstddef>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace gba::input {

class TextEntryModal final {
public:
    static constexpr std::size_t MaximumLength = 96;

    TextEntryModal() = default;
    TextEntryModal(const TextEntryModal&) = delete;
    TextEntryModal& operator=(const TextEntryModal&) = delete;

    [[nodiscard]] std::optional<std::wstring> Show(
        HINSTANCE instance,
        HWND owner,
        std::wstring_view value,
        std::wstring_view placeholder,
        std::size_t maximumLength);

    [[nodiscard]] bool active() const noexcept { return window_ != nullptr; }
    void HandleController(std::wstring_view button) noexcept;
    [[nodiscard]] bool PostController(std::wstring_view button) noexcept;

private:
    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam);
    void CreateControls();
    void Append(wchar_t value);
    void Backspace();
    void Complete(bool commit);
    void MoveFocus(int delta);
    [[nodiscard]] int Scale(int value) const noexcept;

    HINSTANCE instance_{};
    HWND owner_{};
    HWND window_{};
    HWND edit_{};
    std::vector<HWND> focusTargets_;
    std::wstring initialValue_;
    std::wstring placeholder_;
    std::optional<std::wstring> result_;
    std::size_t maximumLength_{};
    UINT dpi_{96};
    std::size_t focusIndex_{};
    bool completed_{};
};

} // namespace gba::input
