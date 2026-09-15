#pragma once
#include "ControllerIsolationCore.h"
#include <span>

namespace widgetrail::isolation {

enum class DualSenseTransport { Usb, Bluetooth };
struct DualSenseReport final { GamepadState state{}; bool guide{}; };
inline bool IsDualSenseProduct(std::uint16_t vendor, std::uint16_t product) noexcept {
    return vendor == 0x054C && (product == 0x0CE6 || product == 0x0DF2);
}

// Bluetooth's extended report carries CRC-32 over the HID input prefix (A1)
// and the first 74 bytes. Simple Bluetooth reports do not carry this checksum.
inline std::uint32_t DualSenseInputCrc(std::span<const std::uint8_t> bytes) noexcept {
    std::uint32_t crc = 0xFFFFFFFF;
    const auto add = [&](std::uint8_t value) {
        crc ^= value;
        for (int bit = 0; bit < 8; ++bit) crc = (crc >> 1) ^ (0xEDB88320U & (0U - (crc & 1U)));
    };
    add(0xA1);
    for (auto value : bytes) add(value);
    return ~crc;
}

inline bool DecodeDualSenseReport(std::span<const std::uint8_t> bytes,
    DualSenseTransport transport, DualSenseReport& output) noexcept {
    output = {};
    if (bytes.empty()) return false;
    std::size_t axes{}, triggers{}, buttons{};
    if (transport == DualSenseTransport::Usb && bytes[0] == 1 && bytes.size() == 64) {
        axes = 1; triggers = 5; buttons = 8;
    } else if (transport == DualSenseTransport::Bluetooth && bytes[0] == 1 &&
               (bytes.size() == 10 || bytes.size() == 78)) {
        // Windows may pad a simple report to the interface's maximum report size.
        axes = 1; triggers = 8; buttons = 5;
    } else if (transport == DualSenseTransport::Bluetooth && bytes[0] == 0x31 && bytes.size() == 78) {
        const auto crc = static_cast<std::uint32_t>(bytes[74]) |
            (static_cast<std::uint32_t>(bytes[75]) << 8) |
            (static_cast<std::uint32_t>(bytes[76]) << 16) |
            (static_cast<std::uint32_t>(bytes[77]) << 24);
        if (crc != DualSenseInputCrc(bytes.first(74))) return false;
        axes = 2; triggers = 6; buttons = 9;
    } else return false;
    const unsigned hat = bytes[buttons] & 15;
    if (hat > 8) return false;
    const auto axis = [](std::uint8_t value, bool invert) {
        // Both central quantization bins are neutral; endpoints retain the full
        // signed range, including -32768 on the negative side.
        const int sample = invert ? 255 - value : value;
        return static_cast<std::int16_t>(sample < 127 ? (sample - 127) * 32768 / 127 :
            sample > 128 ? (sample - 128) * 32767 / 127 : 0);
    };
    auto& state = output.state;
    state.leftThumbX = axis(bytes[axes], false); state.leftThumbY = axis(bytes[axes + 1], true);
    state.rightThumbX = axis(bytes[axes + 2], false); state.rightThumbY = axis(bytes[axes + 3], true);
    state.leftTrigger = bytes[triggers]; state.rightTrigger = bytes[triggers + 1];
    constexpr std::uint16_t hats[]{1, 1|8, 8, 8|2, 2, 2|4, 4, 4|1, 0};
    state.buttons = hats[hat];
    if (bytes[buttons] & 0x10) state.buttons |= 0x4000; // Square -> X
    if (bytes[buttons] & 0x20) state.buttons |= 0x1000; // Cross -> A
    if (bytes[buttons] & 0x40) state.buttons |= 0x2000; // Circle -> B
    if (bytes[buttons] & 0x80) state.buttons |= 0x8000; // Triangle -> Y
    constexpr std::uint16_t extra[]{0x100, 0x200, 0, 0, 0x20, 0x10, 0x40, 0x80};
    for (unsigned bit = 0; bit < 8; ++bit)
        if (bytes[buttons + 1] & (1U << bit)) state.buttons |= extra[bit];
    output.guide = (bytes[buttons + 2] & 1) != 0;
    return true;
}
} // namespace widgetrail::isolation
