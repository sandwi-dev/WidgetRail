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
