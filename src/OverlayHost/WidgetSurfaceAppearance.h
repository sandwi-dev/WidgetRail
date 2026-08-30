#pragma once

#include "WidgetBridgeClient.h"

#include <string_view>
#include <unordered_map>

namespace widgetrail::surface_appearance {

enum class Mode { Theme, Transparent, Solid };

struct Resolution final {
    Mode declared{Mode::Theme};
    Mode requested{Mode::Theme};
    Mode effective{Mode::Theme};
    std::wstring_view fallbackReason;
};

struct Policy final {
    PlatformSurfaceAppearanceOverride global{
        PlatformSurfaceAppearanceOverride::Widget};
    std::unordered_map<std::wstring, PlatformSurfaceAppearanceOverride> widgetOverrides;
    bool highContrast{};
    bool reducedTransparency{};
    bool transparentCompositionSupported{};
    double backdropOpacity{0.64};
};

[[nodiscard]] inline Policy CreatePolicy(
    const PlatformAppearance& appearance,
    const bool highContrast,
    const bool reducedTransparency,
    const bool transparentCompositionSupported) {
    return {
        appearance.widgetSurfaceAppearance,
        appearance.widgetSurfaceAppearanceOverrides,
        highContrast,
        reducedTransparency,
        transparentCompositionSupported,
        appearance.backdropOpacity,
    };
}

[[nodiscard]] inline Mode ParseDeclared(const std::wstring_view value) noexcept {
    if (value == L"transparent") return Mode::Transparent;
    if (value == L"solid") return Mode::Solid;
    return Mode::Theme;
}

[[nodiscard]] inline Mode ResolveOverride(
    const PlatformSurfaceAppearanceOverride value,
    const Mode declared) noexcept {
    switch (value) {
    case PlatformSurfaceAppearanceOverride::Theme: return Mode::Theme;
    case PlatformSurfaceAppearanceOverride::Transparent: return Mode::Transparent;
    case PlatformSurfaceAppearanceOverride::Solid: return Mode::Solid;
    case PlatformSurfaceAppearanceOverride::Widget: return declared;
    }
    return Mode::Solid;
}

[[nodiscard]] inline Resolution Resolve(
    const std::wstring_view declaredValue,
    const Policy& policy,
    const std::wstring_view widgetId,
    const bool pinnedSurface = false) noexcept {
    Resolution result;
    result.declared = ParseDeclared(declaredValue);
    const auto widgetOverride = policy.widgetOverrides.find(
        std::wstring(widgetId));
    result.requested = ResolveOverride(
        widgetOverride == policy.widgetOverrides.end()
            ? policy.global
            : widgetOverride->second,
        result.declared);
    result.effective = result.requested;
    if (policy.highContrast) {
        result.effective = Mode::Solid;
        result.fallbackReason = L"high-contrast";
    } else if (policy.reducedTransparency) {
        result.effective = Mode::Solid;
        result.fallbackReason = L"reduced-transparency";
    } else if (result.requested == Mode::Transparent &&
               !policy.transparentCompositionSupported && !pinnedSurface) {
        result.effective = Mode::Solid;
        result.fallbackReason = L"unsupported-composition";
    } else if (result.requested == Mode::Transparent &&
               policy.backdropOpacity <= 0.0 && !pinnedSurface) {
        result.effective = Mode::Solid;
        result.fallbackReason = L"zero-backdrop-contrast";
    }
    return result;
}

[[nodiscard]] inline std::wstring_view Name(const Mode value) noexcept {
    switch (value) {
    case Mode::Theme: return L"theme";
    case Mode::Transparent: return L"transparent";
    case Mode::Solid: return L"solid";
    }
    return L"solid";
}

} // namespace widgetrail::surface_appearance
