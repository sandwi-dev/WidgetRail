#pragma once
#include <algorithm>
#include <cstdint>

namespace widgetrail {
// All arguments use one monotonic microsecond clock. This schedules CPU work;
// it is not a GPU completion or frame-rate measurement.
class PreparationFrameBudget final {
public:
    [[nodiscard]] std::uint64_t Available(std::uint64_t now, std::uint64_t vblank,
        std::uint64_t period, bool paintPending) noexcept {
        if (paintPending) return 0;
        if (!waitingSince_ || now < waitingSince_) waitingSince_ = now;
        if (period < 1000 || period > 100000 || vblank > now) return 1000;
        const auto remaining = period - (now - vblank) % period;
        const auto reserve = std::min<std::uint64_t>(1500, period / 4);
        if (remaining <= reserve) return 0;
        const auto available = std::min<std::uint64_t>(3000, remaining - reserve);
        // An indivisible item can exceed a high-refresh frame's spare time.
        // Promote aged work instead of withholding it forever. The renderer
        // still yields after the first measurement that consumes the allowance.
        return available >= estimate_ || (available >= 500 && now - waitingSince_ >= 100000) ? available : 0;
    }
    void Observe(std::uint64_t elapsed) noexcept {
        // Estimate a complete slice, including style/preparation overhead.
        estimate_ = std::clamp<std::uint64_t>((estimate_ * 3 + elapsed) / 4, 500, 3000);
        waitingSince_ = 0;
    }
private:
    std::uint64_t estimate_{500};
    std::uint64_t waitingSince_{};
};
}
