#pragma once

#include <cstdint>

namespace gba {

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
