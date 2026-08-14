#pragma once

#include <optional>
#include <functional>
#include <span>
#include <string>
#include <string_view>

namespace gba {

struct PhysicalRect final {
    int left{};
    int top{};
    int right{};
    int bottom{};
};

struct OverlayMarginsDip final {
    float side{24.0F};
    float top{24.0F};
    float bottom{32.0F};
};

struct OverlayPlacement final {
    int x{};
    int y{};
    int width{};
    int height{};
};

struct OverlayRenderMetrics final {
    // Layout viewport in host design DIPs. This becomes smaller when the
    // monitor cannot fit the preferred surface, allowing GBSS/layout to
    // respond instead of scaling a fixed common-resolution canvas.
    float viewportWidthDip{};
    float viewportHeightDip{};
    // Direct2D transform applied after layout. This is the user-selected UI
    // zoom and deliberately does not absorb monitor-fit constraints.
    float interfaceScale{};
    // Physical pixels represented by one layout DIP. Declarative layout uses
    // this to snap edges crisply on arbitrary monitor DPI and UI zoom values.
    float physicalPixelsPerDip{};
};

enum class WidgetSurfaceMode {
    Adaptive,
    Compact,
    Standard,
    Wide,
};

enum class WidgetSurfaceAxisMode {
    Preferred,
    Content,
    FillAvailable,
};

inline constexpr int kWidgetSurfaceAxisProtocolVersion = 17;

[[nodiscard]] std::optional<WidgetSurfaceAxisMode> ParseWidgetSurfaceAxisMode(
    std::wstring_view value,
    int protocolVersion) noexcept;

/// Sanitized native projection of optional per-view widget surface hints.
/// Dimensions describe the useful floating panel, not an HWND. The host owns
/// all surrounding shell chrome and may choose a smaller safe result.
struct WidgetSurfaceRequest final {
    WidgetSurfaceMode mode{WidgetSurfaceMode::Adaptive};
    WidgetSurfaceAxisMode widthMode{WidgetSurfaceAxisMode::Preferred};
    WidgetSurfaceAxisMode heightMode{WidgetSurfaceAxisMode::Preferred};
    std::optional<float> preferredWidthDip;
    std::optional<float> preferredHeightDip;
    std::optional<float> minimumWidthDip;
    std::optional<float> minimumHeightDip;
};

struct ResolvedWidgetSurface final {
    // Host design DIPs before interface zoom is applied to physical placement.
    float windowWidthDip{};
    float windowHeightDip{};
    // Preferred floating-panel extent supplied to responsive shell geometry.
    float panelWidthDip{};
    float panelHeightDip{};
    bool constrainedByWorkArea{};
    unsigned int intrinsicMeasurementPasses{};
};

struct WidgetSurfaceIntrinsicExtent final {
    float widthDip{};
    float heightDip{};
};

using WidgetSurfaceIntrinsicMeasure = std::function<std::optional<WidgetSurfaceIntrinsicExtent>(
    float admittedMaximumWidthDip,
    float admittedMaximumHeightDip)>;

struct WidgetSurfaceConstraints final {
    PhysicalRect workArea{};
    unsigned int dpi{96};
    float interfaceScale{1.0F};
    float textScale{1.0F};
    OverlayMarginsDip margins{};
};

/// Resolves the semantic class and optional panel dimensions without consulting
/// a monitor. An absent request is the package-API-1 compatibility surface.
/// Malformed optional values are ignored in favor of bounded class defaults.
[[nodiscard]] ResolvedWidgetSurface ResolveWidgetSurfaceTarget(
    const std::optional<WidgetSurfaceRequest>& request,
    float textScale) noexcept;

/// Host-authoritative surface resolver. It applies text accessibility growth,
/// reserves shell tray/footer/controller chrome, then clamps the design-DIP
/// viewport against the selected monitor after DPI and interface zoom.
[[nodiscard]] std::optional<ResolvedWidgetSurface> ResolveWidgetSurface(
    const std::optional<WidgetSurfaceRequest>& request,
    WidgetSurfaceConstraints constraints,
    const WidgetSurfaceIntrinsicMeasure& measureIntrinsic = {}) noexcept;

struct OverlaySurfaceGeometry final {
    float panelX{};
    float panelY{};
    float panelWidth{};
    float panelHeight{};
    // The persistent, host-rendered icon tray occupies this band while a
    // widget is open. It is visual context only; widget input ownership is
    // independent of this geometry.
    float trayY{};
    float trayHeight{};
    float widgetViewportX{};
    float widgetViewportY{};
    float widgetViewportWidth{};
    float widgetViewportHeight{};
    // Host-owned footer chrome follows the widget viewport and remains inside
    // the panel. It may collapse at pathological heights.
    float footerY{};
    float footerHeight{};
};

enum class ControllerGuideDensity {
    Minimal,
    Compact,
    Full,
};

/// Selects a single-line controller guide for the actual content width and
/// accessibility text scale. The returned density only removes secondary
/// commands; it never relies on word wrapping inside fixed host chrome.
[[nodiscard]] ControllerGuideDensity ResolveControllerGuideDensity(
    float availableWidthDip,
    float textScale) noexcept;

struct ControllerGuideAction final {
    std::wstring_view button;
    std::wstring_view label;
};

/// Builds a sanitized, character-bounded one-line tray guide. Contextual
/// widget actions are preferred when present so hover shortcuts remain
/// discoverable; required Enter/Back escape actions are always retained.
[[nodiscard]] std::wstring BuildTrayControllerGuide(
    ControllerGuideDensity density,
    bool reorderMode,
    bool selectedBridgeWidget,
    std::span<const ControllerGuideAction> quickActions = {});

/// Computes a bottom-centered physical-pixel window rectangle that is fully
/// contained by the monitor work area. Logical dimensions and margins are
/// device-independent pixels; dpi is the target monitor's effective DPI.
[[nodiscard]] std::optional<OverlayPlacement> ComputeOverlayPlacement(
    PhysicalRect workArea,
    unsigned int dpi,
    float desiredWidthDip,
    float desiredHeightDip,
    OverlayMarginsDip margins = {}) noexcept;

/// Converts a physical client extent into the responsive logical viewport
/// used by the renderer. dpi is the window's current per-monitor DPI and
/// interfaceScale is the bounded host-owned accessibility zoom.
[[nodiscard]] std::optional<OverlayRenderMetrics> ComputeOverlayRenderMetrics(
    int clientWidthPx,
    int clientHeightPx,
    unsigned int dpi,
    float interfaceScale) noexcept;

/// Computes bounded shell and widget geometry for the current responsive
/// logical viewport. The result remains contained even for narrow portrait or
/// pathological tiny extents; content may collapse to zero when no safe space
/// remains, but coordinates never invert or escape the viewport. Preferred
/// body height is optional for legacy callers; when present it may shrink the
/// body but never moves the host-owned tray or expands the shared shell.
[[nodiscard]] std::optional<OverlaySurfaceGeometry> ComputeOverlaySurfaceGeometry(
    float viewportWidthDip,
    float viewportHeightDip,
    float preferredPanelWidthDip,
    std::optional<float> preferredPanelHeightDip = std::nullopt) noexcept;

} // namespace gba
