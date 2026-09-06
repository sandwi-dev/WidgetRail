#include "NativeStyle.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cwchar>
#include <cwctype>
#include <limits>
#include <sstream>
#include <utility>

namespace widgetrail {
namespace {

constexpr std::size_t kMaximumProperties = 64;
constexpr std::size_t kMaximumDiagnostics = 64;
constexpr float kMaximumResolvedDimension = 16'384.0F;
constexpr float kMaximumResolvedSpacing = 4'096.0F;
constexpr float kMaximumResolvedTranslation = 4'096.0F;

enum class LengthBasis { Width, Height, Font, MinimumDimension };

[[nodiscard]] float ClampFinite(float value, float fallback, float minimum, float maximum) noexcept {
    return std::isfinite(value) ? std::clamp(value, minimum, maximum) : fallback;
}

[[nodiscard]] bool ValidContext(const NativeStyleContext& context) noexcept {
    return std::isfinite(context.viewportWidthPx) && context.viewportWidthPx > 0 &&
           std::isfinite(context.viewportHeightPx) && context.viewportHeightPx > 0 &&
           std::isfinite(context.parentWidthPx) && context.parentWidthPx >= 0 &&
           std::isfinite(context.parentHeightPx) && context.parentHeightPx >= 0 &&
           std::isfinite(context.parentFontSizePx) && context.parentFontSizePx > 0 &&
           std::isfinite(context.rootFontSizePx) && context.rootFontSizePx > 0;
}

[[nodiscard]] std::optional<float> ResolveLength(
    double number,
    std::wstring_view unit,
    LengthBasis basis,
    const NativeStyleContext& context,
    float minimum,
    float maximum) noexcept {
    if (!std::isfinite(number) || std::abs(number) > 1'000'000.0) return std::nullopt;
    double resolved = 0;
    if (unit == L"px" || (unit.empty() && number == 0)) resolved = number;
    else if (unit == L"em") resolved = number * context.parentFontSizePx;
    else if (unit == L"rem") resolved = number * context.rootFontSizePx;
    else if (unit == L"vw") resolved = number * context.viewportWidthPx / 100.0;
    else if (unit == L"vh") resolved = number * context.viewportHeightPx / 100.0;
    else if (unit == L"%") {
        const double reference = basis == LengthBasis::Height ? context.parentHeightPx
            : basis == LengthBasis::Font ? context.parentFontSizePx
            : basis == LengthBasis::MinimumDimension
                ? std::min(context.parentWidthPx, context.parentHeightPx)
                : context.parentWidthPx;
        resolved = number * reference / 100.0;
    } else return std::nullopt;
    if (!std::isfinite(resolved)) return std::nullopt;
    return static_cast<float>(std::clamp(resolved, static_cast<double>(minimum), static_cast<double>(maximum)));
}

[[nodiscard]] std::optional<std::pair<double, std::wstring>> ParseCanonicalLength(std::wstring_view token) {
    if (token.empty() || token.size() > 64) return std::nullopt;
    std::wstring owned(token);
    wchar_t* end = nullptr;
    const double number = std::wcstod(owned.c_str(), &end);
    if (end == owned.c_str() || !std::isfinite(number)) return std::nullopt;
    std::wstring unit(end);
    if (unit != L"px" && unit != L"em" && unit != L"rem" && unit != L"%" &&
        unit != L"vw" && unit != L"vh" && !(unit.empty() && number == 0)) return std::nullopt;
    return std::pair{number, std::move(unit)};
}

[[nodiscard]] std::optional<NativeEdges> ParseSpacing(
    std::wstring_view text,
    const NativeStyleContext& context,
    float minimum,
    float maximum) {
    if (text.empty() || text.size() > 256) return std::nullopt;
    std::wistringstream stream{std::wstring(text)};
    std::vector<float> values;
    std::wstring token;
    while (stream >> token) {
        if (values.size() == 4) return std::nullopt;
        const auto parsed = ParseCanonicalLength(token);
        if (!parsed) return std::nullopt;
        const auto resolved = ResolveLength(parsed->first, parsed->second,
            LengthBasis::Width, context, minimum, maximum);
        if (!resolved) return std::nullopt;
        values.push_back(*resolved);
    }
    if (values.empty()) return std::nullopt;
    if (values.size() == 1) return NativeEdges{values[0], values[0], values[0], values[0]};
    if (values.size() == 2) return NativeEdges{values[0], values[1], values[0], values[1]};
    if (values.size() == 3) return NativeEdges{values[0], values[1], values[2], values[1]};
    return NativeEdges{values[0], values[1], values[2], values[3]};
}

[[nodiscard]] std::optional<double> ParseNumber(std::wstring_view text) noexcept {
    if (text.empty() || text.size() > 64) return std::nullopt;
    std::wstring owned(text);
    wchar_t* end = nullptr;
    const double value = std::wcstod(owned.c_str(), &end);
    if (end == owned.c_str() || *end != L'\0' || !std::isfinite(value)) return std::nullopt;
    return value;
}

[[nodiscard]] std::optional<float> ParseColorComponent(std::wstring_view text, bool alpha) {
    const bool percentage = text.ends_with(L"%");
    if (percentage) text.remove_suffix(1);
    const auto number = ParseNumber(text);
    if (!number) return std::nullopt;
    const double maximum = percentage ? 100.0 : alpha ? 1.0 : 255.0;
    if (*number < 0 || *number > maximum) return std::nullopt;
    return static_cast<float>(*number / maximum);
}

[[nodiscard]] std::optional<NativeColor> ParseColor(std::wstring_view text) {
    if (text == L"transparent") return NativeColor{0, 0, 0, 0};
    if (text.starts_with(L"#")) {
        const auto Hex = [](wchar_t character) -> int {
            if (character >= L'0' && character <= L'9') return character - L'0';
            if (character >= L'a' && character <= L'f') return character - L'a' + 10;
            if (character >= L'A' && character <= L'F') return character - L'A' + 10;
            return -1;
        };
        if (text.size() != 4 && text.size() != 5 && text.size() != 7 && text.size() != 9)
            return std::nullopt;
        std::array<int, 4> bytes{0, 0, 0, 255};
        const bool shortForm = text.size() <= 5;
        const int components = text.size() == 5 || text.size() == 9 ? 4 : 3;
        for (int index = 0; index < components; ++index) {
            if (shortForm) {
                const int value = Hex(text[static_cast<std::size_t>(index) + 1]);
                if (value < 0) return std::nullopt;
                bytes[static_cast<std::size_t>(index)] = value * 17;
            } else {
                const auto offset = static_cast<std::size_t>(index) * 2 + 1;
                const int high = Hex(text[offset]);
                const int low = Hex(text[offset + 1]);
                if (high < 0 || low < 0) return std::nullopt;
                bytes[static_cast<std::size_t>(index)] = high * 16 + low;
            }
        }
        return NativeColor{bytes[0] / 255.0F, bytes[1] / 255.0F,
                           bytes[2] / 255.0F, bytes[3] / 255.0F};
    }
    const bool rgb = text.starts_with(L"rgb(") && text.ends_with(L")");
    const bool rgba = text.starts_with(L"rgba(") && text.ends_with(L")");
    if (!rgb && !rgba) return std::nullopt;
    const auto open = text.find(L'(');
    std::wstring arguments(text.substr(open + 1, text.size() - open - 2));
    std::vector<std::wstring> parts;
    std::size_t start = 0;
    for (;;) {
        const auto comma = arguments.find(L',', start);
        auto part = arguments.substr(start, comma == std::wstring::npos ? comma : comma - start);
        const auto first = part.find_first_not_of(L" \t");
        const auto last = part.find_last_not_of(L" \t");
        if (first == std::wstring::npos) return std::nullopt;
        parts.push_back(part.substr(first, last - first + 1));
        if (comma == std::wstring::npos) break;
        start = comma + 1;
    }
    if (parts.size() != (rgba ? 4U : 3U)) return std::nullopt;
    const auto red = ParseColorComponent(parts[0], false);
    const auto green = ParseColorComponent(parts[1], false);
    const auto blue = ParseColorComponent(parts[2], false);
    const auto alpha = rgba ? ParseColorComponent(parts[3], true) : std::optional<float>{1.0F};
    if (!red || !green || !blue || !alpha) return std::nullopt;
    return NativeColor{*red, *green, *blue, *alpha};
}

[[nodiscard]] NativeColor DefaultContrastingColor(const NativeColor& background) noexcept {
    const float luminance = 0.2126F * background.red + 0.7152F * background.green + 0.0722F * background.blue;
    return luminance > 0.5F ? NativeColor{0, 0, 0, 1} : NativeColor{1, 1, 1, 1};
}

template <typename T>
[[nodiscard]] std::optional<T> Keyword(std::wstring_view text,
    std::initializer_list<std::pair<std::wstring_view, T>> values) {
    for (const auto& [name, value] : values) if (text == name) return value;
    return std::nullopt;
}

} // namespace

struct NativeRenderStyle::Data final {
    std::optional<NativeColor> background;
    std::optional<NativeColor> foreground;
    std::optional<NativeColor> borderColor;
    std::optional<NativeColor> outlineColor;
    std::optional<NativeColor> shadowColor;
    std::optional<NativeColor> imageTint;
    std::optional<NativeColor> scrimColor;
    std::optional<float> width;
    std::optional<float> height;
    std::optional<float> minWidth;
    std::optional<float> minHeight;
    std::optional<float> maxWidth;
    std::optional<float> maxHeight;
    float fontSize{16};
    float letterSpacing{};
    float cornerRadius{};
    float outlineWidth{};
    float outlineOffset{};
    float borderWidth{};
    NativeBorderStyle borderEdges{};
    float backgroundBlur{};
    float shadowBlur{};
    float shadowOffsetX{};
    float shadowOffsetY{};
    NativeEdges gap{};
    NativeEdges padding{};
    NativeEdges margin{};
    float opacity{1};
    float scale{1};
    float translateX{};
    float translateY{};
    float transitionDuration{};
    std::optional<float> aspectRatio;
    NativeImageFit imageFit{NativeImageFit::Contain};
    NativeObjectPosition objectPosition{NativeObjectPosition::Center};
    NativeShape shape{NativeShape::Rectangle};
    int fontWeight{400};
    std::wstring fontFamily{L"Segoe UI Variable Text"};
    float lineHeight{1.2F};
    int maxLines{1};
    NativeTextOverflow textOverflow{NativeTextOverflow::Clip};
    NativeOverflowWrap overflowWrap{NativeOverflowWrap::Normal};
    NativeTextTransform textTransform{NativeTextTransform::None};
    NativeTransitionEasing transitionEasing{NativeTransitionEasing::EaseOut};
    float flexGrow{};
    float flexShrink{1};
    std::optional<float> flexBasis;
    bool flexBasisAuto{true};
    NativeFlexWrap flexWrap{NativeFlexWrap::NoWrap};
    NativeAlign align{NativeAlign::Unspecified};
    NativeJustify justify{NativeJustify::Unspecified};
    NativeDirection direction{NativeDirection::Unspecified};
    NativeOverflow overflow{NativeOverflow::Clip};
    NativeTextAlign textAlign{NativeTextAlign::Start};
};

NativeRenderStyle::NativeRenderStyle() : data_(std::make_shared<const Data>()) {}
NativeRenderStyle::NativeRenderStyle(std::shared_ptr<const Data> data) : data_(std::move(data)) {}
#define WRAIL_STYLE_GETTER(type, name, field) type NativeRenderStyle::name() const noexcept { return data_->field; }
WRAIL_STYLE_GETTER(const std::optional<NativeColor>&, background, background)
WRAIL_STYLE_GETTER(const std::optional<NativeColor>&, foreground, foreground)
WRAIL_STYLE_GETTER(const std::optional<NativeColor>&, borderColor, borderColor)
WRAIL_STYLE_GETTER(const std::optional<NativeColor>&, outlineColor, outlineColor)
WRAIL_STYLE_GETTER(const std::optional<NativeColor>&, shadowColor, shadowColor)
WRAIL_STYLE_GETTER(const std::optional<NativeColor>&, imageTint, imageTint)
WRAIL_STYLE_GETTER(const std::optional<NativeColor>&, scrimColor, scrimColor)
WRAIL_STYLE_GETTER(const std::optional<float>&, widthPx, width)
WRAIL_STYLE_GETTER(const std::optional<float>&, heightPx, height)
WRAIL_STYLE_GETTER(const std::optional<float>&, minWidthPx, minWidth)
WRAIL_STYLE_GETTER(const std::optional<float>&, minHeightPx, minHeight)
WRAIL_STYLE_GETTER(const std::optional<float>&, maxWidthPx, maxWidth)
WRAIL_STYLE_GETTER(const std::optional<float>&, maxHeightPx, maxHeight)
WRAIL_STYLE_GETTER(float, fontSizePx, fontSize)
WRAIL_STYLE_GETTER(float, letterSpacingPx, letterSpacing)
WRAIL_STYLE_GETTER(float, cornerRadiusPx, cornerRadius)
WRAIL_STYLE_GETTER(float, outlineWidthPx, outlineWidth)
WRAIL_STYLE_GETTER(float, outlineOffsetPx, outlineOffset)
WRAIL_STYLE_GETTER(float, borderWidthPx, borderWidth)
WRAIL_STYLE_GETTER(const NativeBorderStyle&, borderEdges, borderEdges)
WRAIL_STYLE_GETTER(float, backgroundBlurPx, backgroundBlur)
WRAIL_STYLE_GETTER(float, shadowBlurPx, shadowBlur)
WRAIL_STYLE_GETTER(float, shadowOffsetXPx, shadowOffsetX)
WRAIL_STYLE_GETTER(float, shadowOffsetYPx, shadowOffsetY)
WRAIL_STYLE_GETTER(const NativeEdges&, gapPx, gap)
WRAIL_STYLE_GETTER(const NativeEdges&, paddingPx, padding)
WRAIL_STYLE_GETTER(const NativeEdges&, marginPx, margin)
WRAIL_STYLE_GETTER(float, opacity, opacity)
WRAIL_STYLE_GETTER(float, scale, scale)
WRAIL_STYLE_GETTER(float, translateXPx, translateX)
WRAIL_STYLE_GETTER(float, translateYPx, translateY)
WRAIL_STYLE_GETTER(float, transitionDurationMilliseconds, transitionDuration)
WRAIL_STYLE_GETTER(const std::optional<float>&, aspectRatio, aspectRatio)
WRAIL_STYLE_GETTER(NativeImageFit, imageFit, imageFit)
WRAIL_STYLE_GETTER(NativeObjectPosition, objectPosition, objectPosition)
WRAIL_STYLE_GETTER(NativeShape, shape, shape)
WRAIL_STYLE_GETTER(int, fontWeight, fontWeight)
WRAIL_STYLE_GETTER(const std::wstring&, fontFamily, fontFamily)
WRAIL_STYLE_GETTER(float, lineHeight, lineHeight)
WRAIL_STYLE_GETTER(int, maxLines, maxLines)
WRAIL_STYLE_GETTER(NativeTextOverflow, textOverflow, textOverflow)
WRAIL_STYLE_GETTER(NativeOverflowWrap, overflowWrap, overflowWrap)
WRAIL_STYLE_GETTER(NativeTextTransform, textTransform, textTransform)
WRAIL_STYLE_GETTER(NativeTransitionEasing, transitionEasing, transitionEasing)
WRAIL_STYLE_GETTER(float, flexGrow, flexGrow)
WRAIL_STYLE_GETTER(float, flexShrink, flexShrink)
WRAIL_STYLE_GETTER(const std::optional<float>&, flexBasisPx, flexBasis)
WRAIL_STYLE_GETTER(bool, flexBasisAuto, flexBasisAuto)
WRAIL_STYLE_GETTER(NativeFlexWrap, flexWrap, flexWrap)
WRAIL_STYLE_GETTER(NativeAlign, align, align)
WRAIL_STYLE_GETTER(NativeJustify, justify, justify)
WRAIL_STYLE_GETTER(NativeDirection, direction, direction)
WRAIL_STYLE_GETTER(NativeOverflow, overflow, overflow)
WRAIL_STYLE_GETTER(NativeTextAlign, textAlign, textAlign)
#undef WRAIL_STYLE_GETTER

NativeAccessibilityPolicy CreateNativeAccessibilityPolicy(
    const PlatformAppearance& appearance,
    const bool systemHighContrast,
    const bool systemAnimationsEnabled) {
    NativeAccessibilityPolicy policy;
    policy.reducedTransparency =
        appearance.transparency == PlatformTransparencyPreference::Reduced;
    policy.reducedMotion =
        appearance.motion == PlatformMotionPreference::Reduced ||
        (appearance.motion == PlatformMotionPreference::System &&
         !systemAnimationsEnabled);
    policy.textScale = static_cast<float>(appearance.textScale);
    policy.minimumFontWeight = appearance.boldText ? 600 : 100;

    const bool highContrast =
        appearance.contrast == PlatformContrastPreference::High ||
        (appearance.contrast == PlatformContrastPreference::System &&
         systemHighContrast);
    if (highContrast) {
        // Retain a geometric focus cue and choose the extreme luminance that
        // maximizes contrast against the resolved surface. The policy runs
        // after every WRSS layer, so widgets cannot style around it.
        policy.minimumFocusRingPx = 3.0F;
        policy.contrastHook = [](NativeColor, const NativeColor background) {
            const auto Linearize = [](const float channel) {
                const float bounded = std::clamp(channel, 0.0F, 1.0F);
                return bounded <= 0.04045F
                    ? bounded / 12.92F
                    : std::pow((bounded + 0.055F) / 1.055F, 2.4F);
            };
            const float luminance =
                0.2126F * Linearize(background.red) +
                0.7152F * Linearize(background.green) +
                0.0722F * Linearize(background.blue);
            const float blackContrast = (luminance + 0.05F) / 0.05F;
            const float whiteContrast = 1.05F / (luminance + 0.05F);
            return blackContrast >= whiteContrast
                ? NativeColor{0, 0, 0, 1}
                : NativeColor{1, 1, 1, 1};
        };
    }
    return policy;
}

NativeColor CompositeNativeColor(
    NativeColor foreground,
    NativeColor background) noexcept {
    const auto ClampChannel = [](const float value, const float fallback) {
        return std::isfinite(value) ? std::clamp(value, 0.0F, 1.0F) : fallback;
    };
    foreground.red = ClampChannel(foreground.red, 0.0F);
    foreground.green = ClampChannel(foreground.green, 0.0F);
    foreground.blue = ClampChannel(foreground.blue, 0.0F);
    foreground.alpha = ClampChannel(foreground.alpha, 0.0F);
    background.red = ClampChannel(background.red, 0.0F);
    background.green = ClampChannel(background.green, 0.0F);
    background.blue = ClampChannel(background.blue, 0.0F);
    background.alpha = ClampChannel(background.alpha, 1.0F);

    const float outputAlpha = foreground.alpha +
        background.alpha * (1.0F - foreground.alpha);
    if (outputAlpha <= 0.0F) return NativeColor{0, 0, 0, 0};
    const auto Channel = [&](const float source, const float destination) {
        return (source * foreground.alpha +
                destination * background.alpha * (1.0F - foreground.alpha)) /
            outputAlpha;
    };
    return NativeColor{
        Channel(foreground.red, background.red),
        Channel(foreground.green, background.green),
        Channel(foreground.blue, background.blue),
        outputAlpha,
    };
}

NativeColor ResolveNativeSurfaceColor(
    const std::optional<NativeColor>& layer,
    NativeColor inheritedSurface,
    const float opacity) noexcept {
    if (!layer) return inheritedSurface;
    auto painted = *layer;
    painted.alpha *= std::isfinite(opacity)
        ? std::clamp(opacity, 0.0F, 1.0F)
        : 1.0F;
    return CompositeNativeColor(painted, inheritedSurface);
}

NativeStyleResult NativeStyleAdapter::Adapt(
    const WidgetComputedStyle& computed,
    const NativeStyleContext& requestedContext,
    const NativeAccessibilityPolicy& accessibility) {
    auto data = std::make_shared<NativeRenderStyle::Data>();
    std::vector<NativeStyleDiagnostic> diagnostics;
    const auto Add = [&](std::wstring_view property, std::wstring_view message) {
        if (diagnostics.size() < kMaximumDiagnostics)
            diagnostics.push_back({std::wstring(property.substr(0, 128)), std::wstring(message.substr(0, 256))});
    };

    NativeStyleContext context = requestedContext;
    if (!ValidContext(context)) {
        Add(L"<context>", L"Invalid layout context; safe 1920x1080 defaults were used.");
        context = {};
        context.focused = requestedContext.focused;
    }
    std::vector<std::pair<std::wstring, const WidgetStyleValue*>> properties;
    properties.reserve(computed.size());
    for (const auto& [name, value] : computed) properties.emplace_back(name, &value);
    std::sort(properties.begin(), properties.end(),
        [](const auto& left, const auto& right) { return left.first < right.first; });
    if (properties.size() > kMaximumProperties) {
        Add(L"<style>", L"Style property count exceeded the native safety limit; excess properties were ignored.");
        properties.resize(kMaximumProperties);
    }

    const auto Number = [&](std::wstring_view property, const WidgetStyleValue& value,
                            float minimum, float maximum) -> std::optional<float> {
        if ((value.kind != L"number" && value.kind != L"integer" && value.kind != L"ratio" &&
             value.kind != L"duration") || !value.number || !std::isfinite(*value.number)) {
            Add(property, L"Expected a finite typed numeric value.");
            return std::nullopt;
        }
        return static_cast<float>(std::clamp(*value.number, static_cast<double>(minimum), static_cast<double>(maximum)));
    };
    const auto Length = [&](std::wstring_view property, const WidgetStyleValue& value,
                            LengthBasis basis, float minimum, float maximum) -> std::optional<float> {
        if (value.kind != L"length" || !value.number) {
            Add(property, L"Expected a typed length value.");
            return std::nullopt;
        }
        auto resolved = ResolveLength(*value.number, value.unit, basis, context, minimum, maximum);
        if (!resolved) Add(property, L"Length number or unit was invalid.");
        return resolved;
    };
    const auto Color = [&](std::wstring_view property, const WidgetStyleValue& value) -> std::optional<NativeColor> {
        if (value.kind != L"color") {
            Add(property, L"Expected a typed color value.");
            return std::nullopt;
        }
        auto color = ParseColor(value.text);
        if (!color) Add(property, L"Canonical color was invalid.");
        return color;
    };

    std::optional<float> borderTopWidth;
    std::optional<float> borderRightWidth;
    std::optional<float> borderBottomWidth;
    std::optional<float> borderLeftWidth;
    std::optional<NativeColor> borderTopColor;
    std::optional<NativeColor> borderRightColor;
    std::optional<NativeColor> borderBottomColor;
    std::optional<NativeColor> borderLeftColor;

    for (const auto& [property, pointer] : properties) {
        const auto& value = *pointer;
        if (property == L"background") data->background = Color(property, value);
        else if (property == L"color") data->foreground = Color(property, value);
        else if (property == L"border-color") data->borderColor = Color(property, value);
        else if (property == L"border-top-color") borderTopColor = Color(property, value);
        else if (property == L"border-right-color") borderRightColor = Color(property, value);
        else if (property == L"border-bottom-color") borderBottomColor = Color(property, value);
        else if (property == L"border-left-color") borderLeftColor = Color(property, value);
        else if (property == L"outline-color") data->outlineColor = Color(property, value);
        else if (property == L"shadow-color") data->shadowColor = Color(property, value);
        else if (property == L"image-tint") data->imageTint = Color(property, value);
        else if (property == L"scrim-color") data->scrimColor = Color(property, value);
        else if (property == L"width") data->width = Length(property, value, LengthBasis::Width, 0, kMaximumResolvedDimension);
        else if (property == L"height") data->height = Length(property, value, LengthBasis::Height, 0, kMaximumResolvedDimension);
        else if (property == L"min-width") data->minWidth = Length(property, value, LengthBasis::Width, 0, kMaximumResolvedDimension);
        else if (property == L"min-height") data->minHeight = Length(property, value, LengthBasis::Height, 0, kMaximumResolvedDimension);
        else if (property == L"max-width") data->maxWidth = Length(property, value, LengthBasis::Width, 0, kMaximumResolvedDimension);
        else if (property == L"max-height") data->maxHeight = Length(property, value, LengthBasis::Height, 0, kMaximumResolvedDimension);
        else if (property == L"font-size") {
            if (const auto item = Length(property, value, LengthBasis::Font, 8, 256)) data->fontSize = *item;
        } else if (property == L"letter-spacing") {
            if (const auto item = Length(property, value, LengthBasis::Font, -64, 256)) data->letterSpacing = *item;
        } else if (property == L"corner-radius") {
            if (const auto item = Length(property, value, LengthBasis::MinimumDimension, 0, 4096)) data->cornerRadius = *item;
        } else if (property == L"outline-width") {
            if (const auto item = Length(property, value, LengthBasis::MinimumDimension, 0, 64)) data->outlineWidth = *item;
        } else if (property == L"outline-offset") {
            if (const auto item = Length(property, value, LengthBasis::MinimumDimension, -64, 128)) data->outlineOffset = *item;
        } else if (property == L"border-width") {
            if (const auto item = Length(property, value, LengthBasis::MinimumDimension, 0, 64)) data->borderWidth = *item;
        } else if (property == L"border-top-width") {
            borderTopWidth = Length(property, value, LengthBasis::MinimumDimension, 0, 64);
        } else if (property == L"border-right-width") {
            borderRightWidth = Length(property, value, LengthBasis::MinimumDimension, 0, 64);
        } else if (property == L"border-bottom-width") {
            borderBottomWidth = Length(property, value, LengthBasis::MinimumDimension, 0, 64);
        } else if (property == L"border-left-width") {
            borderLeftWidth = Length(property, value, LengthBasis::MinimumDimension, 0, 64);
        } else if (property == L"background-blur") {
            if (const auto item = Length(property, value, LengthBasis::MinimumDimension, 0, 256)) data->backgroundBlur = *item;
        } else if (property == L"shadow-blur") {
            if (const auto item = Length(property, value, LengthBasis::MinimumDimension, 0, 256)) data->shadowBlur = *item;
        } else if (property == L"shadow-offset-x") {
            if (const auto item = Length(property, value, LengthBasis::Width, -4096, 4096)) data->shadowOffsetX = *item;
        } else if (property == L"shadow-offset-y") {
            if (const auto item = Length(property, value, LengthBasis::Height, -4096, 4096)) data->shadowOffsetY = *item;
        } else if (property == L"gap" || property == L"padding" || property == L"margin") {
            if (value.kind != L"lengthList") Add(property, L"Expected a typed length-list value.");
            else if (const auto edges = ParseSpacing(value.text, context,
                         property == L"margin" ? -kMaximumResolvedSpacing : 0, kMaximumResolvedSpacing)) {
                if (property == L"gap") data->gap = *edges;
                else if (property == L"padding") data->padding = *edges;
                else data->margin = *edges;
            } else Add(property, L"Canonical spacing list was invalid.");
        } else if (property == L"opacity") {
            if (const auto item = Number(property, value, 0, 1)) data->opacity = *item;
        } else if (property == L"scale") {
            if (const auto item = Number(property, value, 0.5F, 2)) data->scale = *item;
        } else if (property == L"translate-x") {
            if (const auto item = Length(property, value, LengthBasis::Width,
                    -kMaximumResolvedTranslation, kMaximumResolvedTranslation))
                data->translateX = *item;
        } else if (property == L"translate-y") {
            if (const auto item = Length(property, value, LengthBasis::Height,
                    -kMaximumResolvedTranslation, kMaximumResolvedTranslation))
                data->translateY = *item;
        } else if (property == L"transition-duration") {
            if (const auto item = Number(property, value, 0, 2000)) data->transitionDuration = *item;
        } else if (property == L"aspect-ratio") {
            if (const auto item = Number(property, value, 0.2F, 5)) data->aspectRatio = *item;
        } else if (property == L"font-weight") {
            if (value.kind == L"keyword" && value.text == L"normal") data->fontWeight = 400;
            else if (value.kind == L"keyword" && value.text == L"bold") data->fontWeight = 700;
            else if (const auto item = Number(property, value, 100, 900))
                data->fontWeight = static_cast<int>(std::round(*item));
            else Add(property, L"Font weight was invalid.");
        } else if (property == L"font-family") {
            const bool controls = std::any_of(value.text.begin(), value.text.end(),
                [](wchar_t character) { return std::iswcntrl(character) != 0; });
            if (value.kind == L"fontFamily" && !value.text.empty() && value.text.size() <= 256 && !controls)
                data->fontFamily = value.text;
            else Add(property, L"Font family was invalid.");
        } else if (property == L"line-height") {
            if (const auto item = Number(property, value, 0.8F, 3)) data->lineHeight = *item;
        } else if (property == L"max-lines") {
            if (const auto item = Number(property, value, 1, 8)) data->maxLines = static_cast<int>(std::round(*item));
        } else if (property == L"flex-grow") {
            if (const auto item = Number(property, value, 0, 8)) data->flexGrow = *item;
        } else if (property == L"flex-shrink") {
            if (const auto item = Number(property, value, 0, 8)) data->flexShrink = *item;
        } else if (property == L"flex-basis") {
            if (value.kind == L"keyword" && value.text == L"auto") {
                data->flexBasis.reset(); data->flexBasisAuto = true;
            } else if (const auto item = Length(property, value, LengthBasis::Width, 0, kMaximumResolvedDimension)) {
                data->flexBasis = *item; data->flexBasisAuto = false;
            }
        } else if (property == L"flex-wrap") {
            if (auto item = Keyword<NativeFlexWrap>(value.text,
                    {{L"nowrap", NativeFlexWrap::NoWrap}, {L"wrap", NativeFlexWrap::Wrap}})) {
                data->flexWrap = *item;
            } else Add(property, L"Flex-wrap keyword was invalid.");
        } else if (property == L"direction") {
            if (auto item = Keyword<NativeDirection>(value.text, {{L"row", NativeDirection::Row}, {L"column", NativeDirection::Column}})) data->direction = *item;
            else Add(property, L"Direction keyword was invalid.");
        } else if (property == L"align") {
            if (auto item = Keyword<NativeAlign>(value.text, {{L"start", NativeAlign::Start}, {L"center", NativeAlign::Center}, {L"end", NativeAlign::End}, {L"stretch", NativeAlign::Stretch}})) data->align = *item;
            else Add(property, L"Align keyword was invalid.");
        } else if (property == L"justify") {
            if (auto item = Keyword<NativeJustify>(value.text, {{L"start", NativeJustify::Start}, {L"center", NativeJustify::Center}, {L"end", NativeJustify::End}, {L"space-between", NativeJustify::SpaceBetween}, {L"space-around", NativeJustify::SpaceAround}})) data->justify = *item;
            else Add(property, L"Justify keyword was invalid.");
        } else if (property == L"overflow") {
            if (auto item = Keyword<NativeOverflow>(value.text, {{L"clip", NativeOverflow::Clip}, {L"visible", NativeOverflow::Visible}})) data->overflow = *item;
            else Add(property, L"Overflow keyword was invalid.");
        } else if (property == L"object-fit") {
            if (auto item = Keyword<NativeImageFit>(value.text, {{L"contain", NativeImageFit::Contain}, {L"cover", NativeImageFit::Cover}, {L"fill", NativeImageFit::Fill}, {L"none", NativeImageFit::None}})) data->imageFit = *item;
            else Add(property, L"Object-fit keyword was invalid.");
        } else if (property == L"object-position") {
            if (auto item = Keyword<NativeObjectPosition>(value.text, {{L"center", NativeObjectPosition::Center}, {L"top", NativeObjectPosition::Top}, {L"right", NativeObjectPosition::Right}, {L"bottom", NativeObjectPosition::Bottom}, {L"left", NativeObjectPosition::Left}, {L"top-left", NativeObjectPosition::TopLeft}, {L"top-right", NativeObjectPosition::TopRight}, {L"bottom-left", NativeObjectPosition::BottomLeft}, {L"bottom-right", NativeObjectPosition::BottomRight}})) data->objectPosition = *item;
            else Add(property, L"Object-position keyword was invalid.");
        } else if (property == L"shape") {
            if (auto item = Keyword<NativeShape>(value.text, {{L"rectangle", NativeShape::Rectangle}, {L"rounded", NativeShape::Rounded}, {L"pill", NativeShape::Pill}, {L"circle", NativeShape::Circle}})) data->shape = *item;
            else Add(property, L"Shape keyword was invalid.");
        } else if (property == L"text-overflow") {
            if (auto item = Keyword<NativeTextOverflow>(value.text, {{L"clip", NativeTextOverflow::Clip}, {L"ellipsis", NativeTextOverflow::Ellipsis}})) data->textOverflow = *item;
            else Add(property, L"Text-overflow keyword was invalid.");
        } else if (property == L"overflow-wrap") {
            if (auto item = Keyword<NativeOverflowWrap>(value.text, {{L"normal", NativeOverflowWrap::Normal}, {L"anywhere", NativeOverflowWrap::Anywhere}})) data->overflowWrap = *item;
            else Add(property, L"Overflow-wrap keyword was invalid.");
        } else if (property == L"text-transform") {
            if (auto item = Keyword<NativeTextTransform>(value.text, {{L"none", NativeTextTransform::None}, {L"uppercase", NativeTextTransform::Uppercase}, {L"lowercase", NativeTextTransform::Lowercase}})) data->textTransform = *item;
            else Add(property, L"Text-transform keyword was invalid.");
        } else if (property == L"text-align") {
            if (auto item = Keyword<NativeTextAlign>(value.text, {{L"start", NativeTextAlign::Start}, {L"center", NativeTextAlign::Center}, {L"end", NativeTextAlign::End}})) data->textAlign = *item;
            else Add(property, L"Text-align keyword was invalid.");
        } else if (property == L"transition-easing") {
            if (auto item = Keyword<NativeTransitionEasing>(value.text, {{L"linear", NativeTransitionEasing::Linear}, {L"ease-out", NativeTransitionEasing::EaseOut}, {L"ease-in-out", NativeTransitionEasing::EaseInOut}, {L"spring", NativeTransitionEasing::Spring}})) data->transitionEasing = *item;
            else Add(property, L"Transition-easing keyword was invalid.");
        } else Add(property, L"Unknown computed style property was ignored.");
    }

    data->borderEdges = {
        {borderTopWidth.value_or(data->borderWidth), borderTopColor ? borderTopColor : data->borderColor},
        {borderRightWidth.value_or(data->borderWidth), borderRightColor ? borderRightColor : data->borderColor},
        {borderBottomWidth.value_or(data->borderWidth), borderBottomColor ? borderBottomColor : data->borderColor},
        {borderLeftWidth.value_or(data->borderWidth), borderLeftColor ? borderLeftColor : data->borderColor},
    };

    if (data->minWidth && data->maxWidth && *data->minWidth > *data->maxWidth) {
        std::swap(data->minWidth, data->maxWidth); Add(L"min-width", L"Minimum and maximum width were reordered.");
    }
    if (data->minHeight && data->maxHeight && *data->minHeight > *data->maxHeight) {
        std::swap(data->minHeight, data->maxHeight); Add(L"min-height", L"Minimum and maximum height were reordered.");
    }

    if (accessibility.reducedTransparency) {
        data->opacity = 1;
        data->backgroundBlur = 0;
        if (data->background) data->background->alpha = 1;
    }
    if (accessibility.reducedMotion) data->transitionDuration = 0;
    data->fontWeight = std::max(
        data->fontWeight,
        std::clamp(accessibility.minimumFontWeight, 100, 900));
    float textScale = accessibility.textScale;
    if (!std::isfinite(textScale) || textScale < 0.85F || textScale > 1.5F) {
        Add(L"<accessibility>", L"Text scale was outside its platform safety bounds; 100% was used.");
        textScale = 1.0F;
    }
    data->fontSize = std::clamp(data->fontSize * textScale, 8.0F, 256.0F);
    data->letterSpacing = std::clamp(data->letterSpacing * textScale, -64.0F, 256.0F);
    const NativeColor inheritedBackground =
        context.effectiveBackground.value_or(NativeColor{0, 0, 0, 1});
    const auto paintedBackground = data->background
        ? data->background
        : context.fallbackBackground;
    const NativeColor background = ResolveNativeSurfaceColor(
        paintedBackground, inheritedBackground, data->opacity);
    const auto ApplyContrast = [&](std::optional<NativeColor>& color,
                                   std::wstring_view property,
                                   const bool synthesizeMissing = false) {
        if (!accessibility.contrastHook || (!color && !synthesizeMissing)) return;
        try {
            NativeColor adjusted = accessibility.contrastHook(
                color.value_or(DefaultContrastingColor(background)), background);
            if (std::isfinite(adjusted.red) && std::isfinite(adjusted.green) &&
                std::isfinite(adjusted.blue) && std::isfinite(adjusted.alpha)) {
                adjusted.red = std::clamp(adjusted.red, 0.0F, 1.0F);
                adjusted.green = std::clamp(adjusted.green, 0.0F, 1.0F);
                adjusted.blue = std::clamp(adjusted.blue, 0.0F, 1.0F);
                adjusted.alpha = std::clamp(adjusted.alpha, 0.0F, 1.0F);
                color = adjusted;
            } else Add(property, L"Contrast hook returned a non-finite color.");
        } catch (...) { Add(property, L"Contrast hook failed; the computed color was retained."); }
    };
    // High contrast must own implicit renderer colors as well as explicit
    // WRSS colors. Materializing the foreground here prevents the renderer's
    // normal-mode text/icon fallback from bypassing the accessibility layer.
    ApplyContrast(data->foreground, L"color", true);
    if (context.focused) {
        data->outlineWidth = std::max(data->outlineWidth,
            ClampFinite(accessibility.minimumFocusRingPx, 2, 1, 16));
        if (!data->outlineColor) data->outlineColor = DefaultContrastingColor(background);
        ApplyContrast(data->outlineColor, L"outline-color");
    }

    return {NativeRenderStyle(std::shared_ptr<const NativeRenderStyle::Data>(std::move(data))),
            std::move(diagnostics)};
}

} // namespace widgetrail
