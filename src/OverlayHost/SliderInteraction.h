#pragma once

#include "ControllerNavigation.h"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>

namespace gba::input {

struct SliderInputDescriptor final {
    std::wstring_view widgetInstanceId;
    std::wstring_view inputScopeId;
    std::wstring_view nodeId;
    std::wstring_view valueChangedActionId;
    long long snapshotSequence{};
    double minimum{};
    double maximum{};
    double value{};
    double step{};
    bool disabled{};
    bool busy{};
};

struct SliderAdjustment final {
    bool consumed{};
    std::optional<double> requestedValue;
};

/// Host-owned optimistic slider targets. Entries are exact-runtime/scope/node
/// keyed, timeout-bounded, and LRU-capped; widget snapshots remain authoritative.
class SliderInteractionState final {
public:
    static constexpr std::size_t MaximumEntries = 256;
    static constexpr std::uint64_t PendingTimeoutMilliseconds = 2'000;

    [[nodiscard]] SliderAdjustment Adjust(
        const SliderInputDescriptor& slider,
        NavigationDirection direction,
        std::uint64_t nowMilliseconds);

    [[nodiscard]] std::optional<double> PresentationValue(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);

    void ForgetWidget(std::wstring_view widgetInstanceId) noexcept;
    [[nodiscard]] std::size_t size() const noexcept { return entries_.size(); }

private:
    struct Entry final {
        double minimum{};
        double maximum{};
        double step{};
        double authoritativeValue{};
        double targetValue{};
        long long snapshotSequence{};
        long long adjustmentSnapshotSequence{};
        std::wstring actionId;
        std::uint64_t lastAdjustment{};
        std::uint64_t lastAccess{};
        bool pending{};
    };

    [[nodiscard]] static bool Valid(const SliderInputDescriptor& slider) noexcept;
    [[nodiscard]] static std::wstring Key(const SliderInputDescriptor& slider);
    [[nodiscard]] Entry* FindAndSynchronize(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);
    [[nodiscard]] Entry* CreateOrSynchronize(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);
    void Trim(std::wstring_view protectedKey);

    std::unordered_map<std::wstring, Entry> entries_;
    std::uint64_t accessClock_{};
};

} // namespace gba::input
