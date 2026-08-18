#include "WidgetSessionCoordinator.h"

#include <Windows.h>

#include <algorithm>
#include <exception>
#include <unordered_set>
#include <utility>

namespace widgetrail {
namespace {

[[nodiscard]] WidgetSessionFailure FailureFrom(
    const WidgetSessionFailureStage stage,
    std::wstring error) {
    if (error.empty()) error = L"Widget session request failed.";
    if (error.size() > 512) error.resize(512);
    return {stage == WidgetSessionFailureStage::None
                ? WidgetSessionFailureStage::Protocol
                : stage,
            std::move(error)};
}

[[nodiscard]] bool SameRuntime(
    const WidgetDescriptor& left,
    const WidgetDescriptor& right) noexcept {
    return left.instanceId == right.instanceId &&
           left.runtimeGeneration == right.runtimeGeneration;
}

[[nodiscard]] std::uint64_t MonotonicMilliseconds() noexcept {
    return static_cast<std::uint64_t>(GetTickCount64());
}

} // namespace

WidgetSessionCoordinator::WidgetSessionCoordinator(
    WidgetSessionOperations operations,
    std::function<void()> completionAvailable,
    std::function<void(const WidgetSessionTraceEvent&)> traceAvailable,
    std::function<std::uint64_t()> timestamp)
    : operations_(std::move(operations)),
      completionAvailable_(std::move(completionAvailable)),
      traceAvailable_(std::move(traceAvailable)),
      timestamp_(timestamp ? std::move(timestamp) : MonotonicMilliseconds),
      worker_([this](const std::stop_token token) { WorkerLoop(token); }) {}

WidgetSessionCoordinator::~WidgetSessionCoordinator() {
    Shutdown();
}

std::optional<WidgetSessionCatalogChange>
WidgetSessionCoordinator::EstablishCatalog() {
    if (!operations_.ensureStarted || !operations_.listWidgets) return std::nullopt;
    auto started = operations_.ensureStarted({});
    if (!started.value || !*started.value) return std::nullopt;
    auto listed = operations_.listWidgets({});
    if (!listed.value) return std::nullopt;
    return ApplyCatalog(std::move(*listed.value));
}

std::optional<WidgetSnapshot> WidgetSessionCoordinator::EstablishPresentationForProbe(
    const std::wstring_view widgetId,
    const WidgetLifecycleState state) {
    const auto* descriptor = FindDescriptor(widgetId);
    if (!descriptor || !operations_.ensureStarted || !operations_.establish ||
        !operations_.setLifecycle) return std::nullopt;
    auto started = operations_.ensureStarted({});
    if (!started.value || !*started.value) return std::nullopt;
    auto established = operations_.establish({}, widgetId, state);
    if (!established.value || established.value->instanceId != descriptor->instanceId)
        return std::nullopt;
    const auto id = std::wstring(widgetId);
    snapshots_.insert_or_assign(id, *established.value);
    refreshStates_.insert_or_assign(id, WidgetRefreshState::Current);
    refreshRequestIds_.erase(id);
    lifecycleStates_.insert_or_assign(id, state);
    auto background = operations_.setLifecycle(
        {}, widgetId, WidgetLifecycleState::Background);
    if (!background.value || !*background.value) return std::nullopt;
    lifecycleStates_.erase(id);
    return established.value;
}

bool WidgetSessionCoordinator::RequestCatalog() {
    return Queue(MakeRequest(RequestKind::Catalog)).accepted();
}

bool WidgetSessionCoordinator::RequestSnapshot(
    const std::wstring_view widgetId,
    const bool explicitRetry,
    const std::uint64_t correlationId) {
    const auto* descriptor = FindDescriptor(widgetId);
    if (!descriptor) return false;
    const auto id = std::wstring(widgetId);
    MarkRefreshRequested(id);
    if (explicitRetry) {
        ++generations_[id];
    }
    if (HasPending(RequestKind::Establish, id)) return true;
    const auto target = lifecycleTargets_.find(id);
    const auto current = lifecycleStates_.find(id);
    const auto lifecycle = target != lifecycleTargets_.end()
        ? target->second
        : current != lifecycleStates_.end()
            ? current->second
            : WidgetLifecycleState::Background;
    const auto queued = Queue(MakeRequest(
        RequestKind::Snapshot, id, lifecycle, correlationId));
    if (queued.accepted() &&
        queued.action != WidgetSessionTraceAction::Deduplicated) {
        MarkRefreshInFlight(id, queued.requestId);
    }
    return queued.accepted();
}

bool WidgetSessionCoordinator::RequestRestart(
    const std::wstring_view widgetId,
    const std::uint64_t correlationId) {
    if (!Contains(widgetId)) return false;
    const auto id = std::wstring(widgetId);
    ++generations_[id];
    HardRemoveCheckpoint(id);
    lifecycleStates_.erase(id);
    lifecycleTargets_.erase(id);
    awaitingRestartSnapshot_.insert(id);
    if (Queue(MakeRequest(
            RequestKind::Restart, id, WidgetLifecycleState::Background,
            correlationId)).accepted()) return true;
    awaitingRestartSnapshot_.erase(id);
    return false;
}

void WidgetSessionCoordinator::SetLifecycleTargets(
    const std::map<std::wstring, WidgetLifecycleState, std::less<>>& desired,
    const bool deferColdStart,
    const std::uint64_t correlationId,
    const std::wstring_view correlationWidgetId) {
    std::vector<std::wstring> retired;
    retired.reserve(lifecycleTargets_.size());
    for (const auto& [widgetId, state] : lifecycleTargets_) {
        if (state != WidgetLifecycleState::Background &&
            !desired.contains(widgetId)) retired.push_back(widgetId);
    }
    for (const auto& widgetId : retired) {
        RevokeRequests(widgetId);
        lifecycleTargets_.erase(widgetId);
    }
    for (const auto& [widgetId, state] : desired) {
        const std::uint64_t widgetCorrelationId = correlationId != 0 &&
            (correlationWidgetId.empty() || widgetId == correlationWidgetId)
            ? correlationId : 0;
        if (!Contains(widgetId)) {
            EmitLifecycleDecision(
                widgetCorrelationId, widgetId, state, RequestKind::Establish,
                WidgetSessionTraceAction::Skipped,
                WidgetSessionTraceReason::MissingDescriptor);
            continue;
        }
        lifecycleTargets_.insert_or_assign(widgetId, state);
        SupersedeSnapshotRequests(widgetId, state);
        if (failures_.contains(widgetId) &&
            !awaitingRestartSnapshot_.contains(widgetId)) {
            EmitLifecycleDecision(
                widgetCorrelationId, widgetId, state, RequestKind::Establish,
                WidgetSessionTraceAction::Skipped,
                WidgetSessionTraceReason::FailureCurrent);
            continue;
        }
        const auto current = lifecycleStates_.find(widgetId);
        const auto refreshState = RefreshState(widgetId);
        if (current != lifecycleStates_.end() && current->second == state &&
            refreshState != WidgetRefreshState::RefreshRequested) {
            EmitLifecycleDecision(
                widgetCorrelationId, widgetId, state, RequestKind::Lifecycle,
                WidgetSessionTraceAction::Skipped,
                WidgetSessionTraceReason::AlreadyCurrent);
            continue;
        }
        const auto requestKind = refreshState == WidgetRefreshState::RefreshRequested
            ? (current != lifecycleStates_.end() && current->second == state
                ? RequestKind::Snapshot
                : RequestKind::Establish)
            : Snapshot(widgetId) ? RequestKind::Lifecycle : RequestKind::Establish;
        if (deferColdStart && current == lifecycleStates_.end() && !Snapshot(widgetId)) {
            EmitLifecycleDecision(
                widgetCorrelationId, widgetId, state, requestKind,
                WidgetSessionTraceAction::Skipped,
                WidgetSessionTraceReason::Deferred);
            continue;
        }
        auto request = MakeRequest(
            requestKind, widgetId, state, widgetCorrelationId);
        const auto queued = Queue(request);
        request.id = queued.requestId;
        request.generation = queued.generation;
        request.queuedAt = queued.queuedAt;
        request.startedAt = queued.startedAt;
        if (queued.accepted() &&
            queued.action != WidgetSessionTraceAction::Deduplicated &&
            (requestKind == RequestKind::Establish ||
             requestKind == RequestKind::Snapshot)) {
            MarkRefreshInFlight(widgetId, queued.requestId);
        }
        EmitLifecycleDecision(
            widgetCorrelationId, widgetId, state, requestKind,
            queued.action, queued.reason, &request);
    }
    for (const auto& widgetId : retired) {
        if (!lifecycleStates_.contains(widgetId)) continue;
        lifecycleTargets_.insert_or_assign(widgetId, WidgetLifecycleState::Background);
        (void)Queue(MakeRequest(
            RequestKind::Lifecycle, widgetId, WidgetLifecycleState::Background));
    }
}

std::vector<WidgetSessionEvent> WidgetSessionCoordinator::TakeEvents() {
    std::deque<Completion> completions;
    {
        std::scoped_lock lock(queueMutex_);
        completions.swap(completed_);
    }
    std::vector<WidgetSessionEvent> events;
    events.reserve(completions.size());
    for (auto& completion : completions) {
        auto& request = completion.request;
        const bool runtimeCurrent = CompletionRuntimeIsCurrent(request);
        const bool primaryStartFailure =
            !request.widgetId.empty() &&
            completion.failure.stage == WidgetSessionFailureStage::Start &&
            runtimeCurrent;
        const bool completionCurrent = CompletionIsCurrent(request);
        const auto* completionDescriptor =
            request.widgetId.empty() ? nullptr : FindDescriptor(request.widgetId);
        if (completion.failure.stage == WidgetSessionFailureStage::None &&
            request.kind == RequestKind::Snapshot && completion.update &&
            runtimeCurrent && completionCurrent) {
            std::wstring updateError;
            const auto* checkpoint = Snapshot(request.widgetId);
            std::optional<WidgetSnapshot> candidate;
            if (checkpoint && completionDescriptor && operations_.materializeUpdate) {
                auto materialized = operations_.materializeUpdate(
                    *checkpoint, *completion.update,
                    completionDescriptor->presentationGeneration);
                if (materialized.value) {
                    candidate = std::move(materialized.value->snapshot);
                    completion.presentationImpact =
                        std::move(materialized.value->impact);
                } else {
                    updateError = std::move(materialized.safeError);
                }
            }
            if (candidate) {
                completion.snapshot = std::move(*candidate);
                completion.update.reset();
            } else {
                EmitTrace(
                    request, WidgetSessionTraceStage::RequestCompleted,
                    WidgetSessionTraceAction::None,
                    WidgetSessionTraceReason::CheckpointFallback,
                    WidgetSessionCompletionDisposition::Failed,
                    completion.completedAt);
                CompleteRefresh(request, false);
                auto fallback = MakeRequest(
                    RequestKind::Snapshot, request.widgetId,
                    request.lifecycle, request.correlationId);
                fallback.allowUpdate = false;
                fallback.baseSequence = 0;
                const auto queued = Queue(fallback);
                if (queued.accepted()) {
                    MarkRefreshInFlight(request.widgetId, queued.requestId);
                    continue;
                }
                completion.update.reset();
                completion.failure = FailureFrom(
                    WidgetSessionFailureStage::Protocol,
                    updateError.empty()
                        ? L"The widget update was rejected and checkpoint recovery could not be queued."
                        : std::move(updateError));
            }
        }
        const bool snapshotProtocolCurrent =
            (request.kind != RequestKind::Establish &&
             request.kind != RequestKind::Snapshot) ||
            (completion.snapshot && completionDescriptor &&
             completion.snapshot->instanceId == completionDescriptor->instanceId &&
             completionDescriptor->instanceId == request.expectedInstanceId &&
             completionDescriptor->runtimeGeneration ==
                 request.expectedRuntimeGeneration &&
             completionDescriptor->presentationGeneration ==
                 request.expectedPresentationGeneration);
        const auto disposition = completion.cancelled
            ? WidgetSessionCompletionDisposition::Cancelled
            : !runtimeCurrent && !primaryStartFailure
                ? WidgetSessionCompletionDisposition::StaleGeneration
                : !completionCurrent && !primaryStartFailure
                    ? WidgetSessionCompletionDisposition::WrongLifecycle
                    : completion.failure.stage != WidgetSessionFailureStage::None
                        ? WidgetSessionCompletionDisposition::Failed
                        : !snapshotProtocolCurrent
                            ? WidgetSessionCompletionDisposition::Failed
                        : WidgetSessionCompletionDisposition::Admitted;
        EmitTrace(
            request, WidgetSessionTraceStage::RequestCompleted,
            WidgetSessionTraceAction::None,
            WidgetSessionTraceReason::None, disposition,
            completion.completedAt);
        const auto makeEvent = [&](const WidgetSessionEventKind kind) {
            WidgetSessionEvent event;
            event.kind = kind;
            event.widgetId = request.widgetId;
            event.correlationId = request.correlationId;
            event.requestId = request.id;
            event.generation = request.generation;
            event.requestKind = request.kind;
            event.lifecycle = request.lifecycle;
            event.completionDisposition = disposition;
            return event;
        };
        if (!completionCurrent && !primaryStartFailure) {
            CompleteRefresh(request, false);
            events.push_back(makeEvent(
                WidgetSessionEventKind::StaleCompletionRejected));
            continue;
        }
        if (completion.failure.stage != WidgetSessionFailureStage::None) {
            CompleteRefresh(request, false);
            if (!request.widgetId.empty()) {
                awaitingRestartSnapshot_.erase(request.widgetId);
                if (primaryStartFailure) RevokeRequests(request.widgetId);
                failures_.insert_or_assign(request.widgetId, completion.failure);
            }
            auto event = makeEvent(WidgetSessionEventKind::Failed);
            event.failure = std::move(completion.failure);
            events.push_back(std::move(event));
            continue;
        }
        if (request.kind == RequestKind::Catalog) {
            auto change = ApplyCatalog(std::move(*completion.descriptors));
            if (change) {
                WidgetSessionEvent event;
                event.kind = WidgetSessionEventKind::CatalogChanged;
                event.catalog = std::move(*change);
                events.push_back(std::move(event));
            }
            continue;
        }
        if (request.kind == RequestKind::Establish || request.kind == RequestKind::Snapshot) {
            if (!snapshotProtocolCurrent) {
                HardRemoveCheckpoint(request.widgetId);
                auto failure = FailureFrom(
                    WidgetSessionFailureStage::Protocol,
                    L"The worker returned a stale or mismatched widget instance.");
                failures_.insert_or_assign(request.widgetId, failure);
                auto event = makeEvent(WidgetSessionEventKind::Failed);
                event.failure = failure;
                event.completionDisposition =
                    WidgetSessionCompletionDisposition::Failed;
                events.push_back(std::move(event));
                continue;
            }
            snapshots_.insert_or_assign(request.widgetId, std::move(*completion.snapshot));
            CompleteRefresh(request, true);
            failures_.erase(request.widgetId);
            if (request.kind == RequestKind::Establish)
                lifecycleStates_.insert_or_assign(request.widgetId, request.lifecycle);
            auto event = makeEvent(WidgetSessionEventKind::SnapshotAdmitted);
            event.completedRestart = awaitingRestartSnapshot_.erase(request.widgetId) > 0;
            event.presentationImpact = std::move(completion.presentationImpact);
            events.push_back(std::move(event));
            continue;
        }
        if (request.kind == RequestKind::Lifecycle) {
            if (request.lifecycle == WidgetLifecycleState::Background) {
                lifecycleStates_.erase(request.widgetId);
                lifecycleTargets_.erase(request.widgetId);
            } else {
                lifecycleStates_.insert_or_assign(request.widgetId, request.lifecycle);
            }
            events.push_back(makeEvent(WidgetSessionEventKind::LifecycleChanged));
            continue;
        }
        if (request.kind == RequestKind::Restart) {
            events.push_back(makeEvent(WidgetSessionEventKind::Restarted));
        }
    }
    return events;
}

const WidgetDescriptor* WidgetSessionCoordinator::FindDescriptor(
    const std::wstring_view widgetId) const noexcept {
    const auto found = std::find_if(
        descriptors_.begin(), descriptors_.end(),
        [widgetId](const WidgetDescriptor& descriptor) {
            return descriptor.id == widgetId;
        });
    return found == descriptors_.end() ? nullptr : &*found;
}

bool WidgetSessionCoordinator::Contains(const std::wstring_view widgetId) const noexcept {
    return FindDescriptor(widgetId) != nullptr;
}

const WidgetSnapshot* WidgetSessionCoordinator::Snapshot(
    const std::wstring_view widgetId) const noexcept {
    const auto found = snapshots_.find(std::wstring(widgetId));
    return found == snapshots_.end() ? nullptr : &found->second;
}

WidgetRefreshState WidgetSessionCoordinator::RefreshState(
    const std::wstring_view widgetId) const noexcept {
    const auto found = refreshStates_.find(std::wstring(widgetId));
    if (found != refreshStates_.end()) return found->second;
    return Snapshot(widgetId)
        ? WidgetRefreshState::Current
        : WidgetRefreshState::RefreshRequested;
}

WidgetSessionPresentation WidgetSessionCoordinator::Presentation(
    const std::wstring_view widgetId) const noexcept {
    const auto* snapshot = Snapshot(widgetId);
    if (!snapshot) return {};
    const auto id = std::wstring(widgetId);
    return {
        snapshot,
        failures_.contains(id)
            ? WidgetPresentationAuthority::FailureRetained
            : RefreshState(id) == WidgetRefreshState::Current
                ? WidgetPresentationAuthority::Current
                : WidgetPresentationAuthority::RefreshRetained,
    };
}

const WidgetSessionFailure* WidgetSessionCoordinator::Failure(
    const std::wstring_view widgetId) const noexcept {
    const auto found = failures_.find(std::wstring(widgetId));
    return found == failures_.end() ? nullptr : &found->second;
}

std::optional<WidgetLifecycleState> WidgetSessionCoordinator::Lifecycle(
    const std::wstring_view widgetId) const noexcept {
    const auto found = lifecycleStates_.find(std::wstring(widgetId));
    return found == lifecycleStates_.end()
        ? std::nullopt
        : std::optional<WidgetLifecycleState>{found->second};
}

void WidgetSessionCoordinator::RecordFailure(
    const std::wstring_view widgetId,
    const WidgetSessionFailureStage stage,
    std::wstring safeMessage) {
    const auto id = std::wstring(widgetId);
    const auto current = failures_.find(id);
    if (current != failures_.end() &&
        current->second.stage == WidgetSessionFailureStage::Start &&
        stage != WidgetSessionFailureStage::Start) {
        return;
    }
    failures_.insert_or_assign(id, FailureFrom(stage, std::move(safeMessage)));
}

void WidgetSessionCoordinator::RecordRuntimeFailure(
    const std::wstring_view widgetId,
    const WidgetSessionFailureStage stage,
    std::wstring safeMessage) {
    const auto id = std::wstring(widgetId);
    RevokeRequests(id);
    awaitingRestartSnapshot_.erase(id);
    RecordFailure(id, stage, std::move(safeMessage));
}

void WidgetSessionCoordinator::ClearFailure(const std::wstring_view widgetId) {
    failures_.erase(std::wstring(widgetId));
}

void WidgetSessionCoordinator::MarkRefreshRequested(
    const std::wstring_view widgetId) {
    if (!Contains(widgetId)) return;
    const auto id = std::wstring(widgetId);
    refreshStates_.insert_or_assign(id, WidgetRefreshState::RefreshRequested);
    refreshRequestIds_.erase(id);
}

void WidgetSessionCoordinator::MarkAllRefreshRequested() {
    for (const auto& [widgetId, snapshot] : snapshots_) {
        (void)snapshot;
        MarkRefreshRequested(widgetId);
    }
}

void WidgetSessionCoordinator::RemoveSnapshot(const std::wstring_view widgetId) {
    HardRemoveCheckpoint(widgetId);
    RevokeRequests(widgetId);
}

std::optional<unsigned int> WidgetSessionCoordinator::NextCatalogRetryDelay(
    const bool refreshSucceeded,
    const bool revisionInFlight,
    const bool hostActive) noexcept {
    if (refreshSucceeded) {
        catalogRetryAttempts_ = 0;
        return std::nullopt;
    }
    if (!revisionInFlight || !hostActive ||
        catalogRetryAttempts_ >= MaximumCatalogRetryAttempts) {
        catalogRetryAttempts_ = 0;
        return std::nullopt;
    }
    return 250U << catalogRetryAttempts_++;
}

std::size_t WidgetSessionCoordinator::PendingRequestCount() const noexcept {
    std::scoped_lock lock(queueMutex_);
    return pending_.size() + completed_.size() + (inFlight_ ? 1U : 0U);
}

bool WidgetSessionCoordinator::DrainLifecycle(const std::chrono::milliseconds timeout) {
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    do {
        SetLifecycleTargets({});
        (void)TakeEvents();
        if (lifecycleStates_.empty() && PendingRequestCount() == 0) return true;
        Sleep(1);
    } while (std::chrono::steady_clock::now() < deadline);
    (void)TakeEvents();
    return lifecycleStates_.empty() && PendingRequestCount() == 0;
}

void WidgetSessionCoordinator::Shutdown() noexcept {
    {
        std::scoped_lock lock(queueMutex_);
        if (shuttingDown_) return;
        shuttingDown_ = true;
        pending_.clear();
        if (inFlightStop_) inFlightStop_->request_stop();
    }
    worker_.request_stop();
    queueChanged_.notify_all();
    if (worker_.joinable()) {
        (void)CancelSynchronousIo(worker_.native_handle());
        worker_.join();
    }
    std::scoped_lock lock(queueMutex_);
    completed_.clear();
}

WidgetSessionCoordinator::QueueResult WidgetSessionCoordinator::Queue(
    Request request) {
    if (request.queuedAt == 0) request.queuedAt = timestamp_();
    QueueResult result{
        WidgetSessionTraceAction::Queued,
        WidgetSessionTraceReason::None};
    std::optional<Request> replaced;
    {
        std::scoped_lock lock(queueMutex_);
        if (shuttingDown_) {
            result = {
                WidgetSessionTraceAction::Skipped,
                WidgetSessionTraceReason::ShuttingDown};
        } else if (auto existing = std::find_if(
                pending_.begin(), pending_.end(), [&](const Request& candidate) {
                return candidate.kind == request.kind &&
                       candidate.widgetId == request.widgetId;
            }); existing != pending_.end()) {
            replaced = *existing;
            *existing = request;
            result = {
                WidgetSessionTraceAction::Replaced,
                WidgetSessionTraceReason::NewerTarget};
        } else if (inFlight_ && inFlight_->kind == request.kind &&
            inFlight_->widgetId == request.widgetId &&
            inFlight_->generation == request.generation &&
            inFlight_->lifecycle == request.lifecycle) {
            request.id = inFlight_->id;
            request.generation = inFlight_->generation;
            request.queuedAt = inFlight_->queuedAt;
            request.startedAt = inFlight_->startedAt;
            result = {
                WidgetSessionTraceAction::Deduplicated,
                WidgetSessionTraceReason::ExistingRequest};
        } else if (pending_.size() + completed_.size() + (inFlight_ ? 1U : 0U) >=
            MaximumPendingRequests) {
            result = {
                WidgetSessionTraceAction::Skipped,
                WidgetSessionTraceReason::QueueFull};
        } else {
            pending_.push_back(request);
            queueChanged_.notify_one();
        }
    }
    if (replaced) {
        EmitTrace(
            *replaced, WidgetSessionTraceStage::RequestCompleted,
            WidgetSessionTraceAction::None,
            WidgetSessionTraceReason::NewerTarget,
            WidgetSessionCompletionDisposition::Cancelled);
    }
    EmitTrace(
        request, WidgetSessionTraceStage::RequestQueued,
        result.action, result.reason);
    result.requestId = request.id;
    result.generation = request.generation;
    result.queuedAt = request.queuedAt;
    result.startedAt = request.startedAt;
    return result;
}

bool WidgetSessionCoordinator::HasPending(
    const RequestKind kind,
    const std::wstring_view widgetId) const noexcept {
    std::scoped_lock lock(queueMutex_);
    return std::any_of(pending_.begin(), pending_.end(), [&](const Request& request) {
        return request.kind == kind && request.widgetId == widgetId;
    }) || (inFlight_ && inFlight_->kind == kind && inFlight_->widgetId == widgetId);
}

void WidgetSessionCoordinator::SupersedeSnapshotRequests(
    const std::wstring_view widgetId,
    const WidgetLifecycleState lifecycle) noexcept {
    const auto id = std::wstring(widgetId);
    bool cancelInFlight = false;
    std::optional<std::uint64_t> cancelledInFlightId;
    std::vector<Request> cancelled;
    {
        std::scoped_lock lock(queueMutex_);
        for (const auto& request : pending_) {
            if (request.kind == RequestKind::Snapshot &&
                request.widgetId == id && request.lifecycle != lifecycle) {
                cancelled.push_back(request);
            }
        }
        std::erase_if(pending_, [&](const Request& request) {
            return request.kind == RequestKind::Snapshot &&
                   request.widgetId == id && request.lifecycle != lifecycle;
        });
        if (inFlight_ && inFlight_->kind == RequestKind::Snapshot &&
            inFlight_->widgetId == id && inFlight_->lifecycle != lifecycle) {
            cancelInFlight = true;
            cancelledInFlightId = inFlight_->id;
            if (inFlightStop_) inFlightStop_->request_stop();
        }
    }
    const auto refreshRequest = refreshRequestIds_.find(id);
    const bool supersededRefresh = refreshRequest != refreshRequestIds_.end() &&
        (std::any_of(cancelled.begin(), cancelled.end(), [&](const Request& request) {
            return request.id == refreshRequest->second;
        }) || cancelledInFlightId == refreshRequest->second);
    if (supersededRefresh) {
        refreshStates_.insert_or_assign(id, WidgetRefreshState::RefreshRequested);
        refreshRequestIds_.erase(id);
    }
    for (const auto& request : cancelled) {
        EmitTrace(
            request, WidgetSessionTraceStage::RequestCompleted,
            WidgetSessionTraceAction::None,
            WidgetSessionTraceReason::NewerTarget,
            WidgetSessionCompletionDisposition::Cancelled);
    }
    if (cancelInFlight && worker_.joinable())
        (void)CancelSynchronousIo(worker_.native_handle());
}

void WidgetSessionCoordinator::RevokeRequests(
    const std::wstring_view widgetId) noexcept {
    const auto id = std::wstring(widgetId);
    bool cancelInFlight = false;
    std::optional<std::uint64_t> cancelledInFlightId;
    std::vector<Request> cancelled;
    {
        std::scoped_lock lock(queueMutex_);
        for (const auto& request : pending_) {
            if (request.widgetId == id) cancelled.push_back(request);
        }
        std::erase_if(pending_, [&](const Request& request) {
            return request.widgetId == id;
        });
        if (inFlight_ && inFlight_->widgetId == id) {
            cancelInFlight = true;
            cancelledInFlightId = inFlight_->id;
            if (inFlightStop_) inFlightStop_->request_stop();
        }
    }
    ++generations_[id];
    const auto refreshRequest = refreshRequestIds_.find(id);
    const bool revokedRefresh = refreshRequest != refreshRequestIds_.end() &&
        (std::any_of(cancelled.begin(), cancelled.end(), [&](const Request& request) {
            return request.id == refreshRequest->second;
        }) || cancelledInFlightId == refreshRequest->second);
    if (revokedRefresh) {
        refreshStates_.insert_or_assign(id, WidgetRefreshState::RefreshRequested);
        refreshRequestIds_.erase(id);
    }
    for (const auto& request : cancelled) {
        EmitTrace(
            request, WidgetSessionTraceStage::RequestCompleted,
            WidgetSessionTraceAction::None,
            WidgetSessionTraceReason::NewerTarget,
            WidgetSessionCompletionDisposition::Cancelled);
    }
    if (cancelInFlight && worker_.joinable())
        (void)CancelSynchronousIo(worker_.native_handle());
}

WidgetSessionCoordinator::Request WidgetSessionCoordinator::MakeRequest(
    const RequestKind kind,
    std::wstring widgetId,
    const WidgetLifecycleState lifecycle,
    const std::uint64_t correlationId) {
    Request request;
    request.id = ++nextRequestId_;
    request.correlationId = correlationId;
    request.kind = kind;
    request.widgetId = std::move(widgetId);
    request.lifecycle = lifecycle;
    if (!request.widgetId.empty()) {
        request.generation = generations_[request.widgetId];
        if (const auto* descriptor = FindDescriptor(request.widgetId)) {
            request.expectedInstanceId = descriptor->instanceId;
            request.expectedRuntimeGeneration = descriptor->runtimeGeneration;
            request.expectedPresentationGeneration = descriptor->presentationGeneration;
        }
        if (kind == RequestKind::Snapshot) {
            const auto* checkpoint = Snapshot(request.widgetId);
            if (checkpoint && checkpoint->sequence > 0 &&
                !checkpoint->documentJson.empty()) {
                request.baseSequence = checkpoint->sequence;
                request.allowUpdate = true;
            }
        }
    }
    return request;
}

void WidgetSessionCoordinator::EmitTrace(
    const Request& request,
    const WidgetSessionTraceStage stage,
    const WidgetSessionTraceAction action,
    const WidgetSessionTraceReason reason,
    const WidgetSessionCompletionDisposition disposition,
    const std::uint64_t completedAt) const {
    if (!traceAvailable_ || request.correlationId == 0) return;
    try {
        traceAvailable_({
            request.correlationId,
            stage,
            action,
            reason,
            disposition,
            request.id,
            request.generation,
            request.kind,
            request.lifecycle,
            request.widgetId,
            request.queuedAt,
            request.startedAt,
            stage == WidgetSessionTraceStage::RequestCompleted
                ? (completedAt != 0 ? completedAt : timestamp_()) : 0,
        });
    } catch (...) {
    }
}

void WidgetSessionCoordinator::EmitLifecycleDecision(
    const std::uint64_t correlationId,
    const std::wstring_view widgetId,
    const WidgetLifecycleState lifecycle,
    const RequestKind requestKind,
    const WidgetSessionTraceAction action,
    const WidgetSessionTraceReason reason,
    const Request* request) const {
    if (!traceAvailable_ || correlationId == 0) return;
    try {
        traceAvailable_({
            correlationId,
            WidgetSessionTraceStage::LifecycleDecision,
            action,
            reason,
            WidgetSessionCompletionDisposition::None,
            request ? request->id : 0,
            request ? request->generation : 0,
            requestKind,
            lifecycle,
            std::wstring(widgetId),
            request ? request->queuedAt : 0,
            0,
            0,
        });
    } catch (...) {
    }
}

void WidgetSessionCoordinator::WorkerLoop(const std::stop_token stopToken) {
    while (!stopToken.stop_requested()) {
        Request request;
        std::stop_token requestToken;
        {
            std::unique_lock lock(queueMutex_);
            queueChanged_.wait(lock, [&] {
                return shuttingDown_ || stopToken.stop_requested() || !pending_.empty();
            });
            if (shuttingDown_ || stopToken.stop_requested()) break;
            request = std::move(pending_.front());
            pending_.pop_front();
            request.startedAt = timestamp_();
            inFlight_ = request;
            inFlightStop_.emplace();
            requestToken = inFlightStop_->get_token();
        }
        EmitTrace(request, WidgetSessionTraceStage::RequestStarted);
        auto completion = Execute(std::move(request), requestToken);
        completion.completedAt = timestamp_();
        completion.cancelled = requestToken.stop_requested();
        if (stopToken.stop_requested()) break;
        {
            std::scoped_lock lock(queueMutex_);
            if (shuttingDown_) break;
            inFlight_.reset();
            inFlightStop_.reset();
            completed_.push_back(std::move(completion));
        }
        if (completionAvailable_) {
            try { completionAvailable_(); }
            catch (...) { }
        }
    }
}

WidgetSessionCoordinator::Completion WidgetSessionCoordinator::Execute(
    Request request,
    const std::stop_token stopToken) {
    Completion completion;
    completion.request = std::move(request);
    try {
        if (!operations_.ensureStarted) {
            completion.failure = FailureFrom(
                WidgetSessionFailureStage::Start, L"Widget session transport is unavailable.");
            return completion;
        }
        auto started = operations_.ensureStarted(stopToken);
        if (!started.value || !*started.value) {
            completion.failure = FailureFrom(started.failureStage, std::move(started.safeError));
            return completion;
        }
        switch (completion.request.kind) {
        case RequestKind::Catalog: {
            if (!operations_.listWidgets) break;
            auto result = operations_.listWidgets(stopToken);
            if (result.value) completion.descriptors = std::move(*result.value);
            else completion.failure = FailureFrom(result.failureStage, std::move(result.safeError));
            return completion;
        }
        case RequestKind::Establish: {
            if (!operations_.establish) break;
            auto result = operations_.establish(
                stopToken, completion.request.widgetId, completion.request.lifecycle);
            if (result.value) completion.snapshot = std::move(*result.value);
            else completion.failure = FailureFrom(result.failureStage, std::move(result.safeError));
            return completion;
        }
        case RequestKind::Snapshot: {
            if (!operations_.getSnapshot) break;
            auto result = operations_.getSnapshot(
                stopToken, completion.request.widgetId,
                completion.request.baseSequence,
                completion.request.allowUpdate);
            if (result.value) {
                completion.snapshot = std::move(result.value->checkpoint);
                completion.update = std::move(result.value->update);
                if (completion.snapshot.has_value() == completion.update.has_value()) {
                    completion.snapshot.reset();
                    completion.update.reset();
                    completion.failure = FailureFrom(
                        WidgetSessionFailureStage::Protocol,
                        L"The bridge returned an invalid widget presentation publication.");
                }
            }
            else completion.failure = FailureFrom(result.failureStage, std::move(result.safeError));
            return completion;
        }
        case RequestKind::Lifecycle: {
            if (!operations_.setLifecycle) break;
            auto result = operations_.setLifecycle(
                stopToken, completion.request.widgetId, completion.request.lifecycle);
            FailCompletion(completion, std::move(result));
            return completion;
        }
        case RequestKind::Restart: {
            if (!operations_.restart) break;
            auto result = operations_.restart(stopToken, completion.request.widgetId);
            FailCompletion(completion, std::move(result));
            return completion;
        }
        }
        completion.failure = FailureFrom(
            WidgetSessionFailureStage::Protocol,
            L"Widget session operation is unavailable.");
    } catch (const std::exception&) {
        completion.failure = FailureFrom(
            WidgetSessionFailureStage::Protocol,
            L"Widget session operation raised an unexpected failure.");
    } catch (...) {
        completion.failure = FailureFrom(
            WidgetSessionFailureStage::Protocol,
            L"Widget session operation raised an unknown failure.");
    }
    return completion;
}

std::optional<WidgetSessionCatalogChange> WidgetSessionCoordinator::ApplyCatalog(
    std::vector<WidgetDescriptor> descriptors) {
    WidgetSessionCatalogChange change;
    std::unordered_set<std::wstring> ids;
    ids.reserve(descriptors.size());
    for (const auto& descriptor : descriptors) {
        if (ids.insert(descriptor.id).second)
            change.availableWidgetIds.push_back(descriptor.id);
    }
    for (const auto& previous : descriptors_) {
        const auto current = std::find_if(
            descriptors.begin(), descriptors.end(),
            [&](const WidgetDescriptor& descriptor) {
                return descriptor.id == previous.id;
            });
        if (current == descriptors.end() || !SameRuntime(previous, *current)) {
            change.runtimeChanges.push_back({previous.id, previous.instanceId});
            ++generations_[previous.id];
            HardRemoveCheckpoint(previous.id);
            failures_.erase(previous.id);
            lifecycleStates_.erase(previous.id);
            lifecycleTargets_.erase(previous.id);
            awaitingRestartSnapshot_.erase(previous.id);
            continue;
        }
        if (previous.presentationGeneration != current->presentationGeneration) {
            ++generations_[previous.id];
            HardRemoveCheckpoint(previous.id);
        }
    }
    for (const auto& current : descriptors) {
        if (!FindDescriptor(current.id)) {
            ++generations_[current.id];
            change.runtimeChanges.push_back({current.id, {}});
        }
    }
    descriptors_ = std::move(descriptors);
    for (const auto& descriptor : descriptors_) {
        refreshStates_.try_emplace(
            descriptor.id,
            Snapshot(descriptor.id)
                ? WidgetRefreshState::Current
                : WidgetRefreshState::RefreshRequested);
    }
    return change;
}

bool WidgetSessionCoordinator::CompletionRuntimeIsCurrent(
    const Request& request) const noexcept {
    if (request.widgetId.empty()) return true;
    const auto found = generations_.find(request.widgetId);
    if (found == generations_.end() || found->second != request.generation) return false;
    const auto* descriptor = FindDescriptor(request.widgetId);
    return descriptor && descriptor->instanceId == request.expectedInstanceId &&
           descriptor->runtimeGeneration == request.expectedRuntimeGeneration &&
           descriptor->presentationGeneration == request.expectedPresentationGeneration;
}

bool WidgetSessionCoordinator::CompletionIsCurrent(const Request& request) const noexcept {
    if (!CompletionRuntimeIsCurrent(request)) return false;
    if (request.widgetId.empty()) return true;
    if (request.kind == RequestKind::Snapshot) {
        const auto target = lifecycleTargets_.find(request.widgetId);
        return request.lifecycle == WidgetLifecycleState::Background
            ? target == lifecycleTargets_.end() ||
                target->second == WidgetLifecycleState::Background
            : target != lifecycleTargets_.end() &&
                target->second == request.lifecycle;
    }
    if (request.kind == RequestKind::Establish ||
        request.kind == RequestKind::Lifecycle) {
        const auto target = lifecycleTargets_.find(request.widgetId);
        if (target == lifecycleTargets_.end() || target->second != request.lifecycle)
            return false;
    }
    return true;
}

void WidgetSessionCoordinator::MarkRefreshInFlight(
    const std::wstring_view widgetId,
    const std::uint64_t requestId) {
    if (widgetId.empty() || requestId == 0) return;
    const auto id = std::wstring(widgetId);
    refreshStates_.insert_or_assign(id, WidgetRefreshState::RefreshInFlight);
    refreshRequestIds_.insert_or_assign(id, requestId);
}

void WidgetSessionCoordinator::CompleteRefresh(
    const Request& request,
    const bool admitted) noexcept {
    if (request.kind != RequestKind::Establish &&
        request.kind != RequestKind::Snapshot) return;
    const auto found = refreshRequestIds_.find(request.widgetId);
    if (found == refreshRequestIds_.end() || found->second != request.id) return;
    refreshStates_.insert_or_assign(
        request.widgetId,
        admitted ? WidgetRefreshState::Current
                 : WidgetRefreshState::RefreshRequested);
    refreshRequestIds_.erase(found);
}

void WidgetSessionCoordinator::HardRemoveCheckpoint(
    const std::wstring_view widgetId) noexcept {
    const auto id = std::wstring(widgetId);
    snapshots_.erase(id);
    refreshStates_.erase(id);
    refreshRequestIds_.erase(id);
}

void WidgetSessionCoordinator::FailCompletion(
    Completion& completion,
    WidgetSessionOperationResult<bool> result) {
    if (!result.value || !*result.value)
        completion.failure = FailureFrom(result.failureStage, std::move(result.safeError));
    else
        completion.acknowledged = true;
}

} // namespace widgetrail
