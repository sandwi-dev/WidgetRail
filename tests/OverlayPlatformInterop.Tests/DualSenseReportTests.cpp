#include "../../src/OverlayPlatformInterop/DualSenseReport.h"
#include <array>
#include <algorithm>
#include <cstdlib>
#include <iostream>
using namespace widgetrail::isolation;
namespace {
int checks{};
void Check(bool result, const char* message) {
    ++checks; if (!result) { std::cerr << "FAIL " << message << '\n'; std::exit(1); }
}
}
int main() {
    Check(IsDualSenseProduct(0x054C, 0x0CE6) && IsDualSenseProduct(0x054C, 0x0DF2) &&
        !IsDualSenseProduct(0x054C, 0x05C4) && !IsDualSenseProduct(0x045E, 0x0CE6), "only supported Sony products match");
    std::array<std::uint8_t, 64> usb{};
    usb[0] = 1; usb[1] = usb[2] = usb[3] = usb[4] = 128; usb[8] = 8;
    DualSenseReport result;
    Check(DecodeDualSenseReport(usb, DualSenseTransport::Usb, result) && result.state == GamepadState{} && !result.guide,
        "USB neutral packet is neutral");
    constexpr std::uint16_t hats[]{1, 9, 8, 10, 2, 6, 4, 5, 0};
    for (unsigned hat = 0; hat < 9; ++hat) {
        usb[8] = static_cast<std::uint8_t>(hat);
        Check(DecodeDualSenseReport(usb, DualSenseTransport::Usb, result) && result.state.buttons == hats[hat], "all hat directions map correctly");
    }
    constexpr std::uint16_t faces[]{0x4000, 0x1000, 0x2000, 0x8000};
    for (unsigned bit = 0; bit < 4; ++bit) {
        usb[8] = static_cast<std::uint8_t>(8 | (0x10 << bit));
        Check(DecodeDualSenseReport(usb, DualSenseTransport::Usb, result) && result.state.buttons == faces[bit], "face button positional mapping");
    }
    usb[8] = 8;
    constexpr std::uint16_t extras[]{0x100, 0x200, 0, 0, 0x20, 0x10, 0x40, 0x80};
    for (unsigned bit = 0; bit < 8; ++bit) {
        usb[9] = static_cast<std::uint8_t>(1 << bit);
        Check(DecodeDualSenseReport(usb, DualSenseTransport::Usb, result) && result.state.buttons == extras[bit], "shoulders Create Options and stick clicks map correctly");
    }
    usb[9] = 0;
    usb[1] = 0; usb[2] = 0; usb[3] = 255; usb[4] = 255; usb[5] = 255; usb[6] = 83; usb[10] = 1;
    Check(DecodeDualSenseReport(usb, DualSenseTransport::Usb, result) && result.guide &&
        result.state.leftThumbX == -32768 && result.state.leftThumbY == 32767 &&
        result.state.rightThumbX == 32767 && result.state.rightThumbY == -32768 &&
        result.state.leftTrigger == 255 && result.state.rightTrigger == 83, "axes endpoints inversion analog triggers and PS");
    const auto expected = result.state;
    std::array<std::uint8_t, 10> simple{1, 0, 0, 255, 255, 8, 0, 1, 255, 83};
    Check(DecodeDualSenseReport(simple, DualSenseTransport::Bluetooth, result) && result.state == expected && result.guide,
        "Bluetooth simple layout differs from USB but produces the same input");
    std::array<std::uint8_t, 78> padded{}; std::copy(simple.begin(), simple.end(), padded.begin());
    Check(DecodeDualSenseReport(padded, DualSenseTransport::Bluetooth, result) && result.state == expected, "Windows-padded Bluetooth simple packet");
    std::array<std::uint8_t, 78> full{};
    full[0] = 0x31; full[2] = full[3] = full[4] = full[5] = 128; full[9] = 8;
    // Independently generated with Python zlib.crc32(A1 + report[0:74]).
    full[74] = 0x65; full[75] = 0xD1; full[76] = 0xE3; full[77] = 0x34;
    Check(DualSenseInputCrc({}) == 0x73D37CF3 && DecodeDualSenseReport(full, DualSenseTransport::Bluetooth, result) &&
        result.state == GamepadState{}, "extended Bluetooth packet and independent CRC fixture");
    for (std::size_t length = 0; length < 64; ++length)
        Check(!DecodeDualSenseReport(std::span(usb).first(length), DualSenseTransport::Usb, result) && result.state == GamepadState{}, "truncated USB reports clear state");
    for (std::size_t byte = 0; byte < full.size(); ++byte) {
        auto corrupt = full; corrupt[byte] ^= 0x80;
        Check(!DecodeDualSenseReport(corrupt, DualSenseTransport::Bluetooth, result) && !result.guide, "corrupt Bluetooth payload and CRC are rejected");
    }
    Check(!DecodeDualSenseReport(simple, DualSenseTransport::Usb, result) &&
        !DecodeDualSenseReport(usb, DualSenseTransport::Bluetooth, result), "wrong transport cannot reinterpret report offsets");
    usb[8] = 15;
    Check(!DecodeDualSenseReport(usb, DualSenseTransport::Usb, result), "invalid hat is rejected");
    usb[8] = 8; usb[9] = 0; usb[10] = 6; usb[5] = usb[6] = 0;
    usb[1] = usb[2] = usb[3] = usb[4] = 127;
    Check(DecodeDualSenseReport(usb, DualSenseTransport::Usb, result) && result.state == GamepadState{} && !result.guide,
        "touch and mute do not synthesize PS; both center bins stay neutral");
    std::cout << "DualSenseReportTests passed " << checks << " checks.\n";
}
