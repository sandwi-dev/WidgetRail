#pragma once

#include <cstdint>

namespace widgetrail::input {

// The strongest controller ownership a normal desktop process can request is
// GameInput's foreground-exclusive policy. It is deliberately not described
// as universal capture: XInput, Raw Input, HID, and remapping drivers are
// separate delivery paths outside GameInput's process arbitration.
enum class ControllerReadPath {
    None,
    GameInputVisibleLease,
    XInputCompatibility,
};

struct ControllerInputOwnershipDecision final {
    ControllerReadPath readPath{ControllerReadPath::None};
    bool foregroundExclusive{};

    [[nodiscard]] friend constexpr bool operator==(
        const ControllerInputOwnershipDecision&,
        const ControllerInputOwnershipDecision&) noexcept = default;
};

// A visible overlay owns a local read lease from show until hide. GameInput's
// background-input focus policy keeps that lease reliable if Windows declines
// activation; foreground exclusivity is an additional best-effort property,
// not a prerequisite for navigation. The host attempts activation once during
// ShowOverlay and never runs a foreground-steal loop from this decision.
[[nodiscard]] constexpr ControllerInputOwnershipDecision
DecideControllerInputOwnership(
    const bool overlayVisible,
    const bool visibleReadLease,
    const bool foregroundConfirmed,
    const bool gameInputAvailable,
    const bool gameInputReadingAvailable = true) noexcept {
    if (!overlayVisible || !visibleReadLease) return {};
    if (gameInputAvailable && gameInputReadingAvailable) {
        return {ControllerReadPath::GameInputVisibleLease, foregroundConfirmed};
    }
    return {ControllerReadPath::XInputCompatibility, false};
}

struct ForegroundAcquisitionPlan final {
    bool attemptDirect{};
    bool attachForegroundThread{};

    [[nodiscard]] friend constexpr bool operator==(
        const ForegroundAcquisitionPlan&,
        const ForegroundAcquisitionPlan&) noexcept = default;
};

// AttachThreadInput is a single bounded fallback, never a loop. There is no
// benefit (and substantial risk) in attaching a thread to itself or to a
// missing foreground queue.
[[nodiscard]] constexpr ForegroundAcquisitionPlan PlanForegroundAcquisition(
    const bool alreadyForeground,
    const std::uint32_t overlayThreadId,
    const std::uint32_t foregroundThreadId) noexcept {
    if (alreadyForeground) return {};
    return {
        true,
        overlayThreadId != 0 && foregroundThreadId != 0 &&
            overlayThreadId != foregroundThreadId,
    };
}

enum class VisibleForegroundTransition {
    Ignore,
    CloseOverlay,
};

// WinEvent callbacks can race with our own backdrop/overlay activation. Only a
// valid foreground window owned by another process is evidence of Alt+Tab (or
// an equivalent user switch) and closes the visible overlay.
[[nodiscard]] constexpr VisibleForegroundTransition
DecideVisibleForegroundTransition(
    const bool overlayVisible,
    const bool candidateIsValid,
    const bool candidateOwnedByOverlayProcess,
    const bool notificationIsCurrent = true) noexcept {
    return overlayVisible && candidateIsValid && !candidateOwnedByOverlayProcess && notificationIsCurrent
        ? VisibleForegroundTransition::CloseOverlay
        : VisibleForegroundTransition::Ignore;
}

enum class BasicKeyboardAction {
    None,
    NavigateLeft,
    NavigateRight,
    NavigateUp,
    NavigateDown,
    Activate,
    Back,
};

// These are the stable Win32 virtual-key values. The pure seam keeps keyboard
// parity testable without turning keyboard-only shortcuts into public hints.
[[nodiscard]] constexpr BasicKeyboardAction ResolveBasicKeyboardAction(
    const std::uint32_t virtualKey) noexcept {
    switch (virtualKey) {
    case 0x25: return BasicKeyboardAction::NavigateLeft;  // VK_LEFT
    case 0x27: return BasicKeyboardAction::NavigateRight; // VK_RIGHT
    case 0x26: return BasicKeyboardAction::NavigateUp;    // VK_UP
    case 0x28: return BasicKeyboardAction::NavigateDown;  // VK_DOWN
    case 0x0D: return BasicKeyboardAction::Activate;      // VK_RETURN
    case 0x1B: return BasicKeyboardAction::Back;          // VK_ESCAPE
    default: return BasicKeyboardAction::None;
    }
}

} // namespace widgetrail::input
