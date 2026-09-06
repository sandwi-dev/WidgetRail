#include "NativeStyle.h"

#include <cassert>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <limits>

namespace {

widgetrail::WidgetStyleValue Length(double number, std::wstring unit) {
    return {L"length", std::to_wstring(number) + unit, number, std::move(unit)};
}

widgetrail::WidgetStyleValue Number(std::wstring kind, double number) {
    return {std::move(kind), std::to_wstring(number), number, {}};
}

widgetrail::WidgetStyleValue Color(std::wstring value) {
    return {L"color", std::move(value), std::nullopt, {}};
}

void Near(float expected, float actual) {
    if (std::abs(expected - actual) >= 0.01F) {
        std::cerr << "Expected " << expected << ", got " << actual << '\n';
        std::abort();
    }
}

} // namespace

int main() {
    using namespace widgetrail;

    WidgetComputedStyle responsive{
        {L"width", Length(50, L"vw")},
        {L"height", Length(25, L"vh")},
        {L"min-width", Length(50, L"%")},
        {L"font-size", Length(1.5, L"em")},
        {L"letter-spacing", Length(0.25, L"rem")},
        {L"padding", {L"lengthList", L"10px 5% 2vh 1em", std::nullopt, {}}},
        {L"background", {L"color", L"#20406080", std::nullopt, {}}},
        {L"direction", {L"keyword", L"row", std::nullopt, {}}},
        {L"flex-wrap", {L"keyword", L"wrap", std::nullopt, {}}},
        {L"object-fit", {L"keyword", L"cover", std::nullopt, {}}},
        {L"flex-grow", Number(L"number", 2)},
        {L"translate-x", Length(10, L"vw")},
        {L"translate-y", Length(-25, L"%")},
    };
    NativeStyleContext context720{1280, 720, 1000, 600, 20, 16, false};
    auto style720 = NativeStyleAdapter::Adapt(responsive, context720).style;
    if (style720.maxLines() != 1) {
        std::cerr << "Default max-lines must keep text visible\n";
        std::abort();
    }
    assert(style720.overflowWrap() == NativeOverflowWrap::Normal);
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
    assert(style720.flexWrap() == NativeFlexWrap::Wrap);
    assert(style720.imageFit() == NativeImageFit::Cover);
    Near(2, style720.flexGrow());
    Near(128, style720.translateXPx());
    Near(-150, style720.translateYPx());
    Near(0x20 / 255.0F, style720.background()->red);
    Near(0x80 / 255.0F, style720.background()->alpha);

    WidgetComputedStyle emergencyWrap{
        {L"overflow-wrap", {L"keyword", L"anywhere", std::nullopt, {}}},
    };
    const auto emergencyWrapStyle = NativeStyleAdapter::Adapt(
        emergencyWrap, context720);
    assert(emergencyWrapStyle.style.overflowWrap() == NativeOverflowWrap::Anywhere);
    assert(emergencyWrapStyle.diagnostics.empty());
    WidgetComputedStyle invalidWrap{
        {L"overflow-wrap", {L"keyword", L"sometimes", std::nullopt, {}}},
    };
    const auto invalidWrapStyle = NativeStyleAdapter::Adapt(invalidWrap, context720);
    assert(invalidWrapStyle.style.overflowWrap() == NativeOverflowWrap::Normal);
    assert(invalidWrapStyle.diagnostics.size() == 1);

    WidgetComputedStyle uniformBorder{
        {L"border-width", Length(2, L"px")},
        {L"border-color", Color(L"#11223380")},
    };
    const auto uniformBorderStyle = NativeStyleAdapter::Adapt(
        uniformBorder, context720).style;
    Near(2, uniformBorderStyle.borderWidthPx());
    for (const auto* edge : {
             &uniformBorderStyle.borderEdges().top,
             &uniformBorderStyle.borderEdges().right,
             &uniformBorderStyle.borderEdges().bottom,
             &uniformBorderStyle.borderEdges().left}) {
        Near(2, edge->widthPx);
        assert(edge->color == uniformBorderStyle.borderColor());
    }

    WidgetComputedStyle mixedBorder{
        {L"border-width", Length(2, L"px")},
        {L"border-color", Color(L"#11223380")},
        {L"border-top-width", Length(4, L"px")},
        {L"border-right-width", Length(0, L"px")},
        {L"border-bottom-width", Length(999, L"px")},
        {L"border-top-color", Color(L"transparent")},
        {L"border-right-color", Color(L"#ff0000")},
        {L"opacity", Number(L"number", 0.25)},
    };
    const auto mixedBorderStyle = NativeStyleAdapter::Adapt(
        mixedBorder, context720).style;
    Near(2, mixedBorderStyle.borderWidthPx());
    Near(4, mixedBorderStyle.borderEdges().top.widthPx);
    Near(0, mixedBorderStyle.borderEdges().right.widthPx);
    Near(64, mixedBorderStyle.borderEdges().bottom.widthPx);
    Near(2, mixedBorderStyle.borderEdges().left.widthPx);
    assert(mixedBorderStyle.borderEdges().top.color.has_value());
    Near(0, mixedBorderStyle.borderEdges().top.color->alpha);
    assert((mixedBorderStyle.borderEdges().right.color == NativeColor{1, 0, 0, 1}));
    assert(mixedBorderStyle.borderEdges().bottom.color == mixedBorderStyle.borderColor());
    Near(0.25F, mixedBorderStyle.opacity());

    NativeAccessibilityPolicy borderAccessibility;
    borderAccessibility.reducedTransparency = true;
    const auto accessibleBorderStyle = NativeStyleAdapter::Adapt(
        mixedBorder, context720, borderAccessibility).style;
    Near(1, accessibleBorderStyle.opacity());
    Near(4, accessibleBorderStyle.borderEdges().top.widthPx);
    Near(0, accessibleBorderStyle.borderEdges().top.color->alpha);

    WidgetComputedStyle malformedBorder{
        {L"border-width", Length(3, L"px")},
        {L"border-color", Color(L"#010203")},
        {L"border-top-width", {L"keyword", L"thick", std::nullopt, {}}},
        {L"border-left-color", Color(L"red")},
    };
    const auto defensiveBorder = NativeStyleAdapter::Adapt(
        malformedBorder, context720);
    Near(3, defensiveBorder.style.borderEdges().top.widthPx);
    assert(defensiveBorder.style.borderEdges().left.color == defensiveBorder.style.borderColor());
    assert(defensiveBorder.diagnostics.size() == 2);

    NativeStyleContext context1080{1920, 1080, 1200, 800, 16, 16, false};
    auto style1080 = NativeStyleAdapter::Adapt(responsive, context1080).style;
    Near(960, *style1080.widthPx());
    Near(270, *style1080.heightPx());
    Near(192, style1080.translateXPx());
    Near(-200, style1080.translateYPx());

    NativeStyleContext ultrawide{3440, 1440, 2000, 1000, 18, 16, false};
    auto styleUltra = NativeStyleAdapter::Adapt(responsive, ultrawide).style;
    Near(1720, *styleUltra.widthPx());
    Near(360, *styleUltra.heightPx());
    Near(344, styleUltra.translateXPx());
    Near(-250, styleUltra.translateYPx());

    WidgetComputedStyle boundedTranslation{
        {L"translate-x", Length(999, L"vw")},
        {L"translate-y", Length(-999, L"vh")},
    };
    const auto boundedTranslationStyle = NativeStyleAdapter::Adapt(
        boundedTranslation, context720).style;
    Near(4096, boundedTranslationStyle.translateXPx());
    Near(-4096, boundedTranslationStyle.translateYPx());

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

    PlatformAppearance accessibilityAppearance;
    accessibilityAppearance.textScale = 1.25;
    accessibilityAppearance.motion = PlatformMotionPreference::System;
    accessibilityAppearance.contrast = PlatformContrastPreference::High;
    accessibilityAppearance.boldText = true;
    accessibilityAppearance.transparency = PlatformTransparencyPreference::Reduced;
    const auto resolvedPolicy = CreateNativeAccessibilityPolicy(
        accessibilityAppearance,
        false,
        false);
    assert(resolvedPolicy.reducedTransparency);
    assert(resolvedPolicy.reducedMotion);
    assert(resolvedPolicy.minimumFontWeight == 600);
    Near(1.25F, resolvedPolicy.textScale);
    Near(3.0F, resolvedPolicy.minimumFocusRingPx);
    assert(static_cast<bool>(resolvedPolicy.contrastHook));
    const auto policyStyle = NativeStyleAdapter::Adapt(
        accessible,
        NativeStyleContext{1920, 1080, 800, 400, 16, 16, true},
        resolvedPolicy).style;
    assert(policyStyle.fontWeight() == 600);
    Near(1, policyStyle.opacity());
    Near(0, policyStyle.backgroundBlurPx());
    Near(0, policyStyle.transitionDurationMilliseconds());
    Near(1, policyStyle.background()->alpha);
    Near(3, policyStyle.outlineWidthPx());

    WidgetComputedStyle inheritedSurfaceText{
        {L"color", {L"color", L"#777777", std::nullopt, {}}},
    };
    const auto inheritedContrast = NativeStyleAdapter::Adapt(
        inheritedSurfaceText,
        NativeStyleContext{
            1920, 1080, 800, 400, 16, 16, false,
            NativeColor{0.95F, 0.95F, 0.95F, 1}},
        resolvedPolicy).style;
    assert((inheritedContrast.foreground() == NativeColor{0, 0, 0, 1}));

    const auto midToneContrast = NativeStyleAdapter::Adapt(
        inheritedSurfaceText,
        NativeStyleContext{
            1920, 1080, 800, 400, 16, 16, false,
            NativeColor{0.5F, 0.5F, 0.5F, 1}},
        resolvedPolicy).style;
    assert((midToneContrast.foreground() == NativeColor{0, 0, 0, 1}));

    // An omitted `color` still receives the final accessibility foreground;
    // DeclarativeRenderer must never fall back to its normal-mode white text
    // after high contrast has been requested.
    const auto implicitOnLight = NativeStyleAdapter::Adapt(
        {},
        NativeStyleContext{
            1920, 1080, 800, 400, 16, 16, false,
            NativeColor{0.95F, 0.95F, 0.95F, 1}},
        resolvedPolicy).style;
    assert((implicitOnLight.foreground() == NativeColor{0, 0, 0, 1}));

    // Contrast is selected against source-over compositing, not the local
    // RGB triplet. Translucent white over black is a dark effective surface.
    WidgetComputedStyle translucentLight{
        {L"background", {L"color", L"rgba(255, 255, 255, 0.25)", std::nullopt, {}}},
    };
    auto compositingPolicy = resolvedPolicy;
    compositingPolicy.reducedTransparency = false;
    const auto compositedText = NativeStyleAdapter::Adapt(
        translucentLight,
        NativeStyleContext{
            1920, 1080, 800, 400, 16, 16, false,
            NativeColor{0, 0, 0, 1}},
        compositingPolicy).style;
    assert((compositedText.foreground() == NativeColor{1, 1, 1, 1}));

    // A semantic fallback surface participates in the same compositing. This
    // covers focused buttons whose renderer-owned fill is translucent.
    const auto fallbackFocus = NativeStyleAdapter::Adapt(
        {},
        NativeStyleContext{
            1920, 1080, 800, 400, 16, 16, true,
            NativeColor{1, 1, 1, 1},
            NativeColor{0, 0, 0, 0.25F}},
        compositingPolicy).style;
    assert((fallbackFocus.foreground() == NativeColor{0, 0, 0, 1}));
    assert((fallbackFocus.outlineColor() == NativeColor{0, 0, 0, 1}));
    Near(3.0F, fallbackFocus.outlineWidthPx());

    // Shell root colors are resolved over an opaque safety fallback before
    // Clear, and nested translucent surfaces then compose over that exact
    // effective parent rather than over their raw theme declarations.
    const auto effectiveCanvas = ResolveNativeSurfaceColor(
        NativeColor{1, 1, 1, 0.25F}, NativeColor{0, 0, 0, 1});
    Near(0.25F, effectiveCanvas.red);
    Near(0.25F, effectiveCanvas.green);
    Near(0.25F, effectiveCanvas.blue);
    Near(1.0F, effectiveCanvas.alpha);
    const auto effectivePanel = ResolveNativeSurfaceColor(
        NativeColor{1, 0, 0, 0.5F}, effectiveCanvas, 0.5F);
    Near(0.4375F, effectivePanel.red);
    Near(0.1875F, effectivePanel.green);
    Near(0.1875F, effectivePanel.blue);
    Near(1.0F, effectivePanel.alpha);
    const auto unchangedSurface = ResolveNativeSurfaceColor(
        std::nullopt, effectivePanel, 0.0F);
    assert(unchangedSurface == effectivePanel);

    // Shell roles that share one WRSS selector still adapt separately for
    // their real paint surfaces: dashboard/canvas, tray, and widget/panel.
    const auto canvasText = NativeStyleAdapter::Adapt(
        {}, NativeStyleContext{1920, 1080, 800, 400, 16, 16, false,
                               NativeColor{0.95F, 0.95F, 0.95F, 1}},
        compositingPolicy).style;
    const auto trayText = NativeStyleAdapter::Adapt(
        {}, NativeStyleContext{1920, 1080, 800, 400, 16, 16, false,
                               NativeColor{0.5F, 0.5F, 0.5F, 1}},
        compositingPolicy).style;
    const auto panelText = NativeStyleAdapter::Adapt(
        {}, NativeStyleContext{1920, 1080, 800, 400, 16, 16, false,
                               NativeColor{0.05F, 0.05F, 0.05F, 1}},
        compositingPolicy).style;
    assert((canvasText.foreground() == NativeColor{0, 0, 0, 1}));
    assert((trayText.foreground() == NativeColor{0, 0, 0, 1}));
    assert((panelText.foreground() == NativeColor{1, 1, 1, 1}));

    accessibilityAppearance.motion = PlatformMotionPreference::Full;
    accessibilityAppearance.contrast = PlatformContrastPreference::Standard;
    accessibilityAppearance.boldText = false;
    accessibilityAppearance.transparency = PlatformTransparencyPreference::Full;
    const auto explicitStandard = CreateNativeAccessibilityPolicy(
        accessibilityAppearance,
        true,
        false);
    assert(!explicitStandard.reducedTransparency);
    assert(!explicitStandard.reducedMotion);
    assert(explicitStandard.minimumFontWeight == 100);
    assert(!explicitStandard.contrastHook);

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
        {L"translate-x", {L"length", L"nanpx", std::numeric_limits<double>::quiet_NaN(), L"px"}},
        {L"translate-y", {L"keyword", L"auto", std::nullopt, {}}},
        {L"future-property", {L"keyword", L"surprise", std::nullopt, {}}},
    };
    auto defensive = NativeStyleAdapter::Adapt(malformed,
        NativeStyleContext{-1, 0, std::numeric_limits<float>::infinity(), 0, 0, 0, false});
    assert(!defensive.style.widthPx());
    assert(!defensive.style.background());
    Near(1, defensive.style.opacity());
    assert(defensive.style.fontFamily() == L"Segoe UI Variable Text");
    Near(0, defensive.style.translateXPx());
    Near(0, defensive.style.translateYPx());
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
