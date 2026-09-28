#pragma once
#include "OverlayPlatformExports.h"
#include <cstdint>

// Independent, additive ABI. No controller/platform session is constructed.
inline constexpr std::uint32_t WRAIL_PROCESS_ABI_VERSION = 1;
enum class WidgetRailProcessResult : std::uint32_t { Owner, Redirected, Failed, AlreadyRunning };
enum class WidgetRailProcessStatus : std::uint32_t { Ok, InvalidArgument, WrongThread };
extern "C" {
WRAIL_OVERLAY_PLATFORM_API WidgetRailProcessResult WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailProcessBegin(std::uint32_t version, const wchar_t* profile, std::uint32_t profileLength,
    std::uint32_t showExisting, std::uint32_t timeoutMilliseconds, void** owner,
    wchar_t* error, std::uint32_t errorCapacity) noexcept;
WRAIL_OVERLAY_PLATFORM_API WidgetRailProcessStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailProcessBindWindow(void* owner, std::uintptr_t window, std::uint32_t message) noexcept;
// Must run on the same OS thread as Begin, after input/Bridge teardown.
WRAIL_OVERLAY_PLATFORM_API WidgetRailProcessStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailProcessEnd(void* owner) noexcept;
}
