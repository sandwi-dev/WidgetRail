#pragma once
#include <algorithm>
#include <cstdint>
#include <optional>
#include <utility>
namespace widgetrail::preview {
inline constexpr uint64_t MaximumBytes = 192ULL * 1024 * 1024;
inline constexpr uint64_t MaximumSourcePixels = 4096ULL * 2160;
inline constexpr size_t MaximumSources = 8;
// WinUI can retain a bound surface and the managed pump's latest pending surface
// while native has already recreated its output. Neither queue can grow.
inline constexpr uint64_t RetiringSurfaceAllowance = 2 * MaximumSources * 960ULL * 540 * 8;
inline constexpr uint64_t MaximumActiveBytes = MaximumBytes - RetiringSurfaceAllowance;
inline std::optional<std::pair<uint32_t, uint32_t>> OutputSize(uint32_t width, uint32_t height) noexcept {
    if (!width || !height || width > 16384 || height > 16384) return std::nullopt;
    const auto scale = std::min({1.0, 960.0 / width, 540.0 / height});
    return std::pair{std::max(1U, uint32_t(width * scale)), std::max(1U, uint32_t(height * scale))};
}
inline bool SourceFits(int32_t width, int32_t height, uint64_t reserved) noexcept {
    if (width <= 0 || height <= 0 || uint64_t(width) * height > MaximumSourcePixels || reserved > MaximumActiveBytes) return false;
    return uint64_t(width) * height * 4 <= MaximumActiveBytes - reserved;
}
}
