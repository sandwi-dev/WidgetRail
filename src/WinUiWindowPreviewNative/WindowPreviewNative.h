#pragma once
#include <cstdint>
#ifdef WRAIL_PREVIEW_EXPORTS
#define PREVIEW_API __declspec(dllexport)
#else
#define PREVIEW_API __declspec(dllimport)
#endif
struct WrailPreviewTarget {
    uint32_t size{sizeof(WrailPreviewTarget)}, version{1};
    uint64_t window{};
    uint32_t processId{}, reserved{};
    uint64_t processCreated{};
    wchar_t className[256]{};
};
struct WrailPreviewStats {
    uint32_t size{sizeof(WrailPreviewStats)}, version{1}, state{}, activeCount{};
    int32_t error{};
    uint32_t sourceWidth{}, sourceHeight{}, width{}, height{}, reserved{};
    uint64_t frames{}, surfaceGeneration{}, totalBytes{};
};
static_assert(sizeof(WrailPreviewTarget) == 544);
static_assert(sizeof(WrailPreviewStats) == 64);
struct WrailPreviewHealth {
    uint32_t size{sizeof(WrailPreviewHealth)}, version{1}, phase{}, reason{}, processId{};
    int32_t error{};
    uint64_t window{}, slot{}, frames{};
    int64_t startedAt{};
};
static_assert(sizeof(WrailPreviewHealth) == 56);
struct WrailPreviewEngine;
struct WrailPreviewHealthReader;
using WrailPreviewAuthority = int32_t(__stdcall*)(void* context);
extern "C" {
PREVIEW_API int32_t __stdcall WrailPreviewCreate(WrailPreviewEngine** result) noexcept;
PREVIEW_API void __stdcall WrailPreviewDestroy(WrailPreviewEngine* engine) noexcept;
PREVIEW_API int32_t __stdcall WrailPreviewAdd(WrailPreviewEngine* engine, const WrailPreviewTarget* target,
    uint64_t hostWindow, uint32_t width, uint32_t height, uint32_t fit,
    WrailPreviewAuthority authority, void* context, uint64_t* id) noexcept;
PREVIEW_API int32_t __stdcall WrailPreviewRenew(WrailPreviewEngine* engine, uint64_t id, int64_t deadline) noexcept;
PREVIEW_API int32_t __stdcall WrailPreviewResize(WrailPreviewEngine* engine, uint64_t id,
    uint32_t width, uint32_t height, uint32_t fit) noexcept;
PREVIEW_API int32_t __stdcall WrailPreviewRemove(WrailPreviewEngine* engine, uint64_t id) noexcept;
PREVIEW_API int32_t __stdcall WrailPreviewSwapChain(WrailPreviewEngine* engine, uint64_t id, void** result, uint64_t* generation) noexcept;
PREVIEW_API int32_t __stdcall WrailPreviewInspect(WrailPreviewEngine* engine, uint64_t id, WrailPreviewStats* result) noexcept;
// Acquire while the engine is alive; the reader then owns diagnostics independently
// and remains readable throughout and after engine destruction. Release readers
// only after their callers finish. Neither acquisition nor reading joins the queue.
PREVIEW_API int32_t __stdcall WrailPreviewAcquireHealth(WrailPreviewEngine* engine, WrailPreviewHealthReader** result) noexcept;
PREVIEW_API void __stdcall WrailPreviewReleaseHealth(WrailPreviewHealthReader* reader) noexcept;
// One bounded attempt. E_PENDING means publication overlapped; result is unchanged.
PREVIEW_API int32_t __stdcall WrailPreviewReadHealth(WrailPreviewHealthReader* reader, WrailPreviewHealth* result) noexcept;
PREVIEW_API int32_t __stdcall WrailPreviewResetDevice(WrailPreviewEngine* engine) noexcept;
PREVIEW_API int32_t __stdcall WrailPreviewReadIdentity(uint64_t window, WrailPreviewTarget* target) noexcept;
}
