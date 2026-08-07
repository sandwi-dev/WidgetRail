#pragma once

#include <cstdint>
#include <optional>

namespace gba::input {

enum class NavigationDirection { None, Left, Right, Up, Down };

struct StickNavigationOptions final {
    int engageThreshold{15'000};
    int releaseThreshold{9'000};
    std::uint64_t initialRepeatMilliseconds{360};
    std::uint64_t repeatMilliseconds{125};
};

/// Converts a noisy two-axis stick into stable four-way navigation with
/// hysteresis, dominant-axis switching, and deterministic repeat timing.
class StickNavigator final {
public:
    explicit StickNavigator(StickNavigationOptions options = {}) noexcept;

    void Prime(short x, short y, std::uint64_t now) noexcept;
    [[nodiscard]] std::optional<NavigationDirection> Update(
        short x, short y, std::uint64_t now) noexcept;
    void Reset() noexcept;

private:
    [[nodiscard]] NavigationDirection Resolve(short x, short y) const noexcept;

    StickNavigationOptions options_;
    NavigationDirection direction_{NavigationDirection::None};
    std::uint64_t nextRepeat_{};
};

} // namespace gba::input
