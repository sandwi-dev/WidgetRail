#include "NativeTextLayout.h"

#include <Windows.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <cmath>
#include <cstdlib>
#include <iostream>
#include <string_view>

namespace {

using Microsoft::WRL::ComPtr;

std::size_t checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) {
        std::cerr << "NativeTextLayoutTests: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Near(
    const float actual,
    const float expected,
    const std::string_view message,
    const float tolerance = 0.02F) {
    Check(std::abs(actual - expected) <= tolerance, message);
}

gba::WidgetStyleValue Length(const double value) {
    return {L"length", std::to_wstring(value) + L"px", value, L"px"};
}

gba::WidgetStyleValue Number(const double value) {
    return {L"number", std::to_wstring(value), value, {}};
}

gba::WidgetStyleValue Keyword(std::wstring value) {
    return {L"keyword", std::move(value), std::nullopt, {}};
}

gba::NativeRenderStyle Style(
    const float fontSize,
    const float lineHeight,
    const int maximumLines,
    const float textScale = 1.0F,
    const gba::NativeTextAlign alignment = gba::NativeTextAlign::Start) {
    gba::WidgetComputedStyle computed{
        {L"font-family", {L"fontFamily", L"Segoe UI Variable Text", std::nullopt, {}}},
        {L"font-size", Length(fontSize)},
        {L"font-weight", Number(500)},
        {L"line-height", Number(lineHeight)},
        {L"max-lines", Number(maximumLines)},
        {L"letter-spacing", Length(0.33)},
        {L"text-overflow", Keyword(L"ellipsis")},
    };
    computed[L"text-align"] = Keyword(
        alignment == gba::NativeTextAlign::Center ? L"center" :
        alignment == gba::NativeTextAlign::End ? L"end" : L"start");
    gba::NativeAccessibilityPolicy accessibility;
    accessibility.textScale = textScale;
    return gba::NativeStyleAdapter::Adapt(
        computed, gba::NativeStyleContext{}, accessibility).style;
}

void SectionHeaderPlanUsesFontMetricsAndContainsInk(IDWriteFactory* factory) {
    const auto style = Style(11.0F, 1.0F, 1);
    const auto plan = gba::CreateNativeTextLayoutPlan(
        factory, L"LIBRARY", style, 180.0F, 30.0F);
    Check(plan.IsValid(), "SectionHeader eyebrow creates a DirectWrite plan");
    Check(plan.baseline > 0.0F && plan.baseline <= 11.0F,
        "font-derived baseline remains inside the authored line box");
    Check(plan.measuredWidth > 0.0F && plan.measuredWidth <= 180.0F,
        "eyebrow width is bounded by the authored measure width");
    Check(plan.measuredHeight >= 11.0F && plan.measuredHeight <= 30.0F,
        "eyebrow height includes the complete authored line and ink overhang");
    Near(
        plan.LayoutOriginY(0.0F, plan.measuredHeight,
            gba::NativeTextVerticalAlignment::Start),
        plan.inkInsetTop,
        "start placement offsets the DirectWrite box by its top ink inset");

    DWRITE_OVERHANG_METRICS overhang{};
    Check(SUCCEEDED(plan.layout->GetOverhangMetrics(&overhang)),
        "SectionHeader plan exposes DirectWrite overhang metrics");
    DWRITE_TEXT_METRICS metrics{};
    Check(SUCCEEDED(plan.layout->GetMetrics(&metrics)),
        "SectionHeader plan exposes DirectWrite text metrics");
    Check(plan.inkInsetTop >= std::max(0.0F, overhang.top) - 0.01F &&
            plan.inkInsetBottom >= std::max(0.0F, overhang.bottom) - 0.01F,
        "measured SectionHeader box retains all positive vertical ink overhang");
    Near(plan.measuredHeight,
        std::max(11.0F, metrics.height + std::max(0.0F, overhang.top) +
            std::max(0.0F, overhang.bottom)),
        "measured SectionHeader height is the exact line-plus-overhang model");
}

void ControlPlacementCentersTheCompletePlan(IDWriteFactory* factory) {
    const auto style = Style(15.0F, 1.3F, 2);
    const auto plan = gba::CreateNativeTextLayoutPlan(
        factory, L"Refresh library", style, 160.0F, 39.0F);
    Check(plan.IsValid(), "Button label creates a DirectWrite plan");
    const auto origin = plan.LayoutOriginY(
        0.0F, 44.0F, gba::NativeTextVerticalAlignment::Center);
    const auto outerTop = origin - plan.inkInsetTop;
    Near(outerTop, (44.0F - plan.measuredHeight) * 0.5F,
        "Button label centers the complete measured ink box");
    Near(44.0F - (outerTop + plan.measuredHeight), outerTop,
        "Button label leaves symmetric vertical space");
}

void WrappingScaleAndAlignmentStayBounded(IDWriteFactory* factory) {
    for (const auto scale : {1.0F, 1.5F}) {
        const auto style = Style(
            13.0F, 1.35F, 2, scale, gba::NativeTextAlign::Center);
        const auto plan = gba::CreateNativeTextLayoutPlan(
            factory,
            L"A long shared component description that wraps without changing paint metrics",
            style,
            220.0F,
            80.0F);
        Check(plan.IsValid(), "wrapped text creates a plan at every supported scale");
        Check(plan.measuredWidth <= 220.0F && plan.measuredHeight <= 80.0F,
            "wrapped plan respects its width and height bounds");
        Check(plan.measuredHeight >= 13.0F * scale * 1.35F,
            "wrapped plan retains at least one complete scaled line");
        Check(plan.baseline <= 13.0F * scale * 1.35F,
            "scaled baseline remains inside its line box");
    }
}

void InvalidInputsFailClosed(IDWriteFactory* factory) {
    const auto style = Style(13.0F, 1.2F, 1);
    Check(!gba::CreateNativeTextLayoutPlan(
        nullptr, L"Text", style, 100.0F, 20.0F).IsValid(),
        "missing DirectWrite factory fails closed");
    Check(!gba::CreateNativeTextLayoutPlan(
        factory, L"Text", style, 0.0F, 20.0F).IsValid(),
        "zero width fails closed");
    Check(!gba::CreateNativeTextLayoutPlan(
        factory, L"Text", style, 100.0F, INFINITY).IsValid(),
        "non-finite height fails closed");
}

} // namespace

int main() {
    const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "COM initializes");
    {
        ComPtr<IDWriteFactory> factory;
        Check(SUCCEEDED(DWriteCreateFactory(
            DWRITE_FACTORY_TYPE_SHARED,
            __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(factory.ReleaseAndGetAddressOf()))),
            "DirectWrite factory initializes");
        SectionHeaderPlanUsesFontMetricsAndContainsInk(factory.Get());
        ControlPlacementCentersTheCompletePlan(factory.Get());
        WrappingScaleAndAlignmentStayBounded(factory.Get());
        InvalidInputsFailClosed(factory.Get());
    }
    std::cout << "NativeTextLayoutTests: " << checks << " checks passed\n";
    CoUninitialize();
    return EXIT_SUCCESS;
}
