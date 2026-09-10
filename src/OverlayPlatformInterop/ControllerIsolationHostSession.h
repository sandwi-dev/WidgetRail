#pragma once

#include "ControllerIsolationCore.h"

#include <cstdint>
#include <memory>
#include <string>

namespace widgetrail::isolation {

enum class LocalControllerProgress : std::uint8_t {
    Disabled, Preparing, Playing, Contained, AwaitingNeutral, Fault,
};

struct ControllerIsolationHostReading final {
    GamepadState state{};
    LocalControllerProgress progress{LocalControllerProgress::Disabled};
    std::uint64_t guideEvent{};
    std::uint32_t remainingInputStates{};
};

// Application-lifetime controller owner. Its private routing thread is the only
// caller of GameInput, the routing state machine and the ViGEm target. The UI
// thread only requests mode changes and consumes bounded local snapshots.
class ControllerIsolationHostSession final {
public:
    ControllerIsolationHostSession() noexcept;
    ~ControllerIsolationHostSession();
    ControllerIsolationHostSession(const ControllerIsolationHostSession&) = delete;
    ControllerIsolationHostSession& operator=(const ControllerIsolationHostSession&) = delete;

    [[nodiscard]] bool Start(bool enabled, std::wstring& diagnostic) noexcept;
    [[nodiscard]] bool PrepareOverlay(std::wstring& diagnostic) noexcept;
    void CloseOverlay() noexcept;
    [[nodiscard]] bool Poll(ControllerIsolationHostReading&, std::wstring&) noexcept;
    [[nodiscard]] bool PollGuide(std::uint64_t&, std::wstring&) noexcept;
    [[nodiscard]] bool active() const noexcept;
    void Stop() noexcept;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace widgetrail::isolation
