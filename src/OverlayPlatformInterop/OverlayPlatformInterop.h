#pragma once

#include "OverlayPlatformExports.h"

#include <cstddef>
#include <cstdint>
#include <type_traits>

inline constexpr std::uint32_t GBA_OVERLAY_PLATFORM_ABI_VERSION = 1;
inline constexpr std::uint32_t GBA_OVERLAY_PLATFORM_FALSE = 0;
inline constexpr std::uint32_t GBA_OVERLAY_PLATFORM_TRUE = 1;

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
    std::uint32_t connected{};
    std::uint32_t foregroundExclusive{};
    GbaOverlayPlatformReadPath readPath{GbaOverlayPlatformReadPath::None};
    GbaOverlayPlatformRawControllerState state{};
    std::uint16_t pressedButtons{};
    std::uint16_t releasedButtons{};
    std::uint32_t leftTriggerPressed{};
    std::uint32_t leftTriggerReleased{};
    std::uint32_t rightTriggerPressed{};
    std::uint32_t rightTriggerReleased{};
    std::uint32_t recoveryChordPressed{};
    std::uint32_t primed{};
    GbaOverlayPlatformNavigationEvent stickNavigation{};
    GbaOverlayPlatformNavigationEvent dpadNavigation{};
};

struct GbaOverlayPlatformPlacementInput final {
    std::uint32_t structSize{sizeof(GbaOverlayPlatformPlacementInput)};
    std::uint32_t abiVersion{GBA_OVERLAY_PLATFORM_ABI_VERSION};
    std::int32_t workLeft{};
    std::int32_t workTop{};
    std::int32_t workRight{};
    std::int32_t workBottom{};
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
    std::int32_t x{};
    std::int32_t y{};
    std::int32_t width{};
    std::int32_t height{};
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

GBA_OVERLAY_PLATFORM_API std::uint32_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformHasGameInput(const GbaOverlayPlatformHandle* handle) noexcept;

GBA_OVERLAY_PLATFORM_API std::uint32_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformRequiresLegacyGuidePolling(
    const GbaOverlayPlatformHandle* handle) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformSetWindowState(
    GbaOverlayPlatformHandle* handle,
    std::uint32_t visible,
    std::uint32_t focused) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformDrainEvent(
    GbaOverlayPlatformHandle* handle,
    std::uint64_t nowMilliseconds,
    GbaOverlayPlatformEvent* event,
    std::uint32_t* hasEvent) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformPollLegacyGuide(
    GbaOverlayPlatformHandle* handle,
    std::uint64_t nowMilliseconds,
    GbaOverlayPlatformEvent* event,
    std::uint32_t* hasEvent) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformPrimeController(
    GbaOverlayPlatformHandle* handle,
    std::uint32_t foregroundConfirmed,
    std::uint64_t nowMilliseconds) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformReadController(
    GbaOverlayPlatformHandle* handle,
    std::uint32_t foregroundConfirmed,
    std::uint64_t nowMilliseconds,
    GbaOverlayPlatformControllerFrame* frame) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformSetOwnedWindows(
    GbaOverlayPlatformHandle* handle,
    std::uintptr_t overlay,
    std::uintptr_t backdrop) noexcept;

GBA_OVERLAY_PLATFORM_API std::uint32_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformObserveForegroundTarget(
    GbaOverlayPlatformHandle* handle,
    std::uintptr_t candidate,
    std::uint32_t candidateIsValid) noexcept;

GBA_OVERLAY_PLATFORM_API std::uintptr_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformRememberedForegroundTarget(
    const GbaOverlayPlatformHandle* handle) noexcept;

GBA_OVERLAY_PLATFORM_API std::uintptr_t GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformResolveForegroundTarget(
    const GbaOverlayPlatformHandle* handle,
    std::uintptr_t fallback,
    std::uint32_t rememberedTargetIsValid) noexcept;

GBA_OVERLAY_PLATFORM_API GbaOverlayPlatformStatus GBA_OVERLAY_PLATFORM_CALL
GbaOverlayPlatformComputePlacement(
    const GbaOverlayPlatformPlacementInput* input,
    GbaOverlayPlatformPlacement* placement,
    std::uint32_t* hasPlacement) noexcept;

}

static_assert(std::is_standard_layout_v<GbaOverlayPlatformEvent>);
static_assert(std::is_standard_layout_v<GbaOverlayPlatformRawControllerState>);
static_assert(std::is_standard_layout_v<GbaOverlayPlatformNavigationEvent>);
static_assert(std::is_standard_layout_v<GbaOverlayPlatformControllerFrame>);
static_assert(std::is_standard_layout_v<GbaOverlayPlatformPlacementInput>);
static_assert(std::is_standard_layout_v<GbaOverlayPlatformPlacement>);
static_assert(std::is_standard_layout_v<GbaOverlayPlatformCreateOptions>);
static_assert(sizeof(GbaOverlayPlatformEvent) == 32);
static_assert(sizeof(GbaOverlayPlatformRawControllerState) == 12);
static_assert(sizeof(GbaOverlayPlatformNavigationEvent) == 8);
static_assert(sizeof(GbaOverlayPlatformControllerFrame) == 76);
static_assert(sizeof(GbaOverlayPlatformPlacementInput) == 48);
static_assert(sizeof(GbaOverlayPlatformPlacement) == 24);
static_assert(sizeof(GbaOverlayPlatformCreateOptions) == 32);
static_assert(offsetof(GbaOverlayPlatformEvent, timestampMilliseconds) == 16);
static_assert(offsetof(GbaOverlayPlatformControllerFrame, state) == 20);
static_assert(offsetof(GbaOverlayPlatformControllerFrame, stickNavigation) == 60);
static_assert(offsetof(GbaOverlayPlatformCreateOptions, callbackContext) == 8);
