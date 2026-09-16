#include "OverlayPlacement.h"
#include "WidgetSurfaceGeometry.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <vector>

namespace widgetrail {
namespace {

constexpr float kMinimumPanelWidthDip = surface_geometry::kMinimumAuthoredContentWidthDip;
constexpr float kMaximumPanelWidthDip = surface_geometry::kMaximumAuthoredContentWidthDip;
constexpr float kMinimumPanelHeightDip = surface_geometry::kMinimumAuthoredContentHeightDip;
constexpr float kMaximumPanelHeightDip = surface_geometry::kMaximumAuthoredContentHeightDip;
constexpr float kShellSideReservationDip = 72.0F;
constexpr float kShellVerticalReservationDip = 178.0F;
constexpr float kPanelHorizontalChromeReservationDip = 2.0F;
constexpr float kPanelVerticalChromeReservationDip = 56.0F;

struct PanelExtent final {
    float width{};
    float height{};
};

[[nodiscard]] std::optional<int> ScaleDip(float value, unsigned int dpi) noexcept {
    if (!std::isfinite(value) || value < 0.0F || dpi == 0 || dpi > 1'000'000U) {
        return std::nullopt;
    }
    const double scaled = static_cast<double>(value) * static_cast<double>(dpi) / 96.0;
    if (!std::isfinite(scaled) || scaled > static_cast<double>(std::numeric_limits<int>::max())) {
        return std::nullopt;
    }
    return static_cast<int>(std::lround(scaled));
}

[[nodiscard]] bool ValidPair(
    const std::optional<float> width,
    const std::optional<float> height,
    const float minimumWidth,
    const float maximumWidth,
    const float minimumHeight,
    const float maximumHeight) noexcept {
    if (width.has_value() != height.has_value()) return false;
    if (!width) return false;
    return std::isfinite(*width) && std::isfinite(*height) &&
           *width >= minimumWidth && *width <= maximumWidth &&
           *height >= minimumHeight && *height <= maximumHeight;
}

[[nodiscard]] PanelExtent DefaultPanelExtent(const WidgetSurfaceMode mode) noexcept {
    switch (mode) {
    case WidgetSurfaceMode::Compact:
        return {560.0F, 420.0F};
    case WidgetSurfaceMode::Wide:
        return {1'120.0F, 620.0F};
    case WidgetSurfaceMode::Standard:
    case WidgetSurfaceMode::Adaptive:
    default:
        return {880.0F, 520.0F};
    }
}

[[nodiscard]] float SanitizedTextScale(const float value) noexcept {
    if (!std::isfinite(value)) return 1.0F;
    return std::clamp(value, 0.85F, 1.5F);
}

} // namespace

std::optional<WidgetSurfaceAxisMode> ParseWidgetSurfaceAxisMode(
    const std::wstring_view value,
    const int protocolVersion) noexcept {
    if (value == L"preferred")
        return WidgetSurfaceAxisMode::Preferred;
    if (protocolVersion < kWidgetSurfaceAxisProtocolVersion) return std::nullopt;
    if (value == L"content") return WidgetSurfaceAxisMode::Content;
    if (value == L"fillAvailable") return WidgetSurfaceAxisMode::FillAvailable;
    return std::nullopt;
}

ResolvedWidgetSurface ResolveWidgetSurfaceTarget(
    const std::optional<WidgetSurfaceRequest>& request,
    const float textScale) noexcept {
    // A protocol-v1/no-hints view keeps the original 1180x700 shell and
    // 880x522 floating panel. This is intentionally distinguishable from an
    // explicit v2 Adaptive request, which opts into compact host chrome.
    if (!request) {
        return {1'180.0F, 700.0F, 880.0F, 522.0F, false};
    }

    PanelExtent target = DefaultPanelExtent(request->mode);
    const bool preferredValid = ValidPair(
        request->preferredWidthDip, request->preferredHeightDip,
        kMinimumPanelWidthDip, kMaximumPanelWidthDip,
        kMinimumPanelHeightDip, kMaximumPanelHeightDip);
    if (preferredValid) {
        target = {*request->preferredWidthDip, *request->preferredHeightDip};
    }

    const bool minimumValid = ValidPair(
        request->minimumWidthDip, request->minimumHeightDip,
        kMinimumPanelWidthDip, kMaximumPanelWidthDip,
        kMinimumPanelHeightDip, kMaximumPanelHeightDip);
    const bool minimumConsistent = minimumValid &&
        (!preferredValid ||
         (*request->minimumWidthDip <= *request->preferredWidthDip &&
          *request->minimumHeightDip <= *request->preferredHeightDip));
    if (minimumConsistent) {
        target.width = std::max(target.width, *request->minimumWidthDip);
        target.height = std::max(target.height, *request->minimumHeightDip);
    }

    // Large text needs additional reflow room, but making width grow as fast
    // as font size creates wasteful ultrawide surfaces. Preserve full vertical
    // growth and half-rate horizontal growth; Scroll remains the overflow
    // contract when the selected monitor cannot satisfy either dimension.
    const float extraTextScale = std::max(0.0F, SanitizedTextScale(textScale) - 1.0F);
    target.width *= 1.0F + extraTextScale * 0.5F;
    target.height *= 1.0F + extraTextScale;
    target.width = std::clamp(
        target.width, kMinimumPanelWidthDip, kMaximumPanelWidthDip);
    target.height = std::clamp(
        target.height, kMinimumPanelHeightDip, kMaximumPanelHeightDip);

    return {
        target.width + kShellSideReservationDip,
        target.height + kShellVerticalReservationDip,
        target.width,
        target.height,
        false,
    };
}

std::optional<ResolvedWidgetSurface> ResolveWidgetSurface(
    const std::optional<WidgetSurfaceRequest>& request,
    const WidgetSurfaceConstraints constraints,
    const WidgetSurfaceIntrinsicMeasure& measureIntrinsic) noexcept {
    const long long workWidth = static_cast<long long>(constraints.workArea.right) -
                                constraints.workArea.left;
    const long long workHeight = static_cast<long long>(constraints.workArea.bottom) -
                                 constraints.workArea.top;
    if (workWidth <= 0 || workHeight <= 0 ||
        constraints.dpi == 0 || constraints.dpi > 1'000'000U ||
        !std::isfinite(constraints.interfaceScale) ||
        constraints.interfaceScale <= 0.0F || constraints.interfaceScale > 100.0F) {
        return std::nullopt;
    }

    const auto side = ScaleDip(constraints.margins.side, constraints.dpi);
    const auto top = ScaleDip(constraints.margins.top, constraints.dpi);
    const auto bottom = ScaleDip(constraints.margins.bottom, constraints.dpi);
    if (!side || !top || !bottom) return std::nullopt;

    const long long horizontalMargins = static_cast<long long>(*side) * 2;
    const long long verticalMargins = static_cast<long long>(*top) + *bottom;
    const long long availableWidthPx = std::max(1LL, workWidth - horizontalMargins);
    const long long availableHeightPx = std::max(1LL, workHeight - verticalMargins);
    const double pixelsPerDesignDip = static_cast<double>(constraints.dpi) / 96.0 *
                                      constraints.interfaceScale;
    if (!std::isfinite(pixelsPerDesignDip) || pixelsPerDesignDip <= 0.0) {
        return std::nullopt;
    }

    const auto target = ResolveWidgetSurfaceTarget(request, constraints.textScale);
    const float maximumWindowWidthDip = static_cast<float>(
        static_cast<double>(availableWidthPx) / pixelsPerDesignDip);
    const float maximumWindowHeightDip = static_cast<float>(
        static_cast<double>(availableHeightPx) / pixelsPerDesignDip);
    if (!std::isfinite(maximumWindowWidthDip) ||
        !std::isfinite(maximumWindowHeightDip) ||
        maximumWindowWidthDip <= 0.0F || maximumWindowHeightDip <= 0.0F) {
        return std::nullopt;
    }

    const float maximumPanelWidthDip = std::max(
        0.0F, maximumWindowWidthDip - kShellSideReservationDip);
    const float maximumPanelHeightDip = std::max(
        0.0F, maximumWindowHeightDip - kShellVerticalReservationDip);
    float panelWidth = std::min(target.panelWidthDip, maximumPanelWidthDip);
    float panelHeight = std::min(target.panelHeightDip, maximumPanelHeightDip);
    unsigned int measurementPasses{};

    if (request &&
        (request->widthMode != WidgetSurfaceAxisMode::Preferred ||
         request->heightMode != WidgetSurfaceAxisMode::Preferred)) {
        const auto validAxisMode = [](const WidgetSurfaceAxisMode mode) noexcept {
            return mode == WidgetSurfaceAxisMode::Preferred ||
                mode == WidgetSurfaceAxisMode::Content ||
                mode == WidgetSurfaceAxisMode::FillAvailable;
        };
        if (!validAxisMode(request->widthMode) ||
            !validAxisMode(request->heightMode)) return std::nullopt;

        const float extraTextScale = std::max(
            0.0F, SanitizedTextScale(constraints.textScale) - 1.0F);
        float minimumPanelWidthDip = kMinimumPanelWidthDip;
        float minimumPanelHeightDip = kMinimumPanelHeightDip;
        const bool minimumValid = ValidPair(
            request->minimumWidthDip, request->minimumHeightDip,
            kMinimumPanelWidthDip, kMaximumPanelWidthDip,
            kMinimumPanelHeightDip, kMaximumPanelHeightDip);
        const bool preferredValid = ValidPair(
            request->preferredWidthDip, request->preferredHeightDip,
            kMinimumPanelWidthDip, kMaximumPanelWidthDip,
            kMinimumPanelHeightDip, kMaximumPanelHeightDip);
        const bool minimumConsistent = minimumValid &&
            (!preferredValid ||
             (*request->minimumWidthDip <= *request->preferredWidthDip &&
              *request->minimumHeightDip <= *request->preferredHeightDip));
        if (minimumConsistent) {
            minimumPanelWidthDip = std::clamp(
                *request->minimumWidthDip * (1.0F + extraTextScale * 0.5F),
                kMinimumPanelWidthDip, kMaximumPanelWidthDip);
            minimumPanelHeightDip = std::clamp(
                *request->minimumHeightDip * (1.0F + extraTextScale),
                kMinimumPanelHeightDip, kMaximumPanelHeightDip);
        }
        minimumPanelWidthDip = std::min(minimumPanelWidthDip, maximumPanelWidthDip);
        minimumPanelHeightDip = std::min(minimumPanelHeightDip, maximumPanelHeightDip);

        std::optional<WidgetSurfaceIntrinsicExtent> measured;
        if ((request->widthMode == WidgetSurfaceAxisMode::Content ||
             request->heightMode == WidgetSurfaceAxisMode::Content) &&
            measureIntrinsic && maximumPanelWidthDip > 0.0F &&
            maximumPanelHeightDip > 0.0F) {
            const float admittedMeasureWidth = request->widthMode ==
                    WidgetSurfaceAxisMode::FillAvailable
                ? maximumPanelWidthDip
                : panelWidth;
            const float admittedMeasureHeight = request->heightMode ==
                    WidgetSurfaceAxisMode::FillAvailable
                ? maximumPanelHeightDip
                : panelHeight;
            measured = measureIntrinsic(
                std::max(0.0F,
                    admittedMeasureWidth - kPanelHorizontalChromeReservationDip),
                std::max(0.0F,
                    admittedMeasureHeight - kPanelVerticalChromeReservationDip));
            measurementPasses = 1;
            if (measured &&
                (!std::isfinite(measured->widthDip) || measured->widthDip < 0.0F ||
                 !std::isfinite(measured->heightDip) || measured->heightDip < 0.0F)) {
                measured.reset();
            }
        }

        const auto resolveAxis = [](
            const WidgetSurfaceAxisMode mode,
            const float preferred,
            const float minimum,
            const float available,
            const std::optional<float> intrinsic) noexcept {
            switch (mode) {
            case WidgetSurfaceAxisMode::FillAvailable:
                return available;
            case WidgetSurfaceAxisMode::Content:
                return std::clamp(
                    intrinsic.value_or(preferred), minimum,
                    std::min(preferred, available));
            case WidgetSurfaceAxisMode::Preferred:
            default:
                return std::min(preferred, available);
            }
        };
        panelWidth = resolveAxis(
            request->widthMode, target.panelWidthDip, minimumPanelWidthDip,
            maximumPanelWidthDip,
            measured ? std::optional<float>{
                measured->widthDip + kPanelHorizontalChromeReservationDip}
                : std::nullopt);
        panelHeight = resolveAxis(
            request->heightMode, target.panelHeightDip, minimumPanelHeightDip,
            maximumPanelHeightDip,
            measured ? std::optional<float>{
                measured->heightDip + kPanelVerticalChromeReservationDip}
                : std::nullopt);
    }

    const float windowWidth = std::min(
        maximumWindowWidthDip, panelWidth + kShellSideReservationDip);
    const float windowHeight = std::min(
        maximumWindowHeightDip, panelHeight + kShellVerticalReservationDip);
    return ResolvedWidgetSurface{
        windowWidth,
        windowHeight,
        panelWidth,
        panelHeight,
        maximumWindowWidthDip + 0.001F < target.windowWidthDip ||
            maximumWindowHeightDip + 0.001F < target.windowHeightDip,
        measurementPasses,
    };
}

std::optional<OverlayRenderMetrics> ComputeOverlayRenderMetrics(
    const int clientWidthPx,
    const int clientHeightPx,
    const unsigned int dpi,
    const float interfaceScale) noexcept {
    if (clientWidthPx <= 0 || clientHeightPx <= 0 || dpi == 0 || dpi > 1'000'000U ||
        !std::isfinite(interfaceScale) || interfaceScale <= 0.0F || interfaceScale > 100.0F) {
        return std::nullopt;
    }

    const double logicalWidth = static_cast<double>(clientWidthPx) * 96.0 /
                                static_cast<double>(dpi);
    const double logicalHeight = static_cast<double>(clientHeightPx) * 96.0 /
                                 static_cast<double>(dpi);
    const double viewportWidth = logicalWidth / interfaceScale;
    const double viewportHeight = logicalHeight / interfaceScale;
    const double physicalPixelsPerDip = static_cast<double>(dpi) * interfaceScale / 96.0;
    if (!std::isfinite(viewportWidth) || !std::isfinite(viewportHeight) ||
        !std::isfinite(physicalPixelsPerDip) || viewportWidth <= 0.0 || viewportHeight <= 0.0 ||
        viewportWidth > std::numeric_limits<float>::max() ||
        viewportHeight > std::numeric_limits<float>::max() ||
        physicalPixelsPerDip > std::numeric_limits<float>::max()) {
        return std::nullopt;
    }

    return OverlayRenderMetrics{
        static_cast<float>(viewportWidth),
        static_cast<float>(viewportHeight),
        interfaceScale,
        static_cast<float>(physicalPixelsPerDip),
    };
}

std::optional<OverlaySurfaceGeometry> ComputeOverlaySurfaceGeometry(
    const float viewportWidthDip,
    const float viewportHeightDip,
    const float preferredPanelWidthDip,
    const std::optional<float> preferredPanelHeightDip) noexcept {
    if (!std::isfinite(viewportWidthDip) || !std::isfinite(viewportHeightDip) ||
        !std::isfinite(preferredPanelWidthDip) || viewportWidthDip <= 0.0F ||
        viewportHeightDip <= 0.0F || preferredPanelWidthDip <= 0.0F ||
        (preferredPanelHeightDip &&
            (!std::isfinite(*preferredPanelHeightDip) ||
             *preferredPanelHeightDip <= 0.0F))) {
        return std::nullopt;
    }

    const float preferredSideInset = std::clamp(viewportWidthDip * 0.04F, 8.0F, 36.0F);
    const float sideInset = std::min(preferredSideInset, viewportWidthDip * 0.1F);
    const float panelWidth = std::min(
        preferredPanelWidthDip, std::max(0.0F, viewportWidthDip - sideInset * 2.0F));
    const float panelX = (viewportWidthDip - panelWidth) * 0.5F;
    const float trayY = std::max(0.0F, viewportHeightDip - 112.0F);
    const float trayBottom = std::max(trayY, viewportHeightDip - 14.0F);
    const float topInset = std::min(
        std::min(20.0F, viewportHeightDip * 0.05F), trayY);
    const float trayReservation = std::min(158.0F, viewportHeightDip * 0.45F);
    const float preferredPanelBottom = std::max(
        topInset, viewportHeightDip - trayReservation);
    const float panelBottom = std::min(preferredPanelBottom, trayY);
    const float availablePanelHeight = panelBottom - topInset;
    const float panelHeight = preferredPanelHeightDip
        ? std::min(*preferredPanelHeightDip, availablePanelHeight)
        : availablePanelHeight;
    // Variable-height authored panels share one native bottom anchor. The
    // visible panel bottom, controller guide, and tray therefore remain a
    // single connected shell instead of leaving preference-sized dead space.
    const float panelY = panelBottom - panelHeight;

    const float contentInset = std::min(
        1.0F, std::min(panelWidth, panelHeight) * 0.1F);
    const float footerReservation = std::min(55.0F, panelHeight * 0.3F);
    const float widgetViewportWidth = std::max(0.0F, panelWidth - contentInset * 2.0F);
    const float widgetViewportHeight = std::max(
        0.0F, panelHeight - footerReservation - contentInset);
    const float footerY = panelY + panelHeight - footerReservation;
    return OverlaySurfaceGeometry{
        panelX,
        panelY,
        panelWidth,
        panelHeight,
        trayY,
        trayBottom - trayY,
        panelX + contentInset,
        panelY + contentInset,
        widgetViewportWidth,
        widgetViewportHeight,
        footerY,
        footerReservation,
    };
}

std::optional<OverlaySurfaceGeometry> ComputePanelLocalSurfaceGeometry(
    const float viewportWidthDip,
    const float viewportHeightDip) noexcept {
    if (!std::isfinite(viewportWidthDip) ||
        !std::isfinite(viewportHeightDip) ||
        viewportWidthDip <= 0.0F || viewportHeightDip <= 0.0F) {
        return std::nullopt;
    }
    const float contentInset = std::min(
        1.0F, std::min(viewportWidthDip, viewportHeightDip) * 0.1F);
    return OverlaySurfaceGeometry{
        0.0F,
        0.0F,
        viewportWidthDip,
        viewportHeightDip,
        0.0F,
        0.0F,
        contentInset,
        contentInset,
        std::max(0.0F, viewportWidthDip - contentInset * 2.0F),
        std::max(0.0F, viewportHeightDip - contentInset),
        viewportHeightDip,
        0.0F,
    };
}

ControllerGuideDensity ResolveControllerGuideDensity(
    const float availableWidthDip,
    const float textScale) noexcept {
    if (!std::isfinite(availableWidthDip) || availableWidthDip <= 0.0F) {
        return ControllerGuideDensity::Minimal;
    }
    const float boundedScale = std::isfinite(textScale)
        ? std::clamp(textScale, 0.75F, 2.0F)
        : 1.0F;
    const float effectiveWidth = availableWidthDip / boundedScale;
    if (effectiveWidth >= 620.0F) return ControllerGuideDensity::Full;
    if (effectiveWidth >= 360.0F) return ControllerGuideDensity::Compact;
    return ControllerGuideDensity::Minimal;
}

std::wstring BuildTrayControllerGuide(
    const ControllerGuideDensity density,
    const bool reorderMode,
    const bool selectedBridgeWidget,
    const float availableWidth,
    const MeasureControllerGuideText& measureText,
    const std::span<const ControllerGuideAction> quickActions,
    ControllerGuideHints* hints,
    const MeasureControllerGuideHints& measureHints,
    const bool radial) {
    if (hints) hints->clear();
    if (reorderMode) {
        if (hints) {
            if (density != ControllerGuideDensity::Minimal) hints->push_back({L"dpad-horizontal", L"Move"});
            hints->push_back({L"Y",L"Done"}); hints->push_back({L"B",L"Close"});
        }
        return density == ControllerGuideDensity::Minimal
            ? L"Y Done   B Close"
            : L"←→ Move   Y Done   B Close";
    }

    const std::size_t actionBudget =
        density == ControllerGuideDensity::Minimal ? 1U : 3U;
    std::wstring required = selectedBridgeWidget
        ? density == ControllerGuideDensity::Full
            ? L"Y Tap reorder / Hold restart   B Close   Menu Options"
            : L"Y Tap/Hold   B Close   Menu Options"
        : L"Y Reorder   B Close   Menu Options";
    if (radial) {
        const auto close = required.find(L"B Close");
        required.replace(close, 7, L"B Back");
    }

    const auto sanitize = [](const std::wstring_view value) {
        std::wstring result;
        result.reserve(value.size());
        bool priorSpace = false;
        for (const wchar_t character : value) {
            const bool whitespace = character == L' ' || character == L'\t' ||
                                    character == L'\r' || character == L'\n';
            if (whitespace) {
                if (!result.empty() && !priorSpace) result.push_back(L' ');
                priorSpace = true;
            } else {
                result.push_back(character);
                priorSpace = false;
            }
        }
        while (!result.empty() && result.back() == L' ') result.pop_back();
        return result;
    };

    const ControllerGuideHints requiredHints{{L"Y",L"Reorder"},{L"B",radial ? L"Back" : L"Close"},{L"Menu",L"Options"}};
    ControllerGuideHints contextualHints;
    ControllerGuideHints candidateHints;
    const auto join = [&](const std::vector<std::wstring>& contextual,
                          const bool includeEnter,
                          const bool includeSwitch) {
        std::wstring result;
        const auto append = [&](const std::wstring_view segment) {
            if (segment.empty()) return;
            if (!result.empty()) result += L"   ";
            result += segment;
        };
        candidateHints.clear();
        if (includeSwitch) candidateHints.push_back({radial ? L"left-stick-move" : L"dpad-horizontal",L"Switch widget"});
        candidateHints.insert(candidateHints.end(),contextualHints.begin(),contextualHints.begin()+contextual.size());
        if (includeEnter) candidateHints.push_back({L"A",L"Open"});
        candidateHints.insert(candidateHints.end(),requiredHints.begin(),requiredHints.end());
        if (hints) *hints=candidateHints;
        if (includeSwitch) append(radial ? L"Left stick Choose" : L"←→ Switch");
        for (const auto& segment : contextual) append(segment);
        if (includeEnter) append(radial ? L"A Open" : L"↑/A Enter");
        append(required);
        return result;
    };
    const auto fits = [&](const std::wstring_view value) {
        if (!std::isfinite(availableWidth) || availableWidth <= 0.0F ||
            !measureText) return false;
        const auto measured = measureHints ? measureHints(candidateHints) : measureText(value);
        return measured && std::isfinite(*measured) &&
            *measured <= availableWidth;
    };

    std::vector<std::wstring> contextual;
    contextual.reserve(std::min(actionBudget, quickActions.size()));
    for (const auto& action : quickActions) {
        if (contextual.size() >= actionBudget) break;
        const auto button = sanitize(action.button);
        const auto label = sanitize(action.label);
        if (button.empty() || label.empty()) continue;
        contextual.push_back(button + L" " + label);
        contextualHints.push_back({button,label});
    }

    const bool genericEnter = density != ControllerGuideDensity::Minimal;
    if (!contextual.empty()) {
        auto result = join(contextual, genericEnter, false);
        if (fits(result)) return result;
        // Generic entry is less important than every exact authored action.
        result = join(contextual, false, false);
        while (!contextual.empty() && !fits(result)) {
            contextual.pop_back();
            result = join(contextual, false, false);
        }
        return result;
    }

    auto result = join({}, genericEnter, density != ControllerGuideDensity::Minimal);
    if (fits(result)) return result;
    result = join({}, genericEnter, false);
    if (fits(result)) return result;
    if (hints) *hints=requiredHints;
    return required;
}

std::optional<EmbeddedMediaSurfaceBounds> ResolveEmbeddedMediaSurfaceBounds(
    const EmbeddedMediaSurfaceBounds safeArea,
    const float minimumWidthDip,
    const float minimumHeightDip,
    const float preferredWidthDip,
    const float preferredHeightDip,
    const float aspectRatio) noexcept {
    const auto positiveFinite = [](const float value) {
        return std::isfinite(value) && value > 0.0F;
    };
    if (!std::isfinite(safeArea.x) || !std::isfinite(safeArea.y) ||
        !positiveFinite(safeArea.width) || !positiveFinite(safeArea.height) ||
        !positiveFinite(minimumWidthDip) || !positiveFinite(minimumHeightDip) ||
        !positiveFinite(preferredWidthDip) || !positiveFinite(preferredHeightDip) ||
        !positiveFinite(aspectRatio) || minimumWidthDip > preferredWidthDip ||
        minimumHeightDip > preferredHeightDip) return std::nullopt;

    const float safeWidth = std::min(
        safeArea.width, safeArea.height * aspectRatio);
    const float authoredPreferredWidth = std::min(
        preferredWidthDip, preferredHeightDip * aspectRatio);
    const float authoredMinimumWidth = std::max(
        minimumWidthDip, minimumHeightDip * aspectRatio);
    const float width = std::min(
        safeWidth, std::max(authoredMinimumWidth, authoredPreferredWidth));
    const float height = width / aspectRatio;
    if (!positiveFinite(height) || height > safeArea.height) return std::nullopt;
    return EmbeddedMediaSurfaceBounds{
        safeArea.x + (safeArea.width - width) * 0.5F,
        safeArea.y + (safeArea.height - height) * 0.5F,
        width,
        height,
    };
}

std::optional<EmbeddedMediaSurfaceBounds>
ResolveOverlayFullscreenMediaSurfaceBounds(
    const EmbeddedMediaSurfaceBounds safeArea,
    const float minimumWidthDip,
    const float minimumHeightDip,
    const float aspectRatio) noexcept {
    const auto positiveFinite = [](const float value) {
        return std::isfinite(value) && value > 0.0F;
    };
    if (!std::isfinite(safeArea.x) || !std::isfinite(safeArea.y) ||
        !positiveFinite(safeArea.width) || !positiveFinite(safeArea.height) ||
        !positiveFinite(minimumWidthDip) || !positiveFinite(minimumHeightDip) ||
        !positiveFinite(aspectRatio)) return std::nullopt;
    const float width = std::min(
        safeArea.width, safeArea.height * aspectRatio);
    const float height = width / aspectRatio;
    if (!positiveFinite(height) || width < minimumWidthDip ||
        height < minimumHeightDip || height > safeArea.height)
        return std::nullopt;
    return EmbeddedMediaSurfaceBounds{
        safeArea.x + (safeArea.width - width) * 0.5F,
        safeArea.y + (safeArea.height - height) * 0.5F,
        width,
        height,
    };
}

std::optional<MediaViewportPresentationGeometry>
ResolveMediaViewportPresentationGeometry(
    const EmbeddedMediaSurfaceBounds viewport,
    const EmbeddedMediaSurfaceBounds clip,
    const float physicalPixelsPerDip) noexcept {
    const auto finiteRect = [](const EmbeddedMediaSurfaceBounds value) {
        return std::isfinite(value.x) && std::isfinite(value.y) &&
            std::isfinite(value.width) && std::isfinite(value.height) &&
            value.width > 0.0F && value.height > 0.0F;
    };
    if (!finiteRect(viewport) || !finiteRect(clip) ||
        !std::isfinite(physicalPixelsPerDip) || physicalPixelsPerDip <= 0.0F)
        return std::nullopt;

    const auto physical = [physicalPixelsPerDip](
                              const EmbeddedMediaSurfaceBounds value)
        -> std::optional<PhysicalRect> {
        constexpr double kMaximumCoordinate =
            static_cast<double>(std::numeric_limits<int>::max());
        const double left = std::round(
            static_cast<double>(value.x) * physicalPixelsPerDip);
        const double top = std::round(
            static_cast<double>(value.y) * physicalPixelsPerDip);
        const double right = std::round(
            static_cast<double>(value.x + value.width) * physicalPixelsPerDip);
        const double bottom = std::round(
            static_cast<double>(value.y + value.height) * physicalPixelsPerDip);
        if (!std::isfinite(left) || !std::isfinite(top) ||
            !std::isfinite(right) || !std::isfinite(bottom) ||
            std::abs(left) > kMaximumCoordinate ||
            std::abs(top) > kMaximumCoordinate ||
            std::abs(right) > kMaximumCoordinate ||
            std::abs(bottom) > kMaximumCoordinate)
            return std::nullopt;
        return PhysicalRect{
            static_cast<int>(left), static_cast<int>(top),
            static_cast<int>(right), static_cast<int>(bottom)};
    };

    const auto hostBounds = physical(viewport);
    auto hostClip = physical(clip);
    if (!hostBounds || !hostClip) return std::nullopt;
    hostClip->left = std::max(hostClip->left, hostBounds->left);
    hostClip->top = std::max(hostClip->top, hostBounds->top);
    hostClip->right = std::min(hostClip->right, hostBounds->right);
    hostClip->bottom = std::min(hostClip->bottom, hostBounds->bottom);
    const int width = hostBounds->right - hostBounds->left;
    const int height = hostBounds->bottom - hostBounds->top;
    if (width <= 0 || height <= 0 ||
        hostClip->right <= hostClip->left || hostClip->bottom <= hostClip->top)
        return std::nullopt;
    return MediaViewportPresentationGeometry{
        *hostBounds,
        *hostClip,
        PhysicalRect{0, 0, width, height},
    };
}

} // namespace widgetrail
