#pragma once
#include "WindowPreviewNative.h"
struct WrailContextCaptureResult {
    uint32_t size{sizeof(WrailContextCaptureResult)}, version{1}, width{}, height{}, frames{};
    int32_t error{};
    double durationSeconds{};
};
static_assert(sizeof(WrailContextCaptureResult) == 32);
extern "C" PREVIEW_API int32_t __stdcall WrailCaptureWindow(const WrailPreviewTarget* target,
    const wchar_t* outputPath, uint32_t kind, WrailPreviewAuthority authority, void* context,
    WrailContextCaptureResult* result) noexcept;
