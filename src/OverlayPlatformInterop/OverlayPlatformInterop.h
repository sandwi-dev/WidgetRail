#pragma once

#include "OverlayPlatformExports.h"

#include <cstddef>
#include <cstdint>
#include <type_traits>

inline constexpr std::uint32_t WRAIL_OVERLAY_PLATFORM_ABI_VERSION = 4;
inline constexpr std::uint32_t WRAIL_OVERLAY_PLATFORM_FALSE = 0;
inline constexpr std::uint32_t WRAIL_OVERLAY_PLATFORM_TRUE = 1;

enum class WidgetRailOverlayPlatformStatus : std::uint32_t {
    Ok = 0,
    InvalidArgument = 1,
    InvalidVersion = 2,
    NotInitialized = 3,
    ShutDown = 4,
    AllocationFailed = 5,
    ControllerIsolationUnavailable = 6,
};

enum class WidgetRailOverlayPlatformEventKind : std::uint32_t {
    None = 0,
    GuideToggleRequested = 1,
    LegacyGuidePollingChanged = 2,
    VisibilityChanged = 3,
    FocusChanged = 4,
};

enum class WidgetRailOverlayPlatformGuideSource : std::uint32_t {
    None = 0,
    GameInput = 1,
    LegacyCompatibility = 2,
    DualSenseHid = 3,
};

enum class WidgetRailOverlayPlatformNavigationDirection : std::uint32_t {
    None = 0,
    Left = 1,
    Right = 2,
    Up = 3,
    Down = 4,
};

enum class WidgetRailOverlayPlatformNavigationPhase : std::uint32_t {
    None = 0,
    Pressed = 1,
    Repeated = 2,
};

enum class WidgetRailOverlayPlatformReadPath : std::uint32_t {
    None = 0,
    GameInputVisibleLease = 1,
    XInputCompatibility = 2,
    ControllerIsolation = 3,
    DualSenseHid = 4,
    DualSenseIsolation = 5,
};


enum class WidgetRailOverlayPlatformControllerFamily : std::uint32_t {
    Unknown = 0,
    Xbox = 1,
    PlayStation = 2,
};

struct WidgetRailOverlayPlatformEvent final {
    std::uint32_t structSize{sizeof(WidgetRailOverlayPlatformEvent)};
    std::uint32_t abiVersion{WRAIL_OVERLAY_PLATFORM_ABI_VERSION};
    WidgetRailOverlayPlatformEventKind kind{WidgetRailOverlayPlatformEventKind::None};
    WidgetRailOverlayPlatformGuideSource guideSource{
        WidgetRailOverlayPlatformGuideSource::None};
    std::uint64_t timestampMilliseconds{};
    std::uint32_t value{};
};

struct WidgetRailOverlayPlatformRawControllerState final {
    std::uint16_t buttons{};
    std::uint8_t leftTrigger{};
    std::uint8_t rightTrigger{};
    std::int16_t leftThumbX{};
    std::int16_t leftThumbY{};
    std::int16_t rightThumbX{};
    std::int16_t rightThumbY{};
};

struct WidgetRailOverlayPlatformNavigationEvent final {
    WidgetRailOverlayPlatformNavigationDirection direction{
        WidgetRailOverlayPlatformNavigationDirection::None};
    WidgetRailOverlayPlatformNavigationPhase phase{
        WidgetRailOverlayPlatformNavigationPhase::None};
};

struct WidgetRailOverlayPlatformControllerFrame final {
    std::uint32_t structSize{sizeof(WidgetRailOverlayPlatformControllerFrame)};
    std::uint32_t abiVersion{WRAIL_OVERLAY_PLATFORM_ABI_VERSION};
    std::uint32_t connected{};
    std::uint32_t foregroundExclusive{};
    WidgetRailOverlayPlatformReadPath readPath{WidgetRailOverlayPlatformReadPath::None};
    WidgetRailOverlayPlatformRawControllerState state{};
    std::uint16_t pressedButtons{};
    std::uint16_t releasedButtons{};
    std::uint32_t leftTriggerPressed{};
    std::uint32_t leftTriggerReleased{};
    std::uint32_t rightTriggerPressed{};
    std::uint32_t rightTriggerReleased{};
    std::uint32_t recoveryChordPressed{};
    std::uint32_t primed{};
    WidgetRailOverlayPlatformNavigationEvent stickNavigation{};
    WidgetRailOverlayPlatformNavigationEvent dpadNavigation{};
    std::uint32_t remainingFrames{};
    WidgetRailOverlayPlatformControllerFamily lastInputFamily{
        WidgetRailOverlayPlatformControllerFamily::Unknown};
};

struct WidgetRailOverlayPlatformPlacementInput final {
    std::uint32_t structSize{sizeof(WidgetRailOverlayPlatformPlacementInput)};
    std::uint32_t abiVersion{WRAIL_OVERLAY_PLATFORM_ABI_VERSION};
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

struct WidgetRailOverlayPlatformPlacement final {
    std::uint32_t structSize{sizeof(WidgetRailOverlayPlatformPlacement)};
    std::uint32_t abiVersion{WRAIL_OVERLAY_PLATFORM_ABI_VERSION};
    std::int32_t x{};
    std::int32_t y{};
    std::int32_t width{};
    std::int32_t height{};
};

using WidgetRailOverlayPlatformEventAvailable = void(WRAIL_OVERLAY_PLATFORM_CALL*)(
    void* context) noexcept;
using WidgetRailOverlayPlatformDiagnostic = void(WRAIL_OVERLAY_PLATFORM_CALL*)(
    void* context,
    const wchar_t* message) noexcept;

struct WidgetRailOverlayPlatformCreateOptions final {
    std::uint32_t structSize{sizeof(WidgetRailOverlayPlatformCreateOptions)};
    std::uint32_t abiVersion{WRAIL_OVERLAY_PLATFORM_ABI_VERSION};
    void* callbackContext{};
    WidgetRailOverlayPlatformEventAvailable eventAvailable{};
    WidgetRailOverlayPlatformDiagnostic diagnostic{};
};

struct WidgetRailOverlayPlatformHandle;

enum class WidgetRailOverlayPlatformNativeShortcutSource : std::uint32_t {
    Unavailable = 0,
    Shared = 1,
    Isolated = 2,
};

extern "C" {

WRAIL_OVERLAY_PLATFORM_API std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformGetAbiVersion() noexcept;

// Process-start configuration consumed by the subsequently created long-lived
// native owner. It intentionally does not widen the stable create-options ABI.
WRAIL_OVERLAY_PLATFORM_API void WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformConfigureControllerIsolation(std::uint32_t enabled) noexcept;

// Host-only shortcut sampling from the same native physical reader as navigation.
// Shared input supplements GameInput/XInput; isolated input is authoritative.
// Unavailable leaves ordinary readers in use. Buttons use the existing ABI mask.
WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformNativeShortcutSource WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformNativeShortcutButtons(WidgetRailOverlayPlatformHandle* handle,
    std::uint16_t* buttons) noexcept;

// Read-only readiness: bit 0 HidHide, bit 1 ViGEmBus, bit 2 GameInput/Guide.
WRAIL_OVERLAY_PLATFORM_API std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformControllerPrerequisites() noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformSetExclusiveControl(WidgetRailOverlayPlatformHandle* handle, std::uint32_t enabled) noexcept;

// 0 unavailable, 1 off, 2 starting, 3 active, 4 waiting, 5 recovery required,
// 6 setup failed with owned hiding restored and ordinary input selected.
WRAIL_OVERLAY_PLATFORM_API std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformControllerControlState(const WidgetRailOverlayPlatformHandle* handle) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformRecoverControllerIsolation(wchar_t* message, std::uint32_t capacity) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformCreate(
    const WidgetRailOverlayPlatformCreateOptions* options,
    WidgetRailOverlayPlatformHandle** handle) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformInitialize(WidgetRailOverlayPlatformHandle* handle) noexcept;

WRAIL_OVERLAY_PLATFORM_API void WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformShutdown(WidgetRailOverlayPlatformHandle* handle) noexcept;

WRAIL_OVERLAY_PLATFORM_API void WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformDestroy(WidgetRailOverlayPlatformHandle* handle) noexcept;

WRAIL_OVERLAY_PLATFORM_API std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformHasGameInput(const WidgetRailOverlayPlatformHandle* handle) noexcept;

WRAIL_OVERLAY_PLATFORM_API std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformRequiresLegacyGuidePolling(
    const WidgetRailOverlayPlatformHandle* handle) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformSetWindowState(
    WidgetRailOverlayPlatformHandle* handle,
    std::uint32_t visible,
    std::uint32_t focused) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformPrepareVisible(
    WidgetRailOverlayPlatformHandle* handle) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformDrainEvent(
    WidgetRailOverlayPlatformHandle* handle,
    std::uint64_t nowMilliseconds,
    WidgetRailOverlayPlatformEvent* event,
    std::uint32_t* hasEvent) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformPollLegacyGuide(
    WidgetRailOverlayPlatformHandle* handle,
    std::uint64_t nowMilliseconds,
    WidgetRailOverlayPlatformEvent* event,
    std::uint32_t* hasEvent) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformPrimeController(
    WidgetRailOverlayPlatformHandle* handle,
    std::uint32_t foregroundConfirmed,
    std::uint64_t nowMilliseconds) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformReadController(
    WidgetRailOverlayPlatformHandle* handle,
    std::uint32_t foregroundConfirmed,
    std::uint64_t nowMilliseconds,
    WidgetRailOverlayPlatformControllerFrame* frame) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformSetOwnedWindows(
    WidgetRailOverlayPlatformHandle* handle,
    std::uintptr_t overlay,
    std::uintptr_t backdrop) noexcept;

WRAIL_OVERLAY_PLATFORM_API std::uint32_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformObserveForegroundTarget(
    WidgetRailOverlayPlatformHandle* handle,
    std::uintptr_t candidate,
    std::uint32_t candidateIsValid) noexcept;

WRAIL_OVERLAY_PLATFORM_API std::uintptr_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformRememberedForegroundTarget(
    const WidgetRailOverlayPlatformHandle* handle) noexcept;

WRAIL_OVERLAY_PLATFORM_API std::uintptr_t WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformResolveForegroundTarget(
    const WidgetRailOverlayPlatformHandle* handle,
    std::uintptr_t fallback,
    std::uint32_t rememberedTargetIsValid) noexcept;

WRAIL_OVERLAY_PLATFORM_API WidgetRailOverlayPlatformStatus WRAIL_OVERLAY_PLATFORM_CALL
WidgetRailOverlayPlatformComputePlacement(
    const WidgetRailOverlayPlatformPlacementInput* input,
    WidgetRailOverlayPlatformPlacement* placement,
    std::uint32_t* hasPlacement) noexcept;

}

static_assert(std::is_standard_layout_v<WidgetRailOverlayPlatformEvent>);
static_assert(std::is_standard_layout_v<WidgetRailOverlayPlatformRawControllerState>);
static_assert(std::is_standard_layout_v<WidgetRailOverlayPlatformNavigationEvent>);
static_assert(std::is_standard_layout_v<WidgetRailOverlayPlatformControllerFrame>);
static_assert(std::is_standard_layout_v<WidgetRailOverlayPlatformPlacementInput>);
static_assert(std::is_standard_layout_v<WidgetRailOverlayPlatformPlacement>);
static_assert(std::is_standard_layout_v<WidgetRailOverlayPlatformCreateOptions>);
static_assert(sizeof(WidgetRailOverlayPlatformEvent) == 32);
static_assert(sizeof(WidgetRailOverlayPlatformRawControllerState) == 12);
static_assert(sizeof(WidgetRailOverlayPlatformNavigationEvent) == 8);
static_assert(sizeof(WidgetRailOverlayPlatformControllerFrame) == 84);
static_assert(sizeof(WidgetRailOverlayPlatformPlacementInput) == 48);
static_assert(sizeof(WidgetRailOverlayPlatformPlacement) == 24);
static_assert(sizeof(WidgetRailOverlayPlatformCreateOptions) == 32);
static_assert(offsetof(WidgetRailOverlayPlatformEvent, timestampMilliseconds) == 16);
static_assert(offsetof(WidgetRailOverlayPlatformControllerFrame, state) == 20);
static_assert(offsetof(WidgetRailOverlayPlatformControllerFrame, stickNavigation) == 60);
static_assert(offsetof(WidgetRailOverlayPlatformCreateOptions, callbackContext) == 8);

static_assert(offsetof(WidgetRailOverlayPlatformControllerFrame, lastInputFamily) == 80);
