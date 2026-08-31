#pragma once

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <span>
#include <string_view>

namespace widgetrail::encoded_artwork {

[[nodiscard]] constexpr std::uint32_t ReadUInt32LittleEndian(
    const std::span<const std::uint8_t> bytes,
    const std::size_t offset) noexcept {
    return static_cast<std::uint32_t>(bytes[offset]) |
        (static_cast<std::uint32_t>(bytes[offset + 1]) << 8U) |
        (static_cast<std::uint32_t>(bytes[offset + 2]) << 16U) |
        (static_cast<std::uint32_t>(bytes[offset + 3]) << 24U);
}

[[nodiscard]] constexpr bool FourCc(
    const std::span<const std::uint8_t> bytes,
    const std::size_t offset,
    const std::string_view expected) noexcept {
    return expected.size() == 4 && offset <= bytes.size() &&
        bytes.size() - offset >= expected.size() &&
        std::equal(expected.begin(), expected.end(), bytes.begin() + offset);
}

[[nodiscard]] constexpr bool IsPng(
    const std::span<const std::uint8_t> bytes) noexcept {
    constexpr std::uint8_t signature[]{137, 80, 78, 71, 13, 10, 26, 10};
    return bytes.size() >= 24 &&
        std::equal(std::begin(signature), std::end(signature), bytes.begin());
}

[[nodiscard]] constexpr bool IsJpeg(
    const std::span<const std::uint8_t> bytes) noexcept {
    return bytes.size() >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8 &&
        bytes[bytes.size() - 2] == 0xff && bytes.back() == 0xd9;
}

[[nodiscard]] constexpr bool ContainsWebPImageChunk(
    const std::span<const std::uint8_t> bytes,
    std::size_t offset,
    const std::size_t end) noexcept {
    bool imagePayload = false;
    while (offset <= end && end - offset >= 8) {
        const auto length = static_cast<std::size_t>(
            ReadUInt32LittleEndian(bytes, offset + 4));
        const auto data = offset + 8;
        if (length > end - data) return false;
        if (FourCc(bytes, offset, "VP8 ")) {
            if (length < 10 || bytes[data + 3] != 0x9d ||
                bytes[data + 4] != 0x01 || bytes[data + 5] != 0x2a)
                return false;
            imagePayload = true;
        } else if (FourCc(bytes, offset, "VP8L")) {
            if (length < 5 || bytes[data] != 0x2f) return false;
            imagePayload = true;
        }
        const auto padded = length + (length & 1U);
        if (padded > end - data) return false;
        offset = data + padded;
    }
    return offset == end && imagePayload;
}

[[nodiscard]] constexpr bool IsWebP(
    const std::span<const std::uint8_t> bytes) noexcept {
    if (bytes.size() < 20 || !FourCc(bytes, 0, "RIFF") ||
        !FourCc(bytes, 8, "WEBP") ||
        bytes.size() - 8 > std::numeric_limits<std::uint32_t>::max() ||
        ReadUInt32LittleEndian(bytes, 4) != bytes.size() - 8)
        return false;

    bool extended = false;
    bool animated = false;
    bool animationHeader = false;
    bool animationFrame = false;
    bool imagePayload = false;
    std::size_t offset = 12;
    while (bytes.size() - offset >= 8) {
        const auto length = static_cast<std::size_t>(
            ReadUInt32LittleEndian(bytes, offset + 4));
        const auto data = offset + 8;
        if (length > bytes.size() - data) return false;
        const auto end = data + length;
        if (FourCc(bytes, offset, "VP8 ")) {
            if (length < 10 || bytes[data + 3] != 0x9d ||
                bytes[data + 4] != 0x01 || bytes[data + 5] != 0x2a)
                return false;
            imagePayload = true;
        } else if (FourCc(bytes, offset, "VP8L")) {
            if (length < 5 || bytes[data] != 0x2f) return false;
            imagePayload = true;
        } else if (FourCc(bytes, offset, "VP8X")) {
            if (extended || length != 10 || (bytes[data] & 0xc1U) != 0)
                return false;
            extended = true;
            animated = (bytes[data] & 0x02U) != 0;
        } else if (FourCc(bytes, offset, "ANIM")) {
            if (!extended || !animated || length != 6) return false;
            animationHeader = true;
        } else if (FourCc(bytes, offset, "ANMF")) {
            if (!extended || !animated || length < 24 ||
                !ContainsWebPImageChunk(bytes, data + 16, end))
                return false;
            animationFrame = true;
        }
        const auto padded = length + (length & 1U);
        if (padded > bytes.size() - data) return false;
        offset = data + padded;
    }
    return offset == bytes.size() &&
        (animated ? animationHeader && animationFrame : imagePayload);
}

[[nodiscard]] constexpr bool Matches(
    const std::wstring_view contentType,
    const std::span<const std::uint8_t> bytes) noexcept {
    if (contentType == L"image/png") return IsPng(bytes);
    if (contentType == L"image/jpeg") return IsJpeg(bytes);
    return contentType == L"image/webp" && IsWebP(bytes);
}

} // namespace widgetrail::encoded_artwork
