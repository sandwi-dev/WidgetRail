#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>

namespace widgetrail {
struct ImageDecodeSize {
    std::uint32_t width{};
    std::uint32_t height{};
    bool valid() const noexcept {
        return (width == 0 && height == 0) ||
            (width > 0 && height > 0 && width <= 2048 && height <= 2048);
    }
};
inline ImageDecodeSize DisplayImageSize(float width, float height, float scale) noexcept {
    if (!std::isfinite(width) || !std::isfinite(height) || !std::isfinite(scale) ||
        width <= 0 || height <= 0 || scale <= 0) return {};
    const auto bucket = [scale](float value) {
        return static_cast<std::uint32_t>(std::clamp(std::ceil(value * scale / 64) * 64, 64.0F, 2048.0F));
    };
    return {bucket(width), bucket(height)};
}
// Preserve source aspect ratio and never upscale. Extreme aspect ratios and
// large backgrounds get a bounded variant instead of retaining unseen pixels.
inline ImageDecodeSize FitDecodedImage(std::uint32_t width, std::uint32_t height,
                                      ImageDecodeSize requested) noexcept {
    if (!width || !height || !requested.width || !requested.valid()) return {width, height};
    double factor = std::min(1.0, std::max(double(requested.width) / width, double(requested.height) / height));
    factor = std::min(factor, 2048.0 / std::max(width, height));
    factor = std::min(factor, std::sqrt(2.0 * requested.width * requested.height / (double(width) * height)));
    return {std::max(1U, static_cast<std::uint32_t>(std::ceil(width * factor))),
            std::max(1U, static_cast<std::uint32_t>(std::ceil(height * factor)))};
}
}
