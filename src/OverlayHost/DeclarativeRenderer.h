#pragma once

#include "DeclarativeLayout.h"
#include "DeclarativeMotion.h"
#include "NativeStyle.h"
#include "WidgetBridgeClient.h"

#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <cstdint>
#include <map>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace gba {

class RemoteImageCache;

enum class RenderDiagnosticSeverity {
    Warning,
    Error,
};

struct RenderDiagnostic final {
    RenderDiagnosticSeverity severity{RenderDiagnosticSeverity::Warning};
    std::wstring nodeId;
    std::wstring code;
    std::wstring message;
};

struct RenderHitRegion final {
    std::wstring nodeId;
    declarative::Rect rect;
    bool enabled{};
};

struct RenderResult final {
    bool succeeded{};
    /// True only while at least one paint-only node transition requires a
    /// future frame. The renderer never owns a timer or animation thread.
    bool animationActive{};
    std::vector<RenderDiagnostic> diagnostics;
    std::vector<RenderHitRegion> hitRegions;
#ifdef GBA_DECLARATIVE_RENDERER_TESTING
    // Test-only exact geometry seam. Production results intentionally retain
    // only interactive geometry so ordinary paints do not allocate two maps
    // for every decorative and structural node.
    std::map<std::wstring, declarative::Rect, std::less<>> elementRects;
    std::map<std::wstring, declarative::Rect, std::less<>> elementVisibleRects;
#endif
    std::map<std::wstring, declarative::Rect, std::less<>> focusRects;
    // Full logical controller geometry includes offscreen descendants of a
    // semantic scroll container. Pointer hit regions remain visible-only.
    std::map<std::wstring, declarative::Rect, std::less<>> navigationRects;
    std::map<std::wstring, bool, std::less<>> navigationEnabled;
    std::set<std::wstring, std::less<>> revealableFocusIds;
    std::map<std::wstring, std::wstring, std::less<>> focusScopes;
    std::map<std::wstring, float, std::less<>> scrollOffsets;
    std::optional<declarative::Rect> currentFocusRect;
    // Deferred outlines normally escape ordinary layout clips. A focused
    // scroll descendant additionally carries the effective scroll viewport so
    // its ring cannot paint over adjacent rows or host chrome.
    std::optional<declarative::Rect> currentFocusOutlineClip;
};

struct ImagePlacement final {
    declarative::Rect destination;
    declarative::Rect source;
};

/// Button content treats an optional leading icon plus label as one visual
/// group for edge alignment. Center alignment keeps the primary label itself
/// centered and places the leading icon beside it when space permits.
struct ButtonContentPlacement final {
    declarative::Rect leading;
    declarative::Rect text;
};

struct DeclarativeRenderOptions final {
    float pixelScale{1.0F};
    float rootFontSizePx{16.0F};
    /// Host surface dimensions before detached chrome (such as the controller
    /// guide footer) is removed from the widget content viewport. Responsive
    /// branches describe the surface the widget requested, not an internal
    /// post-chrome layout rectangle.
    std::optional<declarative::Size> responsiveViewport;
    std::optional<NativeColor> surfaceBackground;
    /// Host-owned rounded viewport mask. Widget roots may paint an opaque
    /// full-viewport background, but cannot square off the shell panel's
    /// corners or paint into detached host chrome.
    float surfaceCornerRadiusPx{};
    NativeAccessibilityPolicy accessibility;
    /// Optional deterministic monotonic clock seam. Production supplies the
    /// host tick; native tests can advance exact transition positions.
    std::optional<std::uint64_t> animationTimestampMilliseconds;
    /// Exact node-ID optimistic values owned by the controller slider state.
    /// The immutable widget snapshot remains authoritative after acknowledgement
    /// or timeout.
    std::map<std::wstring, double, std::less<>> sliderValueOverrides;
    /// Exact focused/actionable node held by a physical controller press. The
    /// host owns this transient state; widget snapshots remain immutable.
    std::wstring pressedElementId;
};

[[nodiscard]] constexpr bool IsCompactResponsiveSurface(
    const declarative::Size surface) noexcept {
    return surface.width < 960.0F || surface.height < 540.0F;
}

/// Resolves the exact bridge-computed state used for a native paint. Pressed
/// is intentionally layered on focused because controller activation always
/// belongs to the focused actionable element.
[[nodiscard]] WidgetComputedStyle ResolveDeclarativeComputedStyle(
    const WidgetNode& node,
    bool focused,
    bool pressed);

/// Accessibility-safe state presentation. High contrast and reduced
/// transparency retain semantic state cues without fading content below the
/// contrast selected by NativeStyleAdapter.
[[nodiscard]] float DeclarativeStateOpacityFactor(
    bool disabled,
    bool busy,
    const NativeAccessibilityPolicy& accessibility) noexcept;

[[nodiscard]] bool UseAccessibleDeclarativeStateCue(
    const NativeAccessibilityPolicy& accessibility) noexcept;

class DeclarativeRenderer final {
public:
    DeclarativeRenderer(
        ID2D1Factory* d2dFactory,
        IDWriteFactory* writeFactory,
        RemoteImageCache* imageCache) noexcept;

    DeclarativeRenderer(const DeclarativeRenderer&) = delete;
    DeclarativeRenderer& operator=(const DeclarativeRenderer&) = delete;

    [[nodiscard]] RenderResult Render(
        ID2D1RenderTarget* renderTarget,
        const WidgetSnapshot& snapshot,
        std::wstring_view focusedElementId,
        declarative::Rect viewport,
        const DeclarativeRenderOptions& options = {});

    void DiscardTargetResources() noexcept;

    /// Drops host-owned offsets when a widget instance is removed or replaced.
    void ForgetWidgetState(std::wstring_view widgetInstanceId) noexcept;

    // Pure deterministic seam used by the renderer and native tests.
    [[nodiscard]] static ImagePlacement ComputeImagePlacement(
        declarative::Size imageSize,
        declarative::Rect destination,
        NativeImageFit fit,
        NativeObjectPosition position) noexcept;

    [[nodiscard]] static ButtonContentPlacement ComputeButtonContentPlacement(
        declarative::Rect content,
        float leadingSize,
        float measuredTextWidth,
        bool hasLeading,
        bool hasText,
        bool reserveTrailingStateCue,
        NativeTextAlign alignment = NativeTextAlign::Center) noexcept;

private:
    struct PreparedNode;
    struct RenderPass;
    struct ScrollStateEntry final {
        float offset{};
        std::uint64_t lastAccess{};
    };

    [[nodiscard]] Microsoft::WRL::ComPtr<ID2D1Bitmap> GetImageBitmap(
        ID2D1RenderTarget* renderTarget,
        const WidgetNode& node,
        RenderPass& pass);
    [[nodiscard]] bool EnsureSurfaceClip(
        ID2D1RenderTarget* renderTarget,
        declarative::Rect viewport,
        float radius);

    ID2D1Factory* d2dFactory_{};
    IDWriteFactory* writeFactory_{};
    RemoteImageCache* imageCache_{};
    ID2D1RenderTarget* bitmapTarget_{};
    ID2D1RenderTarget* surfaceClipTarget_{};
    declarative::Rect surfaceClipRect_{};
    float surfaceClipRadius_{};
    Microsoft::WRL::ComPtr<ID2D1Layer> surfaceClipLayer_;
    Microsoft::WRL::ComPtr<ID2D1RoundedRectangleGeometry> surfaceClipGeometry_;
    std::unordered_map<std::wstring, Microsoft::WRL::ComPtr<ID2D1Bitmap>> bitmaps_;
    std::unordered_map<std::wstring, ScrollStateEntry> scrollOffsets_;
    std::uint64_t scrollStateAccessClock_{};
    DeclarativeMotionTimeline motionTimeline_;
};

} // namespace gba
