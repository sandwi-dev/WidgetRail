#pragma once

#include "ControllerNavigation.h"
#include "PinnedSurfacePolicy.h"
#include "WidgetProtocolPresentationContract.generated.h"
#include "WidgetSurfaceGeometry.h"

#include <filesystem>
#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace widgetrail::pinned {

inline constexpr std::wstring_view kFullWidgetLayoutId =
    protocol_contract::FullWidgetLayoutId;
inline constexpr unsigned int kMinimumOpacityPercent = 30;
inline constexpr unsigned int kMaximumOpacityPercent = 100;

enum class PlacementMode {
    None,
    Move,
    Resize,
    Adjust,
};

enum class PlacementDirection {
    Left,
    Right,
    Up,
    Down,
};

[[nodiscard]] constexpr std::optional<PlacementDirection>
ResolvePlacementDirection(const input::NavigationDirection direction) noexcept {
    switch (direction) {
    case input::NavigationDirection::None: return std::nullopt;
    case input::NavigationDirection::Left: return PlacementDirection::Left;
    case input::NavigationDirection::Right: return PlacementDirection::Right;
    case input::NavigationDirection::Up: return PlacementDirection::Up;
    case input::NavigationDirection::Down: return PlacementDirection::Down;
    }
    return std::nullopt;
}

struct PlacementLimits final {
    float minimumWidthDip{surface_geometry::kMinimumPinnedWidthDip};
    float minimumHeightDip{surface_geometry::kMinimumPinnedHeightDip};
    float maximumWidthDip{960.0F};
    float maximumHeightDip{540.0F};
};

struct DurablePinnedPlacement final {
    int schemaVersion{1};
    std::wstring monitorId;
    double anchorX{};
    double anchorY{};
    float widthDip{480.0F};
    float heightDip{270.0F};
    unsigned int opacityPercent{kMaximumOpacityPercent};
    std::wstring selectedLayoutId{kFullWidgetLayoutId};
};

struct PlacementSession final {
    PlacementMode mode{PlacementMode::None};
    PhysicalRect original;
    PhysicalRect current;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
};

[[nodiscard]] std::optional<ResolvedPlacement> ResolveDurablePlacement(
    const std::vector<MonitorWorkArea>& monitors,
    const std::optional<DurablePinnedPlacement>& persisted,
    PlacementLimits limits = {},
    std::optional<std::pair<float, float>> initialWindowExtentDip = std::nullopt) noexcept;

[[nodiscard]] std::optional<DurablePinnedPlacement> CaptureDurablePlacement(
    const MonitorWorkArea& monitor,
    PhysicalRect bounds,
    PlacementLimits limits = {}) noexcept;

[[nodiscard]] std::optional<PlacementSession> BeginPlacementSession(
    PlacementMode mode,
    PhysicalRect bounds,
    std::wstring runtimeGeneration,
    std::wstring presentationGeneration) noexcept;

[[nodiscard]] bool StepPlacementSession(
    PlacementSession& session,
    PlacementDirection direction,
    const MonitorWorkArea& monitor,
    PlacementLimits limits = {},
    float stepDip = surface_geometry::kPlacementAdjustmentStepDip) noexcept;

[[nodiscard]] bool SetPlacementSessionBounds(
    PlacementSession& session,
    PhysicalRect proposed,
    const MonitorWorkArea& monitor,
    PlacementLimits limits = {}) noexcept;

[[nodiscard]] std::optional<DurablePinnedPlacement> CommitPlacementSession(
    const PlacementSession& session,
    std::wstring_view runtimeGeneration,
    std::wstring_view presentationGeneration,
    const MonitorWorkArea& monitor,
    PlacementLimits limits = {}) noexcept;

class PinnedPlacementStore final {
public:
    explicit PinnedPlacementStore(std::filesystem::path path);

    [[nodiscard]] std::optional<DurablePinnedPlacement> Load(
        std::wstring_view widgetId) const;
    [[nodiscard]] bool Save(
        std::wstring_view widgetId,
        const DurablePinnedPlacement& placement,
        std::wstring& error) const;
    [[nodiscard]] const std::filesystem::path& path() const noexcept { return path_; }

private:
    [[nodiscard]] std::map<std::wstring, DurablePinnedPlacement> LoadAll() const;
    std::filesystem::path path_;
};

} // namespace widgetrail::pinned
