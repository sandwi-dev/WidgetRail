#include "OverlayPlacement.h"
#include "OverlayTargeting.h"

#include <cstdlib>
#include <cmath>
#include <iostream>
#include <limits>
#include <array>

namespace {

int checks{};

void Check(bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void CheckNear(
    const float actual,
    const float expected,
    const char* message,
    const float tolerance = 0.01F) {
    Check(std::abs(actual - expected) <= tolerance, message);
}

void FullyContained(const gba::OverlayPlacement& value, const gba::PhysicalRect& work) {
    Check(value.width > 0 && value.height > 0, "placement has positive size");
    Check(value.x >= work.left && value.y >= work.top, "placement starts inside work area");
    Check(value.x + value.width <= work.right, "placement right edge is contained");
    Check(value.y + value.height <= work.bottom, "placement bottom edge is contained");
}

void SurfaceContained(
    const gba::OverlaySurfaceGeometry& geometry,
    const float width,
    const float height) {
    constexpr float tolerance = 0.002F;
    for (const float value : {
             geometry.panelX, geometry.panelY, geometry.panelWidth,
             geometry.panelHeight, geometry.trayY, geometry.trayHeight,
             geometry.widgetViewportX, geometry.widgetViewportY,
             geometry.widgetViewportWidth, geometry.widgetViewportHeight,
             geometry.footerY, geometry.footerHeight,
         }) {
        Check(std::isfinite(value), "surface geometry remains finite");
        Check(value >= 0.0F, "surface geometry never becomes negative");
    }
    Check(geometry.panelX + geometry.panelWidth <= width + tolerance &&
          geometry.panelY + geometry.panelHeight <= height + tolerance,
          "panel remains fully contained");
    Check(std::abs(
              geometry.panelX + geometry.panelWidth * 0.5F - width * 0.5F) <= tolerance,
          "responsive panel remains horizontally centered");
    Check(geometry.trayY + geometry.trayHeight <= height + tolerance,
          "persistent tray band remains fully contained");
    Check(geometry.panelY + geometry.panelHeight <= geometry.trayY + tolerance,
          "floating widget panel never overlaps persistent tray band");
    Check(geometry.widgetViewportX >= geometry.panelX - tolerance &&
          geometry.widgetViewportY >= geometry.panelY - tolerance,
          "widget viewport begins inside panel");
    Check(geometry.widgetViewportX + geometry.widgetViewportWidth <=
              geometry.panelX + geometry.panelWidth + tolerance &&
          geometry.widgetViewportY + geometry.widgetViewportHeight <=
              geometry.panelY + geometry.panelHeight + tolerance,
          "widget viewport remains contained by panel");
    Check(geometry.footerY >= geometry.panelY - tolerance &&
          geometry.footerY + geometry.footerHeight <=
              geometry.panelY + geometry.panelHeight + tolerance,
          "adaptive footer remains inside panel");
    Check(geometry.widgetViewportY + geometry.widgetViewportHeight <=
              geometry.footerY + tolerance,
          "widget content never overlaps host footer chrome");
}

void ColdDashboardProfilesUseOneBottomAnchor() {
    struct Profile final {
        gba::PhysicalRect work;
        unsigned int dpi;
        float interfaceScale;
    };
    constexpr std::array profiles{
        Profile{{0, 0, 854, 680}, 96, 1.0F},
        Profile{{0, 0, 1280, 680}, 96, 1.0F},
        Profile{{0, 0, 1920, 1040}, 96, 1.0F},
        Profile{{0, 0, 2560, 1400}, 96, 1.0F},
        Profile{{0, 0, 1920, 1040}, 120, 1.0F},
        Profile{{0, 0, 2560, 1320}, 96, 1.5F},
        Profile{{1920, 100, 7040, 1320}, 120, 1.05F},
        Profile{{-3440, -200, 0, 1240}, 144, 1.0F},
    };
    for (const auto& profile : profiles) {
        const auto dashboard = gba::ComputeOverlayPlacement(
            profile.work, profile.dpi,
            1180.0F * profile.interfaceScale,
            180.0F * profile.interfaceScale);
        const auto host = gba::ComputeOverlayPlacement(
            profile.work, profile.dpi,
            1180.0F * profile.interfaceScale,
            700.0F * profile.interfaceScale);
        Check(dashboard.has_value() && host.has_value(),
              "cold dashboard and shared host placements resolve");
        FullyContained(*dashboard, profile.work);
        FullyContained(*host, profile.work);
        const auto presentation = gba::PlanCompositionMotion(
            static_cast<unsigned int>(host->width),
            static_cast<unsigned int>(host->height),
            static_cast<unsigned int>(dashboard->width),
            static_cast<unsigned int>(dashboard->height),
            static_cast<float>(dashboard->width),
            static_cast<float>(dashboard->height),
            gba::CompositionVerticalAnchor::Bottom);
        const float visibleLeft = static_cast<float>(host->x) + presentation.offsetX;
        const float visibleTop = static_cast<float>(host->y) + presentation.offsetY;
        const float visibleRight = visibleLeft + dashboard->width * presentation.scaleX;
        const float visibleBottom = visibleTop + dashboard->height * presentation.scaleY;
        Check(std::abs(visibleBottom - static_cast<float>(host->y + host->height)) <= 0.01F,
              "first visible dashboard content shares the host bottom edge");
        Check(visibleLeft >= profile.work.left && visibleTop >= profile.work.top &&
              visibleRight <= profile.work.right && visibleBottom <= profile.work.bottom,
              "first visible dashboard content remains in live rcWork");

        const float localPointerX = dashboard->width * 0.5F;
        const float localPointerY = dashboard->height - 48.0F;
        const float hostPointerX = presentation.offsetX +
            localPointerX * presentation.scaleX;
        const float hostPointerY = presentation.offsetY +
            localPointerY * presentation.scaleY;
        CheckNear(
            (hostPointerX - presentation.offsetX) / presentation.scaleX,
            localPointerX,
            "bottom-anchored pointer inverse retains dashboard x");
        CheckNear(
            (hostPointerY - presentation.offsetY) / presentation.scaleY,
            localPointerY,
            "bottom-anchored pointer inverse retains tray y");

        const float titleTop = visibleTop + 10.0F * presentation.scaleY;
        const float trayBottom = visibleBottom - 14.0F * presentation.scaleY;
        Check(titleTop >= visibleTop && trayBottom <= visibleBottom &&
              trayBottom > titleTop,
              "dashboard title and tray UIA transforms share visible content bounds");

        const auto widget = gba::PlanCompositionMotion(
            static_cast<unsigned int>(host->width),
            static_cast<unsigned int>(host->height),
            static_cast<unsigned int>(host->width),
            static_cast<unsigned int>(host->height),
            static_cast<float>(host->width),
            static_cast<float>(host->height),
            gba::CompositionVerticalAnchor::Bottom);
        Check(widget.offsetY == 0.0F,
              "first full widget retains the same shared-host bottom anchor");
        const auto reshown = gba::PlanCompositionMotion(
            static_cast<unsigned int>(host->width),
            static_cast<unsigned int>(host->height),
            static_cast<unsigned int>(dashboard->width),
            static_cast<unsigned int>(dashboard->height),
            static_cast<float>(dashboard->width),
            static_cast<float>(dashboard->height),
            gba::CompositionVerticalAnchor::Bottom);
        Check(reshown.offsetY == presentation.offsetY,
              "dashboard re-show restores the exact cold bottom anchor");
    }
}

} // namespace

int main() {
    ColdDashboardProfilesUseOneBottomAnchor();
    using namespace gba;

    Check(ResolveControllerGuideDensity(800.0F, 1.0F) ==
              ControllerGuideDensity::Full,
          "wide footer uses the complete single-line controller guide");
    Check(ResolveControllerGuideDensity(520.0F, 1.0F) ==
              ControllerGuideDensity::Compact,
          "compact widget uses the abbreviated single-line controller guide");
    Check(ResolveControllerGuideDensity(320.0F, 1.0F) ==
              ControllerGuideDensity::Minimal,
          "narrow widget preserves only essential controller guidance");
    Check(ResolveControllerGuideDensity(800.0F, 1.5F) ==
              ControllerGuideDensity::Compact,
          "large accessibility text selects a safer guide density");
    Check(ResolveControllerGuideDensity(
              std::numeric_limits<float>::quiet_NaN(), 1.0F) ==
              ControllerGuideDensity::Minimal,
          "invalid guide width fails to the non-wrapping minimal form");
    const std::array quickActions{
        ControllerGuideAction{L"LB", L"Previous track"},
        ControllerGuideAction{L"X", L"Play / pause"},
        ControllerGuideAction{L"RB", L"Next track"},
    };
    const auto contextualGuide = BuildTrayControllerGuide(
        ControllerGuideDensity::Compact, false, true, quickActions);
    Check(contextualGuide.find(L"LB ") != std::wstring::npos &&
              contextualGuide.find(L"X ") != std::wstring::npos &&
              contextualGuide.find(L"RB ") != std::wstring::npos,
          "compact YT guide keeps all three hover quick actions discoverable");
    Check(contextualGuide.find(L"↑/A Enter") != std::wstring::npos &&
              contextualGuide.find(L"Y Tap/Hold") != std::wstring::npos &&
              contextualGuide.find(L"B Close") != std::wstring::npos,
          "contextual guide retains enter, tap-hold, and escape affordances");
    Check(contextualGuide.size() <= 78U &&
              contextualGuide.find_first_of(L"\r\n") == std::wstring::npos,
          "compact contextual guide is sanitized to one bounded line");
    const std::array hostileAction{
        ControllerGuideAction{L"LB\nRB", L"A deliberately enormous\nwidget supplied label"},
    };
    const auto hostileGuide = BuildTrayControllerGuide(
        ControllerGuideDensity::Minimal, false, true, hostileAction);
    Check(hostileGuide.size() <= 28U &&
              hostileGuide.find_first_of(L"\r\n") == std::wstring::npos,
          "untrusted widget hint text cannot wrap or overflow minimal chrome");
    Check(BuildTrayControllerGuide(
              ControllerGuideDensity::Full, false, true).find(
                  L"Y Tap reorder / Hold refresh") != std::wstring::npos,
          "full tray guide explains both sides of the Y gesture");
    Check(BuildTrayControllerGuide(
              ControllerGuideDensity::Minimal, false, true) ==
              L"Y Tap/Hold   B Close",
          "minimal tray guide keeps the gesture and escape discoverable");
    Check(BuildTrayControllerGuide(
              ControllerGuideDensity::Full, false, false).find(L"Hold") ==
              std::wstring::npos,
          "tray guide does not advertise hold refresh without widget opt-in");
    Check(BuildTrayControllerGuide(
              ControllerGuideDensity::Full, true, true).find(L"Y Done") !=
              std::wstring::npos,
          "reorder mode keeps tap-Y completion for refresh-capable widgets");

    const auto legacySurface = ResolveWidgetSurfaceTarget(std::nullopt, 1.0F);
    CheckNear(legacySurface.windowWidthDip, 1180.0F,
              "missing hints preserve the v1 window width");
    CheckNear(legacySurface.windowHeightDip, 700.0F,
              "missing hints preserve the v1 window height");
    CheckNear(legacySurface.panelWidthDip, 880.0F,
              "missing hints preserve the v1 panel width");

    WidgetSurfaceRequest compactRequest{WidgetSurfaceMode::Compact};
    const auto compactTarget = ResolveWidgetSurfaceTarget(compactRequest, 1.0F);
    CheckNear(compactTarget.windowWidthDip, 632.0F,
              "compact mode adds bounded side chrome");
    CheckNear(compactTarget.windowHeightDip, 598.0F,
              "compact mode reserves panel, footer, and tray height");
    CheckNear(compactTarget.panelWidthDip, 560.0F,
              "compact mode resolves its documented panel width");
    CheckNear(compactTarget.panelHeightDip, 420.0F,
              "compact mode resolves its documented panel height");

    WidgetSurfaceRequest standardRequest{WidgetSurfaceMode::Standard};
    const auto standardTarget = ResolveWidgetSurfaceTarget(standardRequest, 1.0F);
    CheckNear(standardTarget.windowWidthDip, 952.0F,
              "standard mode does not retain the legacy dead side space");
    CheckNear(standardTarget.windowHeightDip, 698.0F,
              "standard mode preserves the established panel shape");

    WidgetSurfaceRequest wideRequest{WidgetSurfaceMode::Wide};
    const auto wideTarget = ResolveWidgetSurfaceTarget(wideRequest, 1.0F);
    CheckNear(wideTarget.windowWidthDip, 1192.0F,
              "wide mode resolves independently of widget identity");
    CheckNear(wideTarget.windowHeightDip, 798.0F,
              "wide mode reserves the same host-owned vertical chrome");
    Check(compactTarget.windowWidthDip != wideTarget.windowWidthDip &&
              compactTarget.windowHeightDip != wideTarget.windowHeightDip,
          "surface class transitions produce distinct presentation extents");
    Check(ResolveWidgetSurfaceTarget(compactRequest, 1.0F).windowWidthDip ==
              compactTarget.windowWidthDip,
          "same surface request deterministically preserves its extent");

    WidgetSurfaceRequest explicitRequest{WidgetSurfaceMode::Compact};
    explicitRequest.preferredWidthDip = 600.0F;
    explicitRequest.preferredHeightDip = 460.0F;
    explicitRequest.minimumWidthDip = 360.0F;
    explicitRequest.minimumHeightDip = 260.0F;
    const auto explicitTarget = ResolveWidgetSurfaceTarget(explicitRequest, 1.0F);
    CheckNear(explicitTarget.panelWidthDip, 600.0F,
              "valid explicit preferred width refines the semantic mode");
    CheckNear(explicitTarget.panelHeightDip, 460.0F,
              "valid explicit preferred height refines the semantic mode");

    WidgetSurfaceRequest partialRequest{WidgetSurfaceMode::Compact};
    partialRequest.preferredWidthDip = 900.0F;
    const auto partialTarget = ResolveWidgetSurfaceTarget(partialRequest, 1.0F);
    CheckNear(partialTarget.panelWidthDip, 560.0F,
              "partial preferred pair safely falls back to the mode width");
    CheckNear(partialTarget.panelHeightDip, 420.0F,
              "partial preferred pair safely falls back to the mode height");

    WidgetSurfaceRequest unknownModeRequest{
        static_cast<WidgetSurfaceMode>(999)};
    const auto unknownModeTarget = ResolveWidgetSurfaceTarget(unknownModeRequest, 1.0F);
    CheckNear(unknownModeTarget.panelWidthDip, 880.0F,
              "unknown native mode safely falls back to Adaptive width");
    CheckNear(unknownModeTarget.panelHeightDip, 520.0F,
              "unknown native mode safely falls back to Adaptive height");

    WidgetSurfaceRequest outOfRangeRequest{WidgetSurfaceMode::Compact};
    outOfRangeRequest.preferredWidthDip = 5'000.0F;
    outOfRangeRequest.preferredHeightDip = 5'000.0F;
    const auto outOfRangeTarget = ResolveWidgetSurfaceTarget(outOfRangeRequest, 1.0F);
    CheckNear(outOfRangeTarget.panelWidthDip, 560.0F,
              "out-of-range preferred dimensions cannot escape mode bounds");
    CheckNear(outOfRangeTarget.panelHeightDip, 420.0F,
              "out-of-range preferred height falls back atomically");

    WidgetSurfaceRequest invalidRequest{WidgetSurfaceMode::Standard};
    invalidRequest.preferredWidthDip = std::numeric_limits<float>::quiet_NaN();
    invalidRequest.preferredHeightDip = 420.0F;
    invalidRequest.minimumWidthDip = 1'000.0F;
    invalidRequest.minimumHeightDip = 700.0F;
    const auto invalidTarget = ResolveWidgetSurfaceTarget(invalidRequest, 1.0F);
    CheckNear(invalidTarget.panelWidthDip, 1000.0F,
              "a valid standalone minimum can raise a malformed preference safely");
    CheckNear(invalidTarget.panelHeightDip, 700.0F,
              "a valid standalone minimum remains host bounded");

    WidgetSurfaceRequest inconsistentRequest{WidgetSurfaceMode::Compact};
    inconsistentRequest.preferredWidthDip = 500.0F;
    inconsistentRequest.preferredHeightDip = 400.0F;
    inconsistentRequest.minimumWidthDip = 900.0F;
    inconsistentRequest.minimumHeightDip = 600.0F;
    const auto inconsistentTarget = ResolveWidgetSurfaceTarget(inconsistentRequest, 1.0F);
    CheckNear(inconsistentTarget.panelWidthDip, 500.0F,
              "minimum exceeding preferred is ignored defensively");
    CheckNear(inconsistentTarget.panelHeightDip, 400.0F,
              "invalid minimum pair cannot enlarge a valid preference");

    const auto largeTextTarget = ResolveWidgetSurfaceTarget(standardRequest, 1.5F);
    CheckNear(largeTextTarget.panelWidthDip, 1100.0F,
              "150 percent text receives bounded horizontal reflow room");
    CheckNear(largeTextTarget.panelHeightDip, 780.0F,
              "150 percent text receives full vertical reflow room");
    const auto invalidTextTarget = ResolveWidgetSurfaceTarget(
        standardRequest, std::numeric_limits<float>::infinity());
    CheckNear(invalidTextTarget.windowWidthDip, standardTarget.windowWidthDip,
              "invalid text scale fails safely to the standard scale");

    const auto compact720p = ResolveWidgetSurface(
        compactRequest,
        WidgetSurfaceConstraints{{0, 0, 1280, 720}, 96, 1.0F, 1.0F});
    Check(compact720p.has_value(), "compact surface resolves on 720p");
    CheckNear(compact720p->windowWidthDip, 632.0F,
              "compact surface avoids empty horizontal space on 720p");
    CheckNear(compact720p->windowHeightDip, 598.0F,
              "compact surface fits 720p with shell margins");
    Check(!compact720p->constrainedByWorkArea,
          "compact default fits an ordinary 720p work area");

    const auto standard720p = ResolveWidgetSurface(
        standardRequest,
        WidgetSurfaceConstraints{{0, 0, 1280, 720}, 96, 1.0F, 1.0F});
    Check(standard720p.has_value() && standard720p->constrainedByWorkArea,
          "standard surface reports its 720p height clamp");
    CheckNear(standard720p->windowWidthDip, 952.0F,
              "720p clamp preserves a fitting standard width");
    CheckNear(standard720p->windowHeightDip, 664.0F,
              "720p clamp reserves host top and bottom work-area margins");

    const auto portraitSurface = ResolveWidgetSurface(
        standardRequest,
        WidgetSurfaceConstraints{{0, 0, 720, 1280}, 96, 1.0F, 1.0F});
    Check(portraitSurface.has_value() && portraitSurface->constrainedByWorkArea,
          "portrait surface clamps only the dimension that cannot fit");
    CheckNear(portraitSurface->windowWidthDip, 672.0F,
              "portrait surface remains inside horizontal work-area margins");
    CheckNear(portraitSurface->windowHeightDip, 698.0F,
              "portrait surface retains a fitting height");

    const auto ultrawideSurface = ResolveWidgetSurface(
        wideRequest,
        WidgetSurfaceConstraints{{-3440, -100, 0, 1300}, 96, 1.0F, 1.0F});
    Check(ultrawideSurface.has_value() && !ultrawideSurface->constrainedByWorkArea,
          "wide request fits an offset ultrawide work area");
    CheckNear(ultrawideSurface->windowWidthDip, 1192.0F,
              "ultrawide placement does not stretch a wide widget");

    const auto highScaleSurface = ResolveWidgetSurface(
        standardRequest,
        WidgetSurfaceConstraints{{0, 0, 1920, 1080}, 192, 1.25F, 1.5F});
    Check(highScaleSurface.has_value() && highScaleSurface->constrainedByWorkArea,
          "high DPI, interface zoom, and text scale resolve against physical work area");
    CheckNear(highScaleSurface->windowWidthDip, 729.6F,
              "high-scale width exactly reconstructs the safe physical work area");
    CheckNear(highScaleSurface->windowHeightDip, 387.2F,
              "high-scale height exactly reconstructs the safe physical work area");
    Check(highScaleSurface->panelWidthDip <= highScaleSurface->windowWidthDip &&
              highScaleSurface->panelHeightDip <= highScaleSurface->windowHeightDip,
          "high-scale panel remains bounded by host chrome and viewport");

    Check(!ResolveWidgetSurface(
              compactRequest,
              WidgetSurfaceConstraints{{0, 0, 0, 720}, 96, 1.0F, 1.0F}),
          "surface resolution rejects an empty work area");
    Check(!ResolveWidgetSurface(
              compactRequest,
              WidgetSurfaceConstraints{{0, 0, 1280, 720}, 0, 1.0F, 1.0F}),
          "surface resolution rejects zero DPI");
    Check(!ResolveWidgetSurface(
              compactRequest,
              WidgetSurfaceConstraints{{0, 0, 1280, 720}, 96, 0.0F, 1.0F}),
          "surface resolution rejects zero interface scale");

    for (const auto work : {
             PhysicalRect{0, 0, 1280, 720},
             PhysicalRect{0, 0, 1920, 1040},
             PhysicalRect{0, 0, 3840, 2120},
             PhysicalRect{0, 0, 1080, 1880},
             PhysicalRect{-3440, -200, 0, 1240},
             PhysicalRect{1920, 100, 7040, 1320},
         }) {
        for (const unsigned int dpi : {96U, 120U, 144U, 192U}) {
            const auto value = ComputeOverlayPlacement(work, dpi, 1180, 700);
            Check(value.has_value(), "common resolution produced placement");
            FullyContained(*value, work);
            Check(value->x + value->width / 2 == work.left + (work.right - work.left) / 2 ||
                      value->x + value->width / 2 + 1 == work.left + (work.right - work.left) / 2,
                  "placement is horizontally centered");
        }
    }

    // Model the host's event-driven re-resolution path rather than assuming a
    // resize is merely proportional. Taskbars can move between edges, the
    // destination monitor can use negative desktop coordinates, and a monitor
    // migration can change work area and DPI at the same time.
    struct DisplayState final {
        PhysicalRect work;
        unsigned int dpi;
        float interfaceScale;
    };
    const auto resolvePlacement = [&](const DisplayState state) {
        const auto surface = ResolveWidgetSurface(
            standardRequest,
            WidgetSurfaceConstraints{
                state.work, state.dpi, state.interfaceScale, 1.0F});
        Check(surface.has_value(), "display transition resolves widget surface");
        const auto placement = ComputeOverlayPlacement(
            state.work, state.dpi,
            surface->windowWidthDip * state.interfaceScale,
            surface->windowHeightDip * state.interfaceScale);
        Check(placement.has_value(), "display transition resolves physical placement");
        FullyContained(*placement, state.work);
        const auto metrics = ComputeOverlayRenderMetrics(
            placement->width, placement->height, state.dpi, state.interfaceScale);
        Check(metrics.has_value(), "display transition resolves logical viewport");
        const auto geometry = ComputeOverlaySurfaceGeometry(
            metrics->viewportWidthDip, metrics->viewportHeightDip,
            surface->panelWidthDip);
        Check(geometry.has_value(), "display transition resolves contained shell geometry");
        SurfaceContained(*geometry, metrics->viewportWidthDip, metrics->viewportHeightDip);
        return *placement;
    };
    constexpr std::array displayTransitions{
        DisplayState{{0, 0, 1920, 1080}, 96U, 1.0F},       // no taskbar reservation
        DisplayState{{0, 0, 1920, 1040}, 96U, 1.0F},       // bottom taskbar
        DisplayState{{72, 0, 1920, 1080}, 96U, 1.0F},      // left taskbar
        DisplayState{{0, 48, 1920, 1080}, 96U, 1.0F},      // top taskbar
        DisplayState{{0, 0, 1856, 1080}, 96U, 1.0F},       // right taskbar
        DisplayState{{-3840, -160, 0, 2000}, 192U, 1.0F},  // mixed-DPI monitor migration
        DisplayState{{1920, 80, 7040, 1400}, 144U, 1.25F}, // offset 32:9 + UI zoom
        DisplayState{{0, 0, 640, 320}, 192U, 1.25F},       // constrained high-DPI work area
    };
    auto previousTransitionPlacement = resolvePlacement(displayTransitions.front());
    for (std::size_t index = 1; index < displayTransitions.size(); ++index) {
        const auto placement = resolvePlacement(displayTransitions[index]);
        Check(placement.x != previousTransitionPlacement.x ||
                  placement.y != previousTransitionPlacement.y ||
                  placement.width != previousTransitionPlacement.width ||
                  placement.height != previousTransitionPlacement.height,
              "work-area, monitor, DPI, or zoom change produces fresh placement");
        previousTransitionPlacement = placement;
    }

    for (const auto work : {
             PhysicalRect{0, 0, 640, 360},
             PhysicalRect{10, 20, 11, 21},
             PhysicalRect{-100, -100, -93, -97},
             PhysicalRect{-1'000'000'000, -20, 1'000'000'000, 20},
         }) {
        const auto small = ComputeOverlayPlacement(work, 96, 1180, 700);
        Check(small.has_value(), "arbitrary positive monitor extent is supported");
        FullyContained(*small, work);
    }

    const auto oversizedMargins = ComputeOverlayPlacement(
        {0, 0, 320, 180}, 96, 1180, 700,
        OverlayMarginsDip{1'000'000.0F, 1'000'000.0F, 1'000'000.0F});
    Check(oversizedMargins.has_value(), "oversized margins degrade to a contained viewport");
    FullyContained(*oversizedMargins, {0, 0, 320, 180});
    Check(!ComputeOverlayPlacement({0, 0, 0, 720}, 96, 1180, 700), "zero width fails closed");
    Check(!ComputeOverlayPlacement({0, 0, 1280, 720}, 0, 1180, 700), "zero DPI fails closed");
    Check(!ComputeOverlayPlacement({0, 0, 1280, 720}, 96, -1, 700), "negative size fails closed");

    for (const auto client : {
             PhysicalRect{0, 0, 1, 1},
             PhysicalRect{0, 0, 853, 479},
             PhysicalRect{0, 0, 3440, 1440},
             PhysicalRect{0, 0, 7680, 4320},
         }) {
        const int clientWidth = client.right - client.left;
        const int clientHeight = client.bottom - client.top;
        for (const unsigned int dpi : {72U, 96U, 120U, 144U, 192U, 288U, 480U}) {
            for (const float interfaceScale : {0.8F, 1.0F, 1.125F, 1.25F}) {
                const auto metrics = ComputeOverlayRenderMetrics(
                    clientWidth, clientHeight, dpi, interfaceScale);
                Check(metrics.has_value(), "arbitrary client DPI and zoom produce render metrics");
                Check(metrics->viewportWidthDip > 0 && metrics->viewportHeightDip > 0,
                      "responsive viewport remains positive");
                const float reconstructedWidth = metrics->viewportWidthDip *
                    metrics->physicalPixelsPerDip;
                const float reconstructedHeight = metrics->viewportHeightDip *
                    metrics->physicalPixelsPerDip;
                Check(std::abs(reconstructedWidth - clientWidth) < 0.01F,
                      "responsive viewport reconstructs physical width");
                Check(std::abs(reconstructedHeight - clientHeight) < 0.01F,
                      "responsive viewport reconstructs physical height");
            }
        }
    }
    Check(!ComputeOverlayRenderMetrics(0, 720, 96, 1), "zero client width fails closed");
    Check(!ComputeOverlayRenderMetrics(1280, 720, 0, 1), "zero render DPI fails closed");
    Check(!ComputeOverlayRenderMetrics(1280, 720, 96, 0), "zero UI scale fails closed");
    Check(!ComputeOverlayRenderMetrics(1280, 720, 96,
          std::numeric_limits<float>::quiet_NaN()), "non-finite UI scale fails closed");

    for (const auto viewport : {
             PhysicalRect{0, 0, 1, 1},
             PhysicalRect{0, 0, 240, 135},
             PhysicalRect{0, 0, 280, 800},
             PhysicalRect{0, 0, 800, 280},
             PhysicalRect{0, 0, 853, 479},
             PhysicalRect{0, 0, 1180, 700},
             PhysicalRect{0, 0, 3840, 2160},
         }) {
        const float width = static_cast<float>(viewport.right);
        const float height = static_cast<float>(viewport.bottom);
        const auto geometry = ComputeOverlaySurfaceGeometry(width, height, 880.0F);
        Check(geometry.has_value(), "tiny portrait and ultrawide viewports produce geometry");
        SurfaceContained(*geometry, width, height);
    }

    // Exercise the shell math as one pipeline, matching the host call order:
    // physical work area -> scaled HWND placement -> logical render viewport ->
    // widget panel/tray/footer geometry. This includes legacy, handheld-sized,
    // portrait, ultrawide, 4K/5K/8K, offset, and negative-coordinate monitors.
    constexpr std::array physicalWorkAreas{
        PhysicalRect{0, 0, 320, 200},
        PhysicalRect{0, 0, 640, 360},
        PhysicalRect{0, 0, 800, 600},
        PhysicalRect{0, 0, 1024, 728},
        PhysicalRect{0, 0, 1280, 680},
        PhysicalRect{0, 0, 1316, 896},
        PhysicalRect{0, 0, 1366, 728},
        PhysicalRect{0, 0, 1920, 1040},
        PhysicalRect{0, 0, 2560, 1040},
        PhysicalRect{0, 0, 3440, 1400},
        PhysicalRect{0, 0, 3840, 1040},
        PhysicalRect{0, 0, 3840, 2120},
        PhysicalRect{0, 0, 5120, 1400},
        PhysicalRect{0, 0, 7680, 2120},
        PhysicalRect{0, 0, 7680, 4280},
        PhysicalRect{0, 0, 720, 1240},
        PhysicalRect{0, 0, 1080, 1880},
        PhysicalRect{0, 0, 1440, 3400},
        PhysicalRect{-5120, -200, -1680, 1240},
        PhysicalRect{1920, 80, 7040, 1480},
    };
    for (const auto work : physicalWorkAreas) {
        for (const unsigned int dpi : {72U, 96U, 120U, 144U, 168U, 192U, 240U, 288U, 384U, 480U}) {
            for (const float interfaceScale : {0.8F, 0.9F, 1.0F, 1.1F, 1.25F}) {
                for (const float desiredHeight : {180.0F, 540.0F, 700.0F}) {
                    const auto placement = ComputeOverlayPlacement(
                        work, dpi, 1180.0F * interfaceScale,
                        desiredHeight * interfaceScale);
                    Check(placement.has_value(),
                          "resolution matrix produces a physical placement");
                    FullyContained(*placement, work);
                    const auto metrics = ComputeOverlayRenderMetrics(
                        placement->width, placement->height, dpi, interfaceScale);
                    Check(metrics.has_value(),
                          "placed client produces responsive render metrics");
                    Check(std::abs(metrics->viewportWidthDip *
                                       metrics->physicalPixelsPerDip - placement->width) < 0.02F &&
                          std::abs(metrics->viewportHeightDip *
                                       metrics->physicalPixelsPerDip - placement->height) < 0.02F,
                          "DPI-derived logical viewport reconstructs placed client");
                    if (desiredHeight == 700.0F) {
                        const auto geometry = ComputeOverlaySurfaceGeometry(
                            metrics->viewportWidthDip, metrics->viewportHeightDip, 880.0F);
                        Check(geometry.has_value(),
                              "resolution matrix produces widget surface geometry");
                        SurfaceContained(
                            *geometry, metrics->viewportWidthDip,
                            metrics->viewportHeightDip);
                    }
                }
            }
        }
    }

    // Dense logical boundary coverage catches discontinuities around tile,
    // footer, tray, margin, and preferred-panel thresholds without encoding a
    // small list of familiar desktop resolutions.
    constexpr std::array logicalWidths{
        1.0F, 2.0F, 7.0F, 15.0F, 31.0F, 63.0F, 64.0F, 65.0F,
        127.0F, 239.0F, 240.0F, 319.0F, 320.0F, 479.0F, 640.0F,
        853.0F, 1180.0F, 1920.0F, 3440.0F, 7680.0F,
    };
    constexpr std::array logicalHeights{
        1.0F, 2.0F, 7.0F, 13.0F, 14.0F, 15.0F, 31.0F, 63.0F,
        111.0F, 112.0F, 113.0F, 179.0F, 180.0F, 239.0F, 240.0F,
        479.0F, 700.0F, 1080.0F, 2160.0F, 4320.0F,
    };
    for (const float preferredPanelWidth : {240.0F, 720.0F, 880.0F, 1600.0F}) {
        for (const float width : logicalWidths) {
            float priorPanelHeight = -1.0F;
            for (const float height : logicalHeights) {
                const auto geometry = ComputeOverlaySurfaceGeometry(
                    width, height, preferredPanelWidth);
                Check(geometry.has_value(),
                      "logical boundary matrix produces surface geometry");
                SurfaceContained(*geometry, width, height);
                Check(geometry->panelWidth <= preferredPanelWidth + 0.002F,
                      "responsive panel never exceeds author preference");
                Check(geometry->panelHeight + 0.002F >= priorPanelHeight,
                      "panel height grows monotonically with available height");
                priorPanelHeight = geometry->panelHeight;
            }
        }
    }

    const auto standardSurface = ComputeOverlaySurfaceGeometry(1180, 700, 880);
    Check(standardSurface.has_value(), "standard widget surface produces geometry");
    Check(std::abs(standardSurface->trayY - 588.0F) < 0.001F &&
          std::abs(standardSurface->trayHeight - 98.0F) < 0.001F,
          "standard widget surface preserves the persistent tray band");
    Check(standardSurface->panelY + standardSurface->panelHeight <
              standardSurface->trayY,
          "standard widget panel floats above the persistent tray");
    Check(std::abs(standardSurface->footerHeight - 55.0F) < 0.001F &&
          std::abs(standardSurface->footerY -
                   (standardSurface->panelY + standardSurface->panelHeight - 55.0F)) < 0.001F,
          "standard widget footer preserves established chrome height");
    const auto compactBody = ComputeOverlaySurfaceGeometry(
        1180.0F, 700.0F, 560.0F, 420.0F);
    const auto wideBody = ComputeOverlaySurfaceGeometry(
        1180.0F, 700.0F, 1120.0F, 620.0F);
    Check(compactBody && wideBody,
          "compact and wide bodies resolve inside one shared shell");
    CheckNear(compactBody->trayY, wideBody->trayY,
              "widget body preference cannot move the shared tray baseline");
    CheckNear(compactBody->trayHeight, wideBody->trayHeight,
              "widget body preference cannot resize the shared tray band");
    Check(compactBody->panelHeight < wideBody->panelHeight &&
              wideBody->panelY + wideBody->panelHeight <= wideBody->trayY,
          "body height preference is clamped below the stationary tray");
    Check(compactBody->panelWidth < wideBody->panelWidth &&
              wideBody->panelX + wideBody->panelWidth <= 1180.0F,
          "body width preference is clamped inside the shared shell");
    Check(!ComputeOverlaySurfaceGeometry(0, 700, 880),
          "zero surface width fails closed");
    Check(!ComputeOverlaySurfaceGeometry(1180, 700, -1),
          "negative preferred panel width fails closed");
    Check(!ComputeOverlaySurfaceGeometry(
              std::numeric_limits<float>::infinity(), 700, 880),
          "non-finite surface width fails closed");
    Check(!ComputeOverlaySurfaceGeometry(
              1180, 700, 880, std::numeric_limits<float>::infinity()),
          "non-finite preferred panel height fails closed");

    std::cout << "OverlayPlacementTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
