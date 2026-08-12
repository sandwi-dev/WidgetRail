#include "OverlayPlacement.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace gba {
namespace {

constexpr float kMinimumPanelWidthDip = 240.0F;
constexpr float kMaximumPanelWidthDip = 1'600.0F;
constexpr float kMinimumPanelHeightDip = 180.0F;
constexpr float kMaximumPanelHeightDip = 1'200.0F;
constexpr float kShellSideReservationDip = 72.0F;
constexpr float kShellVerticalReservationDip = 178.0F;

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
    const WidgetSurfaceConstraints constraints) noexcept {
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

    const float windowWidth = std::min(target.windowWidthDip, maximumWindowWidthDip);
    const float windowHeight = std::min(target.windowHeightDip, maximumWindowHeightDip);
    const float panelWidth = std::min(
        target.panelWidthDip,
        std::max(0.0F, windowWidth - kShellSideReservationDip));
    const float panelHeight = std::min(
        target.panelHeightDip,
        std::max(0.0F, windowHeight - kShellVerticalReservationDip));
    return ResolvedWidgetSurface{
        windowWidth,
        windowHeight,
        panelWidth,
        panelHeight,
        windowWidth + 0.001F < target.windowWidthDip ||
            windowHeight + 0.001F < target.windowHeightDip,
    };
}

std::optional<OverlayPlacement> ComputeOverlayPlacement(
    const PhysicalRect workArea,
    const unsigned int dpi,
    const float desiredWidthDip,
    const float desiredHeightDip,
    const OverlayMarginsDip margins) noexcept {
    const long long workWidth = static_cast<long long>(workArea.right) - workArea.left;
    const long long workHeight = static_cast<long long>(workArea.bottom) - workArea.top;
    if (workWidth <= 0 || workHeight <= 0 || workWidth > std::numeric_limits<int>::max() ||
        workHeight > std::numeric_limits<int>::max()) {
        return std::nullopt;
    }

    const auto desiredWidth = ScaleDip(desiredWidthDip, dpi);
    const auto desiredHeight = ScaleDip(desiredHeightDip, dpi);
    const auto side = ScaleDip(margins.side, dpi);
    const auto top = ScaleDip(margins.top, dpi);
    const auto bottom = ScaleDip(margins.bottom, dpi);
    if (!desiredWidth || !desiredHeight || !side || !top || !bottom ||
        *desiredWidth <= 0 || *desiredHeight <= 0) {
        return std::nullopt;
    }

    const long long horizontalMargins = static_cast<long long>(*side) * 2;
    const long long verticalMargins = static_cast<long long>(*top) + *bottom;
    const int availableWidth = static_cast<int>(std::max(1LL, workWidth - horizontalMargins));
    const int availableHeight = static_cast<int>(std::max(1LL, workHeight - verticalMargins));
    const int width = std::min(*desiredWidth, availableWidth);
    const int height = std::min(*desiredHeight, availableHeight);
    const long long x = static_cast<long long>(workArea.left) +
                        (workWidth - static_cast<long long>(width)) / 2;
    const long long preferredY = static_cast<long long>(workArea.bottom) - *bottom - height;
    const long long maximumY = static_cast<long long>(workArea.bottom) - height;
    const long long y = std::clamp(
        preferredY, static_cast<long long>(workArea.top), maximumY);
    if (x < std::numeric_limits<int>::min() || x > std::numeric_limits<int>::max() ||
        y < std::numeric_limits<int>::min() || y > std::numeric_limits<int>::max()) {
        return std::nullopt;
    }
    return OverlayPlacement{static_cast<int>(x), static_cast<int>(y), width, height};
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
    const float panelY = std::min(
        std::min(20.0F, viewportHeightDip * 0.05F), trayY);
    const float trayReservation = std::min(158.0F, viewportHeightDip * 0.45F);
    const float preferredPanelBottom = std::max(
        panelY, viewportHeightDip - trayReservation);
    const float panelBottom = std::min(preferredPanelBottom, trayY);
    const float availablePanelHeight = panelBottom - panelY;
    const float panelHeight = preferredPanelHeightDip
        ? std::min(*preferredPanelHeightDip, availablePanelHeight)
        : availablePanelHeight;

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
    const std::span<const ControllerGuideAction> quickActions) {
    if (reorderMode) {
        return density == ControllerGuideDensity::Minimal
            ? L"Y Done   B Close"
            : L"←→ Move   Y Done   B Close";
    }

    const std::size_t characterBudget =
        density == ControllerGuideDensity::Full ? 92U :
        density == ControllerGuideDensity::Compact ? 78U : 28U;
    const std::size_t labelBudget =
        density == ControllerGuideDensity::Full ? 14U :
        density == ControllerGuideDensity::Compact ? 8U : 6U;
    const std::size_t actionBudget =
        density == ControllerGuideDensity::Minimal ? 1U : 3U;
    const std::wstring_view suffix =
        density == ControllerGuideDensity::Full
            ? L"↑/A Enter   Y Tap reorder / Hold refresh   B Close"
            : density == ControllerGuideDensity::Compact
                ? L"↑/A Enter   Y Tap/Hold   B Close"
                : L"Y Tap/Hold   B Close";

    const auto sanitize = [](const std::wstring_view value,
                             const std::size_t limit) {
        std::wstring result;
        result.reserve(std::min(value.size(), limit));
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
            if (result.size() >= limit) break;
        }
        while (!result.empty() && result.back() == L' ') result.pop_back();
        if (value.size() > result.size() && result.size() >= 2) {
            result.back() = L'…';
        }
        return result;
    };

    std::wstring result;
    std::size_t accepted{};
    for (const auto& action : quickActions) {
        if (accepted >= actionBudget) break;
        const auto button = sanitize(action.button, 4U);
        const auto label = sanitize(action.label, labelBudget);
        if (button.empty() || label.empty()) continue;
        std::wstring segment = button + L" " + label;
        const std::size_t projected = result.size() +
            (result.empty() ? 0U : 3U) + segment.size() + 3U + suffix.size();
        if (projected > characterBudget) continue;
        if (!result.empty()) result += L"   ";
        result += segment;
        ++accepted;
    }
    if (!result.empty()) {
        result += L"   ";
        result += suffix;
        return result;
    }

    switch (density) {
    case ControllerGuideDensity::Full:
        return L"←→ Switch   ↑/A Enter   Y Tap reorder / Hold refresh   B Close";
    case ControllerGuideDensity::Compact:
        return L"←→ Switch   ↑/A Enter   Y Tap/Hold   B Close";
    case ControllerGuideDensity::Minimal:
        return std::wstring{suffix};
    }
    return std::wstring{suffix};
}

} // namespace gba
