#include "OverlayChrome.h"

namespace gba::shell {

void FillColorKeyRoundedRectangle(
    ID2D1RenderTarget* target,
    const D2D1_ROUNDED_RECT& rectangle,
    ID2D1Brush* brush) noexcept {
    if (!target || !brush) return;
    const auto previous = target->GetAntialiasMode();
    target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
    target->FillRoundedRectangle(rectangle, brush);
    target->SetAntialiasMode(previous);
}

} // namespace gba::shell
