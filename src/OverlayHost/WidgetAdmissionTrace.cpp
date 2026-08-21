#include "WidgetAdmissionTrace.h"

#include <algorithm>
#include <cwctype>

namespace widgetrail {
namespace {

std::wstring_view LifecycleValue(const WidgetLifecycleState value) noexcept {
    switch (value) {
    case WidgetLifecycleState::Visible: return L"visible";
    case WidgetLifecycleState::Interactive: return L"interactive";
    case WidgetLifecycleState::Background:
    default: return L"background";
    }
}

std::wstring_view RequestKindValue(const WidgetSessionRequestKind value) noexcept {
    switch (value) {
    case WidgetSessionRequestKind::Catalog: return L"catalog";
    case WidgetSessionRequestKind::Establish: return L"establish";
    case WidgetSessionRequestKind::Snapshot: return L"snapshot";
    case WidgetSessionRequestKind::Lifecycle: return L"lifecycle";
    case WidgetSessionRequestKind::Restart: return L"restart";
    }
    return L"snapshot";
}

std::wstring_view TraceStageValue(const WidgetSessionTraceStage value) noexcept {
    switch (value) {
    case WidgetSessionTraceStage::LifecycleDecision: return L"lifecycle-decision";
    case WidgetSessionTraceStage::RequestQueued: return L"request-queued";
    case WidgetSessionTraceStage::RequestStarted: return L"request-started";
    case WidgetSessionTraceStage::RequestCompleted: return L"request-completed";
    }
    return L"lifecycle-decision";
}

std::wstring_view ActionValue(const WidgetSessionTraceAction value) noexcept {
    switch (value) {
    case WidgetSessionTraceAction::Queued: return L"queued";
    case WidgetSessionTraceAction::Deduplicated: return L"deduplicated";
    case WidgetSessionTraceAction::Replaced: return L"replaced";
    case WidgetSessionTraceAction::Skipped: return L"skipped";
    case WidgetSessionTraceAction::None:
    default: return L"none";
    }
}

std::wstring_view ReasonValue(const WidgetSessionTraceReason value) noexcept {
    switch (value) {
    case WidgetSessionTraceReason::Deferred: return L"deferred";
    case WidgetSessionTraceReason::AlreadyCurrent: return L"already-current";
    case WidgetSessionTraceReason::FailureCurrent: return L"failure-current";
    case WidgetSessionTraceReason::QueueFull: return L"queue-full";
    case WidgetSessionTraceReason::MissingDescriptor: return L"missing-descriptor";
    case WidgetSessionTraceReason::ExistingRequest: return L"existing-request";
    case WidgetSessionTraceReason::NewerTarget: return L"newer-target";
    case WidgetSessionTraceReason::StaleBaseResynchronization:
        return L"stale-base-resynchronization";
    case WidgetSessionTraceReason::ShuttingDown: return L"shutting-down";
    case WidgetSessionTraceReason::None:
    default: return L"none";
    }
}

std::wstring_view DispositionValue(
    const WidgetSessionCompletionDisposition value) noexcept {
    switch (value) {
    case WidgetSessionCompletionDisposition::Admitted: return L"admitted";
    case WidgetSessionCompletionDisposition::Failed: return L"failed";
    case WidgetSessionCompletionDisposition::StaleGeneration:
        return L"stale-generation";
    case WidgetSessionCompletionDisposition::WrongLifecycle:
        return L"wrong-lifecycle";
    case WidgetSessionCompletionDisposition::Cancelled: return L"cancelled";
    case WidgetSessionCompletionDisposition::None:
    default: return L"none";
    }
}

} // namespace

WidgetAdmissionTrace::WidgetAdmissionTrace(DiagnosticSink sink)
    : sink_(std::move(sink)) {
    if (sink_) {
        writer_ = std::jthread(
            [this](const std::stop_token token) { WriterLoop(token); });
    }
}

WidgetAdmissionTrace::~WidgetAdmissionTrace() {
    {
        std::scoped_lock lock(diagnosticMutex_);
        stopping_ = true;
    }
    diagnosticChanged_.notify_all();
    if (writer_.joinable()) writer_.join();
}

std::uint64_t WidgetAdmissionTrace::BeginSelection(
    const std::wstring_view selectedWidget,
    const std::wstring_view activeWidget,
    const bool deferColdStart,
    const bool currentSnapshotPresent,
    const std::uint64_t timestamp) {
    std::scoped_lock lock(mutex_);
    if (transitions_.size() >= MaximumTransitions) {
        const auto removable = std::find_if(
            transitions_.begin(), transitions_.end(),
            [](const WidgetAdmissionTransitionTrace& transition) {
                return transition.terminal;
            });
        transitions_.erase(removable != transitions_.end()
            ? removable : transitions_.begin());
    }
    WidgetAdmissionTransitionTrace transition;
    transition.id = ++nextTransitionId_;
    transition.startedAt = timestamp;
    transition.selectedWidget = Sanitize(selectedWidget);
    transition.activeWidget = Sanitize(activeWidget);
    transition.trackedWidget = transition.activeWidget.empty()
        ? transition.selectedWidget : transition.activeWidget;
    transition.deferColdStart = deferColdStart;
    transition.currentSnapshotPresent = currentSnapshotPresent;
    transitions_.push_back(std::move(transition));
    auto& current = transitions_.back();
    AppendLocked(current, {
        WidgetAdmissionTraceStage::Selection,
        timestamp,
        current.trackedWidget,
    });
    return current.id;
}

void WidgetAdmissionTrace::RecordRefreshPosted(
    const std::uint64_t transitionId,
    const std::wstring_view widgetId,
    const bool posted,
    const std::uint64_t timestamp) {
    std::scoped_lock lock(mutex_);
    if (auto* transition = FindLocked(transitionId)) {
        if (!MatchesTrackedWidget(*transition, widgetId)) return;
        if (posted) transition->refreshPostedAt = timestamp;
        AppendLocked(*transition, {
            WidgetAdmissionTraceStage::RefreshPosted,
            timestamp,
            Sanitize(widgetId),
            posted,
            timestamp >= transition->startedAt
                ? timestamp - transition->startedAt : 0,
        });
    }
}

void WidgetAdmissionTrace::RecordRefreshDequeued(
    const std::uint64_t transitionId,
    const std::wstring_view widgetId,
    const std::uint64_t timestamp) {
    std::scoped_lock lock(mutex_);
    if (auto* transition = FindLocked(transitionId)) {
        if (!MatchesTrackedWidget(*transition, widgetId)) return;
        AppendLocked(*transition, {
            WidgetAdmissionTraceStage::RefreshDequeued,
            timestamp,
            Sanitize(widgetId),
            true,
            transition->refreshPostedAt != 0 &&
                    timestamp >= transition->refreshPostedAt
                ? timestamp - transition->refreshPostedAt : 0,
        });
    }
}

void WidgetAdmissionTrace::RecordSession(const WidgetSessionTraceEvent& event) {
    if (event.correlationId == 0) return;
    std::scoped_lock lock(mutex_);
    auto* transition = FindLocked(event.correlationId);
    if (!transition) return;
    if (!MatchesTrackedWidget(*transition, event.widgetId)) return;
    WidgetAdmissionTraceRecord record;
    record.stage = WidgetAdmissionTraceStage::Session;
    record.timestamp = event.completedAt != 0
        ? event.completedAt
        : event.startedAt != 0 ? event.startedAt : event.queuedAt;
    record.widgetId = Sanitize(event.widgetId);
    record.elapsed = record.timestamp >= transition->startedAt
        ? record.timestamp - transition->startedAt : 0;
    record.session = event;
    record.session.widgetId = record.widgetId;
    AppendLocked(*transition, std::move(record));
    if (event.stage == WidgetSessionTraceStage::RequestCompleted &&
        event.disposition == WidgetSessionCompletionDisposition::Failed) {
        transition->terminal = true;
    }
}

void WidgetAdmissionTrace::RecordAdmissionPresentation(
    const std::uint64_t transitionId,
    const std::wstring_view widgetId,
    const bool presentationRefreshed,
    const std::uint64_t timestamp) {
    std::scoped_lock lock(mutex_);
    if (auto* transition = FindLocked(transitionId)) {
        if (!MatchesTrackedWidget(*transition, widgetId)) return;
        AppendLocked(*transition, {
            WidgetAdmissionTraceStage::AdmissionPresentation,
            timestamp,
            Sanitize(widgetId),
            presentationRefreshed,
            timestamp >= transition->startedAt
                ? timestamp - transition->startedAt : 0,
        });
        transition->terminal = true;
    }
}

void WidgetAdmissionTrace::RecordMeaningfulInteractive(
    const std::uint64_t transitionId,
    const std::wstring_view widgetId,
    const WidgetLifecycleState before,
    const WidgetLifecycleState after,
    const std::uint64_t timestamp) {
    if (before != WidgetLifecycleState::Visible ||
        after != WidgetLifecycleState::Interactive) return;
    std::scoped_lock lock(mutex_);
    if (auto* transition = FindLocked(transitionId)) {
        if (!MatchesTrackedWidget(*transition, widgetId)) return;
        WidgetAdmissionTraceRecord record;
        record.stage = WidgetAdmissionTraceStage::MeaningfulInteractive;
        record.timestamp = timestamp;
        record.widgetId = Sanitize(widgetId);
        record.elapsed = timestamp >= transition->startedAt
            ? timestamp - transition->startedAt : 0;
        record.session.lifecycle = after;
        AppendLocked(*transition, std::move(record));
    }
}

void WidgetAdmissionTrace::ObserveSlow(const std::uint64_t timestamp) {
    std::scoped_lock lock(mutex_);
    for (auto& transition : transitions_) {
        if (transition.terminal || transition.slowEmitted ||
            timestamp < transition.startedAt ||
            timestamp - transition.startedAt < SlowAdmissionMilliseconds) {
            continue;
        }
        transition.slowEmitted = true;
        AppendLocked(transition, {
            WidgetAdmissionTraceStage::SlowThreshold,
            timestamp,
            transition.trackedWidget,
            true,
            timestamp - transition.startedAt,
        });
    }
}

std::vector<WidgetAdmissionTransitionTrace> WidgetAdmissionTrace::Snapshot() const {
    std::scoped_lock lock(mutex_);
    return {transitions_.begin(), transitions_.end()};
}

bool WidgetAdmissionTrace::Flush(const std::chrono::milliseconds timeout) {
    std::unique_lock lock(diagnosticMutex_);
    return diagnosticChanged_.wait_for(lock, timeout, [this] {
        return pendingDiagnostics_.empty() && !diagnosticWriting_;
    });
}

std::wstring WidgetAdmissionTrace::Sanitize(const std::wstring_view value) {
    std::wstring result;
    result.reserve(std::min<std::size_t>(value.size(), 64));
    for (const wchar_t character : value.substr(0, 64)) {
        result.push_back(std::iswalnum(character) || character == L'-' ||
                character == L'_' || character == L'.'
            ? character : L'?');
    }
    return result;
}

std::wstring WidgetAdmissionTrace::Format(
    const WidgetAdmissionTransitionTrace& transition,
    const WidgetAdmissionTraceRecord& record) {
    std::wstring message =
        L"Admission trace transition=" + std::to_wstring(transition.id) +
        L" selected=" + transition.selectedWidget +
        L" active=" + transition.activeWidget +
        L" target=" + transition.trackedWidget +
        L" widget=" + record.widgetId;
    switch (record.stage) {
    case WidgetAdmissionTraceStage::Selection:
        message += L" stage=selection deferColdStart=" +
            std::wstring(transition.deferColdStart ? L"true" : L"false") +
            L" currentSnapshot=" +
            std::wstring(transition.currentSnapshotPresent ? L"true" : L"false");
        break;
    case WidgetAdmissionTraceStage::RefreshPosted:
        message += L" stage=refresh-posted posted=" +
            std::wstring(record.flag ? L"true" : L"false");
        break;
    case WidgetAdmissionTraceStage::RefreshDequeued:
        message += L" stage=refresh-dequeued queueDelayMs=" +
            std::to_wstring(record.elapsed);
        break;
    case WidgetAdmissionTraceStage::Session: {
        const auto& session = record.session;
        message += L" stage=" + std::wstring(TraceStageValue(session.stage)) +
            L" action=" + std::wstring(ActionValue(session.action)) +
            L" reason=" + std::wstring(ReasonValue(session.reason)) +
            L" request=" + std::to_wstring(session.requestId) +
            L" generation=" + std::to_wstring(session.generation) +
            L" lifecycle=" + std::wstring(LifecycleValue(session.lifecycle)) +
            L" kind=" + std::wstring(RequestKindValue(session.requestKind)) +
            L" disposition=" +
                std::wstring(DispositionValue(session.disposition));
        if (session.startedAt >= session.queuedAt && session.queuedAt != 0) {
            message += L" requestQueueMs=" +
                std::to_wstring(session.startedAt - session.queuedAt);
        }
        if (session.completedAt >= session.startedAt && session.startedAt != 0) {
            message += L" workerMs=" +
                std::to_wstring(session.completedAt - session.startedAt);
        }
        break;
    }
    case WidgetAdmissionTraceStage::AdmissionPresentation:
        message += L" stage=admission presentation=" +
            std::wstring(record.flag ? L"refreshed" : L"not-refreshed");
        break;
    case WidgetAdmissionTraceStage::MeaningfulInteractive:
        message += L" stage=input-a lifecycle=visible-to-interactive";
        break;
    case WidgetAdmissionTraceStage::SlowThreshold:
        message += L" stage=slow-threshold elapsedMs=" +
            std::to_wstring(record.elapsed);
        break;
    }
    message += L" elapsedMs=" + std::to_wstring(record.elapsed);
    if (message.size() > 640) message.resize(640);
    return message;
}

WidgetAdmissionTransitionTrace* WidgetAdmissionTrace::FindLocked(
    const std::uint64_t transitionId) noexcept {
    const auto found = std::find_if(
        transitions_.begin(), transitions_.end(),
        [transitionId](const WidgetAdmissionTransitionTrace& transition) {
            return transition.id == transitionId;
        });
    return found == transitions_.end() ? nullptr : &*found;
}

bool WidgetAdmissionTrace::MatchesTrackedWidget(
    const WidgetAdmissionTransitionTrace& transition,
    const std::wstring_view widgetId) {
    return !transition.trackedWidget.empty() &&
        transition.trackedWidget == Sanitize(widgetId);
}

void WidgetAdmissionTrace::AppendLocked(
    WidgetAdmissionTransitionTrace& transition,
    WidgetAdmissionTraceRecord record) {
    if (!transition.records.empty() && transition.records.back() == record) return;
    if (transition.records.size() >= MaximumRecordsPerTransition) {
        transition.records.erase(
            transition.records.size() > 1
                ? transition.records.begin() + 1
                : transition.records.begin());
    }
    transition.records.push_back(std::move(record));
    QueueDiagnostic(Format(transition, transition.records.back()));
}

void WidgetAdmissionTrace::QueueDiagnostic(std::wstring message) {
    if (!sink_) return;
    {
        std::scoped_lock lock(diagnosticMutex_);
        if (pendingDiagnostics_.size() >= MaximumPendingDiagnostics)
            pendingDiagnostics_.pop_front();
        pendingDiagnostics_.push_back(std::move(message));
    }
    diagnosticChanged_.notify_one();
}

void WidgetAdmissionTrace::WriterLoop(const std::stop_token stopToken) {
    for (;;) {
        std::wstring message;
        {
            std::unique_lock lock(diagnosticMutex_);
            diagnosticChanged_.wait(lock, [this, &stopToken] {
                return stopping_ || stopToken.stop_requested() ||
                    !pendingDiagnostics_.empty();
            });
            if (pendingDiagnostics_.empty() &&
                (stopping_ || stopToken.stop_requested())) return;
            message = std::move(pendingDiagnostics_.front());
            pendingDiagnostics_.pop_front();
            diagnosticWriting_ = true;
        }
        try { sink_(std::move(message)); }
        catch (...) {
        }
        {
            std::scoped_lock lock(diagnosticMutex_);
            diagnosticWriting_ = false;
        }
        diagnosticChanged_.notify_all();
    }
}

} // namespace widgetrail
