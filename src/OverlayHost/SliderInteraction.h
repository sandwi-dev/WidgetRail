#pragma once

#include "ControllerNavigation.h"

#include <cstddef>
#include <cstdint>
#include <deque>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <variant>
#include <vector>

namespace widgetrail::input {

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
    bool activationRequired{};
};

struct SliderAdjustment final {
    bool consumed{};
};

struct SliderDispatch final {
    double requestedValue{};
    std::uint64_t intentGeneration{};
    NavigationDirection direction{NavigationDirection::None};
};

struct SliderReconciliation final {
    bool stateChanged{};
    bool visualChanged{};
};

struct SliderPresentationIdentity final {
    std::wstring widgetInstanceId;
    std::wstring inputScopeId;
    std::wstring nodeId;
    std::wstring valueChangedActionId;
    long long snapshotSequence{};
};

/// Host-owned optimistic slider targets. Entries are exact-runtime/scope/node
/// keyed, timeout-bounded, and LRU-capped; widget snapshots remain authoritative.
class SliderInteractionState final {
public:
    static constexpr std::size_t MaximumEntries = 256;
    static constexpr std::size_t MaximumRecentDispatchedValues = 16;
    static constexpr std::uint64_t SettlementDelayMilliseconds = 150;
    static constexpr std::uint64_t PendingTimeoutMilliseconds = 2'000;

    [[nodiscard]] SliderAdjustment Adjust(
        const SliderInputDescriptor& slider,
        NavigationDirection direction,
        std::uint64_t nowMilliseconds);

    /// Applies an exact host-originated value request (for example UIA
    /// RangeValue) through the same optimistic presentation owner used by
    /// controller steps. The widget snapshot remains authoritative.
    [[nodiscard]] bool SetRequestedValue(
        const SliderInputDescriptor& slider,
        double requestedValue,
        std::uint64_t nowMilliseconds);

    /// Takes the latest unsettled value once its trailing quiet period has
    /// elapsed. A forced take is used only while the exact current authority
    /// still owns a final A/B or focus-departure flush.
    [[nodiscard]] std::optional<SliderDispatch> TakePendingDispatch(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds,
        bool force = false);

    [[nodiscard]] bool EnterAdjustmentMode(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);

    [[nodiscard]] bool ExitAdjustmentMode(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);

    [[nodiscard]] bool AdjustmentModeActive(
        const SliderInputDescriptor& slider) const;

    /// Clears edit state everywhere except the currently focused exact slider.
    /// Optimistic value state remains available for reconciliation.
    void RetainAdjustmentMode(
        std::wstring_view widgetInstanceId,
        std::wstring_view inputScopeId,
        std::wstring_view nodeId) noexcept;

    /// Clears edit mode and every optimistic value when focus/lifecycle
    /// authority moves away. Exact identities let the existing renderer union
    /// rollback pixels with ordinary bounded focus damage.
    std::vector<SliderPresentationIdentity> DeactivateAll();

    /// Changes whenever the host-visible optimistic value set changes.
    [[nodiscard]] std::uint64_t presentationRevision() const noexcept {
        return presentationRevision_;
    }

    [[nodiscard]] std::optional<std::uint64_t> NextReconcileDeadline() const noexcept;

    [[nodiscard]] std::optional<double> PresentationValue(
        const SliderInputDescriptor& slider) const;

    /// Reconciles one exact slider against a newer authoritative snapshot or
    /// the bounded timeout. visualChanged is false for an acknowledgement that
    /// confirms pixels already presented by the optimistic target.
    [[nodiscard]] SliderReconciliation Reconcile(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);

    /// Cancels only the exact current slider request. Used by typed action
    /// failure/denial paths; unrelated and stale results cannot alter it.
    [[nodiscard]] bool CancelPending(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);
    [[nodiscard]] bool CancelPending(
        const SliderPresentationIdentity& identity,
        std::uint64_t intentGeneration,
        std::uint64_t nowMilliseconds);

    void ForgetWidget(std::wstring_view widgetInstanceId) noexcept;
    [[nodiscard]] std::size_t size() const noexcept { return entries_.size(); }

private:
    struct AuthoritativeValue final {};
    struct SettlingValue final {
        double target{};
        std::uint64_t lastActualChange{};
        NavigationDirection direction{NavigationDirection::None};
    };
    struct DispatchedValue final {
        double target{};
        std::uint64_t intentGeneration{};
        std::uint64_t expiresAt{};
    };
    struct GuardedEchoValue final {
        double presentedValue{};
        std::uint64_t guardUntil{};
    };
    using ValueState = std::variant<
        AuthoritativeValue, SettlingValue, DispatchedValue, GuardedEchoValue>;

    enum class AdjustmentMode {
        Inactive,
        Active,
    };

    struct RecentSentValue final {
        double value{};
        std::uint64_t intentGeneration{};
        std::uint64_t expiresAt{};
    };

    enum class UpdateKind {
        StepInput,
        AbsoluteInput,
        SnapshotAdmission,
        PumpDue,
        SynchronousReject,
        RetireValue,
        EnterAdjustment,
        ExitAdjustment,
    };

    struct UpdateEvent final {
        UpdateKind kind{UpdateKind::SnapshotAdmission};
        NavigationDirection direction{NavigationDirection::None};
        std::optional<double> absoluteValue;
        std::uint64_t intentGeneration{};
        bool force{};
    };

    struct UpdateResult final {
        bool consumed{};
        bool stateChanged{};
        bool visualChanged{};
        std::optional<SliderDispatch> dispatch;
    };

    struct Entry final {
        double minimum{};
        double maximum{};
        double step{};
        double latestObservedValue{};
        long long snapshotSequence{};
        std::wstring actionId;
        std::wstring widgetInstanceId;
        std::wstring inputScopeId;
        std::wstring nodeId;
        std::uint64_t lastAccess{};
        bool activationRequired{};
        AdjustmentMode adjustmentMode{AdjustmentMode::Inactive};
        ValueState valueState{AuthoritativeValue{}};
        std::uint64_t nextIntentGeneration{};
        std::deque<RecentSentValue> recentSentValues;
    };

    [[nodiscard]] static bool Valid(const SliderInputDescriptor& slider) noexcept;
    [[nodiscard]] static std::wstring Key(const SliderInputDescriptor& slider);
    [[nodiscard]] static SliderPresentationIdentity Identity(const Entry& entry);
    [[nodiscard]] Entry* AcquireForUpdate(
        const SliderInputDescriptor& slider);
    [[nodiscard]] const Entry* FindCurrentEntry(
        const SliderInputDescriptor& slider) const;
    [[nodiscard]] UpdateResult ApplyUpdate(
        const SliderInputDescriptor& slider,
        UpdateEvent event,
        std::uint64_t nowMilliseconds);
    [[nodiscard]] UpdateResult ApplyUpdate(
        Entry& entry,
        const SliderInputDescriptor* slider,
        UpdateEvent event,
        std::uint64_t nowMilliseconds);
    [[nodiscard]] static std::optional<double> PresentedValue(
        const Entry& entry) noexcept;
    [[nodiscard]] static bool MatchesRecentSentValue(
        const Entry& entry,
        double value) noexcept;
    [[nodiscard]] static std::optional<std::uint64_t> GuardExpiryForValue(
        const Entry& entry,
        double value) noexcept;
    static void PruneRecentSentValues(
        Entry& entry,
        std::uint64_t nowMilliseconds);
    [[nodiscard]] static std::uint64_t DeadlineAfter(
        std::uint64_t start,
        std::uint64_t delay) noexcept;
    [[nodiscard]] static bool HasSentDifferentValue(
        const Entry& entry,
        double value) noexcept;
    void Trim(std::wstring_view protectedKey);

    std::unordered_map<std::wstring, Entry> entries_;
    std::uint64_t accessClock_{};
    std::uint64_t presentationRevision_{};
};

} // namespace widgetrail::input
