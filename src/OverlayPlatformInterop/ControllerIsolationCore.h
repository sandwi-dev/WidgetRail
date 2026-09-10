#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <set>
#include <string>
#include <vector>

namespace widgetrail::isolation {

// These are proposed defaults for the first physical evaluation. Latency
// targets are deliberately separate from runtime safety limits: an outlier is
// evidence for acceptance review, not authority to disconnect healthy input.
struct RoutingBudgets final {
    std::size_t maximumQueuedReadings{256};
    std::uint64_t maximumQueuedReadingAgeMilliseconds{100};
    std::uint64_t minimumNeutralDwellMilliseconds{20};
    std::uint64_t maximumNeutralDwellMilliseconds{50};

    [[nodiscard]] friend constexpr bool operator==(
        const RoutingBudgets&, const RoutingBudgets&) noexcept = default;
};

struct RoutingAuthority final {
    std::uint64_t sessionGeneration{};
    std::uint64_t deviceGeneration{};
    std::uint64_t targetGeneration{};
    std::uint64_t leaseId{};

    [[nodiscard]] constexpr bool valid() const noexcept {
        return sessionGeneration != 0 && deviceGeneration != 0 &&
            targetGeneration != 0 && leaseId != 0;
    }

    [[nodiscard]] friend constexpr bool operator==(
        const RoutingAuthority&, const RoutingAuthority&) noexcept = default;
};

struct SelectedDeviceIdentity final {
    std::uint64_t enrollmentToken{};
    std::array<std::uint8_t, 32> applicationLocalId{};
    std::array<std::uint8_t, 32> applicationLocalRootId{};
    std::wstring containerId;
    std::wstring pnpPath;
    std::uint16_t vendorId{};
    std::uint16_t productId{};
    bool knownVirtualOutput{};

    [[nodiscard]] bool valid() const noexcept;

    [[nodiscard]] friend bool operator==(
        const SelectedDeviceIdentity&,
        const SelectedDeviceIdentity&) noexcept = default;
};

struct GamepadState final {
    std::uint16_t buttons{};
    std::uint8_t leftTrigger{};
    std::uint8_t rightTrigger{};
    std::int16_t leftThumbX{};
    std::int16_t leftThumbY{};
    std::int16_t rightThumbX{};
    std::int16_t rightThumbY{};

    [[nodiscard]] friend constexpr bool operator==(
        const GamepadState&, const GamepadState&) noexcept = default;
};

// The single policy owner for admitting an initial or resumed neutral report.
// Callers that acquire an output backend must validate this before creating a
// target; ControllerIsolationCore revalidates it when the session begins.
[[nodiscard]] bool ControllerIsolationNeutralEntry(
    const GamepadState& state) noexcept;

struct DeviceReading final {
    RoutingAuthority authority;
    // The serialized reader owner assigns a non-zero token to the exact
    // enrolled device identity. Per-report traffic carries only that fixed
    // token and generation-bound authority; paths and strings never enter the
    // hot queue.
    std::uint64_t deviceEnrollmentToken{};
    // WidgetRail assigns this ordinal in the chronologically serialized
    // GameInput callback. It is not GameInput v3's currently unimplemented
    // per-kind sequence number.
    std::uint64_t ordinal{};
    std::uint64_t inputTimestampMicroseconds{};
    std::uint64_t observedAtMilliseconds{};
    GamepadState state;
    bool connected{};

    [[nodiscard]] friend bool operator==(
        const DeviceReading&, const DeviceReading&) noexcept = default;
};

enum class RoutingMode {
    Disabled,
    Playing,
    OverlayInteraction,
    AwaitingNeutral,
    Fault,
};

enum class RoutingFault {
    None,
    InvalidBudgets,
    InvalidInitialAuthority,
    InitialStateNotNeutral,
    DeviceDisconnected,
    ReadingOrderViolation,
    ReadingQueueOverflow,
    StaleQueuedReading,
    OutputSubmissionFailed,
    HostLeaseExpired,
};

enum class ReadingAdmission {
    Accepted,
    Duplicate,
    RejectedAuthority,
    RejectedDevice,
    RejectedOrder,
    Faulted,
};

enum class CommandResult {
    Applied,
    Resumed,
    Waiting,
    RejectedAuthority,
    RejectedState,
    Faulted,
};

class VirtualOutputEffects {
public:
    virtual ~VirtualOutputEffects() = default;
    [[nodiscard]] virtual bool Submit(const GamepadState& state) noexcept = 0;
    virtual void RemoveOwnedTarget() noexcept = 0;
};

// All calls must be made by one serialized control/reading owner. The core is
// intentionally lock-free; callers may not race commands with report intake.
class ControllerIsolationCore final {
public:
    static constexpr std::size_t MaximumReadingCapacity = 256;

    explicit ControllerIsolationCore(
        VirtualOutputEffects& output,
        RoutingBudgets budgets = {}) noexcept;

    [[nodiscard]] CommandResult BeginSession(
        const RoutingAuthority& authority,
        const SelectedDeviceIdentity& device,
        const DeviceReading& current,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] ReadingAdmission EnqueueReading(
        const DeviceReading& reading) noexcept;
    [[nodiscard]] CommandResult Drain(std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] CommandResult EnterOverlay(
        const RoutingAuthority& authority,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] CommandResult CloseOverlay(
        const RoutingAuthority& authority,
        const DeviceReading& current,
        std::uint64_t measuredP99ReadingIntervalMilliseconds,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] CommandResult ObserveCurrent(
        const RoutingAuthority& authority,
        const DeviceReading& current,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] CommandResult HoldOverlay(
        const RoutingAuthority& authority,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] CommandResult Tick(
        std::uint64_t nowMilliseconds,
        const std::optional<DeviceReading>& current = std::nullopt) noexcept;
    [[nodiscard]] CommandResult StopSession(
        const RoutingAuthority& authority) noexcept;

    [[nodiscard]] RoutingMode mode() const noexcept { return mode_; }
    [[nodiscard]] RoutingFault fault() const noexcept { return fault_; }
    [[nodiscard]] std::size_t queuedReadings() const noexcept {
        return readingCount_;
    }
    [[nodiscard]] std::uint64_t resumeAfterOrdinal() const noexcept {
        return resumeAfterOrdinal_;
    }

private:
    [[nodiscard]] bool Matches(
        const RoutingAuthority& authority,
        std::uint64_t deviceEnrollmentToken) const noexcept;
    [[nodiscard]] bool ExactCurrent(const DeviceReading& current) const noexcept;
    [[nodiscard]] bool EquivalentReport(
        const DeviceReading& left,
        const DeviceReading& right) const noexcept;
    void ClearReadings() noexcept;
    void PushReading(const DeviceReading& reading) noexcept;
    [[nodiscard]] DeviceReading PopReading() noexcept;
    [[nodiscard]] CommandResult ApplyAwaitingNeutral(
        const DeviceReading& current,
        std::uint64_t nowMilliseconds) noexcept;
    [[nodiscard]] bool SubmitNeutral() noexcept;
    void RetireOwnedTarget() noexcept;
    void EnterFault(RoutingFault fault) noexcept;

    VirtualOutputEffects& output_;
    RoutingBudgets budgets_;
    RoutingMode mode_{RoutingMode::Disabled};
    RoutingFault fault_{RoutingFault::None};
    RoutingAuthority authority_{};
    SelectedDeviceIdentity device_{};
    std::array<DeviceReading, MaximumReadingCapacity> readings_{};
    std::size_t readingHead_{};
    std::size_t readingCount_{};
    std::optional<DeviceReading> current_;
    std::optional<DeviceReading> lastAdmittedReading_;
    std::optional<std::uint64_t> neutralSinceMilliseconds_;
    std::uint64_t neutralDwellMilliseconds_{};
    std::uint64_t lastQueuedOrdinal_{};
    std::uint64_t lastObservedOrdinal_{};
    std::uint64_t resumeAfterOrdinal_{};
    bool budgetsValid_{};
    bool targetRetired_{true};
};

struct LatencyAcceptanceTargets final {
    std::uint64_t physicalToSubmissionP95Microseconds{3'000};
    std::uint64_t physicalToSubmissionP99Microseconds{5'000};
    std::uint64_t physicalToSubmissionWorstMicroseconds{10'000};
    std::uint64_t enterOverlayToNeutralP95Microseconds{8'000};
    std::uint64_t enterOverlayToNeutralP99Microseconds{12'000};
};

class LatencyAcceptanceEvidence final {
public:
    void ObservePhysicalToSubmission(
        std::uint64_t elapsedMicroseconds,
        const LatencyAcceptanceTargets& targets = {}) noexcept;
    void ObserveEnterOverlayToNeutral(
        std::uint64_t elapsedMicroseconds,
        const LatencyAcceptanceTargets& targets = {}) noexcept;

    [[nodiscard]] std::uint64_t observations() const noexcept {
        return observations_;
    }
    [[nodiscard]] std::uint64_t outliers() const noexcept { return outliers_; }

private:
    std::uint64_t observations_{};
    std::uint64_t outliers_{};
};

struct HidHideSnapshot final {
    // The platform adapter canonicalizes these strings before they enter the
    // core. This owner performs only exact ordinal set reconciliation.
    std::set<std::wstring> applicationPaths;
    std::set<std::wstring> deviceInstanceIds;
    bool active{};
    bool applicationListInverted{};

    [[nodiscard]] friend bool operator==(
        const HidHideSnapshot&, const HidHideSnapshot&) noexcept = default;
};

struct HidHideJournal final {
    HidHideSnapshot before;
    std::set<std::wstring> ownedApplicationPaths;
    std::set<std::wstring> ownedDeviceInstanceIds;
    bool activatedBySession{};
};

struct HidHideApplyPlan final {
    HidHideJournal journal;
    HidHideSnapshot desired;
};

enum class HidHideApplyStatus {
    Ready,
    InvalidInput,
    ActivationConflict,
};

struct HidHideApplyResult final {
    HidHideApplyStatus status{HidHideApplyStatus::InvalidInput};
    std::optional<HidHideApplyPlan> plan;
};

enum class HidHideRestoreStatus {
    Restored,
    ExternalAdditionsPreserved,
    Conflict,
};

struct HidHideRestorePlan final {
    HidHideRestoreStatus status{HidHideRestoreStatus::Conflict};
    std::optional<HidHideSnapshot> desired;
};

[[nodiscard]] HidHideApplyResult PlanHidHideApply(
    const HidHideSnapshot& before,
    const std::wstring& workerApplicationPath,
    const std::set<std::wstring>& selectedDeviceInstanceIds);

[[nodiscard]] HidHideRestorePlan PlanHidHideRestore(
    const HidHideJournal& journal,
    const HidHideSnapshot& current);
[[nodiscard]] HidHideRestorePlan PlanHidHideRecovery(
    const HidHideJournal& journal,
    const HidHideSnapshot& current);

} // namespace widgetrail::isolation
