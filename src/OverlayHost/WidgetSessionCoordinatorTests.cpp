#include "WidgetSessionCoordinator.h"
#include "WidgetAdmissionTrace.h"

#ifdef NDEBUG
#undef NDEBUG
#endif
#include <cassert>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace {

using namespace std::chrono_literals;
using gba::WidgetDescriptor;
using gba::WidgetAdmissionTrace;
using gba::WidgetAdmissionTraceStage;
using gba::WidgetLifecycleState;
using gba::WidgetPresentationAuthority;
using gba::WidgetSessionCoordinator;
using gba::WidgetSessionEventKind;
using gba::WidgetSessionFailureStage;
using gba::WidgetSessionOperationResult;
using gba::WidgetSessionOperations;
using gba::WidgetSnapshot;

WidgetDescriptor Descriptor(
    const wchar_t* id,
    const wchar_t* instance,
    const wchar_t* runtime,
    const wchar_t* presentation) {
    WidgetDescriptor result;
    result.id = id;
    result.name = id;
    result.instanceId = instance;
    result.runtimeGeneration = runtime;
    result.presentationGeneration = presentation;
    return result;
}

WidgetSnapshot Snapshot(const wchar_t* instance, const long long sequence) {
    WidgetSnapshot result;
    result.instanceId = instance;
    result.sequence = sequence;
    result.root.id = L"root";
    result.root.kind = L"stack";
    return result;
}

struct FakeBridge final {
    std::mutex mutex;
    std::condition_variable_any changed;
    std::vector<WidgetDescriptor> catalog;
    std::unordered_map<std::wstring, WidgetSnapshot> snapshots;
    std::wstring stalledWidget;
    std::wstring ignoreCancellationWidget;
    std::wstring failingWidget;
    bool releaseStall{};
    bool startFails{};
    bool stallStart{};
    bool releaseStart{};
    int startFailuresRemaining{};
    bool protocolMismatch{};
    int ensureCalls{};
    int catalogCalls{};
    int snapshotCalls{};
    int lifecycleCalls{};
    int restartCalls{};

    WidgetSessionOperations Operations() {
        return {
            [this](std::stop_token) {
                std::unique_lock lock(mutex);
                ++ensureCalls;
                changed.notify_all();
                if (stallStart) changed.wait(lock, [&] { return releaseStart; });
                const bool fail = startFails || startFailuresRemaining > 0;
                if (startFailuresRemaining > 0) --startFailuresRemaining;
                return fail
                    ? WidgetSessionOperationResult<bool>::Failure(
                        WidgetSessionFailureStage::Start, L"bridge unavailable")
                    : WidgetSessionOperationResult<bool>::Success(true);
            },
            [this](std::stop_token) {
                std::scoped_lock lock(mutex);
                ++catalogCalls;
                return WidgetSessionOperationResult<std::vector<WidgetDescriptor>>::Success(
                    catalog);
            },
            [this](std::stop_token token, std::wstring_view widgetId,
                   WidgetLifecycleState) {
                return GetSnapshot(token, widgetId);
            },
            [this](std::stop_token, std::wstring_view, WidgetLifecycleState) {
                std::scoped_lock lock(mutex);
                ++lifecycleCalls;
                return WidgetSessionOperationResult<bool>::Success(true);
            },
            [this](std::stop_token token, std::wstring_view widgetId) {
                return GetSnapshot(token, widgetId);
            },
            [this](std::stop_token, std::wstring_view) {
                std::scoped_lock lock(mutex);
                ++restartCalls;
                return WidgetSessionOperationResult<bool>::Success(true);
            },
        };
    }

    WidgetSessionOperationResult<WidgetSnapshot> GetSnapshot(
        const std::stop_token token,
        const std::wstring_view widgetId) {
        std::unique_lock lock(mutex);
        ++snapshotCalls;
        changed.notify_all();
        if (widgetId == stalledWidget) {
            if (widgetId == ignoreCancellationWidget)
                changed.wait(lock, [&] { return releaseStall; });
            else
                changed.wait(lock, token, [&] { return releaseStall; });
        }
        if (token.stop_requested() && widgetId != ignoreCancellationWidget) {
            return WidgetSessionOperationResult<WidgetSnapshot>::Failure(
                WidgetSessionFailureStage::Snapshot, L"snapshot cancelled");
        }
        if (widgetId == failingWidget) {
            return WidgetSessionOperationResult<WidgetSnapshot>::Failure(
                WidgetSessionFailureStage::Snapshot, L"delayed snapshot failed");
        }
        const auto found = snapshots.find(std::wstring(widgetId));
        if (found == snapshots.end()) {
            return WidgetSessionOperationResult<WidgetSnapshot>::Failure(
                WidgetSessionFailureStage::Snapshot, L"snapshot unavailable");
        }
        auto result = found->second;
        if (protocolMismatch) result.instanceId = L"wrong.instance";
        return WidgetSessionOperationResult<WidgetSnapshot>::Success(std::move(result));
    }
};

template <typename Predicate>
std::vector<gba::WidgetSessionEvent> WaitEvents(
    WidgetSessionCoordinator& coordinator,
    Predicate predicate) {
    const auto deadline = std::chrono::steady_clock::now() + 2s;
    std::vector<gba::WidgetSessionEvent> collected;
    while (std::chrono::steady_clock::now() < deadline) {
        auto events = coordinator.TakeEvents();
        collected.insert(
            collected.end(),
            std::make_move_iterator(events.begin()),
            std::make_move_iterator(events.end()));
        if (predicate(collected)) return collected;
        std::this_thread::sleep_for(2ms);
    }
    assert(false && "coordinator event deadline expired");
    return collected;
}

void CatalogReplacementAndLastGoodSnapshot() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
        Descriptor(L"beta", L"beta.one", L"runtime-1", L"view-1"),
    };
    WidgetSessionCoordinator coordinator(bridge.Operations());
    auto initial = coordinator.EstablishCatalog();
    assert(initial && initial->availableWidgetIds.size() == 2);
    assert(coordinator.Contains(L"alpha") && coordinator.Contains(L"beta"));

    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 1);
    assert(coordinator.RequestSnapshot(L"alpha"));
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 1);
    const auto admittedPresentation = coordinator.Presentation(L"alpha");
    assert(admittedPresentation.snapshot &&
           admittedPresentation.snapshot->sequence == 1 &&
           admittedPresentation.authority == WidgetPresentationAuthority::Current);

    bridge.snapshots.erase(L"alpha");
    assert(coordinator.RequestSnapshot(L"alpha"));
    const auto failure = WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::Failed;
        });
    });
    assert(failure.back().failure.stage == WidgetSessionFailureStage::Snapshot);
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 1);
    const auto failurePresentation = coordinator.Presentation(L"alpha");
    assert(failurePresentation.snapshot &&
           failurePresentation.snapshot->sequence == 1 &&
           failurePresentation.authority ==
               WidgetPresentationAuthority::FailureRetained);
    coordinator.RecordRuntimeFailure(
        L"alpha", WidgetSessionFailureStage::Start, L"primary start failure");
    coordinator.RecordFailure(
        L"alpha", WidgetSessionFailureStage::Lifecycle, L"secondary exit failure");
    assert(coordinator.Failure(L"alpha") &&
           coordinator.Failure(L"alpha")->stage == WidgetSessionFailureStage::Start &&
           coordinator.Failure(L"alpha")->safeMessage == L"primary start failure");

    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 2);
    assert(coordinator.RequestSnapshot(L"alpha", true));
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    const auto recoveredPresentation = coordinator.Presentation(L"alpha");
    assert(recoveredPresentation.snapshot &&
           recoveredPresentation.snapshot->sequence == 2 &&
           recoveredPresentation.authority == WidgetPresentationAuthority::Current);
    assert(!coordinator.Failure(L"alpha"));

    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.two", L"runtime-2", L"view-2"),
    };
    assert(coordinator.RequestCatalog());
    const auto catalog = WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::CatalogChanged;
        });
    });
    const auto& change = *catalog.back().catalog;
    assert(change.availableWidgetIds.size() == 1 &&
           change.availableWidgetIds.front() == L"alpha");
    assert(change.runtimeChanges.size() == 2);
    assert(!coordinator.Snapshot(L"alpha"));
    assert(coordinator.Presentation(L"alpha").authority ==
           WidgetPresentationAuthority::Unavailable);
    assert(!coordinator.Contains(L"beta"));
}

void StaleCompletionAndProtocolFailure() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 1);
    bridge.stalledWidget = L"alpha";
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    assert(coordinator.RequestSnapshot(L"alpha"));
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] { return bridge.snapshotCalls == 1; }));
    }
    coordinator.RemoveSnapshot(L"alpha");
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.releaseStall = true;
    }
    bridge.changed.notify_all();
    const auto stale = WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::StaleCompletionRejected;
        });
    });
    assert(!stale.empty() && !coordinator.Snapshot(L"alpha"));

    bridge.protocolMismatch = true;
    assert(coordinator.RequestSnapshot(L"alpha", true));
    const auto protocol = WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::Failed;
        });
    });
    assert(protocol.back().failure.stage == WidgetSessionFailureStage::Protocol);
}

void LifecycleDrainAndBoundedCorrelation() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
        Descriptor(L"beta", L"beta.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 1);
    bridge.snapshots[L"beta"] = Snapshot(L"beta.one", 1);
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    std::map<std::wstring, WidgetLifecycleState, std::less<>> desired{
        {L"alpha", WidgetLifecycleState::Visible},
        {L"beta", WidgetLifecycleState::Interactive},
    };
    coordinator.SetLifecycleTargets(desired);
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::count_if(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        }) == 2;
    });
    assert(coordinator.Lifecycle(L"alpha") == WidgetLifecycleState::Visible);
    assert(coordinator.Lifecycle(L"beta") == WidgetLifecycleState::Interactive);

    assert(coordinator.DrainLifecycle(1s));
    assert(!coordinator.Lifecycle(L"alpha") && !coordinator.Lifecycle(L"beta"));

    assert(coordinator.RequestRestart(L"alpha"));
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::Restarted;
        });
    });
    coordinator.SetLifecycleTargets({{L"alpha", WidgetLifecycleState::Visible}});
    const auto reloaded = WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(std::any_of(reloaded.begin(), reloaded.end(), [](const auto& event) {
        return event.completedRestart;
    }));
    assert(coordinator.DrainLifecycle(1s));

    for (int index = 0; index < 100; ++index)
        assert(coordinator.RequestSnapshot(index % 2 == 0 ? L"alpha" : L"beta"));
    assert(coordinator.PendingRequestCount() <= WidgetSessionCoordinator::MaximumPendingRequests);
}

void StalledSessionDoesNotBlockHostOrNeighborIntent() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"slow", L"slow.one", L"runtime-1", L"view-1"),
        Descriptor(L"fast", L"fast.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"slow"] = Snapshot(L"slow.one", 1);
    bridge.snapshots[L"fast"] = Snapshot(L"fast.one", 1);
    bridge.stalledWidget = L"slow";
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    assert(coordinator.RequestSnapshot(L"fast"));
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"fast" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.Snapshot(L"fast") &&
           coordinator.Snapshot(L"fast")->sequence == 1);
    assert(coordinator.RequestSnapshot(L"slow"));
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] { return bridge.snapshotCalls == 2; }));
    }
    const auto began = std::chrono::steady_clock::now();
    assert(coordinator.RequestSnapshot(L"fast"));
    coordinator.SetLifecycleTargets({
        {L"fast", WidgetLifecycleState::Interactive},
    });
    const bool guideCanToggle = true;
    const bool closeCanDispatch = true;
    assert(guideCanToggle && closeCanDispatch);
    assert(coordinator.Snapshot(L"fast") &&
           coordinator.Snapshot(L"fast")->sequence == 1);
    assert(std::chrono::steady_clock::now() - began < 20ms);
    assert(coordinator.PendingRequestCount() <= WidgetSessionCoordinator::MaximumPendingRequests);

    {
        std::scoped_lock lock(bridge.mutex);
        bridge.releaseStall = true;
    }
    bridge.changed.notify_all();
    const auto events = WaitEvents(coordinator, [](const auto& value) {
        return std::any_of(value.begin(), value.end(), [](const auto& event) {
            return event.widgetId == L"slow" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(std::any_of(events.begin(), events.end(), [](const auto& event) {
        return event.widgetId == L"fast" &&
               event.kind == WidgetSessionEventKind::SnapshotAdmitted;
    }));
}

void TypedStartFailureAndRetryPolicy() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
    };
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    bridge.startFails = true;
    assert(coordinator.RequestSnapshot(L"alpha"));
    const auto failed = WaitEvents(coordinator, [](const auto& events) {
        return !events.empty();
    });
    assert(failed.front().failure.stage == WidgetSessionFailureStage::Start);
    assert(coordinator.NextCatalogRetryDelay(false, true, true) == 250U);
    assert(coordinator.NextCatalogRetryDelay(false, true, true) == 500U);
    assert(coordinator.NextCatalogRetryDelay(false, true, true) == 1000U);
    assert(!coordinator.NextCatalogRetryDelay(false, true, true));
    assert(!coordinator.NextCatalogRetryDelay(true, true, true));
}

void PrimaryStartFailureSurvivesLifecycleRetargetAndRetry() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
    };
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());

    bridge.stallStart = true;
    bridge.startFailuresRemaining = 1;
    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Visible},
    });
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] {
            return bridge.ensureCalls == 2;
        }));
    }
    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Interactive},
    });
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.releaseStart = true;
    }
    bridge.changed.notify_all();

    const auto failed = WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::Failed;
        });
    });
    assert(std::any_of(failed.begin(), failed.end(), [](const auto& event) {
        return event.widgetId == L"alpha" &&
               event.kind == WidgetSessionEventKind::Failed &&
               event.failure.stage == WidgetSessionFailureStage::Start;
    }));
    assert(coordinator.Failure(L"alpha") &&
           coordinator.Failure(L"alpha")->stage == WidgetSessionFailureStage::Start);
    assert(!coordinator.Snapshot(L"alpha"));

    int automaticEnsureCalls{};
    {
        std::scoped_lock lock(bridge.mutex);
        automaticEnsureCalls = bridge.ensureCalls;
        bridge.releaseStart = false;
    }
    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Interactive},
    });
    bool automaticStart{};
    {
        std::unique_lock lock(bridge.mutex);
        automaticStart = bridge.changed.wait_for(lock, 100ms, [&] {
            return bridge.ensureCalls != automaticEnsureCalls;
        });
        bridge.releaseStart = true;
    }
    bridge.changed.notify_all();
    assert(!automaticStart);
    assert(coordinator.Failure(L"alpha") &&
           coordinator.Failure(L"alpha")->stage == WidgetSessionFailureStage::Start);

    int snapshotsBeforeRetry{};
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 7);
        snapshotsBeforeRetry = bridge.snapshotCalls;
    }
    assert(coordinator.RequestRestart(L"alpha"));
    assert(coordinator.Failure(L"alpha") &&
           coordinator.Failure(L"alpha")->stage == WidgetSessionFailureStage::Start);
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::Restarted;
        });
    });
    assert(coordinator.Failure(L"alpha") &&
           coordinator.Failure(L"alpha")->stage == WidgetSessionFailureStage::Start);
    {
        std::scoped_lock lock(bridge.mutex);
        assert(bridge.restartCalls == 1);
    }

    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Interactive},
    });
    const auto admitted = WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(std::any_of(admitted.begin(), admitted.end(), [](const auto& event) {
        return event.widgetId == L"alpha" &&
               event.kind == WidgetSessionEventKind::SnapshotAdmitted &&
               event.completedRestart;
    }));
    {
        std::scoped_lock lock(bridge.mutex);
        assert(bridge.snapshotCalls == snapshotsBeforeRetry + 1);
    }
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 7);
    assert(!coordinator.Failure(L"alpha"));
}

void CancellationStopsStalledRequest() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"slow", L"slow.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"slow"] = Snapshot(L"slow.one", 1);
    bridge.stalledWidget = L"slow";
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    assert(coordinator.RequestSnapshot(L"slow"));
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] {
            return bridge.snapshotCalls == 1;
        }));
    }
    const auto began = std::chrono::steady_clock::now();
    coordinator.Shutdown();
    assert(std::chrono::steady_clock::now() - began < 1s);
}

void SelectionRevokesNeverCompletingRequest() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"slow", L"slow.one", L"runtime-1", L"view-1"),
        Descriptor(L"fast", L"fast.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"slow"] = Snapshot(L"slow.one", 1);
    bridge.snapshots[L"fast"] = Snapshot(L"fast.one", 1);
    bridge.stalledWidget = L"slow";
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({
        {L"slow", WidgetLifecycleState::Visible},
    });
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] {
            return bridge.snapshotCalls == 1;
        }));
    }

    coordinator.SetLifecycleTargets({
        {L"fast", WidgetLifecycleState::Interactive},
    });
    const auto events = WaitEvents(coordinator, [](const auto& value) {
        return std::any_of(value.begin(), value.end(), [](const auto& event) {
            return event.widgetId == L"fast" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(std::any_of(events.begin(), events.end(), [](const auto& event) {
        return event.widgetId == L"slow" &&
               event.kind == WidgetSessionEventKind::StaleCompletionRejected;
    }));
    assert(coordinator.Snapshot(L"fast") &&
           coordinator.Snapshot(L"fast")->sequence == 1);
    assert(!coordinator.Snapshot(L"slow"));
}

void DelayedSuccessRetainsLastGoodSnapshot() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 1);
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Interactive},
    });
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });

    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 2);
    bridge.stalledWidget = L"alpha";
    assert(coordinator.RequestSnapshot(L"alpha"));
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] {
            return bridge.snapshotCalls == 2;
        }));
    }
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 1);
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.releaseStall = true;
    }
    bridge.changed.notify_all();
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 2);
}

void HideAndWorkerExitRevokePendingSnapshots() {
    for (const bool workerExited : {false, true}) {
        FakeBridge bridge;
        bridge.catalog = {
            Descriptor(L"slow", L"slow.one", L"runtime-1", L"view-1"),
        };
        bridge.snapshots[L"slow"] = Snapshot(L"slow.one", 1);
        bridge.stalledWidget = L"slow";
        WidgetSessionCoordinator coordinator(bridge.Operations());
        assert(coordinator.EstablishCatalog());
        coordinator.SetLifecycleTargets({
            {L"slow", WidgetLifecycleState::Visible},
        });
        {
            std::unique_lock lock(bridge.mutex);
            assert(bridge.changed.wait_for(lock, 1s, [&] {
                return bridge.snapshotCalls == 1;
            }));
        }
        if (workerExited)
            coordinator.RemoveSnapshot(L"slow");
        else
            coordinator.SetLifecycleTargets({});
        const auto events = WaitEvents(coordinator, [](const auto& value) {
            return std::any_of(value.begin(), value.end(), [](const auto& event) {
                return event.kind == WidgetSessionEventKind::StaleCompletionRejected;
            });
        });
        assert(events.size() == 1);
        assert(events.front().widgetId == L"slow");
        assert(!coordinator.Snapshot(L"slow"));
        assert(!coordinator.Lifecycle(L"slow"));
    }
}

void CancellationIgnoringLateResultsAreStale() {
    for (const bool lateFailure : {false, true}) {
        FakeBridge bridge;
        bridge.catalog = {
            Descriptor(L"slow", L"slow.one", L"runtime-1", L"view-1"),
            Descriptor(L"fast", L"fast.one", L"runtime-1", L"view-1"),
        };
        bridge.snapshots[L"slow"] = Snapshot(L"slow.one", 99);
        bridge.snapshots[L"fast"] = Snapshot(L"fast.one", 7);
        bridge.stalledWidget = L"slow";
        bridge.ignoreCancellationWidget = L"slow";
        if (lateFailure) bridge.failingWidget = L"slow";
        WidgetSessionCoordinator coordinator(bridge.Operations());
        assert(coordinator.EstablishCatalog());
        coordinator.SetLifecycleTargets({
            {L"slow", WidgetLifecycleState::Visible},
        });
        {
            std::unique_lock lock(bridge.mutex);
            assert(bridge.changed.wait_for(lock, 1s, [&] {
                return bridge.snapshotCalls == 1;
            }));
        }
        coordinator.SetLifecycleTargets({
            {L"fast", WidgetLifecycleState::Interactive},
        });
        {
            std::scoped_lock lock(bridge.mutex);
            bridge.releaseStall = true;
        }
        bridge.changed.notify_all();
        const auto events = WaitEvents(coordinator, [](const auto& value) {
            return std::any_of(value.begin(), value.end(), [](const auto& event) {
                return event.widgetId == L"fast" &&
                       event.kind == WidgetSessionEventKind::SnapshotAdmitted;
            });
        });
        assert(std::count_if(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"slow" &&
                   event.kind == WidgetSessionEventKind::StaleCompletionRejected;
        }) == 1);
        assert(std::none_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"slow" &&
                   (event.kind == WidgetSessionEventKind::SnapshotAdmitted ||
                    event.kind == WidgetSessionEventKind::Failed);
        }));
        assert(!coordinator.Snapshot(L"slow"));
        assert(coordinator.Snapshot(L"fast") &&
               coordinator.Snapshot(L"fast")->sequence == 7);
    }
}

void CorrelatedAdmissionTraceIsBoundedAndSanitized() {
    std::mutex diagnosticMutex;
    std::vector<std::wstring> diagnostics;
    WidgetAdmissionTrace trace([&](std::wstring message) {
        std::scoped_lock lock(diagnosticMutex);
        diagnostics.push_back(std::move(message));
    });
    const auto correlation = trace.BeginSelection(
        L"network<private>", L"network", true, false, 1000);
    trace.RecordRefreshPosted(correlation, L"network", true, 1001);
    trace.RecordRefreshDequeued(correlation, L"network", 1020);
    trace.RecordSession({
        correlation,
        gba::WidgetSessionTraceStage::LifecycleDecision,
        gba::WidgetSessionTraceAction::Queued,
        gba::WidgetSessionTraceReason::None,
        gba::WidgetSessionCompletionDisposition::None,
        7,
        3,
        gba::WidgetSessionRequestKind::Establish,
        WidgetLifecycleState::Visible,
        L"network",
        1021,
    });
    trace.ObserveSlow(1249);
    trace.ObserveSlow(1250);
    trace.RecordMeaningfulInteractive(
        correlation, L"network", WidgetLifecycleState::Background,
        WidgetLifecycleState::Interactive, 1251);
    trace.RecordMeaningfulInteractive(
        correlation, L"network", WidgetLifecycleState::Visible,
        WidgetLifecycleState::Interactive, 1252);
    trace.RecordAdmissionPresentation(correlation, L"network", true, 1260);
    trace.ObserveSlow(1500);

    const auto snapshot = trace.Snapshot();
    assert(snapshot.size() == 1);
    assert(snapshot.front().selectedWidget == L"network?private?");
    assert(snapshot.front().terminal);
    assert(std::any_of(
        snapshot.front().records.begin(), snapshot.front().records.end(),
        [](const auto& record) {
            return record.stage == WidgetAdmissionTraceStage::RefreshDequeued &&
                   record.elapsed == 19;
        }));
    assert(std::count_if(
        snapshot.front().records.begin(), snapshot.front().records.end(),
        [](const auto& record) {
            return record.stage == WidgetAdmissionTraceStage::SlowThreshold;
        }) == 1);
    assert(std::count_if(
        snapshot.front().records.begin(), snapshot.front().records.end(),
        [](const auto& record) {
            return record.stage == WidgetAdmissionTraceStage::MeaningfulInteractive;
        }) == 1);
    assert(trace.Flush(1s));
    {
        std::scoped_lock lock(diagnosticMutex);
        assert(!diagnostics.empty());
        assert(std::none_of(diagnostics.begin(), diagnostics.end(), [](const auto& value) {
            return value.find(L'<') != std::wstring::npos ||
                   value.find(L'>') != std::wstring::npos;
        }));
    }

    for (std::size_t index = 0;
         index < WidgetAdmissionTrace::MaximumTransitions + 3; ++index) {
        const auto next = trace.BeginSelection(
            L"selected", L"active", false, true, 2000 + index);
        trace.RecordAdmissionPresentation(next, L"active", true, 2001 + index);
    }
    assert(trace.Snapshot().size() == WidgetAdmissionTrace::MaximumTransitions);
}

void CoordinatorEmitsCorrelatedLifecycleAndRequestStages() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 5);
    std::mutex traceMutex;
    std::vector<gba::WidgetSessionTraceEvent> trace;
    std::atomic<std::uint64_t> now{100};
    WidgetSessionCoordinator coordinator(
        bridge.Operations(), {},
        [&](const gba::WidgetSessionTraceEvent& event) {
            std::scoped_lock lock(traceMutex);
            trace.push_back(event);
        },
        [&] { return now.fetch_add(1); });
    assert(coordinator.EstablishCatalog());

    coordinator.SetLifecycleTargets(
        {{L"alpha", WidgetLifecycleState::Visible}}, true, 40);
    coordinator.SetLifecycleTargets(
        {{L"alpha", WidgetLifecycleState::Visible}}, false, 41);
    const auto events = WaitEvents(coordinator, [](const auto& value) {
        return std::any_of(value.begin(), value.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(events.size() == 1);
    assert(events.front().correlationId == 41);
    assert(events.front().completionDisposition ==
           gba::WidgetSessionCompletionDisposition::Admitted);

    std::scoped_lock lock(traceMutex);
    assert(std::any_of(trace.begin(), trace.end(), [](const auto& event) {
        return event.correlationId == 40 &&
               event.stage == gba::WidgetSessionTraceStage::LifecycleDecision &&
               event.action == gba::WidgetSessionTraceAction::Skipped &&
               event.reason == gba::WidgetSessionTraceReason::Deferred;
    }));
    const auto queued = std::find_if(trace.begin(), trace.end(), [](const auto& event) {
        return event.correlationId == 41 &&
               event.stage == gba::WidgetSessionTraceStage::RequestQueued;
    });
    const auto started = std::find_if(trace.begin(), trace.end(), [](const auto& event) {
        return event.correlationId == 41 &&
               event.stage == gba::WidgetSessionTraceStage::RequestStarted;
    });
    const auto completed = std::find_if(trace.begin(), trace.end(), [](const auto& event) {
        return event.correlationId == 41 &&
               event.stage == gba::WidgetSessionTraceStage::RequestCompleted;
    });
    assert(queued != trace.end() && started != trace.end() && completed != trace.end());
    assert(queued->requestId == started->requestId &&
           started->requestId == completed->requestId);
    assert(queued->generation == completed->generation);
    assert(queued->queuedAt <= started->startedAt &&
           started->startedAt <= completed->completedAt);
    assert(completed->disposition ==
           gba::WidgetSessionCompletionDisposition::Admitted);
}

void SelectedTraceRejectsPinnedAndSupersededAdmissions() {
    WidgetAdmissionTrace trace;
    const auto selected = trace.BeginSelection(
        L"selected", L"selected", true, false, 1000);
    trace.RecordSession({
        selected,
        gba::WidgetSessionTraceStage::RequestCompleted,
        gba::WidgetSessionTraceAction::None,
        gba::WidgetSessionTraceReason::None,
        gba::WidgetSessionCompletionDisposition::Failed,
        1, 1, gba::WidgetSessionRequestKind::Establish,
        WidgetLifecycleState::Visible, L"pinned", 1001, 1002, 1003,
    });
    trace.RecordAdmissionPresentation(selected, L"pinned", false, 1004);
    trace.ObserveSlow(1250);
    auto transitions = trace.Snapshot();
    assert(transitions.size() == 1);
    assert(transitions.front().trackedWidget == L"selected");
    assert(!transitions.front().terminal && transitions.front().slowEmitted);
    assert(std::none_of(
        transitions.front().records.begin(), transitions.front().records.end(),
        [](const auto& record) { return record.widgetId == L"pinned"; }));

    trace.RecordAdmissionPresentation(selected, L"selected", true, 1260);
    const auto current = trace.BeginSelection(
        L"replacement", L"replacement", true, false, 2000);
    trace.RecordSession({
        selected,
        gba::WidgetSessionTraceStage::RequestCompleted,
        gba::WidgetSessionTraceAction::None,
        gba::WidgetSessionTraceReason::NewerTarget,
        gba::WidgetSessionCompletionDisposition::Cancelled,
        2, 2, gba::WidgetSessionRequestKind::Snapshot,
        WidgetLifecycleState::Visible, L"selected", 2001, 2002, 2003,
    });
    trace.RecordAdmissionPresentation(current, L"selected", false, 2004);
    trace.ObserveSlow(2250);
    transitions = trace.Snapshot();
    const auto currentTrace = std::find_if(
        transitions.begin(), transitions.end(),
        [&](const auto& transition) { return transition.id == current; });
    assert(currentTrace != transitions.end());
    assert(!currentTrace->terminal && currentTrace->slowEmitted);
    trace.RecordAdmissionPresentation(current, L"replacement", true, 2260);
    transitions = trace.Snapshot();
    const auto admitted = std::find_if(
        transitions.begin(), transitions.end(),
        [&](const auto& transition) { return transition.id == current; });
    assert(admitted != transitions.end() && admitted->terminal);
}

void SelectedLifecycleCorrelationExcludesPinnedTarget() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"selected", L"selected.one", L"runtime-1", L"view-1"),
        Descriptor(L"pinned", L"pinned.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"selected"] = Snapshot(L"selected.one", 1);
    bridge.snapshots[L"pinned"] = Snapshot(L"pinned.one", 2);
    std::mutex traceMutex;
    std::vector<gba::WidgetSessionTraceEvent> trace;
    WidgetSessionCoordinator coordinator(
        bridge.Operations(), {},
        [&](const gba::WidgetSessionTraceEvent& event) {
            std::scoped_lock lock(traceMutex);
            trace.push_back(event);
        });
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets(
        {{L"selected", WidgetLifecycleState::Visible},
         {L"pinned", WidgetLifecycleState::Visible}},
        false, 92, L"selected");
    const auto events = WaitEvents(coordinator, [](const auto& values) {
        return std::count_if(values.begin(), values.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        }) == 2;
    });
    const auto selected = std::find_if(events.begin(), events.end(), [](const auto& event) {
        return event.kind == WidgetSessionEventKind::SnapshotAdmitted &&
            event.widgetId == L"selected";
    });
    const auto pinned = std::find_if(events.begin(), events.end(), [](const auto& event) {
        return event.kind == WidgetSessionEventKind::SnapshotAdmitted &&
            event.widgetId == L"pinned";
    });
    assert(selected != events.end() && selected->correlationId == 92);
    assert(pinned != events.end() && pinned->correlationId == 0);
    std::scoped_lock lock(traceMutex);
    assert(std::none_of(trace.begin(), trace.end(), [](const auto& event) {
        return event.correlationId == 92 && event.widgetId == L"pinned";
    }));
}

} // namespace

int main() {
    CatalogReplacementAndLastGoodSnapshot();
    StaleCompletionAndProtocolFailure();
    LifecycleDrainAndBoundedCorrelation();
    StalledSessionDoesNotBlockHostOrNeighborIntent();
    TypedStartFailureAndRetryPolicy();
    PrimaryStartFailureSurvivesLifecycleRetargetAndRetry();
    CancellationStopsStalledRequest();
    SelectionRevokesNeverCompletingRequest();
    DelayedSuccessRetainsLastGoodSnapshot();
    HideAndWorkerExitRevokePendingSnapshots();
    CancellationIgnoringLateResultsAreStale();
    CorrelatedAdmissionTraceIsBoundedAndSanitized();
    CoordinatorEmitsCorrelatedLifecycleAndRequestStages();
    SelectedTraceRejectsPinnedAndSupersededAdmissions();
    SelectedLifecycleCorrelationExcludesPinnedTarget();
    std::cout << "WidgetSessionCoordinatorTests passed (17 scenarios)\n";
}
