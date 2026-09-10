#pragma once

#include "ControllerIsolationCore.h"

#include <cstdint>
#include <memory>
#include <string>
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
#include "ControllerIsolationRoutingSession.h"
#include "LocalControllerPolicy.h"
#endif

namespace widgetrail::isolation {

enum class LocalControllerProgress : std::uint8_t {
    Disabled, Preparing, Playing, Contained, AwaitingNeutral, Fault,
};

enum class LocalControllerOwnerAction : std::uint8_t { None, Enter, Recontain, Close };

[[nodiscard]] constexpr LocalControllerOwnerAction DecideLocalControllerOwnerAction(
    const LocalControllerProgress progress, const bool desiredOverlay) noexcept {
    if (desiredOverlay && progress == LocalControllerProgress::Playing)
        return LocalControllerOwnerAction::Enter;
    if (desiredOverlay && progress == LocalControllerProgress::AwaitingNeutral)
        return LocalControllerOwnerAction::Recontain;
    if (!desiredOverlay && progress == LocalControllerProgress::Contained)
        return LocalControllerOwnerAction::Close;
    return LocalControllerOwnerAction::None;
}

[[nodiscard]] constexpr bool LocalControllerIsolationConfigured(
    const LocalControllerProgress progress) noexcept {
    return progress != LocalControllerProgress::Disabled;
}

struct ControllerIsolationHostReading final {
    GamepadState state{};
    LocalControllerProgress progress{LocalControllerProgress::Disabled};
    std::uint64_t guideEvent{};
    std::uint32_t remainingInputStates{};
    bool queuedInput{};
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

    using Notify = void (*)(void*) noexcept;
    [[nodiscard]] bool Start(bool enabled, std::wstring& diagnostic,
                             Notify notify = nullptr, void* context = nullptr) noexcept;
    [[nodiscard]] bool PrepareOverlay(std::wstring& diagnostic) noexcept;
    void CloseOverlay() noexcept;
    [[nodiscard]] bool Poll(ControllerIsolationHostReading&, std::wstring&) noexcept;
    [[nodiscard]] bool PollGuide(std::uint64_t&, std::wstring&) noexcept;
    [[nodiscard]] bool active() const noexcept;
    void Stop() noexcept;
#if defined(WRAIL_LOCAL_CONTROLLER_TESTING)
    struct TestDependencies {
        using PollGuideCompatibility = std::uint8_t (*)(void*) noexcept;
        LocalPolicyEffects* effects{};
        SelectedControllerSource* source{};
        ControllerIsolationOutput* output{};
        PollGuideCompatibility pollGuideCompatibility{};
        void* guideCompatibilityContext{};
        SelectedControllerDescriptor descriptor{};
        std::filesystem::path journalPath;
        bool throwAfterApply{};
    };
    explicit ControllerIsolationHostSession(TestDependencies dependencies);
#endif

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace widgetrail::isolation
