#include "PinnedSurfacePlacement.h"

#include <Windows.h>

#include <algorithm>
#include <cmath>
#include <fstream>
#include <iomanip>
#include <limits>
#include <sstream>
#include <utility>

namespace widgetrail::pinned {
namespace {

constexpr std::size_t kMaximumStoredWidgets = 64;
constexpr std::size_t kMaximumLayoutIdLength = 128;
// The durable parser owns a structural envelope, not a widget's current
// interactive resize policy: maximum authored content plus pinned chrome.
constexpr PlacementLimits kDurableStorageLimits{
    surface_geometry::kMinimumPinnedWidthDip,
    surface_geometry::kMinimumPinnedHeightDip,
    surface_geometry::kMaximumPinnedWidthDip,
    surface_geometry::kMaximumPinnedHeightDip};

[[nodiscard]] bool ValidLimits(const PlacementLimits& limits) noexcept {
    return std::isfinite(limits.minimumWidthDip) &&
        std::isfinite(limits.minimumHeightDip) &&
        std::isfinite(limits.maximumWidthDip) &&
        std::isfinite(limits.maximumHeightDip) &&
        limits.minimumWidthDip > 0.0F && limits.minimumHeightDip > 0.0F &&
        limits.maximumWidthDip >= limits.minimumWidthDip &&
        limits.maximumHeightDip >= limits.minimumHeightDip;
}

[[nodiscard]] bool ValidMonitor(const MonitorWorkArea& monitor) noexcept {
    return !monitor.stableId.empty() && monitor.dpi >= 48 && monitor.dpi <= 960 &&
        monitor.workArea.right > monitor.workArea.left &&
        monitor.workArea.bottom > monitor.workArea.top;
}

[[nodiscard]] int ScaleDip(const float dip, const unsigned int dpi) noexcept {
    return static_cast<int>(std::lround(static_cast<double>(dip) * dpi / 96.0));
}

[[nodiscard]] const MonitorWorkArea* SelectMonitor(
    const std::vector<MonitorWorkArea>& monitors,
    const std::wstring_view desired,
    bool& fallback) noexcept {
    const auto exact = std::ranges::find_if(monitors, [&](const auto& monitor) {
        return ValidMonitor(monitor) && monitor.stableId == desired;
    });
    if (exact != monitors.end()) return &*exact;
    if (!desired.empty()) fallback = true;
    const auto primary = std::ranges::find_if(monitors, [](const auto& monitor) {
        return ValidMonitor(monitor) && monitor.primary;
    });
    if (primary != monitors.end()) return &*primary;
    const auto first = std::ranges::find_if(monitors, ValidMonitor);
    return first == monitors.end() ? nullptr : &*first;
}

[[nodiscard]] PhysicalRect Constrain(
    PhysicalRect bounds,
    const MonitorWorkArea& monitor,
    const PlacementLimits& limits,
    const PlacementMode mode) noexcept {
    const auto& work = monitor.workArea;
    const int workWidth = work.right - work.left;
    const int workHeight = work.bottom - work.top;
    const int minimumWidth = std::min(workWidth, std::max(1, ScaleDip(limits.minimumWidthDip, monitor.dpi)));
    const int minimumHeight = std::min(workHeight, std::max(1, ScaleDip(limits.minimumHeightDip, monitor.dpi)));
    const int maximumWidth = std::min(workWidth, std::max(minimumWidth, ScaleDip(limits.maximumWidthDip, monitor.dpi)));
    const int maximumHeight = std::min(workHeight, std::max(minimumHeight, ScaleDip(limits.maximumHeightDip, monitor.dpi)));
    int width = std::clamp(bounds.right - bounds.left, minimumWidth, maximumWidth);
    int height = std::clamp(bounds.bottom - bounds.top, minimumHeight, maximumHeight);
    int left = bounds.left;
    int top = bounds.top;
    if (mode == PlacementMode::Resize) {
        left = std::clamp(left, work.left, work.right - minimumWidth);
        top = std::clamp(top, work.top, work.bottom - minimumHeight);
        width = std::min(width, work.right - left);
        height = std::min(height, work.bottom - top);
    } else {
        left = std::clamp(left, work.left, work.right - width);
        top = std::clamp(top, work.top, work.bottom - height);
    }
    return {left, top, left + width, top + height};
}

[[nodiscard]] bool ValidPlacement(
    const DurablePinnedPlacement& placement,
    const PlacementLimits& limits) noexcept {
    return placement.schemaVersion == 1 && !placement.monitorId.empty() &&
        std::isfinite(placement.anchorX) && std::isfinite(placement.anchorY) &&
        placement.anchorX >= 0.0 && placement.anchorX <= 1.0 &&
        placement.anchorY >= 0.0 && placement.anchorY <= 1.0 &&
        std::isfinite(placement.widthDip) && std::isfinite(placement.heightDip) &&
        placement.widthDip >= limits.minimumWidthDip &&
        placement.heightDip >= limits.minimumHeightDip &&
        placement.widthDip <= limits.maximumWidthDip &&
        placement.heightDip <= limits.maximumHeightDip &&
        !placement.selectedLayoutId.empty() &&
        placement.selectedLayoutId.size() <= kMaximumLayoutIdLength;
}

} // namespace

std::optional<ResolvedPlacement> ResolveDurablePlacement(
    const std::vector<MonitorWorkArea>& monitors,
    const std::optional<DurablePinnedPlacement>& persisted,
    const PlacementLimits limits,
    const std::optional<std::pair<float, float>> initialWindowExtentDip) noexcept {
    if (!ValidLimits(limits)) return std::nullopt;
    const bool persistedValid = persisted && ValidPlacement(*persisted, limits);
    bool fallback = persisted.has_value() && !persistedValid;
    const auto* monitor = SelectMonitor(
        monitors, persistedValid ? persisted->monitorId : std::wstring_view{}, fallback);
    if (!monitor) return std::nullopt;
    DurablePinnedPlacement placement;
    if (persistedValid) placement = *persisted;
    else {
        placement.monitorId = monitor->stableId;
        placement.anchorX = 1.0;
        placement.anchorY = 0.0;
        const float initialWidth = initialWindowExtentDip &&
                std::isfinite(initialWindowExtentDip->first)
            ? initialWindowExtentDip->first : 480.0F;
        const float initialHeight = initialWindowExtentDip &&
                std::isfinite(initialWindowExtentDip->second)
            ? initialWindowExtentDip->second : 270.0F;
        placement.widthDip = std::clamp(
            initialWidth, limits.minimumWidthDip, limits.maximumWidthDip);
        placement.heightDip = std::clamp(
            initialHeight, limits.minimumHeightDip, limits.maximumHeightDip);
    }
    const int workWidth = monitor->workArea.right - monitor->workArea.left;
    const int workHeight = monitor->workArea.bottom - monitor->workArea.top;
    if (workWidth < ScaleDip(limits.minimumWidthDip, monitor->dpi) ||
        workHeight < ScaleDip(limits.minimumHeightDip, monitor->dpi)) return std::nullopt;
    const int width = std::min(workWidth, std::max(1, ScaleDip(placement.widthDip, monitor->dpi)));
    const int height = std::min(workHeight, std::max(1, ScaleDip(placement.heightDip, monitor->dpi)));
    const int travelX = std::max(0, workWidth - width);
    const int travelY = std::max(0, workHeight - height);
    if (!persistedValid) {
        const int margin = std::max(0, ScaleDip(16.0F, monitor->dpi));
        placement.anchorX = travelX == 0 ? 0.0
            : static_cast<double>(std::max(0, travelX - margin)) / travelX;
        placement.anchorY = travelY == 0 ? 0.0
            : static_cast<double>(std::min(travelY, margin)) / travelY;
    }
    const int left = monitor->workArea.left + static_cast<int>(std::lround(placement.anchorX * travelX));
    const int top = monitor->workArea.top + static_cast<int>(std::lround(placement.anchorY * travelY));
    const auto constrained = Constrain(
        {left, top, left + width, top + height}, *monitor, limits, PlacementMode::Move);
    return ResolvedPlacement{
        monitor->stableId, constrained, monitor->dpi, fallback,
        constrained.left != left || constrained.top != top ||
            constrained.right != left + width || constrained.bottom != top + height,
    };
}

std::optional<DurablePinnedPlacement> CaptureDurablePlacement(
    const MonitorWorkArea& monitor,
    const PhysicalRect bounds,
    const PlacementLimits limits) noexcept {
    if (!ValidMonitor(monitor) || !ValidLimits(limits)) return std::nullopt;
    if (monitor.workArea.right - monitor.workArea.left <
            ScaleDip(limits.minimumWidthDip, monitor.dpi) ||
        monitor.workArea.bottom - monitor.workArea.top <
            ScaleDip(limits.minimumHeightDip, monitor.dpi)) return std::nullopt;
    const auto constrained = Constrain(bounds, monitor, limits, PlacementMode::Move);
    const int width = constrained.right - constrained.left;
    const int height = constrained.bottom - constrained.top;
    const int travelX = std::max(0, monitor.workArea.right - monitor.workArea.left - width);
    const int travelY = std::max(0, monitor.workArea.bottom - monitor.workArea.top - height);
    return DurablePinnedPlacement{
        1,
        monitor.stableId,
        travelX == 0 ? 0.0 : static_cast<double>(constrained.left - monitor.workArea.left) / travelX,
        travelY == 0 ? 0.0 : static_cast<double>(constrained.top - monitor.workArea.top) / travelY,
        static_cast<float>(width) * 96.0F / monitor.dpi,
        static_cast<float>(height) * 96.0F / monitor.dpi,
        kMaximumOpacityPercent,
        std::wstring{kFullWidgetLayoutId},
    };
}

std::optional<PlacementSession> BeginPlacementSession(
    const PlacementMode mode,
    const PhysicalRect bounds,
    std::wstring runtimeGeneration,
    std::wstring presentationGeneration) noexcept {
    if ((mode != PlacementMode::Move && mode != PlacementMode::Resize &&
         mode != PlacementMode::Adjust) ||
        bounds.right <= bounds.left || bounds.bottom <= bounds.top ||
        runtimeGeneration.empty() || presentationGeneration.empty()) return std::nullopt;
    return PlacementSession{
        mode, bounds, bounds, std::move(runtimeGeneration),
        std::move(presentationGeneration)};
}

bool SetPlacementSessionBounds(
    PlacementSession& session,
    const PhysicalRect proposed,
    const MonitorWorkArea& monitor,
    const PlacementLimits limits) noexcept {
    if (session.mode == PlacementMode::None || !ValidMonitor(monitor) ||
        !ValidLimits(limits)) return false;
    const auto constrained = Constrain(proposed, monitor, limits, session.mode);
    if (constrained.left == session.current.left &&
        constrained.top == session.current.top &&
        constrained.right == session.current.right &&
        constrained.bottom == session.current.bottom) return false;
    session.current = constrained;
    return true;
}

bool StepPlacementSession(
    PlacementSession& session,
    const PlacementDirection direction,
    const MonitorWorkArea& monitor,
    const PlacementLimits limits,
    const float stepDip) noexcept {
    if (!std::isfinite(stepDip) || stepDip <= 0.0F) return false;
    const int step = std::max(1, ScaleDip(stepDip, monitor.dpi));
    auto proposed = session.current;
    if (session.mode == PlacementMode::Move) {
        if (direction == PlacementDirection::Left) proposed.left -= step, proposed.right -= step;
        if (direction == PlacementDirection::Right) proposed.left += step, proposed.right += step;
        if (direction == PlacementDirection::Up) proposed.top -= step, proposed.bottom -= step;
        if (direction == PlacementDirection::Down) proposed.top += step, proposed.bottom += step;
    } else if (session.mode == PlacementMode::Resize) {
        if (direction == PlacementDirection::Left) proposed.right -= step;
        if (direction == PlacementDirection::Right) proposed.right += step;
        if (direction == PlacementDirection::Up) proposed.bottom -= step;
        if (direction == PlacementDirection::Down) proposed.bottom += step;
    } else return false;
    return SetPlacementSessionBounds(session, proposed, monitor, limits);
}

std::optional<DurablePinnedPlacement> CommitPlacementSession(
    const PlacementSession& session,
    const std::wstring_view runtimeGeneration,
    const std::wstring_view presentationGeneration,
    const MonitorWorkArea& monitor,
    const PlacementLimits limits) noexcept {
    if (session.mode == PlacementMode::None ||
        session.runtimeGeneration != runtimeGeneration ||
        session.presentationGeneration != presentationGeneration) return std::nullopt;
    return CaptureDurablePlacement(monitor, session.current, limits);
}

PinnedPlacementStore::PinnedPlacementStore(std::filesystem::path path)
    : path_(std::move(path)) {}

std::map<std::wstring, DurablePinnedPlacement> PinnedPlacementStore::LoadAll() const {
    std::map<std::wstring, DurablePinnedPlacement> placements;
    std::wifstream input(path_);
    std::wstring header;
    std::size_t count{};
    if (!(input >> header >> count) ||
        (header != L"wrail-pinned-placement-v1" &&
         header != L"wrail-pinned-placement-v2" &&
         header != L"wrail-pinned-placement-v3") ||
        count > kMaximumStoredWidgets) return {};
    const bool hasOpacity = header != L"wrail-pinned-placement-v1";
    const bool hasLayout = header == L"wrail-pinned-placement-v3";
    input.ignore(std::numeric_limits<std::streamsize>::max(), L'\n');
    for (std::size_t index = 0; index < count; ++index) {
        std::wstring line;
        if (!std::getline(input, line)) return {};
        std::wistringstream row(line);
        std::wstring widgetId;
        DurablePinnedPlacement placement;
        if (!(row >> std::quoted(widgetId) >> placement.schemaVersion >>
              std::quoted(placement.monitorId) >> placement.anchorX >> placement.anchorY >>
              placement.widthDip >> placement.heightDip) ||
            widgetId.empty() || widgetId.size() > 128 ||
            !ValidPlacement(placement, kDurableStorageLimits)) return {};
        if (hasOpacity) {
            std::wstring opacityToken;
            if (row >> opacityToken) {
                std::size_t consumed{};
                try {
                    const auto parsed = std::stoull(opacityToken, &consumed, 10);
                    placement.opacityPercent = consumed == opacityToken.size() &&
                            parsed <= std::numeric_limits<unsigned int>::max()
                        ? static_cast<unsigned int>(parsed)
                        : kMaximumOpacityPercent;
                } catch (...) {
                    placement.opacityPercent = kMaximumOpacityPercent;
                }
            }
        }
        if (hasLayout && !(row >> std::quoted(placement.selectedLayoutId))) return {};
        std::wstring trailingField;
        if (row >> trailingField) return {};
        if (placement.opacityPercent < kMinimumOpacityPercent ||
            placement.opacityPercent > kMaximumOpacityPercent)
            placement.opacityPercent = kMaximumOpacityPercent;
        placements.insert_or_assign(std::move(widgetId), std::move(placement));
    }
    std::wstring trailingLine;
    while (std::getline(input, trailingLine)) {
        if (trailingLine.find_first_not_of(L" \t\r\n") != std::wstring::npos)
            return {};
    }
    return placements;
}

std::optional<DurablePinnedPlacement> PinnedPlacementStore::Load(
    const std::wstring_view widgetId) const {
    const auto all = LoadAll();
    const auto found = all.find(std::wstring(widgetId));
    return found == all.end() ? std::nullopt
                              : std::optional<DurablePinnedPlacement>(found->second);
}

bool PinnedPlacementStore::Save(
    const std::wstring_view widgetId,
    const DurablePinnedPlacement& placement,
    std::wstring& error) const {
    if (widgetId.empty() || widgetId.size() > 128 ||
        !ValidPlacement(placement, kDurableStorageLimits)) {
        error = L"Pinned placement data is invalid.";
        return false;
    }
    auto all = LoadAll();
    if (!all.contains(std::wstring(widgetId)) && all.size() >= kMaximumStoredWidgets) {
        error = L"Pinned placement storage is full.";
        return false;
    }
    all.insert_or_assign(std::wstring(widgetId), placement);
    std::error_code directoryError;
    std::filesystem::create_directories(path_.parent_path(), directoryError);
    if (directoryError) {
        error = L"Pinned placement directory is unavailable.";
        return false;
    }
    const auto temporary = path_.wstring() + L".tmp-" + std::to_wstring(GetCurrentProcessId());
    {
        std::wofstream output(temporary, std::ios::trunc);
        if (!output) {
            error = L"Pinned placement temporary file could not be created.";
            return false;
        }
        output << L"wrail-pinned-placement-v3 " << all.size() << L'\n'
               << std::setprecision(17);
        for (const auto& [id, value] : all) {
            output << std::quoted(id) << L' ' << value.schemaVersion << L' '
                   << std::quoted(value.monitorId) << L' ' << value.anchorX << L' '
                   << value.anchorY << L' ' << value.widthDip << L' '
                   << value.heightDip << L' ' << value.opacityPercent << L' '
                   << std::quoted(value.selectedLayoutId) << L'\n';
        }
        output.flush();
        if (!output) {
            DeleteFileW(temporary.c_str());
            error = L"Pinned placement temporary file could not be completed.";
            return false;
        }
    }
    if (!MoveFileExW(
            temporary.c_str(), path_.c_str(),
            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        DeleteFileW(temporary.c_str());
        error = L"Pinned placement could not be committed atomically.";
        return false;
    }
    error.clear();
    return true;
}

} // namespace widgetrail::pinned
