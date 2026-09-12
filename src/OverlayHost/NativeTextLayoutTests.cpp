#include "NativeTextLayout.h"

#include <Windows.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <cmath>
#include <cstdlib>
#include <iostream>
#include <string>
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

widgetrail::WidgetStyleValue Length(const double value) {
    return {L"length", std::to_wstring(value) + L"px", value, L"px"};
}

widgetrail::WidgetStyleValue Number(const double value) {
    return {L"number", std::to_wstring(value), value, {}};
}

widgetrail::WidgetStyleValue Keyword(std::wstring value) {
    return {L"keyword", std::move(value), std::nullopt, {}};
}

widgetrail::NativeRenderStyle Style(
    const float fontSize,
    const float lineHeight,
    const int maximumLines,
    const float textScale = 1.0F,
    const widgetrail::NativeTextAlign alignment = widgetrail::NativeTextAlign::Start,
    std::wstring fontFamily = L"Segoe UI Variable Text",
    const widgetrail::NativeOverflowWrap overflowWrap =
        widgetrail::NativeOverflowWrap::Normal) {
    widgetrail::WidgetComputedStyle computed{
        {L"font-family", {L"fontFamily", std::move(fontFamily), std::nullopt, {}}},
        {L"font-size", Length(fontSize)},
        {L"font-weight", Number(500)},
        {L"line-height", Number(lineHeight)},
        {L"max-lines", Number(maximumLines)},
        {L"letter-spacing", Length(0.33)},
        {L"text-overflow", Keyword(L"ellipsis")},
    };
    computed[L"text-align"] = Keyword(
        alignment == widgetrail::NativeTextAlign::Center ? L"center" :
        alignment == widgetrail::NativeTextAlign::End ? L"end" : L"start");
    computed[L"overflow-wrap"] = Keyword(
        overflowWrap == widgetrail::NativeOverflowWrap::Anywhere
            ? L"anywhere" : L"normal");
    widgetrail::NativeAccessibilityPolicy accessibility;
    accessibility.textScale = textScale;
    return widgetrail::NativeStyleAdapter::Adapt(
        computed, widgetrail::NativeStyleContext{}, accessibility).style;
}

void SectionHeaderPlanUsesFontMetricsAndContainsInk(IDWriteFactory* factory) {
    const auto style = Style(11.0F, 1.0F, 1);
    const auto plan = widgetrail::CreateNativeTextLayoutPlan(
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
            widgetrail::NativeTextVerticalAlignment::Start),
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
    const auto plan = widgetrail::CreateNativeTextLayoutPlan(
        factory, L"Refresh library", style, 160.0F, 39.0F);
    Check(plan.IsValid(), "Button label creates a DirectWrite plan");
    const auto origin = plan.LayoutOriginY(
        0.0F, 44.0F, widgetrail::NativeTextVerticalAlignment::Center);
    const auto outerTop = origin - plan.inkInsetTop;
    Near(outerTop, (44.0F - plan.measuredHeight) * 0.5F,
        "Button label centers the complete measured ink box");
    Near(44.0F - (outerTop + plan.measuredHeight), outerTop,
        "Button label leaves symmetric vertical space");
}

void WrappingScaleAndAlignmentStayBounded(IDWriteFactory* factory) {
    for (const auto scale : {1.0F, 1.5F}) {
        const auto style = Style(
            13.0F, 1.35F, 2, scale, widgetrail::NativeTextAlign::Center);
        const auto plan = widgetrail::CreateNativeTextLayoutPlan(
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

void DiagnosticMetricRowsRemainCompleteAtMaximumScale(IDWriteFactory* factory) {
    constexpr std::wstring_view rows[] = {
        L"Artwork requests: 9223372036854775807",
        L"Artwork completions: 9223372036854775807; failures: 9223372036854775807",
        L"Artwork in flight: 9223372036854775807; peak: 9223372036854775807",
        L"Artwork payload: 9223372036854775807 raw bytes",
        L"Artwork encoding: 9223372036854775807 Base64 characters",
        L"Managed heap: 9223372036854775807 bytes",
        L"Large object heap: 9223372036854775807 bytes",
        L"Allocation rate: 9223372036854775807 bytes/second",
        L"Gen2 collections: 9223372036854775807",
        L"Private memory: 9223372036854775807 bytes",
        L"Working set: 9223372036854775807 bytes",
    };
    const auto style = Style(
        13.0F, 1.4F, 2, 1.5F, widgetrail::NativeTextAlign::Start, L"Consolas",
        widgetrail::NativeOverflowWrap::Anywhere);
    for (const auto row : rows) {
        const auto plan = widgetrail::CreateNativeTextLayoutPlan(
            factory, row, style, 460.0F, 80.0F);
        Check(plan.IsValid(),
            "maximum-value diagnostic row creates a DirectWrite plan");
        DWRITE_LINE_METRICS lines[2]{};
        UINT32 actual{};
        Check(SUCCEEDED(plan.layout->GetLineMetrics(lines, 2, &actual)),
            "maximum-value diagnostic row exposes line metrics");
        Check(actual > 0 && actual <= 2,
            "maximum-value diagnostic row fits the two-line bounded row");
        Check(std::none_of(lines, lines + actual, [](const auto& line) {
            return line.isTrimmed;
        }), "maximum-value diagnostic row remains complete without trimming");
    }

    const auto areaStyle = Style(
        13.0F, 1.3F, 8, 1.5F, widgetrail::NativeTextAlign::Start,
        L"Segoe UI Variable Text", widgetrail::NativeOverflowWrap::Anywhere);
    constexpr std::wstring_view bridgeArea =
        L"OK Bridge: Native host connected; application workers "
        L"2147483647/2147483647 (user-configured count limit); reported memory "
        L"guidance 2147483647 MiB; control plane 2147483647 "
        L"(2147483647 MiB reported)";
    for (const auto text : {
             bridgeArea,
             std::wstring_view(L"diagnostics_unavailable_diagnostics_unavailable_0123456789"),
             std::wstring_view(L"\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE\U0001F3AE")}) {
        const auto plan = widgetrail::CreateNativeTextLayoutPlan(
            factory, text, areaStyle, 480.0F, 240.0F);
        Check(plan.IsValid(), "wrapped diagnostic area creates a DirectWrite plan");
        DWRITE_LINE_METRICS lines[8]{};
        UINT32 actual{};
        Check(SUCCEEDED(plan.layout->GetLineMetrics(lines, 8, &actual)),
            "wrapped diagnostic area exposes line metrics");
        Check(actual > 0 && actual <= 8,
            "wrapped diagnostic area fits the eight-line content bound");
        Check(std::none_of(lines, lines + actual, [](const auto& line) {
            return line.isTrimmed;
        }), "wrapped diagnostic area remains complete without trimming");
    }

}

void RetainedPlansAreBoundedAndConstraintExact(IDWriteFactory* factory) {
    widgetrail::NativeTextLayoutCache cache;
    const auto style = Style(13.0F, 1.2F, 1);
    const auto first = cache.Get(factory, L"Retained", style, 200, 40);
    const auto same = cache.Get(factory, L"Retained", style, 200, 40);
    Check(first.layout.Get() == same.layout.Get(), "same text and constraints reuse immutable DirectWrite layout");
    Check(cache.Get(factory, L"Retained", style, 201, 40).layout.Get() != first.layout.Get(), "width invalidates text layout");
    Check(cache.Get(factory, L"Changed", style, 200, 40).layout.Get() != first.layout.Get(), "content invalidates text layout");
    Check(cache.Get(factory, L"Retained", Style(18.0F, 1.2F, 1), 200, 40).layout.Get() != first.layout.Get(), "font size invalidates layout");
    for (int i=0;i<1100;++i) (void)cache.Get(factory, std::to_wstring(i), style, 200, 40);
    Check(cache.size() <= 1024, "retained layout count stays bounded");
    Check(first.IsValid(), "eviction does not invalidate a caller's retained plan");
    cache.Clear(); Check(cache.size()==0,"clear releases retained plans");
}

void InvalidInputsFailClosed(IDWriteFactory* factory) {
    const auto style = Style(13.0F, 1.2F, 1);
    Check(!widgetrail::CreateNativeTextLayoutPlan(
        nullptr, L"Text", style, 100.0F, 20.0F).IsValid(),
        "missing DirectWrite factory fails closed");
    Check(!widgetrail::CreateNativeTextLayoutPlan(
        factory, L"Text", style, 0.0F, 20.0F).IsValid(),
        "zero width fails closed");
    Check(!widgetrail::CreateNativeTextLayoutPlan(
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
        DiagnosticMetricRowsRemainCompleteAtMaximumScale(factory.Get());
        InvalidInputsFailClosed(factory.Get());
        RetainedPlansAreBoundedAndConstraintExact(factory.Get());
    }
    std::cout << "NativeTextLayoutTests: " << checks << " checks passed\n";
    CoUninitialize();
    return EXIT_SUCCESS;
}
