#pragma once

#include <optional>

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
};

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
/// remains, but coordinates never invert or escape the viewport.
[[nodiscard]] std::optional<OverlaySurfaceGeometry> ComputeOverlaySurfaceGeometry(
    float viewportWidthDip,
    float viewportHeightDip,
    float preferredPanelWidthDip) noexcept;

} // namespace gba
