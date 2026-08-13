#pragma once

#include "OverlayPlatformExports.h"

#include <cstddef>
#include <cstdint>

inline constexpr std::uint32_t GBA_OVERLAY_PLATFORM_ABI_VERSION = 1;

enum class GbaOverlayPlatformStatus : std::uint32_t {
    Ok = 0,
    InvalidArgument = 1,
    InvalidVersion = 2,
    NotInitialized = 3,
    ShutDown = 4,
    AllocationFailed = 5,
};

enum class GbaOverlayPlatformEventKind : std::uint32_t {
    None = 0,
    GuideToggleRequested = 1,
    LegacyGuidePollingChanged = 2,
    VisibilityChanged = 3,
    FocusChanged = 4,
};

enum class GbaOverlayPlatformGuideSource : std::uint32_t {
    None = 0,
    GameInput = 1,
    LegacyCompatibility = 2,
};

enum class GbaOverlayPlatformNavigationDirection : std::uint32_t {
    None = 0,
    Left = 1,
    Right = 2,
    Up = 3,
    Down = 4,
};

enum class GbaOverlayPlatformNavigationPhase : std::uint32_t {
    None = 0,
    Pressed = 1,
    Repeated = 2,
};

enum class GbaOverlayPlatformReadPath : std::uint32_t {
    None = 0,
    GameInputVisibleLease = 1,
    XInputCompatibility = 2,
};

struct GbaOverlayPlatformEvent final {
    std::uint32_t structSize{sizeof(GbaOverlayPlatformEvent)};
    std::uint32_t abiVersion{GBA_OVERLAY_PLATFORM_ABI_VERSION};
    GbaOverlayPlatformEventKind kind{GbaOverlayPlatformEventKind::None};
    GbaOverlayPlatformGuideSource guideSource{
        GbaOverlayPlatformGuideSource::None};
    std::uint64_t timestampMilliseconds{};
    std::uint32_t value{};
};

struct GbaOverlayPlatformRawControllerState final {
    std::uint16_t buttons{};
    std::uint8_t leftTrigger{};
    std::uint8_t rightTrigger{};
    std::int16_t leftThumbX{};
    std::int16_t leftThumbY{};
    std::int16_t rightThumbX{};
    std::int16_t rightThumbY{};
};

struct GbaOverlayPlatformNavigationEvent final {
    GbaOverlayPlatformNavigationDirection direction{
        GbaOverlayPlatformNavigationDirection::None};
    GbaOverlayPlatformNavigationPhase phase{
        GbaOverlayPlatformNavigationPhase::None};
};

struct GbaOverlayPlatformControllerFrame final {
    std::uint32_t structSize{sizeof(GbaOverlayPlatformControllerFrame)};
    std::uint32_t abiVersion{GBA_OVERLAY_PLATFORM_ABI_VERSION};
    bool connected{};
    bool foregroundExclusive{};
    GbaOverlayPlatformReadPath readPath{GbaOverlayPlatformReadPath::None};
    GbaOverlayPlatformRawControllerState state{};
    std::uint16_t pressedButtons{};
    std::uint16_t releasedButtons{};
    bool leftTriggerPressed{};
    bool leftTriggerReleased{};
    bool rightTriggerPressed{};
    bool rightTriggerReleased{};
    bool recoveryChordPressed{};
    bool primed{};
    GbaOverlayPlatformNavigationEvent stickNavigation{};
    GbaOverlayPlatformNavigationEvent dpadNavigation{};
};

struct GbaOverlayPlatformPlacementInput final {
    std::uint32_t structSize{sizeof(GbaOverlayPlatformPlacementInput)};
    std::uint32_t abiVersion{GBA_OVERLAY_PLATFORM_ABI_VERSION};
    int workLeft{};
    int workTop{};
    int workRight{};
    int workBottom{};
    std::uint32_t dpi{96};
    float desiredWidthDip{};
    float desiredHeightDip{};
    float sideMarginDip{24.0F};
    float topMarginDip{24.0F};
    float bottomMarginDip{32.0F};
};

struct GbaOverlayPlatformPlacement final {
    std::uint32_t structSize{sizeof(GbaOverlayPlatformPlacement)};
    std::uint32_t abiVersion{GBA_OVERLAY_PLATFORM_ABI_VERSION};
    int x{};
    int y{};
    int width{};
    int height{};
};

using GbaOverlayPlatformEventAvailable = void(GBA_OVERLAY_PLATFORM_CALL*)(
    void* context) noexcept;
using GbaOverlayPlatformDiagnostic = void(GBA_OVERLAY_PLATFORM_CALL*)(
    void* context,
    const wchar_t* message) noexcept;

struct GbaOverlayPlatformCreateOptions final {
    std::uint32_t structSize{sizeof(GbaOverlayPlatformCreateOptions)};
    std::uint32_t abiVersion{GBA_OVERLAY_PLATFORM_ABI_VERSION};
    void* callbackContext{};
    GbaOverlayPlatformEventAvailable eventAvailable{};
    GbaOverlayPlatformDiagnostic diagnostic{};
};

struct GbaOverlayPlatformHandle;

extern "C" {

GBA_OVERLAY_PLATFORM_API std::uint32_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformGetAbiVersion() noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformCreate(
    const GbaOverlayPlatformCreateOptions* options,
    GbaOverlayPlatformHandle** handle) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformInitialize(GbaOverlayPlatformHandle* handle) noexcept;

GBA_OVERLAY_PLATFORM_API void GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformShutdown(GbaOverlayPlatformHandle* handle) noexcept;

GBA_OVERLAY_PLATFORM_API void GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformDestroy(GbaOverlayPlatformHandle* handle) noexcept;

GBA_OVERLAY_PLATFORM_API bool GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformHasGameInput(const GbaOverlayPlatformHandle* handle) noexcept;

GBA_OVERLAY_PLATFORM_API bool GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformRequiresLegacyGuidePolling(
    const GbaOverlayPlatformHandle* handle) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformSetWindowState(
    GbaOverlayPlatformHandle* handle,
    bool visible,
    bool focused) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformDrainEvent(
    GbaOverlayPlatformHandle* handle,
    std::uint64_t nowMilliseconds,
    GbaOverlayPlatformEvent* event,
    bool* hasEvent) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformPollLegacyGuide(
    GbaOverlayPlatformHandle* handle,
    std::uint64_t nowMilliseconds,
    GbaOverlayPlatformEvent* event,
    bool* hasEvent) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformPrimeController(
    GbaOverlayPlatformHandle* handle,
    bool foregroundConfirmed,
    std::uint64_t nowMilliseconds) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformReadController(
    GbaOverlayPlatformHandle* handle,
    bool foregroundConfirmed,
    std::uint64_t nowMilliseconds,
    GbaOverlayPlatformControllerFrame* frame) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformSetOwnedWindows(
    GbaOverlayPlatformHandle* handle,
    std::uintptr_t overlay,
    std::uintptr_t backdrop) noexcept;

GBA_OVERLAY_PLATFORM_API bool GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformObserveForegroundTarget(
    GbaOverlayPlatformHandle* handle,
    std::uintptr_t candidate,
    bool candidateIsValid) noexcept;

GBA_OVERLAY_PLATFORM_API std::uintptr_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformRememberedForegroundTarget(
    const GbaOverlayPlatformHandle* handle) noexcept;

GBA_OVERLAY_PLATFORM_API std::uintptr_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformResolveForegroundTarget(
    const GbaOverlayPlatformHandle* handle,
    std::uintptr_t fallback,
    bool rememberedTargetIsValid) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformComputePlacement(
    const GbaOverlayPlatformPlacementInput* input,
    GbaOverlayPlatformPlacement* placement,
    bool* hasPlacement) noexcept;

}
