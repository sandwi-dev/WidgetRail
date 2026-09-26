#pragma once

#include "DeclarativeLayout.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <string_view>

namespace widgetrail::animation {
using Rect = declarative::Rect;

enum class SectionStyle { Paging, Slide, None, VerticalSlide, Reveal, CoverSlide };
enum class ModalStyle { Lift, None, Zoom };
enum class FocusStyle { Fade, Slide, None, Settle };
struct FocusPreset {
    std::wstring_view id, label;
    FocusStyle style;
    unsigned milliseconds;
    bool inPlace;
};
inline constexpr std::array FocusPresets{
    FocusPreset{L"fade", L"Fade", FocusStyle::Fade, 120, true},
    FocusPreset{L"settle", L"Settle", FocusStyle::Settle, 220, true},
    FocusPreset{L"slide", L"Slide", FocusStyle::Slide, 140, false},
    FocusPreset{L"none", L"None", FocusStyle::None, 0, false},
};
constexpr FocusStyle ParseFocusStyle(std::wstring_view id) noexcept {
    for (const auto &preset : FocusPresets)
        if (preset.id == id) return preset.style;
    return FocusStyle::Fade;
}
constexpr const FocusPreset &FocusPresetFor(FocusStyle style) noexcept {
    for (const auto &preset : FocusPresets)
        if (preset.style == style) return preset;
    return FocusPresets.front();
}

// Host preferences, independent of widget declarations and graphics resources.
// Stable IDs can be persisted by settings without serializing enum ordinals.
struct SectionPreset {
    std::wstring_view id, label;
    SectionStyle style;
};
inline constexpr std::array SectionPresets{
    SectionPreset{L"slide", L"Slide", SectionStyle::Slide},
    SectionPreset{L"paging", L"Paging", SectionStyle::Paging},
    SectionPreset{L"verticalslide", L"Vertical slide", SectionStyle::VerticalSlide},
    SectionPreset{L"reveal", L"Reveal", SectionStyle::Reveal},
    SectionPreset{L"coverslide", L"Cover slide", SectionStyle::CoverSlide},
    SectionPreset{L"none", L"None", SectionStyle::None},
};
constexpr SectionStyle ParseSectionStyle(std::wstring_view id) noexcept {
    for (const auto &preset : SectionPresets)
        if (preset.id == id)
            return preset.style;
    return SectionStyle::Slide;
}
constexpr std::wstring_view SectionStyleId(SectionStyle style) noexcept {
    for (const auto &preset : SectionPresets)
        if (preset.style == style)
            return preset.id;
    return L"slide";
}
struct Options {
    SectionStyle section{SectionStyle::Slide};
    ModalStyle modal{ModalStyle::Zoom};
    double speed{1};
    FocusStyle focus{FocusStyle::Fade};
    bool operator==(const Options &) const = default;
};

// Both CPU sampling and DirectComposition consume this normalized polynomial.
// Keeping the coefficients shared prevents capture/input from drifting away
// from the pixels displayed by DWM.
struct Curve {
    double linear{}, quadratic{3}, cubic{-2}; // smoothstep: gentle at both ends
    constexpr bool operator==(const Curve &) const = default;
    double Evaluate(double t) const noexcept {
        t = std::clamp(t, 0.0, 1.0);
        return ((cubic * t + quadratic) * t + linear) * t;
    }
    std::array<float, 4> Coefficients(float from, float to, double seconds) const noexcept {
        if (seconds <= 0)
            return {to, 0, 0, 0};
        const double delta = static_cast<double>(to) - from;
        return {from, static_cast<float>(delta * linear / seconds),
                static_cast<float>(delta * quadratic / (seconds * seconds)),
                static_cast<float>(delta * cubic / (seconds * seconds * seconds))};
    }
};
inline constexpr Curve Smooth{0, 3, -2};
inline constexpr Curve EaseOut{3, -3, 1};

struct Pose {
    Rect bounds;
    float opacity{1};
    Rect clip;
};
struct Motion {
    Pose from, to;
    std::int64_t start{}, duration{};
    Curve curve{Smooth};
};
inline Pose Sample(const Motion &motion, std::int64_t now) noexcept {
    if (motion.duration <= 0 || now >= motion.start + motion.duration)
        return motion.to;
    const auto p =
        static_cast<float>(motion.curve.Evaluate(static_cast<double>(now - motion.start) / motion.duration));
    const auto mix = [p](float a, float b) { return a + (b - a) * p; };
    const auto rect = [&](Rect a, Rect b) {
        return Rect{mix(a.x, b.x), mix(a.y, b.y), mix(a.width, b.width), mix(a.height, b.height)};
    };
    return {rect(motion.from.bounds, motion.to.bounds), mix(motion.from.opacity, motion.to.opacity),
            rect(motion.from.clip, motion.to.clip)};
}

struct Transform {
    float scaleX{1}, scaleY{1}, offsetX{}, offsetY{};
};
inline Transform Map(Rect basis, Rect pose) noexcept {
    const float sx = basis.width > 0 ? pose.width / basis.width : 1;
    const float sy = basis.height > 0 ? pose.height / basis.height : 1;
    return {sx, sy, pose.x - basis.x * sx, pose.y - basis.y * sy};
}

struct Recipe {
    Pose from, to;
    unsigned milliseconds{};
    Curve curve{Smooth};
    Motion Start(std::int64_t now, std::int64_t ticksPerSecond, double speed = 1) const noexcept {
        const double boundedSpeed = std::isfinite(speed) ? std::clamp(speed,.5,2.0) : 1;
        return {from, to, now, static_cast<std::int64_t>(ticksPerSecond * milliseconds / (1000 * boundedSpeed)), curve};
    }
};
inline Recipe Stationary(Rect bounds, Rect clip) noexcept {
    return {{bounds, 1, clip}, {bounds, 1, clip}, 0, Smooth};
}
inline Recipe PopupEnter(Rect bounds, Rect clip, Rect anchor) noexcept {
    auto recipe = Stationary(bounds, clip);
    constexpr float scale = .92F;
    const float x = std::clamp(anchor.x + anchor.width * .5F, bounds.x, bounds.x + bounds.width);
    const float y = std::clamp(anchor.y + anchor.height * .5F, bounds.y, bounds.y + bounds.height);
    recipe.from.bounds = {x + (bounds.x - x) * scale, y + (bounds.y - y) * scale,
        bounds.width * scale, bounds.height * scale};
    recipe.milliseconds = 160;
    return recipe;
}
inline unsigned SectionDuration(SectionStyle style) noexcept {
    return style == SectionStyle::None ? 0 : 208;
}
struct SectionPlan {
    Recipe incoming, outgoing;
};
inline SectionPlan Section(SectionStyle style, Rect bounds, Rect clip, Rect outgoing,
                           int direction) noexcept {
    SectionPlan plan{Stationary(bounds, clip), Stationary(outgoing, clip)};
    plan.incoming.milliseconds = plan.outgoing.milliseconds = SectionDuration(style);
    if (style == SectionStyle::None)
        return plan;
    if (style == SectionStyle::Slide) {
        const float travel = std::max(bounds.width, clip.width) * (direction < 0 ? -1 : 1);
        plan.incoming.from.bounds.x += travel;
        plan.outgoing.to.bounds.x -= travel;
        return plan;
    }
    if (style == SectionStyle::VerticalSlide) {
        const float travel = std::max(bounds.height, clip.height) * (direction < 0 ? -1 : 1);
        plan.incoming.from.bounds.y += travel;
        plan.outgoing.to.bounds.y -= travel;
        return plan;
    }
    if (style == SectionStyle::Reveal || style == SectionStyle::CoverSlide) {
        // Complementary clips keep translucent content from displaying both
        // pages at once. Clips live in viewport coordinates, outside transforms.
        const bool forward = direction >= 0;
        plan.incoming.from.clip = {forward ? clip.x + clip.width : clip.x, clip.y, 0, clip.height};
        plan.outgoing.to.clip = {forward ? clip.x : clip.x + clip.width, clip.y, 0, clip.height};
        if (style == SectionStyle::CoverSlide)
            plan.incoming.from.bounds.x += (forward ? 1 : -1) * std::max(bounds.width, clip.width);
        return plan;
    }
    // The new page rises from below; the old page recedes behind it. Neither
    // changes opacity. A moving reveal edge prevents translucent page content
    // from showing old text through the incoming page.
    const float travel = std::max(bounds.height, clip.height);
    plan.incoming.from.bounds.y += travel;
    constexpr float depthScale = .96F;
    auto &back = plan.outgoing.to.bounds;
    back.x += back.width * (1 - depthScale) * .5F;
    back.y += back.height * (1 - depthScale) * .5F - travel * .08F;
    back.width *= depthScale;
    back.height *= depthScale;
    const float top = std::min(bounds.y, clip.y);
    plan.outgoing.from.clip = {clip.x, top, clip.width, plan.incoming.from.bounds.y - top};
    plan.outgoing.to.clip = {clip.x, top, clip.width, bounds.y - top};
    return plan;
}
inline Recipe Layout(SectionStyle style, Rect bounds, Rect clip, Pose previous, bool resize) noexcept {
    auto recipe = Stationary(bounds, clip);
    recipe.from.bounds = previous.bounds;
    // Labels move without stretching their glyphs. Selection surfaces may
    // interpolate their size to fit the newly selected label.
    if (!resize) {
        recipe.from.bounds.width = bounds.width;
        recipe.from.bounds.height = bounds.height;
    }
    recipe.milliseconds = SectionDuration(style);
    return recipe;
}
inline Rect ModalPose(ModalStyle style, Rect bounds, float amount) noexcept {
    if (style == ModalStyle::Zoom) {
        const float inset = .05F * amount;
        bounds.x += bounds.width * inset;
        bounds.y += bounds.height * inset;
        bounds.width *= 1 - 2 * inset;
        bounds.height *= 1 - 2 * inset;
    }
    bounds.y += 14 * amount;
    return bounds;
}
inline Recipe ModalEnter(ModalStyle style, Rect bounds, Rect clip, float opacity, bool scrim) noexcept {
    auto recipe = Stationary(bounds, clip);
    recipe.milliseconds = style == ModalStyle::None ? 0 : 260;
    recipe.curve = Smooth;
    recipe.from.opacity = opacity;
    if (!scrim)
        recipe.from.bounds = ModalPose(style, bounds, 1 - opacity);
    return recipe;
}
inline Recipe ModalExit(ModalStyle style, Rect bounds, Rect clip, float opacity, bool scrim) noexcept {
    auto recipe = Stationary(bounds, clip);
    recipe.milliseconds = style == ModalStyle::None ? 0 : 260;
    recipe.curve = Smooth;
    recipe.from.opacity = scrim ? opacity : 1;
    recipe.to.opacity = 0;
    if (!scrim)
        recipe.to.bounds = ModalPose(style, bounds, opacity);
    return recipe;
}
} // namespace widgetrail::animation
