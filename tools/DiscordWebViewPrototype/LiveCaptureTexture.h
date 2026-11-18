#pragma once
#include <d3d11.h>
#include <cstdint>

// Private native capture transport. The caller already owns exact window,
// process-creation and class authority. No input injection or path authority.
using WrailTextureCaptureContinue=int32_t(__stdcall*)();
using WrailTextureCaptureAudio=void(__stdcall*)(uint32_t,uint32_t,uint32_t,int64_t,const void*,uint32_t);
using WrailTextureCaptureFrame=void(__stdcall*)(uint32_t,uint32_t,uint32_t,uint32_t,int64_t,ID3D11Texture2D*);
extern "C" __declspec(dllimport) int32_t __stdcall WrailLiveCaptureRunTextureV1(uint64_t window,uint32_t processId,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t maximumWidth,uint32_t maximumHeight,uint32_t framesPerSecond,uint32_t includeCursor,
    WrailTextureCaptureContinue current,WrailTextureCaptureAudio audioAndState,WrailTextureCaptureFrame texture) noexcept;
// Failure stage is 0 for source/graphics and 1 for process-audio activation or
// drain. This avoids misreporting an unavailable audio device as a lost window.
extern "C" __declspec(dllimport) int32_t __stdcall WrailLiveCaptureRunTextureV2(uint64_t window,uint32_t processId,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t maximumWidth,uint32_t maximumHeight,uint32_t framesPerSecond,uint32_t includeCursor,
    WrailTextureCaptureContinue current,WrailTextureCaptureAudio audioAndState,WrailTextureCaptureFrame texture,uint32_t* failureStage) noexcept;

extern "C" __declspec(dllimport) int32_t __stdcall WrailLiveCaptureRunTextureV3(uint64_t window,uint32_t processId,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t maximumWidth,uint32_t maximumHeight,uint32_t framesPerSecond,uint32_t includeCursor,uint64_t adapterLuid,
    WrailTextureCaptureContinue current,WrailTextureCaptureAudio audioAndState,WrailTextureCaptureFrame texture,uint32_t* failureStage) noexcept;

// Exact, already-qualified advanced canvas; legacy V1-V3 retain bounds semantics.
extern "C" __declspec(dllimport) int32_t __stdcall WrailLiveCaptureRunTextureV4(uint64_t window,uint32_t processId,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t width,uint32_t height,uint32_t framesPerSecond,uint32_t includeCursor,uint64_t adapterLuid,
    WrailTextureCaptureContinue current,WrailTextureCaptureAudio audioAndState,WrailTextureCaptureFrame texture,uint32_t* failureStage) noexcept;
