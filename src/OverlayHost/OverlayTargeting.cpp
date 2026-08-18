#include "OverlayTargeting.h"

namespace widgetrail {

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

} // namespace widgetrail
