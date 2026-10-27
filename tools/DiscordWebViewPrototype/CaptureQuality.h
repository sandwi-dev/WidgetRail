#pragma once
#include <cstdint>

// Current bounded transport presets. Providers may narrow these independently.
// Nominal height is independent of the source's
// aspect ratio: retain the existing ultrawide envelope rather than stretching.
struct LiveCaptureQuality {
    uint32_t maximumWidth{}, maximumHeight{}, framesPerSecond{};
};
constexpr uint32_t MaximumLiveCaptureVideoBytes=5120u*1440u*4u;
constexpr bool ValidLiveCaptureQuality(LiveCaptureQuality value) {
    return ((value.maximumWidth == 1706 && value.maximumHeight == 480) ||
        (value.maximumWidth == 2560 && value.maximumHeight == 720) ||
        (value.maximumWidth == 3840 && value.maximumHeight == 1080) ||
        (value.maximumWidth == 5120 && value.maximumHeight == 1440)) &&
        (value.framesPerSecond == 15 || value.framesPerSecond == 30 || value.framesPerSecond == 60);
}
constexpr int64_t LiveCaptureFrameInterval(LiveCaptureQuality value) {
    return ValidLiveCaptureQuality(value) ? 1000000 / value.framesPerSecond : 0;
}
