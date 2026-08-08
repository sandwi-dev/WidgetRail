#include "OverlayTargeting.h"

namespace gba {

bool DisplayRefreshAccumulator::Enqueue(
    const bool visible,
    const DisplayEnvironmentChange change) noexcept {
    const auto next = DecideDisplayRefresh(visible, change);
    if (!next.recreateGraphics && !next.reapplyAppearance &&
        !next.repositionWindows) {
        return false;
    }
    pending_.recreateGraphics |= next.recreateGraphics;
    pending_.reapplyAppearance |= next.reapplyAppearance;
    pending_.repositionWindows |= next.repositionWindows;
    if (scheduled_) return false;
    scheduled_ = true;
    return true;
}

DisplayRefreshPlan DisplayRefreshAccumulator::Take() noexcept {
    const auto result = pending_;
    pending_ = {};
    scheduled_ = false;
    return result;
}

void ForegroundTargetTracker::SetOwnedWindows(
    const std::uintptr_t overlay,
    const std::uintptr_t backdrop) noexcept {
    overlay_ = overlay;
    backdrop_ = backdrop;
    if (remembered_ == overlay_ || remembered_ == backdrop_) remembered_ = 0;
}

bool ForegroundTargetTracker::Observe(
    const std::uintptr_t candidate,
    const bool candidateIsValid) noexcept {
    if (!candidateIsValid || candidate == 0 || candidate == overlay_ || candidate == backdrop_) {
        return false;
    }
    if (candidate == remembered_) return false;
    remembered_ = candidate;
    return true;
}

std::uintptr_t ForegroundTargetTracker::Resolve(
    const std::uintptr_t fallback,
    const bool rememberedTargetIsValid) const noexcept {
    return remembered_ != 0 && rememberedTargetIsValid ? remembered_ : fallback;
}

bool PlacementRefreshGate::TryEnter() noexcept {
    if (active_) {
        pending_ = true;
        return false;
    }
    active_ = true;
    return true;
}

bool PlacementRefreshGate::Complete() noexcept {
    active_ = false;
    const bool result = pending_;
    pending_ = false;
    return result;
}

} // namespace gba
