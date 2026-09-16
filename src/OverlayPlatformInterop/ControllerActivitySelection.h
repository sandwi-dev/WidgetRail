#pragma once

#include "OverlayPlatformInterop.h"
#include "../OverlayHost/ControllerInputOwnership.h"

#include <algorithm>
#include <cstdlib>
#include <optional>

namespace widgetrail::platform {

// Separate, raw-axis thresholds so controller settings can supply per-stick
// deadzones later. These select an input source; they do not rescale its axes.
struct ControllerActivityOptions final {
    int leftStickDeadzone{7'849};
    int rightStickDeadzone{8'000};
    int triggerThreshold{30};
};

struct ControllerSource final {
    input::ControllerReadPath path{input::ControllerReadPath::None};
    std::uint64_t device{};
    friend bool operator==(const ControllerSource&, const ControllerSource&) = default;
};

struct ControllerActivitySample final {
    ControllerSource source{};
    WidgetRailOverlayPlatformRawControllerState state{};
    bool connected{};
};

class ControllerActivitySelection final {
public:
    void SetOptions(ControllerActivityOptions options) noexcept {
        options.leftStickDeadzone = std::clamp(options.leftStickDeadzone, 0, 32'767);
        options.rightStickDeadzone = std::clamp(options.rightStickDeadzone, 0, 32'767);
        options.triggerThreshold = std::clamp(options.triggerThreshold, 1, 255);
        options_ = options;
    }

    [[nodiscard]] bool HasActivity(const WidgetRailOverlayPlatformRawControllerState& state) const noexcept {
        return state.buttons != 0 || state.leftTrigger >= options_.triggerThreshold ||
            state.rightTrigger >= options_.triggerThreshold ||
            std::abs(static_cast<int>(state.leftThumbX)) > options_.leftStickDeadzone ||
            std::abs(static_cast<int>(state.leftThumbY)) > options_.leftStickDeadzone ||
            std::abs(static_cast<int>(state.rightThumbX)) > options_.rightStickDeadzone ||
            std::abs(static_cast<int>(state.rightThumbY)) > options_.rightStickDeadzone;
    }

    // read({GameInput, 0}) asks for the latest device; a nonzero identity asks
    // for that exact device. XInput identities are slots; HID identities are
    // connection generations. Readers must not substitute a different owner.
    template<class Reader>
    ControllerActivitySample Poll(Reader&& read) noexcept {
        using Path = input::ControllerReadPath;
        if (held_) {
            auto sample = read(*held_);
            if (!sample.connected || sample.source != *held_) {
                const auto previous = *held_;
                held_.reset();
                return {previous, {}, false};
            }
            if (!HasActivity(sample.state)) {
                held_.reset();
                sample.state = {};
            }
            // Always deliver the old owner's release before considering another
            // controller. This also retires scroll and held-repeat state.
            return sample;
        }

        ControllerActivitySample idle{};
        const auto consider = [&](ControllerSource source) -> std::optional<ControllerActivitySample> {
            auto sample = read(source);
            if (!sample.connected) return std::nullopt;
            if (HasActivity(sample.state)) {
                held_ = last_ = sample.source;
                return sample;
            }
            if (!idle.connected || (last_ && sample.source == *last_)) idle = sample;
            return std::nullopt;
        };
        if (auto sample = consider({Path::GameInputVisibleLease, 0})) return *sample;
        for (std::uint64_t slot = 0; slot < 4; ++slot)
            if (auto sample = consider({Path::XInputCompatibility, slot})) return *sample;
        if (auto sample = consider({Path::DualSenseHid, 0})) return *sample;
        idle.state = {};
        return idle;
    }

    void Reset() noexcept { held_.reset(); last_.reset(); }

private:
    ControllerActivityOptions options_{};
    std::optional<ControllerSource> held_;
    std::optional<ControllerSource> last_;
};

} // namespace widgetrail::platform
