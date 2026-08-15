#include "OverlayChrome.h"

namespace gba::shell {

namespace {

bool SameRect(const RECT& left, const RECT& right) noexcept {
    return left.left == right.left && left.top == right.top &&
        left.right == right.right && left.bottom == right.bottom;
}

} // namespace

TrayInvalidationPlan PlanTrayInvalidation(
    const RetainedTrayState* retained,
    const RetainedTrayState& next) {
    TrayInvalidationPlan plan;
    if (!retained || retained->width != next.width ||
        retained->height != next.height ||
        retained->appearanceRevision != next.appearanceRevision ||
        retained->items.size() != next.items.size()) {
        plan.full = true;
        return plan;
    }
    for (std::size_t index = 0; index < next.items.size(); ++index) {
        const auto& before = retained->items[index];
        const auto& after = next.items[index];
        if (!SameRect(before.bounds, after.bounds)) {
            plan.full = true;
            plan.dirtyRects.clear();
            return plan;
        }
        if (before.identity != after.identity ||
            before.selected != after.selected ||
            before.focused != after.focused) {
            plan.dirtyRects.push_back(after.bounds);
        }
    }
    return plan;
}

void FillColorKeyRoundedRectangle(
    ID2D1RenderTarget* target,
    const D2D1_ROUNDED_RECT& rectangle,
    ID2D1Brush* brush,
    const OuterChromeBoundary boundary) noexcept {
    if (!target || !brush) return;
    if (boundary == OuterChromeBoundary::PremultipliedAlpha) {
        target->FillRoundedRectangle(rectangle, brush);
        return;
    }
    const auto previous = target->GetAntialiasMode();
    target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
    target->FillRoundedRectangle(rectangle, brush);
    target->SetAntialiasMode(previous);
}

} // namespace gba::shell
