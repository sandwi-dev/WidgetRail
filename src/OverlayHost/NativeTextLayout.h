#pragma once

#include "NativeStyle.h"

#include <dwrite.h>
#include <wrl/client.h>

#include <string_view>

namespace gba {

enum class NativeTextVerticalAlignment {
    Start,
    Center,
};

/// One immutable DirectWrite layout used by both intrinsic measurement and
/// paint. Its measured height includes vertical font ink that DirectWrite
/// reports outside the authored line box, while the layout origin keeps that
/// ink inside the measured component box.
struct NativeTextLayoutPlan final {
    Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
    float measuredWidth{};
    float measuredHeight{};
    float layoutWidth{};
    float layoutHeight{};
    float inkInsetTop{};
    float inkInsetBottom{};
    float baseline{};

    [[nodiscard]] bool IsValid() const noexcept { return layout != nullptr; }
    [[nodiscard]] float LayoutOriginY(
        float availableY,
        float availableHeight,
        NativeTextVerticalAlignment alignment) const noexcept;
};

/// Creates the single bounded DirectWrite plan consumed by measurement and
/// paint. Text transformation, font selection, line spacing, character
/// spacing, wrapping, trimming, and ink-overhang accounting cannot diverge
/// between those two phases.
[[nodiscard]] NativeTextLayoutPlan CreateNativeTextLayoutPlan(
    IDWriteFactory* factory,
    std::wstring_view text,
    const NativeRenderStyle& style,
    float maximumWidth,
    float maximumHeight);

} // namespace gba
