#pragma once
#include "WindowPreviewNative.h"
#include "CaptureEncodingEnvironment.h"
struct WrailContextCaptureResult {
    uint32_t size{sizeof(WrailContextCaptureResult)}, version{1}, width{}, height{}, frames{};
    int32_t error{};
    double durationSeconds{};
};
static_assert(sizeof(WrailContextCaptureResult) == 32);
inline constexpr uint64_t WrailCaptureTemporaryMaximumBytes = 128ULL * 1024 * 1024;
inline constexpr uint64_t WrailCaptureLibraryMaximumBytes = 2ULL * 1024 * 1024 * 1024;
struct WrailCaptureOptions {
    uint32_t size{sizeof(WrailCaptureOptions)}, version{3}, kind{1}, maximumWidth{}, maximumHeight{},
        framesPerSecond{15}, bitrate{3000000}, durationSeconds{5}, includeCursor{}, encoder{1};
    uint64_t maximumBytes{WrailCaptureTemporaryMaximumBytes};
    uint32_t audioMode{}, reserved{}; // 0=None, 1=target process tree; never system audio.
};
struct WrailCaptureResult {
    uint32_t size{sizeof(WrailCaptureResult)}, version{3}, width{}, height{}, frames{};
    int32_t error{};
    double durationSeconds{};
    uint32_t sourceWidth{}, sourceHeight{}, missedFrames{}, encoder{};
    uint32_t audioMode{}, reserved{};
};
static_assert(sizeof(WrailCaptureOptions) == 56);
static_assert(sizeof(WrailCaptureResult) == 56);
struct WrailCaptureEncodingProbe {
    uint32_t size{sizeof(WrailCaptureEncodingProbe)},version{1},supported{},hardware{};
    int32_t error{};uint32_t reserved{};
    double elapsedMilliseconds{};
};
static_assert(sizeof(WrailCaptureEncodingProbe)==32);
extern "C" PREVIEW_API int32_t __stdcall WrailProbeCaptureEncoding(const WrailCaptureOptions* options,
    WrailCaptureEncodingProbe* result) noexcept;
extern "C" PREVIEW_API int32_t __stdcall WrailProbeCaptureAudio(WrailCaptureEncodingProbe* result) noexcept;
// Authority callback: 0 = cancelled/discard, 1 = continue, 2 = stop/finalize video.
extern "C" PREVIEW_API int32_t __stdcall WrailCaptureOverlayScreenshot(const WrailPreviewTarget* target, void* overlay, const wchar_t* path,
    const WrailCaptureOptions* options, WrailPreviewAuthority authority, void* context, WrailCaptureResult* result) noexcept;
extern "C" PREVIEW_API int32_t __stdcall WrailCaptureWindowConfigured(const WrailPreviewTarget* target,
    const wchar_t* outputPath, const WrailCaptureOptions* options, WrailPreviewAuthority authority, void* context,
    WrailCaptureResult* result) noexcept;
extern "C" PREVIEW_API int32_t __stdcall WrailCaptureWindow(const WrailPreviewTarget* target,
    const wchar_t* outputPath, uint32_t kind, WrailPreviewAuthority authority, void* context,
    WrailContextCaptureResult* result) noexcept;

// Cheap adapter/driver inventory, separate from GPU encoder qualification.
extern "C" PREVIEW_API int32_t __stdcall WrailGetCaptureEncodingEnvironment(WrailCaptureEncodingEnvironment* result) noexcept;
extern "C" PREVIEW_API int32_t __stdcall WrailReadCaptureSourceSize(const WrailPreviewTarget* target,uint32_t* width,uint32_t* height) noexcept;

struct WrailCaptureEncodingSelection {
    uint32_t size{sizeof(WrailCaptureEncodingSelection)},version{1},codec{1},pixelFormat{1},rateControl{1},quality{75};
    uint64_t adapterLuid{};
};
static_assert(sizeof(WrailCaptureEncodingSelection)==32);
struct WrailCaptureEncodingProbeV2 {
    uint32_t size{sizeof(WrailCaptureEncodingProbeV2)},version{2},supported{},hardware{};
    int32_t error{};uint32_t stage{};double elapsedMilliseconds{};
    uint32_t encodedFrames{},decodedFrames{};uint64_t adapterLuid{};
    uint32_t codec{},pixelFormat{},rateControl{},quality{},replayGop{},replayNoBFrames{},reserved0{},reserved1{};
};
static_assert(sizeof(WrailCaptureEncodingProbeV2)==80);
extern "C" PREVIEW_API int32_t __stdcall WrailProbeCaptureEncodingV2(const WrailCaptureOptions* options,
    const WrailCaptureEncodingSelection* selection,WrailCaptureEncodingProbeV2* result) noexcept;

extern "C" PREVIEW_API int32_t __stdcall WrailCaptureWindowConfiguredV4(const WrailPreviewTarget* target,const wchar_t* outputPath,
    const WrailCaptureOptions* options,const WrailCaptureEncodingSelection* selection,uint32_t expectedSourceWidth,uint32_t expectedSourceHeight,
    WrailPreviewAuthority authority,void* context,WrailCaptureResult* result) noexcept;

// Library output is a broker-reserved file, never a temporary widget attachment.
// A null selection retains the legacy profile/bounds path and requires zero source sizes.
extern "C" PREVIEW_API int32_t __stdcall WrailCaptureWindowLibraryV1(const WrailPreviewTarget* target,const wchar_t* outputPath,
    const WrailCaptureOptions* options,const WrailCaptureEncodingSelection* selection,uint32_t expectedSourceWidth,uint32_t expectedSourceHeight,
    WrailPreviewAuthority authority,void* context,WrailCaptureResult* result) noexcept;
