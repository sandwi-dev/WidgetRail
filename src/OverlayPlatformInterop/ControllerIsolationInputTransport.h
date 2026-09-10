#pragma once

#include "ControllerIsolationCore.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <optional>

namespace widgetrail::isolation {

// Bounded in-process edge queue. Gameplay routing never waits for this UI consumer.
class ControllerIsolationInputEventQueue final {
public:
    static constexpr std::size_t Capacity = 256;
    [[nodiscard]] bool BeginInteraction() noexcept {
        if (generation_ == std::numeric_limits<std::uint64_t>::max()) return false;
        Clear(); ++generation_; active_ = true; return true;
    }
    void RetireInteraction() noexcept { active_ = false; Clear(); }
    [[nodiscard]] bool Push(const GamepadState& state, std::uint64_t ordinal) noexcept {
        if (!active_) return true;
        if (count_ == events_.size() || ordinal == 0) return false;
        events_[(head_ + count_) % events_.size()] = {state, ordinal}; ++count_;
        return true;
    }
    [[nodiscard]] std::optional<GamepadState> TakeNext() noexcept {
        if (count_ == 0) return std::nullopt;
        const auto result = events_[head_].state;
        head_ = (head_ + 1) % events_.size(); --count_; return result;
    }
    [[nodiscard]] std::size_t size() const noexcept { return count_; }
private:
    struct Event final { GamepadState state{}; std::uint64_t ordinal{}; };
    std::array<Event, Capacity> events_{};
    std::size_t head_{}, count_{};
    std::uint64_t generation_{};
    bool active_{};
    void Clear() noexcept { head_ = 0; count_ = 0; }
};

} // namespace widgetrail::isolation
