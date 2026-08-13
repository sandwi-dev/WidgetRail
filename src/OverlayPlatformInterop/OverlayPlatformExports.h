#pragma once

#if defined(_WIN32)
#define GBA_OVERLAY_PLATFORM_CALL __stdcall
#if defined(GBA_OVERLAY_PLATFORM_EXPORTS)
#define GBA_OVERLAY_PLATFORM_API __declspec(dllexport)
#elif defined(GBA_OVERLAY_PLATFORM_IMPORTS)
#define GBA_OVERLAY_PLATFORM_API __declspec(dllimport)
#else
#define GBA_OVERLAY_PLATFORM_API
#endif
#else
#define GBA_OVERLAY_PLATFORM_CALL
#define GBA_OVERLAY_PLATFORM_API
#endif
