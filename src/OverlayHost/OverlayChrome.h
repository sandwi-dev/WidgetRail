#pragma once

#include <d2d1.h>

namespace gba::shell {

/// A color-keyed layered HWND cannot represent partially transparent pixels at
/// its outer boundary: antialiasing blends authored chrome into the key color
/// and turns the blend into an opaque dark fringe. Paint only the outer shell
/// boundary aliased; inner widget content keeps its ordinary antialiased,
/// rounded clip.
void FillColorKeyRoundedRectangle(
    ID2D1RenderTarget* target,
    const D2D1_ROUNDED_RECT& rectangle,
    ID2D1Brush* brush) noexcept;

} // namespace gba::shell
