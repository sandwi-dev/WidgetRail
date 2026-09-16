#pragma once

#include "WidgetBridgeClient.h"
#include "WidgetLifecycle.h"

#include <condition_variable>
#include <chrono>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <functional>
#include <map>
#include <mutex>
#include <optional>
#include <stop_token>
#include <string>
#include <string_view>
#include <thread>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace widgetrail {

enum class WidgetSessionFailureStage {
    None,
    Start,
    Catalog,
    Snapshot,
    Protocol,
    Lifecycle,
    Restart,
};

enum class WidgetSessionEventKind {
    BridgeSessionReplaced,
    CatalogChanged,
    SnapshotAdmitted,
    LifecycleChanged,
    Restarted,
    Failed,
    StaleCompletionRejected,
};

enum class WidgetSessionRequestKind {
    Catalog,
    Establish,
    Snapshot,
    Lifecycle,
    Restart,
};

enum class WidgetSessionTraceStage {
    LifecycleDecision,
    RequestQueued,
    RequestStarted,
    RequestCompleted,
};

enum class WidgetSessionTraceAction {
    None,
    Queued,
    Deduplicated,
    Replaced,
    Skipped,
};

enum class WidgetSessionTraceReason {
    None,
    Deferred,
    AlreadyCurrent,
    FailureCurrent,
    QueueFull,
    MissingDescriptor,
    ExistingRequest,
    NewerTarget,
    CheckpointFallback,
    StaleBaseResynchronization,
    ShuttingDown,
};

enum class WidgetSessionCompletionDisposition {
    None,
    Admitted,
    Failed,
    StaleGeneration,
    WrongLifecycle,
    Cancelled,
};

struct WidgetSessionTraceEvent final {
    std::uint64_t correlationId{};
    WidgetSessionTraceStage stage{WidgetSessionTraceStage::LifecycleDecision};
    WidgetSessionTraceAction action{WidgetSessionTraceAction::None};
    WidgetSessionTraceReason reason{WidgetSessionTraceReason::None};
    WidgetSessionCompletionDisposition disposition{
        WidgetSessionCompletionDisposition::None};
    std::uint64_t requestId{};
    std::uint64_t generation{};
    WidgetSessionRequestKind requestKind{WidgetSessionRequestKind::Snapshot};
    WidgetLifecycleState lifecycle{WidgetLifecycleState::Background};
    std::wstring widgetId;
    std::uint64_t queuedAt{};
    std::uint64_t startedAt{};
    std::uint64_t completedAt{};

    friend bool operator==(
        const WidgetSessionTraceEvent&,
        const WidgetSessionTraceEvent&) = default;
};

struct WidgetSessionFailure final {
    WidgetSessionFailureStage stage{WidgetSessionFailureStage::None};
    std::wstring safeMessage;
};

enum class WidgetPresentationAuthority {
    Unavailable,
    Current,
    RefreshRetained,
    FailureRetained,
};

enum class WidgetRefreshState {
    Current,
    RefreshRequested,
    RefreshInFlight,
};

enum class WidgetCommittedViewUse {
    Presentation,
    Interaction,
    DashboardQuickAction,
};

struct WidgetSessionPresentation final {
    const WidgetSnapshot* snapshot{};
    WidgetPresentationAuthority authority{WidgetPresentationAuthority::Unavailable};
    WidgetLifecycleState lifecycle{WidgetLifecycleState::Background};

    /// One admitted checkpoint remains the coherent presentation and
    /// Interactive input/UIA owner while an ordinary replacement is in flight.
    /// Background dashboard actions may use the last admitted declaration;
    /// the Bridge revalidates its exact binding before delivery. They also
    /// require a descriptor whose residency policy admits background actions.
    /// Failure and transition-retained content remain outside this policy.
    [[nodiscard]] bool HasCommittedViewAuthority(
        const WidgetCommittedViewUse use =
            WidgetCommittedViewUse::Presentation,
        const bool allowBackgroundDashboard = false) const noexcept {
        if (!snapshot) return false;
        switch (use) {
        case WidgetCommittedViewUse::Presentation:
            return authority == WidgetPresentationAuthority::Current ||
                authority == WidgetPresentationAuthority::RefreshRetained;
        case WidgetCommittedViewUse::Interaction:
            return (authority == WidgetPresentationAuthority::Current ||
                    authority == WidgetPresentationAuthority::RefreshRetained) &&
                lifecycle == WidgetLifecycleState::Interactive;
        case WidgetCommittedViewUse::DashboardQuickAction:
            return (authority == WidgetPresentationAuthority::Current &&
                    lifecycle == WidgetLifecycleState::Visible) ||
                (allowBackgroundDashboard && lifecycle == WidgetLifecycleState::Background &&
                 (authority == WidgetPresentationAuthority::Current ||
                  authority == WidgetPresentationAuthority::RefreshRetained));
        }
        return false;
    }

    [[nodiscard]] bool RefreshPending() const noexcept {
        return snapshot &&
            authority == WidgetPresentationAuthority::RefreshRetained;
    }
};

struct WidgetSessionRuntimeChange final {
    std::wstring widgetId;
    std::wstring previousInstanceId;
};

struct WidgetSessionCatalogChange final {
    std::vector<WidgetSessionRuntimeChange> runtimeChanges;
    std::vector<std::wstring> availableWidgetIds;
};

struct WidgetSessionEvent final {
    WidgetSessionEventKind kind{WidgetSessionEventKind::Failed};
    std::wstring widgetId;
    WidgetSessionFailure failure;
    std::optional<WidgetSessionCatalogChange> catalog;
    bool completedRestart{};
    std::uint64_t correlationId{};
    std::uint64_t requestId{};
    std::uint64_t generation{};
    WidgetSessionRequestKind requestKind{WidgetSessionRequestKind::Snapshot};
    WidgetLifecycleState lifecycle{WidgetLifecycleState::Background};
    WidgetSessionCompletionDisposition completionDisposition{
        WidgetSessionCompletionDisposition::None};
    std::optional<WidgetPresentationImpact> presentationImpact;
    long long bridgeSessionGeneration{};
};

[[nodiscard]] bool TryCoalescePresentationEvents(const WidgetSessionEvent& previous, WidgetSessionEvent& current);

template <typename Value>
struct WidgetSessionOperationResult final {
    std::optional<Value> value;
    WidgetSessionFailureStage failureStage{WidgetSessionFailureStage::None};
    std::wstring safeError;
    WidgetBridgeRequestFailureCategory requestFailureCategory{
        WidgetBridgeRequestFailureCategory::None};

    [[nodiscard]] static WidgetSessionOperationResult Success(Value result) {
        return {std::move(result), WidgetSessionFailureStage::None, {}};
    }

    [[nodiscard]] static WidgetSessionOperationResult Failure(
        WidgetSessionFailureStage stage,
        std::wstring safeError,
        WidgetBridgeRequestFailureCategory requestFailureCategory =
            WidgetBridgeRequestFailureCategory::None) {
        return {
            std::nullopt, stage, std::move(safeError), requestFailureCategory};
    }
};

struct WidgetSessionOperations final {
    std::function<WidgetSessionOperationResult<bool>(std::stop_token)> ensureStarted;
    std::function<WidgetSessionOperationResult<std::vector<WidgetDescriptor>>(
        std::stop_token)> listWidgets;
    std::function<WidgetSessionOperationResult<WidgetPresentationPublication>(
        std::stop_token, std::wstring_view, WidgetLifecycleState, long long,
        WidgetPresentationTransactionKind, long long)>
        establish;
    std::function<WidgetSessionOperationResult<bool>(
        std::stop_token, std::wstring_view, WidgetLifecycleState)> setLifecycle;
    std::function<WidgetSessionOperationResult<WidgetPresentationPublication>(
        std::stop_token, std::wstring_view, long long,
        WidgetPresentationTransactionKind, long long)> getSnapshot;
    std::function<WidgetSessionOperationResult<WidgetPresentationMaterialization>(
        const WidgetSnapshot&, const WidgetPresentationUpdate&, std::wstring_view)>
        materializeUpdate;
    std::function<WidgetSessionOperationResult<bool>(
        std::stop_token, std::wstring_view)> restart;
    std::function<long long()> bridgeSessionGeneration;
    std::function<std::optional<WidgetPresentationImpact>(const WidgetSnapshot&, const WidgetSnapshot&)> compareSnapshots;
};

/// Owns bridge-facing widget session state and serial request policy. The host
/// remains the sole HWND, renderer, input, focus, and presentation authority.
/// Worker completions cross back as bounded values and are applied only by the
/// UI thread through TakeEvents().
class WidgetSessionCoordinator final {
public:
    static constexpr std::size_t MaximumPendingRequests = 32;
    static constexpr unsigned int MaximumCatalogRetryAttempts = 3;

    explicit WidgetSessionCoordinator(
        WidgetSessionOperations operations,
        std::function<void()> completionAvailable = {},
        std::function<void(const WidgetSessionTraceEvent&)> traceAvailable = {},
        std::function<std::uint64_t()> timestamp = {});
    ~WidgetSessionCoordinator();

    WidgetSessionCoordinator(const WidgetSessionCoordinator&) = delete;
    WidgetSessionCoordinator& operator=(const WidgetSessionCoordinator&) = delete;

    /// Startup/development-only blocking catalog establishment before the
    /// Win32 message loop is exposed. Normal refreshes use RequestCatalog().
    [[nodiscard]] std::optional<WidgetSessionCatalogChange> EstablishCatalog();
    [[nodiscard]] std::optional<WidgetSnapshot> EstablishPresentationForProbe(
        std::wstring_view widgetId,
        WidgetLifecycleState state);
    [[nodiscard]] bool RequestCatalog();
    [[nodiscard]] bool RequestSnapshot(
        std::wstring_view widgetId,
        bool explicitRetry = false,
        std::uint64_t correlationId = 0);
    [[nodiscard]] bool RequestRestart(
        std::wstring_view widgetId,
        std::uint64_t correlationId = 0);
    void SetLifecycleTargets(
        const std::map<std::wstring, WidgetLifecycleState, std::less<>>& desired,
        bool deferColdStart = false,
        std::uint64_t correlationId = 0,
        std::wstring_view correlationWidgetId = {});

    /// Applies completed operations on the caller/UI thread. No callback runs
    /// while coordinator state is mutating.
    [[nodiscard]] std::vector<WidgetSessionEvent> TakeEvents();

    [[nodiscard]] const std::vector<WidgetDescriptor>& descriptors() const noexcept {
        return descriptors_;
    }
    [[nodiscard]] const WidgetDescriptor* FindDescriptor(std::wstring_view widgetId) const noexcept;
    [[nodiscard]] bool Contains(std::wstring_view widgetId) const noexcept;
    [[nodiscard]] const WidgetSnapshot* Snapshot(std::wstring_view widgetId) const noexcept;
    [[nodiscard]] WidgetRefreshState RefreshState(
        std::wstring_view widgetId) const noexcept;
    [[nodiscard]] WidgetSessionPresentation Presentation(
        std::wstring_view widgetId) const noexcept;
    [[nodiscard]] const WidgetSessionFailure* Failure(std::wstring_view widgetId) const noexcept;
    [[nodiscard]] std::optional<WidgetLifecycleState> Lifecycle(
        std::wstring_view widgetId) const noexcept;

    void RecordFailure(
        std::wstring_view widgetId,
        WidgetSessionFailureStage stage,
        std::wstring safeMessage);
    void RecordRuntimeFailure(
        std::wstring_view widgetId,
        WidgetSessionFailureStage stage,
        std::wstring safeMessage);
    void ClearFailure(std::wstring_view widgetId);
    /// Records semantic refresh demand without waking a worker or removing its
    /// last-admitted checkpoint. RequestSnapshot/SetLifecycleTargets own work.
    void MarkRefreshRequested(std::wstring_view widgetId);
    void MarkAllRefreshRequested();
    /// Hard removal only: restart, runtime/generation replacement, trust or
    /// protocol/corruption authority transitions. Ordinary invalidation must
    /// use MarkRefreshRequested().
    void RemoveSnapshot(std::wstring_view widgetId);

    [[nodiscard]] std::size_t RetainedCheckpointCount() const noexcept {
        return snapshots_.size();
    }

    void ResetCatalogRetry() noexcept { catalogRetryAttempts_ = 0; }
    [[nodiscard]] std::optional<unsigned int> NextCatalogRetryDelay(
        bool refreshSucceeded,
        bool revisionInFlight,
        bool hostActive) noexcept;

    [[nodiscard]] std::size_t PendingRequestCount() const noexcept;
    [[nodiscard]] bool DrainLifecycle(std::chrono::milliseconds timeout);
    void Shutdown() noexcept;

private:
    using RequestKind = WidgetSessionRequestKind;

    struct QueueResult final {
        WidgetSessionTraceAction action{WidgetSessionTraceAction::Skipped};
        WidgetSessionTraceReason reason{WidgetSessionTraceReason::None};
        std::uint64_t requestId{};
        std::uint64_t generation{};
        std::uint64_t queuedAt{};
        std::uint64_t startedAt{};

        [[nodiscard]] bool accepted() const noexcept {
            return action != WidgetSessionTraceAction::Skipped;
        }
    };

    struct Request final {
        std::uint64_t id{};
        std::uint64_t correlationId{};
        std::uint64_t generation{};
        RequestKind kind{RequestKind::Snapshot};
        std::wstring widgetId;
        WidgetLifecycleState lifecycle{WidgetLifecycleState::Background};
        std::wstring expectedInstanceId;
        std::wstring expectedRuntimeGeneration;
        std::wstring expectedPresentationGeneration;
        long long expectedBridgeSessionGeneration{};
        std::uint64_t refreshRevision{};
        long long baseSequence{};
        WidgetPresentationTransactionKind transactionKind{
            WidgetPresentationTransactionKind::OrdinaryCheckpoint};
        long long recoveryOriginSequence{};
        std::uint64_t queuedAt{};
        std::uint64_t startedAt{};
    };

    struct Completion final {
        Request request;
        WidgetSessionFailure failure;
        WidgetBridgeRequestFailureCategory requestFailureCategory{
            WidgetBridgeRequestFailureCategory::None};
        std::optional<std::vector<WidgetDescriptor>> descriptors;
        std::optional<WidgetSnapshot> snapshot;
        std::optional<WidgetPresentationUpdate> update;
        std::optional<WidgetPresentationTransactionKind> transactionKind;
        long long responseBaseSequence{};
        long long responseRecoveryOriginSequence{};
        std::optional<WidgetPresentationImpact> presentationImpact;
        std::optional<bool> acknowledged;
        std::uint64_t completedAt{};
        bool cancelled{};
        long long bridgeSessionGeneration{};
    };

    [[nodiscard]] QueueResult Queue(Request request);
    [[nodiscard]] bool HasPending(RequestKind kind, std::wstring_view widgetId) const noexcept;
    [[nodiscard]] bool HasConflictingLifecycleRequest(
        std::wstring_view widgetId,
        WidgetLifecycleState lifecycle) const noexcept;
    [[nodiscard]] static bool IsPresentationChanging(RequestKind kind) noexcept;
    [[nodiscard]] static bool SamePresentationAuthority(
        const Request& left,
        const Request& right) noexcept;
    [[nodiscard]] bool PresentationRequestBlockedLocked(
        const Request& request) const noexcept;
    [[nodiscard]] bool HasExecutableRequestLocked() const noexcept;
    void ReleasePresentationAdmission(const Request& request) noexcept;
    void QueueCoalescedRefreshAfterAdmission(
        const Request& request,
        bool refreshRequestedDuringAdmission);
    void SupersedeSnapshotRequests(
        std::wstring_view widgetId,
        WidgetLifecycleState lifecycle) noexcept;
    void RevokeRequests(std::wstring_view widgetId) noexcept;
    [[nodiscard]] Request MakeRequest(
        RequestKind kind,
        std::wstring widgetId = {},
        WidgetLifecycleState lifecycle = WidgetLifecycleState::Background,
        std::uint64_t correlationId = 0);
    void EmitTrace(
        const Request& request,
        WidgetSessionTraceStage stage,
        WidgetSessionTraceAction action = WidgetSessionTraceAction::None,
        WidgetSessionTraceReason reason = WidgetSessionTraceReason::None,
        WidgetSessionCompletionDisposition disposition =
            WidgetSessionCompletionDisposition::None,
        std::uint64_t completedAt = 0) const;
    [[nodiscard]] bool AttachDeduplicatedCompletionObserverLocked(
        const Request& owner,
        const Request& request);
    void EmitCompletionTrace(
        const Request& request,
        WidgetSessionTraceReason reason,
        WidgetSessionCompletionDisposition disposition,
        std::uint64_t completedAt = 0);
    void EmitLifecycleDecision(
        std::uint64_t correlationId,
        std::wstring_view widgetId,
        WidgetLifecycleState lifecycle,
        RequestKind requestKind,
        WidgetSessionTraceAction action,
        WidgetSessionTraceReason reason,
        const Request* request = nullptr) const;
    void WorkerLoop(std::stop_token stopToken);
    [[nodiscard]] Completion Execute(Request request, std::stop_token stopToken);
    [[nodiscard]] std::optional<WidgetSessionCatalogChange> ApplyCatalog(
        std::vector<WidgetDescriptor> descriptors);
    [[nodiscard]] bool CompletionRuntimeIsCurrent(const Request& request) const noexcept;
    [[nodiscard]] bool CompletionIsCurrent(const Request& request) const noexcept;
    void MarkRefreshInFlight(std::wstring_view widgetId, std::uint64_t requestId);
    void CompleteRefresh(const Request& request, bool admitted) noexcept;
    void HardRemoveCheckpoint(std::wstring_view widgetId) noexcept;
    [[nodiscard]] WidgetSessionCatalogChange ResetBridgeSessionAuthority(
        long long bridgeSessionGeneration);
    [[nodiscard]] long long CurrentBridgeSessionGeneration() const noexcept;
    void FailCompletion(Completion& completion, WidgetSessionOperationResult<bool> result);

    WidgetSessionOperations operations_;
    std::function<void()> completionAvailable_;
    std::function<void(const WidgetSessionTraceEvent&)> traceAvailable_;
    std::function<std::uint64_t()> timestamp_;
    mutable std::mutex queueMutex_;
    std::condition_variable queueChanged_;
    std::deque<Request> pending_;
    std::deque<Completion> completed_;
    std::unordered_map<std::wstring, Request> presentationAdmissions_;
    std::unordered_map<std::uint64_t, std::vector<Request>>
        deduplicatedCompletionObservers_;
    std::size_t deduplicatedCompletionObserverCount_{};
    std::optional<Request> inFlight_;
    std::optional<std::stop_source> inFlightStop_;
    std::jthread worker_;
    std::uint64_t nextRequestId_{};
    bool shuttingDown_{};

    std::vector<WidgetDescriptor> descriptors_;
    std::unordered_map<std::wstring, WidgetSnapshot> snapshots_;
    std::unordered_map<std::wstring, WidgetRefreshState> refreshStates_;
    std::unordered_map<std::wstring, std::uint64_t> refreshRequestIds_;
    std::unordered_map<std::wstring, std::uint64_t> refreshRevisions_;
    std::unordered_map<std::wstring, WidgetSessionFailure> failures_;
    std::unordered_map<std::wstring, WidgetLifecycleState> lifecycleStates_;
    std::unordered_map<std::wstring, WidgetLifecycleState> lifecycleTargets_;
    std::unordered_map<std::wstring, std::uint64_t> generations_;
    std::unordered_set<std::wstring> awaitingRestartSnapshot_;
    std::atomic<long long> bridgeSessionGeneration_{};
    unsigned int catalogRetryAttempts_{};
};

} // namespace widgetrail
