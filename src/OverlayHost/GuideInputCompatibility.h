#pragma once

#include <Windows.h>
#include <Xinput.h>

#include <array>
#include <cstdint>

namespace gba::input {

/// Pure rising-edge tracker shared by the compatibility adapter and tests.
class GuideEdgeTracker final {
public:
    [[nodiscard]] std::uint8_t Update(
        const std::array<bool, XUSER_MAX_COUNT>& pressed) noexcept;

private:
    std::array<bool, XUSER_MAX_COUNT> previous_{};
    bool primed_{};
};

/// Compatibility adapter for Xbox-360-class drivers that omit Guide from
/// GameInput callbacks. The ordinal is intentionally isolated here because it
/// is not a Microsoft-supported production contract. GameInput remains the
/// primary source; this adapter can be removed without touching host routing.
class XInputGuideCompatibility final {
public:
    XInputGuideCompatibility() = default;
    ~XInputGuideCompatibility();
    XInputGuideCompatibility(const XInputGuideCompatibility&) = delete;
    XInputGuideCompatibility& operator=(const XInputGuideCompatibility&) = delete;

    [[nodiscard]] bool Initialize() noexcept;
    void Shutdown() noexcept;
    [[nodiscard]] std::uint8_t PollRisingEdges() noexcept;
    [[nodiscard]] bool available() const noexcept { return getStateEx_ != nullptr; }

private:
    using GetStateEx = DWORD(WINAPI*)(DWORD, XINPUT_STATE*);
    HMODULE module_{};
    GetStateEx getStateEx_{};
    GuideEdgeTracker edges_;
};

} // namespace gba::input
