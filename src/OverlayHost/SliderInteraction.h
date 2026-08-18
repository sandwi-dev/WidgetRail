#pragma once

#include "ControllerNavigation.h"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
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
    std::optional<double> requestedValue;
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

    [[nodiscard]] bool EnterAdjustmentMode(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);

    [[nodiscard]] bool ExitAdjustmentMode(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);

    [[nodiscard]] bool AdjustmentModeActive(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);

    /// Clears edit state everywhere except the currently focused exact slider.
    /// Pending optimistic values remain available for reconciliation.
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

    /// Clears timed-out entries. Callers paint only identities still present in
    /// the current admitted tree; retired/off-tree entries need no raster work.
    [[nodiscard]] std::vector<SliderPresentationIdentity> ExpireTimedOut(
        std::uint64_t nowMilliseconds);

    [[nodiscard]] std::optional<double> PresentationValue(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);

    /// Reconciles one exact slider against a newer authoritative snapshot or
    /// the bounded timeout. visualChanged is false for an acknowledgement that
    /// confirms pixels already presented by the optimistic target.
    [[nodiscard]] SliderReconciliation Reconcile(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);

    /// Cancels only the exact pending slider request. Used by typed action
    /// failure/denial paths; unrelated and stale results cannot alter it.
    [[nodiscard]] bool CancelPending(
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
        std::wstring widgetInstanceId;
        std::wstring inputScopeId;
        std::wstring nodeId;
        std::uint64_t lastAdjustment{};
        std::uint64_t lastAccess{};
        bool pending{};
        bool activationRequired{};
        bool adjustmentActive{};
    };

    [[nodiscard]] static bool Valid(const SliderInputDescriptor& slider) noexcept;
    [[nodiscard]] static std::wstring Key(const SliderInputDescriptor& slider);
    [[nodiscard]] static SliderPresentationIdentity Identity(const Entry& entry);
    [[nodiscard]] Entry* FindAndSynchronize(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds,
        SliderReconciliation* reconciliation = nullptr);
    [[nodiscard]] Entry* CreateOrSynchronize(
        const SliderInputDescriptor& slider,
        std::uint64_t nowMilliseconds);
    void Trim(std::wstring_view protectedKey);

    std::unordered_map<std::wstring, Entry> entries_;
    std::uint64_t accessClock_{};
    std::uint64_t presentationRevision_{};
};

} // namespace widgetrail::input
