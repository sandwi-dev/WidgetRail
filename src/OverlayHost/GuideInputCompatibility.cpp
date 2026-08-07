#include "GuideInputCompatibility.h"

#include <array>
#include <filesystem>

namespace gba::input {
namespace {

constexpr WORD kGuideButtonBit = 0x0400;

HMODULE LoadSystemXInput() noexcept {
    if (const auto module = LoadLibraryExW(
            L"xinput1_4.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32)) {
        return module;
    }
    std::array<wchar_t, MAX_PATH> systemDirectory{};
    const auto length = GetSystemDirectoryW(
        systemDirectory.data(), static_cast<UINT>(systemDirectory.size()));
    if (length == 0 || length >= systemDirectory.size()) return nullptr;
    const auto path = std::filesystem::path(
        std::wstring_view(systemDirectory.data(), length)) / L"xinput1_4.dll";
    return LoadLibraryW(path.c_str());
}

} // namespace

std::uint8_t GuideEdgeTracker::Update(
    const std::array<bool, XUSER_MAX_COUNT>& pressed) noexcept {
    if (!primed_) {
        previous_ = pressed;
        primed_ = true;
        return 0;
    }
    std::uint8_t rising{};
    for (std::size_t slot = 0; slot < pressed.size(); ++slot) {
        if (pressed[slot] && !previous_[slot]) {
            rising |= static_cast<std::uint8_t>(1U << slot);
        }
    }
    previous_ = pressed;
    return rising;
}

XInputGuideCompatibility::~XInputGuideCompatibility() {
    Shutdown();
}

bool XInputGuideCompatibility::Initialize() noexcept {
    Shutdown();
    module_ = LoadSystemXInput();
    if (!module_) return false;
    // Ordinal 100 is intentionally resolved dynamically. Do not link or
    // expose it through the widget SDK: it is a replaceable host adapter.
    getStateEx_ = reinterpret_cast<GetStateEx>(
        GetProcAddress(module_, reinterpret_cast<LPCSTR>(100)));
    if (!getStateEx_) {
        Shutdown();
        return false;
    }
    (void)PollRisingEdges();
    return true;
}

void XInputGuideCompatibility::Shutdown() noexcept {
    getStateEx_ = nullptr;
    if (module_) {
        FreeLibrary(module_);
        module_ = nullptr;
    }
}

std::uint8_t XInputGuideCompatibility::PollRisingEdges() noexcept {
    std::array<bool, XUSER_MAX_COUNT> pressed{};
    if (getStateEx_) {
        for (DWORD slot = 0; slot < XUSER_MAX_COUNT; ++slot) {
            XINPUT_STATE state{};
            pressed[slot] = getStateEx_(slot, &state) == ERROR_SUCCESS &&
                            (state.Gamepad.wButtons & kGuideButtonBit) != 0;
        }
    }
    return edges_.Update(pressed);
}

} // namespace gba::input
