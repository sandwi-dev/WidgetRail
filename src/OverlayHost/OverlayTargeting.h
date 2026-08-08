#pragma once

#include <cstdint>

namespace gba {

struct OverlayPresentationExtent final {
    int widthDip{};
    int heightDip{};

    [[nodiscard]] friend constexpr bool operator==(
        const OverlayPresentationExtent&,
        const OverlayPresentationExtent&) noexcept = default;
};

enum class OverlayPresentationDirective {
    None,
    Hide,
    Repaint,
    Place,
};

enum class DisplayEnvironmentChange {
    Dpi,
    Topology,
    SystemSettings,
};

struct DisplayRefreshPlan final {
    bool recreateGraphics{};
    bool reapplyAppearance{};
    bool repositionWindows{};

    [[nodiscard]] friend constexpr bool operator==(
        const DisplayRefreshPlan&,
        const DisplayRefreshPlan&) noexcept = default;
};

// Pure policy for Win32 display notifications. Hidden windows resolve current
// state when next shown. Visible windows always re-read monitor/work-area/DPI;
// system settings additionally reapply accessibility and theme policy.
[[nodiscard]] constexpr DisplayRefreshPlan DecideDisplayRefresh(
    const bool visible,
    const DisplayEnvironmentChange change) noexcept {
    if (!visible) return {};
    return {true, change == DisplayEnvironmentChange::SystemSettings, true};
}

// Display migration is reported as a burst on real systems: one monitor move
// can synchronously produce DPI, topology, size, and work-area notifications.
// Applying each notification independently exposes intermediate geometry and
// repeatedly destroys the render target. This accumulator merges all pending
// work into one posted message-loop refresh without polling or dropping the
// stronger appearance refresh requested by WM_SETTINGCHANGE.
class DisplayRefreshAccumulator final {
public:
    // Returns true only when the caller must post a refresh message. Further
    // notifications merge into the already-posted unit of work.
    [[nodiscard]] bool Enqueue(
        bool visible,
        DisplayEnvironmentChange change) noexcept;

    // Consumes the merged plan and permits a later notification to schedule a
    // fresh message. A queued plan may safely be ignored if the overlay became
    // hidden before delivery; its next show resolves the environment afresh.
    [[nodiscard]] DisplayRefreshPlan Take() noexcept;

private:
    bool scheduled_{};
    DisplayRefreshPlan pending_{};
};

// Pure presentation policy shared by state transitions and asynchronous
// snapshot refreshes. A visible HWND only needs another placement pass when
// its requested logical extent or monitor target changed. Selection, focus,
// and content-only changes are repaints; repeatedly calling SetWindowPos with
// SWP_SHOWWINDOW for those changes causes unnecessary DWM/Direct2D churn.
[[nodiscard]] constexpr OverlayPresentationDirective DecideOverlayPresentation(
    const bool wasVisible,
    const bool isVisible,
    const OverlayPresentationExtent before,
    const OverlayPresentationExtent after,
    const bool targetChanged = false) noexcept {
    if (!isVisible) {
        return wasVisible
            ? OverlayPresentationDirective::Hide
            : OverlayPresentationDirective::None;
    }
    if (!wasVisible || targetChanged || before != after) {
        return OverlayPresentationDirective::Place;
    }
    return OverlayPresentationDirective::Repaint;
}

/// A resize or monitor move of an already visible HWND must commit its new
/// frame before returning to the message loop; otherwise DWM can briefly show
/// the newly exposed client strip. Initial show and repaint-only transitions
/// do not need this synchronous path.
[[nodiscard]] constexpr bool ShouldCommitVisiblePlacementSynchronously(
    const bool wasWindowVisible,
    const OverlayPresentationDirective directive) noexcept {
    return wasWindowVisible && directive == OverlayPresentationDirective::Place;
}

// Pure foreground-target state used by the HWND host and deterministic tests.
// Native window validity remains an OS concern supplied at each boundary.
class ForegroundTargetTracker final {
public:
    void SetOwnedWindows(std::uintptr_t overlay, std::uintptr_t backdrop) noexcept;

    // Returns true only when a valid external foreground target changed.
    bool Observe(std::uintptr_t candidate, bool candidateIsValid) noexcept;

    // Uses the remembered external target while it remains valid, otherwise
    // falls back without erasing history needed for a later focus restore.
    [[nodiscard]] std::uintptr_t Resolve(
        std::uintptr_t fallback,
        bool rememberedTargetIsValid) const noexcept;

    [[nodiscard]] std::uintptr_t remembered() const noexcept { return remembered_; }

private:
    std::uintptr_t overlay_{};
    std::uintptr_t backdrop_{};
    std::uintptr_t remembered_{};
};

// Prevents synchronous WM_DPICHANGED/WM_SIZE dispatch from recursively
// entering placement while SetWindowPos is still applying the first move.
// A single deferred refresh preserves the latest display state.
class PlacementRefreshGate final {
public:
    [[nodiscard]] bool TryEnter() noexcept;
    [[nodiscard]] bool Complete() noexcept;

private:
    bool active_{};
    bool pending_{};
};

} // namespace gba
