#pragma once

#include "PinnedSurfacePolicy.h"

#include <filesystem>
#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace widgetrail::pinned {

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

struct PlacementLimits final {
    float minimumWidthDip{240.0F};
    float minimumHeightDip{135.0F};
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
    unsigned int opacityPercent{100};
    std::wstring selectedLayoutId{L"host.full-widget"};
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
    float stepDip = 16.0F) noexcept;

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
