#pragma once
#include <algorithm>
#include <cstdint>
struct CaptureGeometry { uint32_t width{}, height{}; };
constexpr CaptureGeometry ConfiguredCaptureSize(int32_t width, int32_t height, uint32_t maximumWidth, uint32_t maximumHeight, bool video) {
    if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || static_cast<uint64_t>(width) * height > 33554432ULL ||
        ((maximumWidth == 0) != (maximumHeight == 0)) || maximumWidth > 16384 || maximumHeight > 16384 ||
        (video && (width < 2 || height < 2 || maximumWidth < 2 || maximumHeight < 2))) return {};
    const double scale = maximumWidth == 0 ? 1.0 : std::min({1.0, double(maximumWidth) / width, double(maximumHeight) / height});
    auto w = std::max(1U, static_cast<uint32_t>(width * scale));
    auto h = std::max(1U, static_cast<uint32_t>(height * scale));
    if (video) { w = std::max(2U, w & ~1U); h = std::max(2U, h & ~1U); }
    return {w, h};
}
// Native kinds: 1 = context PNG, 2 = H.264 clip, 3 = original PNG.
constexpr CaptureGeometry CaptureSize(int32_t width, int32_t height, uint32_t kind) {
    if (width <= 0 || height <= 0 || kind < 1 || kind > 3) return {};
    if (kind == 3) {
        if (width > 16384 || height > 16384 || static_cast<uint64_t>(width) * height > 33554432ULL) return {};
        return {static_cast<uint32_t>(width), static_cast<uint32_t>(height)};
    }
    const double scale = std::min({1.0, (kind == 1 ? 1920.0 : 1280.0) / width, (kind == 1 ? 1080.0 : 720.0) / height});
    auto w = std::max(1U, static_cast<uint32_t>(width * scale));
    auto h = std::max(1U, static_cast<uint32_t>(height * scale));
    if (kind == 2) { w = std::max(2U, w & ~1U); h = std::max(2U, h & ~1U); }
    return {w, h};
}
