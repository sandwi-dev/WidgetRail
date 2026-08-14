#include "WidgetSessionCoordinator.h"

#include <Windows.h>

#include <algorithm>
#include <exception>
#include <unordered_set>
#include <utility>

namespace gba {
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

} // namespace

WidgetSessionCoordinator::WidgetSessionCoordinator(
    WidgetSessionOperations operations,
    std::function<void()> completionAvailable)
    : operations_(std::move(operations)),
      completionAvailable_(std::move(completionAvailable)),
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
    snapshots_.insert_or_assign(std::wstring(widgetId), *established.value);
    lifecycleStates_.insert_or_assign(std::wstring(widgetId), state);
    auto background = operations_.setLifecycle(
        {}, widgetId, WidgetLifecycleState::Background);
    if (!background.value || !*background.value) return std::nullopt;
    lifecycleStates_.erase(std::wstring(widgetId));
    return established.value;
}

bool WidgetSessionCoordinator::RequestCatalog() {
    return Queue(MakeRequest(RequestKind::Catalog));
}

bool WidgetSessionCoordinator::RequestSnapshot(
    const std::wstring_view widgetId,
    const bool explicitRetry) {
    const auto* descriptor = FindDescriptor(widgetId);
    if (!descriptor) return false;
    if (explicitRetry) {
        ++generations_[std::wstring(widgetId)];
    }
    return Queue(MakeRequest(RequestKind::Snapshot, std::wstring(widgetId)));
}

bool WidgetSessionCoordinator::RequestRestart(const std::wstring_view widgetId) {
    if (!Contains(widgetId)) return false;
    const auto id = std::wstring(widgetId);
    ++generations_[id];
    snapshots_.erase(id);
    lifecycleStates_.erase(id);
    lifecycleTargets_.erase(id);
    awaitingRestartSnapshot_.insert(id);
    if (Queue(MakeRequest(RequestKind::Restart, id))) return true;
    awaitingRestartSnapshot_.erase(id);
    return false;
}

void WidgetSessionCoordinator::SetLifecycleTargets(
    const std::map<std::wstring, WidgetLifecycleState, std::less<>>& desired,
    const bool deferColdStart) {
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
        if (!Contains(widgetId)) continue;
        lifecycleTargets_.insert_or_assign(widgetId, state);
        if (failures_.contains(widgetId) &&
            !awaitingRestartSnapshot_.contains(widgetId)) continue;
        const auto current = lifecycleStates_.find(widgetId);
        if (current != lifecycleStates_.end() && current->second == state) continue;
        if (deferColdStart && current == lifecycleStates_.end() && !Snapshot(widgetId)) continue;
        (void)Queue(MakeRequest(
            Snapshot(widgetId) ? RequestKind::Lifecycle : RequestKind::Establish,
            widgetId, state));
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
        const bool primaryStartFailure =
            !request.widgetId.empty() &&
            completion.failure.stage == WidgetSessionFailureStage::Start &&
            CompletionRuntimeIsCurrent(request);
        if (!CompletionIsCurrent(request) && !primaryStartFailure) {
            events.push_back({
                WidgetSessionEventKind::StaleCompletionRejected,
                request.widgetId});
            continue;
        }
        if (completion.failure.stage != WidgetSessionFailureStage::None) {
            if (!request.widgetId.empty()) {
                awaitingRestartSnapshot_.erase(request.widgetId);
                if (primaryStartFailure) RevokeRequests(request.widgetId);
                failures_.insert_or_assign(request.widgetId, completion.failure);
            }
            events.push_back({
                WidgetSessionEventKind::Failed,
                request.widgetId,
                std::move(completion.failure)});
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
            const auto* descriptor = FindDescriptor(request.widgetId);
            if (!descriptor || !completion.snapshot ||
                completion.snapshot->instanceId != descriptor->instanceId ||
                descriptor->instanceId != request.expectedInstanceId ||
                descriptor->runtimeGeneration != request.expectedRuntimeGeneration ||
                descriptor->presentationGeneration != request.expectedPresentationGeneration) {
                auto failure = FailureFrom(
                    WidgetSessionFailureStage::Protocol,
                    L"The worker returned a stale or mismatched widget instance.");
                failures_.insert_or_assign(request.widgetId, failure);
                events.push_back({WidgetSessionEventKind::Failed, request.widgetId, failure});
                continue;
            }
            snapshots_.insert_or_assign(request.widgetId, std::move(*completion.snapshot));
            failures_.erase(request.widgetId);
            if (request.kind == RequestKind::Establish)
                lifecycleStates_.insert_or_assign(request.widgetId, request.lifecycle);
            WidgetSessionEvent event;
            event.kind = WidgetSessionEventKind::SnapshotAdmitted;
            event.widgetId = request.widgetId;
            event.completedRestart = awaitingRestartSnapshot_.erase(request.widgetId) > 0;
            events.push_back(std::move(event));
            continue;
        }
        if (request.kind == RequestKind::Lifecycle) {
            if (request.lifecycle == WidgetLifecycleState::Background) {
                lifecycleStates_.erase(request.widgetId);
                lifecycleTargets_.erase(request.widgetId);
            } else {
                lifecycleStates_.insert_or_assign(request.widgetId, request.lifecycle);
                failures_.erase(request.widgetId);
            }
            events.push_back({
                WidgetSessionEventKind::LifecycleChanged,
                request.widgetId});
            continue;
        }
        if (request.kind == RequestKind::Restart) {
            events.push_back({WidgetSessionEventKind::Restarted, request.widgetId});
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
    failures_.insert_or_assign(
        std::wstring(widgetId), FailureFrom(stage, std::move(safeMessage)));
}

void WidgetSessionCoordinator::ClearFailure(const std::wstring_view widgetId) {
    failures_.erase(std::wstring(widgetId));
}

void WidgetSessionCoordinator::RemoveSnapshot(const std::wstring_view widgetId) {
    snapshots_.erase(std::wstring(widgetId));
    RevokeRequests(widgetId);
}

void WidgetSessionCoordinator::ClearSnapshots() {
    snapshots_.clear();
    for (auto& [widgetId, generation] : generations_) {
        (void)widgetId;
        ++generation;
    }
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

bool WidgetSessionCoordinator::Queue(Request request) {
    std::scoped_lock lock(queueMutex_);
    if (shuttingDown_) return false;
    if (auto existing = std::find_if(
            pending_.begin(), pending_.end(), [&](const Request& candidate) {
            return candidate.kind == request.kind &&
                   candidate.widgetId == request.widgetId;
        }); existing != pending_.end()) {
        *existing = std::move(request);
        return true;
    }
    if (inFlight_ && inFlight_->kind == request.kind &&
        inFlight_->widgetId == request.widgetId &&
        inFlight_->generation == request.generation &&
        inFlight_->lifecycle == request.lifecycle) return true;
    if (pending_.size() + completed_.size() + (inFlight_ ? 1U : 0U) >=
        MaximumPendingRequests) return false;
    pending_.push_back(std::move(request));
    queueChanged_.notify_one();
    return true;
}

bool WidgetSessionCoordinator::HasPending(
    const RequestKind kind,
    const std::wstring_view widgetId) const noexcept {
    std::scoped_lock lock(queueMutex_);
    return std::any_of(pending_.begin(), pending_.end(), [&](const Request& request) {
        return request.kind == kind && request.widgetId == widgetId;
    }) || (inFlight_ && inFlight_->kind == kind && inFlight_->widgetId == widgetId);
}

void WidgetSessionCoordinator::RevokeRequests(
    const std::wstring_view widgetId) noexcept {
    const auto id = std::wstring(widgetId);
    bool cancelInFlight = false;
    {
        std::scoped_lock lock(queueMutex_);
        std::erase_if(pending_, [&](const Request& request) {
            return request.widgetId == id;
        });
        if (inFlight_ && inFlight_->widgetId == id) {
            cancelInFlight = true;
            if (inFlightStop_) inFlightStop_->request_stop();
        }
    }
    ++generations_[id];
    if (cancelInFlight && worker_.joinable())
        (void)CancelSynchronousIo(worker_.native_handle());
}

WidgetSessionCoordinator::Request WidgetSessionCoordinator::MakeRequest(
    const RequestKind kind,
    std::wstring widgetId,
    const WidgetLifecycleState lifecycle) {
    Request request;
    request.id = ++nextRequestId_;
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
    }
    return request;
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
            inFlight_ = request;
            inFlightStop_.emplace();
            requestToken = inFlightStop_->get_token();
        }
        auto completion = Execute(std::move(request), requestToken);
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
            auto result = operations_.getSnapshot(stopToken, completion.request.widgetId);
            if (result.value) completion.snapshot = std::move(*result.value);
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
            snapshots_.erase(previous.id);
            failures_.erase(previous.id);
            lifecycleStates_.erase(previous.id);
            lifecycleTargets_.erase(previous.id);
            awaitingRestartSnapshot_.erase(previous.id);
            continue;
        }
        if (previous.presentationGeneration != current->presentationGeneration) {
            ++generations_[previous.id];
            snapshots_.erase(previous.id);
        }
    }
    for (const auto& current : descriptors) {
        if (!FindDescriptor(current.id)) {
            ++generations_[current.id];
            change.runtimeChanges.push_back({current.id, {}});
        }
    }
    descriptors_ = std::move(descriptors);
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
    if (request.kind == RequestKind::Establish ||
        request.kind == RequestKind::Lifecycle) {
        const auto target = lifecycleTargets_.find(request.widgetId);
        if (target == lifecycleTargets_.end() || target->second != request.lifecycle)
            return false;
    }
    return true;
}

void WidgetSessionCoordinator::FailCompletion(
    Completion& completion,
    WidgetSessionOperationResult<bool> result) {
    if (!result.value || !*result.value)
        completion.failure = FailureFrom(result.failureStage, std::move(result.safeError));
    else
        completion.acknowledged = true;
}

} // namespace gba
