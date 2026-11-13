#pragma once
#include <algorithm>
#include <chrono>

// Advance the established cadence rather than accumulating each wake-up's lateness.
// A late worker gets at least half an interval before the next frame, so a stall
// cannot trigger a burst of catch-up captures. Source timestamps remain QPC-owned.
template<class Clock, class Duration>
typename Clock::time_point AdvanceCaptureDeadline(typename Clock::time_point deadline,
    typename Clock::time_point now, Duration interval) {
    return std::max(deadline + interval, now + interval / 2);
}
