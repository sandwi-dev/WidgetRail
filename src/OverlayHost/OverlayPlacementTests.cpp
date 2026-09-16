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

void FullyContained(const widgetrail::OverlayPlacement& value, const widgetrail::PhysicalRect& work) {
    Check(value.width > 0 && value.height > 0, "placement has positive size");
    Check(value.x >= work.left && value.y >= work.top, "placement starts inside work area");
    Check(value.x + value.width <= work.right, "placement right edge is contained");
    Check(value.y + value.height <= work.bottom, "placement bottom edge is contained");
}

void SurfaceContained(
    const widgetrail::OverlaySurfaceGeometry& geometry,
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
        widgetrail::PhysicalRect work;
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
        const auto dashboard = widgetrail::ComputeOverlayPlacement(
            profile.work, profile.dpi,
            1180.0F * profile.interfaceScale,
            180.0F * profile.interfaceScale);
        const auto host = widgetrail::ComputeOverlayPlacement(
            profile.work, profile.dpi,
            1180.0F * profile.interfaceScale,
            700.0F * profile.interfaceScale);
        Check(dashboard.has_value() && host.has_value(),
              "cold dashboard and shared host placements resolve");
        FullyContained(*dashboard, profile.work);
        FullyContained(*host, profile.work);
        const auto presentation = widgetrail::PlanCompositionMotion(
            static_cast<unsigned int>(host->width),
            static_cast<unsigned int>(host->height),
            static_cast<unsigned int>(dashboard->width),
            static_cast<unsigned int>(dashboard->height),
            static_cast<float>(dashboard->width),
            static_cast<float>(dashboard->height),
            widgetrail::CompositionVerticalAnchor::Bottom);
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

        const auto widget = widgetrail::PlanCompositionMotion(
            static_cast<unsigned int>(host->width),
            static_cast<unsigned int>(host->height),
            static_cast<unsigned int>(host->width),
            static_cast<unsigned int>(host->height),
            static_cast<float>(host->width),
            static_cast<float>(host->height),
            widgetrail::CompositionVerticalAnchor::Bottom);
        Check(widget.offsetY == 0.0F,
              "first full widget retains the same shared-host bottom anchor");
        const auto reshown = widgetrail::PlanCompositionMotion(
            static_cast<unsigned int>(host->width),
            static_cast<unsigned int>(host->height),
            static_cast<unsigned int>(dashboard->width),
            static_cast<unsigned int>(dashboard->height),
            static_cast<float>(dashboard->width),
            static_cast<float>(dashboard->height),
            widgetrail::CompositionVerticalAnchor::Bottom);
        Check(reshown.offsetY == presentation.offsetY,
              "dashboard re-show restores the exact cold bottom anchor");
    }
}

void VariableWidgetSurfacesKeepHostChromeStationary() {
    struct Profile final {
        widgetrail::PhysicalRect work;
        unsigned int dpi;
        float interfaceScale;
    };
    constexpr std::array profiles{
        Profile{{0, 0, 1280, 680}, 96U, 1.0F},
        Profile{{0, 0, 1920, 1040}, 96U, 1.0F},
        Profile{{0, 0, 2560, 1400}, 144U, 1.0F},
        Profile{{72, 48, 1920, 1080}, 96U, 1.5F},
        Profile{{-3440, -200, 0, 1240}, 144U, 1.0F},
    };

    widgetrail::WidgetSurfaceRequest compact{widgetrail::WidgetSurfaceMode::Compact};
    widgetrail::WidgetSurfaceRequest standard{widgetrail::WidgetSurfaceMode::Standard};
    widgetrail::WidgetSurfaceRequest wide{widgetrail::WidgetSurfaceMode::Wide};
    auto heightOnly = compact;
    heightOnly.preferredWidthDip = 560.0F;
    heightOnly.preferredHeightDip = 700.0F;
    constexpr float textScale = 1.0F;

    for (const auto& profile : profiles) {
        struct ChromeBounds final {
            float visualPanelBottom{};
            float guideBottom{};
            float trayTop{};
            float trayBottom{};
        };
        std::optional<ChromeBounds> reference;
        for (const auto& request : {compact, standard, wide, heightOnly}) {
            const auto surface = widgetrail::ResolveWidgetSurface(
                request,
                widgetrail::WidgetSurfaceConstraints{
                    profile.work, profile.dpi, profile.interfaceScale, textScale});
            Check(surface.has_value(),
                  "variable widget surface resolves against live work area");
            const auto placement = widgetrail::ComputeOverlayPlacement(
                profile.work, profile.dpi,
                surface->windowWidthDip * profile.interfaceScale,
                surface->windowHeightDip * profile.interfaceScale);
            Check(placement.has_value(),
                  "variable widget surface receives a physical placement");
            FullyContained(*placement, profile.work);
            const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
                placement->width, placement->height,
                profile.dpi, profile.interfaceScale);
            Check(metrics.has_value(),
                  "variable widget placement produces render metrics");
            const auto geometry = widgetrail::ComputeOverlaySurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip,
                surface->panelWidthDip, surface->panelHeightDip);
            Check(geometry.has_value(),
                  "variable widget placement produces host chrome geometry");
            SurfaceContained(
                *geometry, metrics->viewportWidthDip, metrics->viewportHeightDip);

            const auto screenY = [&](const float logicalY) {
                return static_cast<float>(placement->y) +
                    logicalY * metrics->physicalPixelsPerDip;
            };
            const ChromeBounds bounds{
                screenY(geometry->footerY),
                screenY(geometry->footerY + geometry->footerHeight),
                screenY(geometry->trayY),
                screenY(geometry->trayY + geometry->trayHeight),
            };
            CheckNear(
                bounds.guideBottom,
                screenY(geometry->panelY + geometry->panelHeight),
                "controller guide ends at the explicit panel bottom anchor",
                0.51F);
            Check(bounds.visualPanelBottom <= bounds.guideBottom &&
                      bounds.guideBottom <= bounds.trayTop,
                  "visible panel, guide, and tray remain ordered without overlap");
            CheckNear(
                bounds.trayTop - bounds.guideBottom,
                46.0F * metrics->physicalPixelsPerDip,
                "guide-to-tray offset remains the fixed host-chrome reservation",
                0.51F);
            if (!reference) {
                reference = bounds;
                continue;
            }
            CheckNear(bounds.visualPanelBottom, reference->visualPanelBottom,
                      "visible panel bottom stays fixed across surface switches", 0.51F);
            CheckNear(bounds.guideBottom, reference->guideBottom,
                      "controller guide bottom stays fixed across surface switches", 0.51F);
            CheckNear(bounds.trayTop, reference->trayTop,
                      "tray top stays fixed across surface switches", 0.51F);
            CheckNear(bounds.trayBottom, reference->trayBottom,
                      "tray bottom stays fixed across surface switches", 0.51F);
        }
    }
}

} // namespace

int main() {
    ColdDashboardProfilesUseOneBottomAnchor();
    VariableWidgetSurfacesKeepHostChromeStationary();
    using namespace widgetrail;

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
    const auto measureGuide = [](const std::wstring_view text)
        -> std::optional<float> {
        return static_cast<float>(text.size());
    };
    const std::array quickActions{
        ControllerGuideAction{L"X", L"Play or pause"},
        ControllerGuideAction{L"LT", L"Seek backward"},
        ControllerGuideAction{L"RT", L"Seek forward"},
    };
    const auto contextualGuide = BuildTrayControllerGuide(
        ControllerGuideDensity::Compact, false, true, 200.0F,
        measureGuide, quickActions);
    Check(contextualGuide.find(L"X Play or pause") != std::wstring::npos &&
              contextualGuide.find(L"LT Seek backward") != std::wstring::npos &&
              contextualGuide.find(L"RT Seek forward") != std::wstring::npos,
          "compact media guide keeps all three complete authored actions");
    Check(contextualGuide.find(L"↑/A Enter") != std::wstring::npos &&
              contextualGuide.find(L"Y Tap/Hold") != std::wstring::npos &&
              contextualGuide.find(L"B Close") != std::wstring::npos &&
              contextualGuide.find(L"Menu Options") != std::wstring::npos,
          "wide contextual guide retains complete generic and shell affordances");
    const std::wstring completeAuthoredWithoutEnter =
        L"X Play or pause   LT Seek backward   RT Seek forward   "
        L"Y Tap/Hold   B Close   Menu Options";
    const auto authoredPriorityGuide = BuildTrayControllerGuide(
        ControllerGuideDensity::Compact, false, true,
        static_cast<float>(completeAuthoredWithoutEnter.size()),
        measureGuide, quickActions);
    Check(authoredPriorityGuide == completeAuthoredWithoutEnter &&
              authoredPriorityGuide.find(L"↑/A Enter") == std::wstring::npos,
          "constrained guide drops generic Enter before any authored action");
    const auto wholeActionGuide = BuildTrayControllerGuide(
        ControllerGuideDensity::Compact, false, true,
        static_cast<float>(completeAuthoredWithoutEnter.size() - 1U),
        measureGuide, quickActions);
    Check(wholeActionGuide.find(L"X Play or pause") != std::wstring::npos &&
              wholeActionGuide.find(L"LT Seek backward") != std::wstring::npos &&
              wholeActionGuide.find(L"RT ") == std::wstring::npos &&
              wholeActionGuide.find(L'…') == std::wstring::npos,
          "further constraint removes the lowest-priority action whole");
    const std::array hostileAction{
        ControllerGuideAction{L"LB\nRB", L"A deliberately enormous\nwidget supplied label"},
    };
    const auto hostileGuide = BuildTrayControllerGuide(
        ControllerGuideDensity::Minimal, false, true, 50.0F,
        measureGuide, hostileAction);
    Check(hostileGuide == L"Y Tap/Hold   B Close   Menu Options" &&
              hostileGuide.find_first_of(L"\r\n…") == std::wstring::npos,
          "untrusted widget hint is dropped whole without wrapping or ellipsis");
    Check(BuildTrayControllerGuide(
              ControllerGuideDensity::Full, false, true, 200.0F,
              measureGuide).find(
                  L"Y Tap reorder / Hold restart") != std::wstring::npos,
          "full tray guide explains both sides of the Y gesture");
    Check(BuildTrayControllerGuide(
              ControllerGuideDensity::Minimal, false, true, 200.0F,
              measureGuide) ==
              L"Y Tap/Hold   B Close   Menu Options",
          "minimal tray guide keeps complete gesture, escape, and options");
    Check(BuildTrayControllerGuide(
                ControllerGuideDensity::Full, false, false, 200.0F,
                measureGuide).find(L"Hold") ==
                std::wstring::npos,
          "tray guide does not advertise hold restart for a non-bridge item");
    Check(BuildTrayControllerGuide(
              ControllerGuideDensity::Full, true, true, 200.0F,
              measureGuide).find(L"Y Done") !=
              std::wstring::npos,
          "reorder mode keeps tap-Y completion for bridge widgets");

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

    Check(ParseWidgetSurfaceAxisMode(L"preferred", 2) ==
              WidgetSurfaceAxisMode::Preferred,
          "legacy protocol accepts the omitted/default Preferred axis");
    Check(!ParseWidgetSurfaceAxisMode(L"content", 16),
          "legacy protocol rejects Content axis metadata");
    Check(ParseWidgetSurfaceAxisMode(L"content", 17) ==
              WidgetSurfaceAxisMode::Content,
          "protocol v17 parses Content once into the native request");
    Check(ParseWidgetSurfaceAxisMode(L"fillAvailable", 17) ==
              WidgetSurfaceAxisMode::FillAvailable,
          "protocol v17 parses FillAvailable once into the native request");
    Check(!ParseWidgetSurfaceAxisMode(L"fill-remaining", 17),
          "unknown native axis metadata fails closed");
    Check(!ParseWidgetSurfaceAxisMode(L"", 17),
          "present-but-empty native axis metadata fails closed");

    WidgetSurfaceRequest contentRequest{WidgetSurfaceMode::Standard};
    contentRequest.widthMode = WidgetSurfaceAxisMode::Content;
    contentRequest.heightMode = WidgetSurfaceAxisMode::Content;
    contentRequest.preferredWidthDip = 880.0F;
    contentRequest.preferredHeightDip = 520.0F;
    contentRequest.minimumWidthDip = 420.0F;
    contentRequest.minimumHeightDip = 280.0F;
    const WidgetSurfaceConstraints ordinaryWork{
        {0, 0, 1920, 1080}, 96, 1.0F, 1.0F};
    unsigned int measureCalls{};
    float measuredAtWidth{};
    float measuredAtHeight{};
    const auto smallerContent = ResolveWidgetSurface(
        contentRequest, ordinaryWork,
        [&](const float width, const float height)
            -> std::optional<WidgetSurfaceIntrinsicExtent> {
            ++measureCalls;
            measuredAtWidth = width;
            measuredAtHeight = height;
            return WidgetSurfaceIntrinsicExtent{300.0F, 100.0F};
        });
    Check(smallerContent && measureCalls == 1 &&
              smallerContent->intrinsicMeasurementPasses == 1,
          "Content axes perform exactly one intrinsic pass before final layout");
    CheckNear(measuredAtWidth, 878.0F,
              "intrinsic width is bounded by preferred minus fixed panel inset");
    CheckNear(measuredAtHeight, 464.0F,
              "intrinsic height is bounded by preferred minus host footer chrome");
    CheckNear(smallerContent->panelWidthDip, 420.0F,
              "content smaller than authored width clamps to the minimum");
    CheckNear(smallerContent->panelHeightDip, 280.0F,
              "content smaller than authored height clamps to the minimum");

    const auto betweenContent = ResolveWidgetSurface(
        contentRequest, ordinaryWork,
        [](float, float) -> std::optional<WidgetSurfaceIntrinsicExtent> {
            return WidgetSurfaceIntrinsicExtent{600.0F, 340.0F};
        });
    Check(betweenContent.has_value(), "between-bounds content resolves");
    CheckNear(betweenContent->panelWidthDip, 602.0F,
              "between-bounds intrinsic width adds only fixed panel chrome");
    CheckNear(betweenContent->panelHeightDip, 396.0F,
              "between-bounds intrinsic height adds the fixed guide reservation");

    const auto overflowingContent = ResolveWidgetSurface(
        contentRequest, ordinaryWork,
        [](float, float) -> std::optional<WidgetSurfaceIntrinsicExtent> {
            return WidgetSurfaceIntrinsicExtent{2'000.0F, 1'000.0F};
        });
    Check(overflowingContent.has_value(), "overflowing content resolves");
    CheckNear(overflowingContent->panelWidthDip, 880.0F,
              "intrinsic overflow is capped at authored preferred width");
    CheckNear(overflowingContent->panelHeightDip, 520.0F,
              "intrinsic overflow is capped at authored preferred height");

    const auto invalidMeasurement = ResolveWidgetSurface(
        contentRequest, ordinaryWork,
        [](float, float) -> std::optional<WidgetSurfaceIntrinsicExtent> {
            return WidgetSurfaceIntrinsicExtent{
                std::numeric_limits<float>::quiet_NaN(), 340.0F};
        });
    Check(invalidMeasurement &&
              invalidMeasurement->intrinsicMeasurementPasses == 1,
          "non-finite intrinsic output fails safely after one bounded pass");
    CheckNear(invalidMeasurement->panelWidthDip, 880.0F,
              "invalid intrinsic output preserves the validated preferred width");
    CheckNear(invalidMeasurement->panelHeightDip, 520.0F,
              "invalid intrinsic output preserves the validated preferred height");

    WidgetSurfaceRequest fillRequest = contentRequest;
    fillRequest.widthMode = WidgetSurfaceAxisMode::FillAvailable;
    fillRequest.heightMode = WidgetSurfaceAxisMode::FillAvailable;
    measureCalls = 0;
    const auto fill = ResolveWidgetSurface(
        fillRequest, ordinaryWork,
        [&](float, float) -> std::optional<WidgetSurfaceIntrinsicExtent> {
            ++measureCalls;
            return WidgetSurfaceIntrinsicExtent{};
        });
    Check(fill && measureCalls == 0 && fill->intrinsicMeasurementPasses == 0,
          "FillAvailable axes consume work authority without intrinsic layout");
    CheckNear(fill->windowWidthDip, 1872.0F,
              "FillAvailable width consumes the safe admitted work extent");
    CheckNear(fill->windowHeightDip, 1024.0F,
              "FillAvailable height consumes the safe admitted work extent");

    WidgetSurfaceRequest mixedRequest = contentRequest;
    mixedRequest.widthMode = WidgetSurfaceAxisMode::Preferred;
    const auto mixed = ResolveWidgetSurface(
        mixedRequest,
        WidgetSurfaceConstraints{{0, 0, 1280, 720}, 96, 1.0F, 1.0F},
        [](float, float) -> std::optional<WidgetSurfaceIntrinsicExtent> {
            return WidgetSurfaceIntrinsicExtent{400.0F, 900.0F};
        });
    Check(mixed.has_value(), "independent Preferred/Content axes resolve");
    CheckNear(mixed->panelWidthDip, 880.0F,
              "Preferred width remains stable when height is Content");
    CheckNear(mixed->panelHeightDip, 486.0F,
              "Content height is capped by the admitted work area");

    WidgetSurfaceRequest invalidAxis = contentRequest;
    invalidAxis.widthMode = static_cast<WidgetSurfaceAxisMode>(999);
    Check(!ResolveWidgetSurface(invalidAxis, ordinaryWork),
          "invalid native axis mode rejects the surface admission");

    const auto preferredPanel = ComputePanelLocalSurfaceGeometry(880.0F, 520.0F);
    const auto contentPanel = ComputePanelLocalSurfaceGeometry(
        betweenContent->panelWidthDip, betweenContent->panelHeightDip);
    Check(preferredPanel && contentPanel,
          "Preferred and Content panels both resolve local composition geometry");
    CheckNear(preferredPanel->panelX, 0.0F,
              "Preferred panel starts at the content surface origin");
    CheckNear(preferredPanel->panelY, 0.0F,
              "Preferred panel has no external chrome top reservation");
    CheckNear(preferredPanel->panelWidth, 880.0F,
              "Preferred panel retains its authored local width");
    CheckNear(preferredPanel->footerY, 520.0F,
              "Preferred panel ends at its local surface bottom");
    CheckNear(preferredPanel->footerHeight, 0.0F,
              "fixed guide is excluded from the content surface");
    CheckNear(preferredPanel->trayHeight, 0.0F,
              "fixed tray is excluded from the content surface");
    CheckNear(contentPanel->panelWidth, betweenContent->panelWidthDip,
              "Content width becomes the exact local panel width");
    CheckNear(contentPanel->panelHeight, betweenContent->panelHeightDip,
              "Content height becomes the exact local panel height");
    Check(!ComputePanelLocalSurfaceGeometry(0.0F, 520.0F) &&
              !ComputePanelLocalSurfaceGeometry(
                  std::numeric_limits<float>::quiet_NaN(), 520.0F),
          "invalid panel-local geometry fails closed");

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

    const auto mediaBounds = ResolveEmbeddedMediaSurfaceBounds(
        {20.0F, 10.0F, 760.0F, 480.0F},
        320.0F, 180.0F, 760.0F, 425.0F, 16.0F / 9.0F);
    Check(mediaBounds.has_value(),
          "bounded embedded media surface resolves inside widget safe area");
    CheckNear(mediaBounds->x, 22.2222F,
              "embedded media aspect fit remains centered in safe area");
    CheckNear(mediaBounds->y, 37.5F,
              "embedded media aspect ratio centers within safe area");
    CheckNear(mediaBounds->width, 755.5556F,
              "embedded media fits the preferred extent to the declared aspect");
    CheckNear(mediaBounds->height, 425.0F,
              "embedded media applies the declared aspect ratio");
    const auto clampedMediaBounds = ResolveEmbeddedMediaSurfaceBounds(
        {8.0F, 12.0F, 300.0F, 160.0F},
        320.0F, 180.0F, 760.0F, 425.0F, 16.0F / 9.0F);
    Check(clampedMediaBounds &&
              clampedMediaBounds->x >= 8.0F && clampedMediaBounds->y >= 12.0F &&
              clampedMediaBounds->x + clampedMediaBounds->width <= 308.001F &&
              clampedMediaBounds->y + clampedMediaBounds->height <= 172.001F,
          "embedded media clamps below authored minimum when the safe area is smaller");
    Check(!ResolveEmbeddedMediaSurfaceBounds(
              {0.0F, 0.0F, 760.0F, 425.0F},
              800.0F, 180.0F, 760.0F, 425.0F, 16.0F / 9.0F),
          "inverted embedded media minimum/preferred bounds fail closed");

    const auto fullscreenMediaBounds = ResolveOverlayFullscreenMediaSurfaceBounds(
        {24.0F, 28.0F, 1920.0F, 1040.0F},
        320.0F, 180.0F, 16.0F / 9.0F);
    Check(fullscreenMediaBounds.has_value(),
          "overlay fullscreen media resolves inside the safe work area");
    CheckNear(fullscreenMediaBounds->x, 59.5556F,
              "overlay fullscreen aspect fit centers horizontally");
    CheckNear(fullscreenMediaBounds->y, 28.0F,
              "overlay fullscreen aspect fit retains the safe-area top");
    CheckNear(fullscreenMediaBounds->width, 1848.8889F,
              "overlay fullscreen aspect fit consumes the bounded safe width");
    CheckNear(fullscreenMediaBounds->height, 1040.0F,
              "overlay fullscreen aspect fit consumes the bounded safe height");
    Check(!ResolveOverlayFullscreenMediaSurfaceBounds(
              {0.0F, 0.0F, 300.0F, 160.0F},
              320.0F, 180.0F, 16.0F / 9.0F),
          "overlay fullscreen media fails closed below its declared minimum");
    Check(!ResolveOverlayFullscreenMediaSurfaceBounds(
              {0.0F, 0.0F, 1920.0F, 1040.0F},
              320.0F, 180.0F, 0.0F),
          "overlay fullscreen media rejects an invalid aspect ratio");

    const auto mediaPresentation = ResolveMediaViewportPresentationGeometry(
        {100.0F, 50.0F, 640.0F, 360.0F},
        {120.0F, 60.0F, 600.0F, 340.0F}, 1.5F);
    Check(mediaPresentation.has_value(),
          "media viewport geometry scales from final declarative DIPs");
    Check(mediaPresentation->hostBounds.left == 150 &&
              mediaPresentation->hostBounds.top == 75 &&
              mediaPresentation->hostBounds.right == 1110 &&
              mediaPresentation->hostBounds.bottom == 615,
          "media viewport host placement uses the exact final layout box");
    Check(mediaPresentation->hostClip.left == 180 &&
              mediaPresentation->hostClip.top == 90 &&
              mediaPresentation->hostClip.right == 1080 &&
              mediaPresentation->hostClip.bottom == 600,
          "media viewport clip uses the exact final ancestor intersection");
    Check(mediaPresentation->controllerBounds.left == 0 &&
              mediaPresentation->controllerBounds.top == 0 &&
              mediaPresentation->controllerBounds.right == 960 &&
              mediaPresentation->controllerBounds.bottom == 540,
          "media controller remains local so host placement is applied once");
    Check(!ResolveMediaViewportPresentationGeometry(
              {100.0F, 50.0F, 640.0F, 360.0F},
              {0.0F, 0.0F, 20.0F, 20.0F}, 1.5F),
          "media viewport rejects a clip outside the committed layout box");
    Check(!ResolveMediaViewportPresentationGeometry(
              {100.0F, 50.0F, 640.0F, 360.0F},
              {120.0F, 60.0F, 600.0F, 340.0F}, 0.0F),
          "media viewport rejects an invalid DPI scale");

    {
        ControllerGuideHints hints;
        const auto measure=[](std::wstring_view text)->std::optional<float> {return static_cast<float>(text.size());};
        const auto cards=[](std::span<const ControllerGuideHint> value)->std::optional<float> {return value.size()*100.0F;};
        const std::array<ControllerGuideAction,2> actions{{{L"LB",L"Previous"},{L"RT",L"Next"}}};
        (void)BuildTrayControllerGuide(ControllerGuideDensity::Full,false,true,400,measure,actions,&hints,cards);
        Check(hints.size()==4 && hints.front().button==L"LB","tray card budget retains highest priority contextual action");
        Check(hints[1].label==L"Reorder" && hints[2].button==L"B" && hints[3].button==L"Menu",
            "tray keeps concise reorder, close and options controls");
        (void)BuildTrayControllerGuide(ControllerGuideDensity::Full,true,true,400,measure,{},&hints,cards);
        Check(hints.size()==3 && hints[0].button==L"dpad-horizontal" && hints[1].label==L"Done",
            "reorder mode updates its structured hints without changing navigation");
    }
    std::cout << "OverlayPlacementTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
