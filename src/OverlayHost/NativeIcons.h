#pragma once

#include <cstdint>
#include <d2d1.h>
#include <string_view>

namespace gba::icons {

// Keep this closed set in lockstep with WidgetProtocol.WidgetGlyph. Widgets
// select a semantic ID; they never provide geometry, fonts, SVG, or paths.
enum class NativeIcon : std::uint8_t {
    Music,
    Play,
    Pause,
    Previous,
    Next,
    Refresh,
    Shuffle,
    Like,
    Dislike,
    Repeat,
    Settings,
    Warning,
    Check,
    Connection,
    Volume,
    Muted,
    Microphone,
};

// Accepts protocol glyph names plus a small closed list of built-in transport
// action IDs. Matching is ordinal and case-sensitive.
[[nodiscard]] bool TryParseNativeIcon(
    std::wstring_view semanticId,
    NativeIcon& icon) noexcept;

// Bounds and stroke are expressed in Direct2D device-independent pixels. The
// icon is optically centered in the largest square inside bounds. Invalid,
// non-finite, or unreasonably large input fails closed without drawing.
[[nodiscard]] bool DrawNativeIcon(
    ID2D1RenderTarget* renderTarget,
    NativeIcon icon,
    D2D1_RECT_F bounds,
    ID2D1Brush* brush,
    float strokeWidth = 2.0F) noexcept;

[[nodiscard]] bool DrawNativeIcon(
    ID2D1RenderTarget* renderTarget,
    NativeIcon icon,
    D2D1_POINT_2F center,
    float size,
    ID2D1Brush* brush,
    float strokeWidth = 2.0F) noexcept;

} // namespace gba::icons
