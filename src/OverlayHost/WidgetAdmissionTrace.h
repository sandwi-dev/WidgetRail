#pragma once

#include "WidgetSessionCoordinator.h"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <functional>
#include <mutex>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace gba {

enum class WidgetAdmissionTraceStage {
    Selection,
    RefreshPosted,
    RefreshDequeued,
    Session,
    AdmissionPresentation,
    MeaningfulInteractive,
    SlowThreshold,
};

struct WidgetAdmissionTraceRecord final {
    WidgetAdmissionTraceStage stage{WidgetAdmissionTraceStage::Selection};
    std::uint64_t timestamp{};
    std::wstring widgetId;
    bool flag{};
    std::uint64_t elapsed{};
    WidgetSessionTraceEvent session;

    friend bool operator==(
        const WidgetAdmissionTraceRecord&,
        const WidgetAdmissionTraceRecord&) = default;
};

struct WidgetAdmissionTransitionTrace final {
    std::uint64_t id{};
    std::uint64_t startedAt{};
    std::wstring selectedWidget;
    std::wstring activeWidget;
    std::wstring trackedWidget;
    bool deferColdStart{};
    bool currentSnapshotPresent{};
    bool terminal{};
    bool slowEmitted{};
    std::uint64_t refreshPostedAt{};
    std::vector<WidgetAdmissionTraceRecord> records;
};

/// Bounded diagnostic history for one selection-to-admission path. This type
/// observes existing owners only; it owns no selection, lifecycle, request,
/// transport, or presentation decision. Diagnostic file writes are delegated
/// to one background sink so UI-thread recording remains memory-only.
class WidgetAdmissionTrace final {
public:
    static constexpr std::size_t MaximumTransitions = 16;
    static constexpr std::size_t MaximumRecordsPerTransition = 32;
    static constexpr std::size_t MaximumPendingDiagnostics = 64;
    static constexpr std::uint64_t SlowAdmissionMilliseconds = 250;

    using DiagnosticSink = std::function<void(std::wstring)>;

    explicit WidgetAdmissionTrace(DiagnosticSink sink = {});
    ~WidgetAdmissionTrace();

    WidgetAdmissionTrace(const WidgetAdmissionTrace&) = delete;
    WidgetAdmissionTrace& operator=(const WidgetAdmissionTrace&) = delete;

    [[nodiscard]] std::uint64_t BeginSelection(
        std::wstring_view selectedWidget,
        std::wstring_view activeWidget,
        bool deferColdStart,
        bool currentSnapshotPresent,
        std::uint64_t timestamp);
    void RecordRefreshPosted(
        std::uint64_t transitionId,
        std::wstring_view widgetId,
        bool posted,
        std::uint64_t timestamp);
    void RecordRefreshDequeued(
        std::uint64_t transitionId,
        std::wstring_view widgetId,
        std::uint64_t timestamp);
    void RecordSession(const WidgetSessionTraceEvent& event);
    void RecordAdmissionPresentation(
        std::uint64_t transitionId,
        std::wstring_view widgetId,
        bool presentationRefreshed,
        std::uint64_t timestamp);
    void RecordMeaningfulInteractive(
        std::uint64_t transitionId,
        std::wstring_view widgetId,
        WidgetLifecycleState before,
        WidgetLifecycleState after,
        std::uint64_t timestamp);
    void ObserveSlow(std::uint64_t timestamp);

    [[nodiscard]] std::vector<WidgetAdmissionTransitionTrace> Snapshot() const;
    [[nodiscard]] bool Flush(std::chrono::milliseconds timeout);

private:
    [[nodiscard]] static std::wstring Sanitize(std::wstring_view value);
    [[nodiscard]] static std::wstring Format(
        const WidgetAdmissionTransitionTrace& transition,
        const WidgetAdmissionTraceRecord& record);
    [[nodiscard]] WidgetAdmissionTransitionTrace* FindLocked(
        std::uint64_t transitionId) noexcept;
    [[nodiscard]] static bool MatchesTrackedWidget(
        const WidgetAdmissionTransitionTrace& transition,
        std::wstring_view widgetId);
    void AppendLocked(
        WidgetAdmissionTransitionTrace& transition,
        WidgetAdmissionTraceRecord record);
    void QueueDiagnostic(std::wstring message);
    void WriterLoop(std::stop_token stopToken);

    mutable std::mutex mutex_;
    std::deque<WidgetAdmissionTransitionTrace> transitions_;
    std::uint64_t nextTransitionId_{};

    DiagnosticSink sink_;
    std::mutex diagnosticMutex_;
    std::condition_variable diagnosticChanged_;
    std::deque<std::wstring> pendingDiagnostics_;
    bool diagnosticWriting_{};
    bool stopping_{};
    std::jthread writer_;
};

} // namespace gba
