#include "NativeStyle.h"

#include <cassert>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <limits>

namespace {

gba::WidgetStyleValue Length(double number, std::wstring unit) {
    return {L"length", std::to_wstring(number) + unit, number, std::move(unit)};
}

gba::WidgetStyleValue Number(std::wstring kind, double number) {
    return {std::move(kind), std::to_wstring(number), number, {}};
}

void Near(float expected, float actual) {
    if (std::abs(expected - actual) >= 0.01F) {
        std::cerr << "Expected " << expected << ", got " << actual << '\n';
        std::abort();
    }
}

} // namespace

int main() {
    using namespace gba;

    WidgetComputedStyle responsive{
        {L"width", Length(50, L"vw")},
        {L"height", Length(25, L"vh")},
        {L"min-width", Length(50, L"%")},
        {L"font-size", Length(1.5, L"em")},
        {L"letter-spacing", Length(0.25, L"rem")},
        {L"padding", {L"lengthList", L"10px 5% 2vh 1em", std::nullopt, {}}},
        {L"background", {L"color", L"#20406080", std::nullopt, {}}},
        {L"direction", {L"keyword", L"row", std::nullopt, {}}},
        {L"object-fit", {L"keyword", L"cover", std::nullopt, {}}},
        {L"flex-grow", Number(L"number", 2)},
    };
    NativeStyleContext context720{1280, 720, 1000, 600, 20, 16, false};
    auto style720 = NativeStyleAdapter::Adapt(responsive, context720).style;
    if (style720.maxLines() != 1) {
        std::cerr << "Default max-lines must keep text visible\n";
        std::abort();
    }
    Near(640, *style720.widthPx());
    Near(180, *style720.heightPx());
    Near(500, *style720.minWidthPx());
    Near(30, style720.fontSizePx());
    Near(4, style720.letterSpacingPx());
    Near(10, style720.paddingPx().top);
    Near(50, style720.paddingPx().right);
    Near(14.4F, style720.paddingPx().bottom);
    Near(20, style720.paddingPx().left);
    assert(style720.direction() == NativeDirection::Row);
    assert(style720.imageFit() == NativeImageFit::Cover);
    Near(2, style720.flexGrow());
    Near(0x20 / 255.0F, style720.background()->red);
    Near(0x80 / 255.0F, style720.background()->alpha);

    NativeStyleContext context1080{1920, 1080, 1200, 800, 16, 16, false};
    auto style1080 = NativeStyleAdapter::Adapt(responsive, context1080).style;
    Near(960, *style1080.widthPx());
    Near(270, *style1080.heightPx());

    NativeStyleContext ultrawide{3440, 1440, 2000, 1000, 18, 16, false};
    auto styleUltra = NativeStyleAdapter::Adapt(responsive, ultrawide).style;
    Near(1720, *styleUltra.widthPx());
    Near(360, *styleUltra.heightPx());

    WidgetComputedStyle accessible{
        {L"background", {L"color", L"rgba(20, 30, 40, 0.2)", std::nullopt, {}}},
        {L"color", {L"color", L"#333333", std::nullopt, {}}},
        {L"outline-width", Length(0, L"px")},
        {L"opacity", Number(L"number", 0.2)},
        {L"background-blur", Length(40, L"px")},
        {L"transition-duration", Number(L"duration", 900)},
    };
    NativeAccessibilityPolicy policy;
    policy.reducedTransparency = true;
    policy.reducedMotion = true;
    policy.minimumFocusRingPx = 3;
    policy.contrastHook = [](NativeColor, NativeColor) { return NativeColor{1, 1, 0, 1}; };
    auto accessibleStyle = NativeStyleAdapter::Adapt(accessible,
        NativeStyleContext{1920, 1080, 800, 400, 16, 16, true}, policy).style;
    Near(1, accessibleStyle.opacity());
    Near(0, accessibleStyle.backgroundBlurPx());
    Near(0, accessibleStyle.transitionDurationMilliseconds());
    Near(1, accessibleStyle.background()->alpha);
    Near(3, accessibleStyle.outlineWidthPx());
    assert((accessibleStyle.outlineColor() == NativeColor{1, 1, 0, 1}));

    WidgetComputedStyle zoomedText{
        {L"font-size", Length(20, L"px")},
        {L"letter-spacing", Length(2, L"px")},
    };
    NativeAccessibilityPolicy textZoom;
    textZoom.textScale = 1.5F;
    const auto zoomedStyle = NativeStyleAdapter::Adapt(
        zoomedText,
        NativeStyleContext{1920, 1080, 800, 400, 16, 16, false},
        textZoom);
    Near(30, zoomedStyle.style.fontSizePx());
    Near(3, zoomedStyle.style.letterSpacingPx());

    NativeAccessibilityPolicy invalidTextZoom;
    invalidTextZoom.textScale = std::numeric_limits<float>::infinity();
    const auto safeTextZoom = NativeStyleAdapter::Adapt(
        zoomedText,
        NativeStyleContext{1920, 1080, 800, 400, 16, 16, false},
        invalidTextZoom);
    Near(20, safeTextZoom.style.fontSizePx());
    assert(!safeTextZoom.diagnostics.empty());

    WidgetComputedStyle malformed{
        {L"width", {L"length", L"nanpx", std::numeric_limits<double>::quiet_NaN(), L"px"}},
        {L"padding", {L"lengthList", L"1px 2px 3px 4px 5px", std::nullopt, {}}},
        {L"background", {L"color", L"url(evil)", std::nullopt, {}}},
        {L"opacity", Number(L"number", 99)},
        {L"font-family", {L"fontFamily", L"bad\nfont", std::nullopt, {}}},
        {L"future-property", {L"keyword", L"surprise", std::nullopt, {}}},
    };
    auto defensive = NativeStyleAdapter::Adapt(malformed,
        NativeStyleContext{-1, 0, std::numeric_limits<float>::infinity(), 0, 0, 0, false});
    assert(!defensive.style.widthPx());
    assert(!defensive.style.background());
    Near(1, defensive.style.opacity());
    assert(defensive.style.fontFamily() == L"Segoe UI Variable Text");
    assert(defensive.diagnostics.size() >= 6);

    // Platform shell roles use the same immutable adapter as widget nodes.
    // Exercise the primitives consumed by OverlayHost so themes cannot bypass
    // its bounds or accessibility policy.
    WidgetComputedStyle shellPanel{
        {L"background", {L"color", L"#18202bcc", std::nullopt, {}}},
        {L"color", {L"color", L"#f4f7ff", std::nullopt, {}}},
        {L"corner-radius", Length(18, L"px")},
        {L"outline-width", Length(1.5, L"px")},
        {L"outline-color", {L"color", L"#68a8ff", std::nullopt, {}}},
        {L"font-size", Length(24, L"px")},
        {L"font-weight", Number(L"integer", 650)},
        {L"font-family", {L"fontFamily", L"Segoe UI Variable Display", std::nullopt, {}}},
    };
    const auto shellResult = NativeStyleAdapter::Adapt(
        shellPanel, NativeStyleContext{1180, 700, 1180, 700, 16, 16, true});
    assert(shellResult.diagnostics.empty());
    Near(0x18 / 255.0F, shellResult.style.background()->red);
    Near(0xcc / 255.0F, shellResult.style.background()->alpha);
    Near(18, shellResult.style.cornerRadiusPx());
    // Focused shell items inherit the host's minimum two-DIP focus ring.
    Near(2.0F, shellResult.style.outlineWidthPx());
    Near(24, shellResult.style.fontSizePx());
    assert(shellResult.style.fontWeight() == 650);
    assert(shellResult.style.fontFamily() == L"Segoe UI Variable Display");

    std::cout << "NativeStyleTests passed\n";
}
