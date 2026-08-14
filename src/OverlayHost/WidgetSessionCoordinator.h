#pragma once

#include "WidgetBridgeClient.h"
#include "WidgetLifecycle.h"

#include <condition_variable>
#include <chrono>
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

namespace gba {

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
    CatalogChanged,
    SnapshotAdmitted,
    LifecycleChanged,
    Restarted,
    Failed,
    StaleCompletionRejected,
};

struct WidgetSessionFailure final {
    WidgetSessionFailureStage stage{WidgetSessionFailureStage::None};
    std::wstring safeMessage;
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
};

template <typename Value>
struct WidgetSessionOperationResult final {
    std::optional<Value> value;
    WidgetSessionFailureStage failureStage{WidgetSessionFailureStage::None};
    std::wstring safeError;

    [[nodiscard]] static WidgetSessionOperationResult Success(Value result) {
        return {std::move(result), WidgetSessionFailureStage::None, {}};
    }

    [[nodiscard]] static WidgetSessionOperationResult Failure(
        WidgetSessionFailureStage stage,
        std::wstring safeError) {
        return {std::nullopt, stage, std::move(safeError)};
    }
};

struct WidgetSessionOperations final {
    std::function<WidgetSessionOperationResult<bool>(std::stop_token)> ensureStarted;
    std::function<WidgetSessionOperationResult<std::vector<WidgetDescriptor>>(
        std::stop_token)> listWidgets;
    std::function<WidgetSessionOperationResult<WidgetSnapshot>(
        std::stop_token, std::wstring_view, WidgetLifecycleState)> establish;
    std::function<WidgetSessionOperationResult<bool>(
        std::stop_token, std::wstring_view, WidgetLifecycleState)> setLifecycle;
    std::function<WidgetSessionOperationResult<WidgetSnapshot>(
        std::stop_token, std::wstring_view)> getSnapshot;
    std::function<WidgetSessionOperationResult<bool>(
        std::stop_token, std::wstring_view)> restart;
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
        std::function<void()> completionAvailable = {});
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
    [[nodiscard]] bool RequestSnapshot(std::wstring_view widgetId, bool explicitRetry = false);
    [[nodiscard]] bool RequestRestart(std::wstring_view widgetId);
    void SetLifecycleTargets(
        const std::map<std::wstring, WidgetLifecycleState, std::less<>>& desired,
        bool deferColdStart = false);

    /// Applies completed operations on the caller/UI thread. No callback runs
    /// while coordinator state is mutating.
    [[nodiscard]] std::vector<WidgetSessionEvent> TakeEvents();

    [[nodiscard]] const std::vector<WidgetDescriptor>& descriptors() const noexcept {
        return descriptors_;
    }
    [[nodiscard]] const WidgetDescriptor* FindDescriptor(std::wstring_view widgetId) const noexcept;
    [[nodiscard]] bool Contains(std::wstring_view widgetId) const noexcept;
    [[nodiscard]] const WidgetSnapshot* Snapshot(std::wstring_view widgetId) const noexcept;
    [[nodiscard]] const WidgetSessionFailure* Failure(std::wstring_view widgetId) const noexcept;
    [[nodiscard]] std::optional<WidgetLifecycleState> Lifecycle(
        std::wstring_view widgetId) const noexcept;

    void RecordFailure(
        std::wstring_view widgetId,
        WidgetSessionFailureStage stage,
        std::wstring safeMessage);
    void ClearFailure(std::wstring_view widgetId);
    void RemoveSnapshot(std::wstring_view widgetId);
    void ClearSnapshots();

    void ResetCatalogRetry() noexcept { catalogRetryAttempts_ = 0; }
    [[nodiscard]] std::optional<unsigned int> NextCatalogRetryDelay(
        bool refreshSucceeded,
        bool revisionInFlight,
        bool hostActive) noexcept;

    [[nodiscard]] std::size_t PendingRequestCount() const noexcept;
    [[nodiscard]] bool DrainLifecycle(std::chrono::milliseconds timeout);
    void Shutdown() noexcept;

private:
    enum class RequestKind { Catalog, Establish, Snapshot, Lifecycle, Restart };

    struct Request final {
        std::uint64_t id{};
        std::uint64_t generation{};
        RequestKind kind{RequestKind::Snapshot};
        std::wstring widgetId;
        WidgetLifecycleState lifecycle{WidgetLifecycleState::Background};
        std::wstring expectedInstanceId;
        std::wstring expectedRuntimeGeneration;
        std::wstring expectedPresentationGeneration;
    };

    struct Completion final {
        Request request;
        WidgetSessionFailure failure;
        std::optional<std::vector<WidgetDescriptor>> descriptors;
        std::optional<WidgetSnapshot> snapshot;
        std::optional<bool> acknowledged;
    };

    [[nodiscard]] bool Queue(Request request);
    [[nodiscard]] bool HasPending(RequestKind kind, std::wstring_view widgetId) const noexcept;
    void RevokeRequests(std::wstring_view widgetId) noexcept;
    [[nodiscard]] Request MakeRequest(
        RequestKind kind,
        std::wstring widgetId = {},
        WidgetLifecycleState lifecycle = WidgetLifecycleState::Background);
    void WorkerLoop(std::stop_token stopToken);
    [[nodiscard]] Completion Execute(Request request, std::stop_token stopToken);
    [[nodiscard]] std::optional<WidgetSessionCatalogChange> ApplyCatalog(
        std::vector<WidgetDescriptor> descriptors);
    [[nodiscard]] bool CompletionIsCurrent(const Request& request) const noexcept;
    void FailCompletion(Completion& completion, WidgetSessionOperationResult<bool> result);

    WidgetSessionOperations operations_;
    std::function<void()> completionAvailable_;
    mutable std::mutex queueMutex_;
    std::condition_variable queueChanged_;
    std::deque<Request> pending_;
    std::deque<Completion> completed_;
    std::optional<Request> inFlight_;
    std::optional<std::stop_source> inFlightStop_;
    std::jthread worker_;
    std::uint64_t nextRequestId_{};
    bool shuttingDown_{};

    std::vector<WidgetDescriptor> descriptors_;
    std::unordered_map<std::wstring, WidgetSnapshot> snapshots_;
    std::unordered_map<std::wstring, WidgetSessionFailure> failures_;
    std::unordered_map<std::wstring, WidgetLifecycleState> lifecycleStates_;
    std::unordered_map<std::wstring, WidgetLifecycleState> lifecycleTargets_;
    std::unordered_map<std::wstring, std::uint64_t> generations_;
    std::unordered_set<std::wstring> awaitingRestartSnapshot_;
    unsigned int catalogRetryAttempts_{};
};

} // namespace gba
