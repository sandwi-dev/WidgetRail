#pragma once

#if defined(_WIN32)
#define WRAIL_OVERLAY_PLATFORM_CALL __stdcall
#if defined(WRAIL_OVERLAY_PLATFORM_EXPORTS)
#define WRAIL_OVERLAY_PLATFORM_API __declspec(dllexport)
#elif defined(WRAIL_OVERLAY_PLATFORM_IMPORTS)
#define WRAIL_OVERLAY_PLATFORM_API __declspec(dllimport)
#else
#define WRAIL_OVERLAY_PLATFORM_API
#endif
#else
#define WRAIL_OVERLAY_PLATFORM_CALL
#define WRAIL_OVERLAY_PLATFORM_API
#endif
