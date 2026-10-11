#pragma once
#include "WindowPreviewNative.h"
struct WrailContextCaptureResult {
    uint32_t size{sizeof(WrailContextCaptureResult)}, version{1}, width{}, height{}, frames{};
    int32_t error{};
    double durationSeconds{};
};
static_assert(sizeof(WrailContextCaptureResult) == 32);
struct WrailCaptureOptions {
    uint32_t size{sizeof(WrailCaptureOptions)}, version{2}, kind{1}, maximumWidth{}, maximumHeight{},
        framesPerSecond{15}, bitrate{3000000}, durationSeconds{5}, includeCursor{}, encoder{1};
    uint64_t maximumBytes{128ULL * 1024 * 1024};
};
struct WrailCaptureResult {
    uint32_t size{sizeof(WrailCaptureResult)}, version{2}, width{}, height{}, frames{};
    int32_t error{};
    double durationSeconds{};
    uint32_t sourceWidth{}, sourceHeight{}, missedFrames{}, encoder{};
};
static_assert(sizeof(WrailCaptureOptions) == 48);
static_assert(sizeof(WrailCaptureResult) == 48);
// Authority callback: 0 = cancelled/discard, 1 = continue, 2 = stop/finalize video.
extern "C" PREVIEW_API int32_t __stdcall WrailCaptureWindowConfigured(const WrailPreviewTarget* target,
    const wchar_t* outputPath, const WrailCaptureOptions* options, WrailPreviewAuthority authority, void* context,
    WrailCaptureResult* result) noexcept;
extern "C" PREVIEW_API int32_t __stdcall WrailCaptureWindow(const WrailPreviewTarget* target,
    const wchar_t* outputPath, uint32_t kind, WrailPreviewAuthority authority, void* context,
    WrailContextCaptureResult* result) noexcept;
