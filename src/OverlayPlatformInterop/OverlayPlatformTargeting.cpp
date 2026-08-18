#include "../OverlayHost/OverlayTargeting.h"

namespace widgetrail {

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
    if (!candidateIsValid || candidate == 0 ||
        candidate == overlay_ || candidate == backdrop_) {
        return false;
    }
    if (candidate == remembered_) return false;
    remembered_ = candidate;
    return true;
}

std::uintptr_t ForegroundTargetTracker::Resolve(
    const std::uintptr_t fallback,
    const bool rememberedTargetIsValid) const noexcept {
    return remembered_ != 0 && rememberedTargetIsValid
        ? remembered_
        : fallback;
}

} // namespace widgetrail
