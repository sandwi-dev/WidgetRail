#pragma once

#include "WidgetBridgeClient.h"

#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <vector>

namespace gba {

struct NativeColor final {
    float red{};
    float green{};
    float blue{};
    float alpha{1.0F};
    friend bool operator==(const NativeColor&, const NativeColor&) = default;
};

struct NativeEdges final {
    float top{};
    float right{};
    float bottom{};
    float left{};
    friend bool operator==(const NativeEdges&, const NativeEdges&) = default;
};

struct NativeBorderEdgeStyle final {
    float widthPx{};
    std::optional<NativeColor> color;
    friend bool operator==(const NativeBorderEdgeStyle&, const NativeBorderEdgeStyle&) = default;
};

/// Resolved physical-edge border values. Each edge falls back to the uniform
/// border-width/border-color contract unless its corresponding edge-specific
/// GBSS property is present.
struct NativeBorderStyle final {
    NativeBorderEdgeStyle top;
    NativeBorderEdgeStyle right;
    NativeBorderEdgeStyle bottom;
    NativeBorderEdgeStyle left;
    friend bool operator==(const NativeBorderStyle&, const NativeBorderStyle&) = default;
};

enum class NativeDirection { Unspecified, Row, Column };
enum class NativeFlexWrap { NoWrap, Wrap };
enum class NativeAlign { Unspecified, Start, Center, End, Stretch };
enum class NativeJustify { Unspecified, Start, Center, End, SpaceBetween, SpaceAround };
enum class NativeOverflow { Clip, Visible };
enum class NativeImageFit { Contain, Cover, Fill, None };
enum class NativeObjectPosition { Center, Top, Right, Bottom, Left, TopLeft, TopRight, BottomLeft, BottomRight };
enum class NativeShape { Rectangle, Rounded, Pill, Circle };
enum class NativeTextOverflow { Clip, Ellipsis };
enum class NativeTextTransform { None, Uppercase, Lowercase };
enum class NativeTextAlign { Start, Center, End };
enum class NativeTransitionEasing { Linear, EaseOut, EaseInOut, Spring };

struct NativeStyleContext final {
    /// All dimensions are device-independent pixels.
    float viewportWidthPx{1920.0F};
    float viewportHeightPx{1080.0F};
    float parentWidthPx{1920.0F};
    float parentHeightPx{1080.0F};
    float parentFontSizePx{16.0F};
    float rootFontSizePx{16.0F};
    bool focused{};
    /// Opaque/effective surface inherited from the parent when this style
    /// does not declare its own background.
    std::optional<NativeColor> effectiveBackground;
    /// Renderer-owned background used when a semantic control paints a
    /// built-in surface without declaring `background` in GBSS (for example,
    /// the default button fill). Accessibility contrast is resolved against
    /// this painted fallback, not the inherited surface behind it.
    std::optional<NativeColor> fallbackBackground;
};

/// Composites an unassociated-alpha foreground over a background. Renderers
/// use this alongside NativeStyleAdapter so descendants and accessibility
/// policy agree on the surface that is actually visible.
[[nodiscard]] NativeColor CompositeNativeColor(
    NativeColor foreground,
    NativeColor background) noexcept;

/// Resolves an optional painted layer, including its style opacity, over an
/// already-effective parent surface. An absent layer preserves the parent.
[[nodiscard]] NativeColor ResolveNativeSurfaceColor(
    const std::optional<NativeColor>& layer,
    NativeColor inheritedSurface,
    float opacity = 1.0F) noexcept;

struct NativeAccessibilityPolicy final {
    bool reducedTransparency{};
    bool reducedMotion{};
    /// Host-owned minimum text weight applied after every theme layer.
    int minimumFontWeight{100};
    /// Host-owned text zoom applied after widget/theme style resolution.
    /// The platform settings contract bounds this to 0.85 through 1.5.
    float textScale{1.0F};
    float minimumFocusRingPx{2.0F};
    /// Called last for text and focused outline colors. Arguments are foreground/background.
    std::function<NativeColor(NativeColor, NativeColor)> contrastHook;
};

/// Resolves persisted preferences together with documented Windows system
/// accessibility state into the renderer-owned policy that is applied after
/// GBSS. The boolean inputs keep OS querying out of render/layout tests.
[[nodiscard]] NativeAccessibilityPolicy CreateNativeAccessibilityPolicy(
    const PlatformAppearance& appearance,
    bool systemHighContrast,
    bool systemAnimationsEnabled);

struct NativeStyleDiagnostic final {
    std::wstring property;
    std::wstring message;
};

/// Immutable, fully resolved style. It contains no GBSS strings except the
/// validated font family; all lengths are device-independent pixels.
class NativeRenderStyle final {
public:
    NativeRenderStyle();

    [[nodiscard]] const std::optional<NativeColor>& background() const noexcept;
    [[nodiscard]] const std::optional<NativeColor>& foreground() const noexcept;
    [[nodiscard]] const std::optional<NativeColor>& borderColor() const noexcept;
    [[nodiscard]] const std::optional<NativeColor>& outlineColor() const noexcept;
    [[nodiscard]] const std::optional<NativeColor>& shadowColor() const noexcept;
    [[nodiscard]] const std::optional<NativeColor>& imageTint() const noexcept;
    [[nodiscard]] const std::optional<NativeColor>& scrimColor() const noexcept;
    [[nodiscard]] const std::optional<float>& widthPx() const noexcept;
    [[nodiscard]] const std::optional<float>& heightPx() const noexcept;
    [[nodiscard]] const std::optional<float>& minWidthPx() const noexcept;
    [[nodiscard]] const std::optional<float>& minHeightPx() const noexcept;
    [[nodiscard]] const std::optional<float>& maxWidthPx() const noexcept;
    [[nodiscard]] const std::optional<float>& maxHeightPx() const noexcept;
    [[nodiscard]] float fontSizePx() const noexcept;
    [[nodiscard]] float letterSpacingPx() const noexcept;
    [[nodiscard]] float cornerRadiusPx() const noexcept;
    [[nodiscard]] float outlineWidthPx() const noexcept;
    [[nodiscard]] float outlineOffsetPx() const noexcept;
    [[nodiscard]] float borderWidthPx() const noexcept;
    [[nodiscard]] const NativeBorderStyle& borderEdges() const noexcept;
    [[nodiscard]] float backgroundBlurPx() const noexcept;
    [[nodiscard]] float shadowBlurPx() const noexcept;
    [[nodiscard]] float shadowOffsetXPx() const noexcept;
    [[nodiscard]] float shadowOffsetYPx() const noexcept;
    [[nodiscard]] const NativeEdges& gapPx() const noexcept;
    [[nodiscard]] const NativeEdges& paddingPx() const noexcept;
    [[nodiscard]] const NativeEdges& marginPx() const noexcept;
    [[nodiscard]] float opacity() const noexcept;
    [[nodiscard]] float scale() const noexcept;
    [[nodiscard]] float transitionDurationMilliseconds() const noexcept;
    [[nodiscard]] const std::optional<float>& aspectRatio() const noexcept;
    [[nodiscard]] NativeImageFit imageFit() const noexcept;
    [[nodiscard]] NativeObjectPosition objectPosition() const noexcept;
    [[nodiscard]] NativeShape shape() const noexcept;
    [[nodiscard]] int fontWeight() const noexcept;
    [[nodiscard]] const std::wstring& fontFamily() const noexcept;
    [[nodiscard]] float lineHeight() const noexcept;
    [[nodiscard]] int maxLines() const noexcept;
    [[nodiscard]] NativeTextOverflow textOverflow() const noexcept;
    [[nodiscard]] NativeTextTransform textTransform() const noexcept;
    [[nodiscard]] NativeTransitionEasing transitionEasing() const noexcept;
    [[nodiscard]] float flexGrow() const noexcept;
    [[nodiscard]] float flexShrink() const noexcept;
    [[nodiscard]] const std::optional<float>& flexBasisPx() const noexcept;
    [[nodiscard]] bool flexBasisAuto() const noexcept;
    [[nodiscard]] NativeFlexWrap flexWrap() const noexcept;
    [[nodiscard]] NativeAlign align() const noexcept;
    [[nodiscard]] NativeJustify justify() const noexcept;
    [[nodiscard]] NativeDirection direction() const noexcept;
    [[nodiscard]] NativeOverflow overflow() const noexcept;
    [[nodiscard]] NativeTextAlign textAlign() const noexcept;

private:
    struct Data;
    explicit NativeRenderStyle(std::shared_ptr<const Data> data);
    std::shared_ptr<const Data> data_;
    friend class NativeStyleAdapter;
};

struct NativeStyleResult final {
    NativeRenderStyle style;
    std::vector<NativeStyleDiagnostic> diagnostics;
};

class NativeStyleAdapter final {
public:
    /// Defensively adapts bridge-computed typed values. Unknown/malformed
    /// properties fall back to host defaults and produce bounded diagnostics.
    [[nodiscard]] static NativeStyleResult Adapt(
        const WidgetComputedStyle& computed,
        const NativeStyleContext& context,
        const NativeAccessibilityPolicy& accessibility = {});
};

} // namespace gba
