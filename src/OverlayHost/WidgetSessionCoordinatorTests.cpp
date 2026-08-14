#include "WidgetSessionCoordinator.h"

#ifdef NDEBUG
#undef NDEBUG
#endif
#include <cassert>
#include <algorithm>
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
    std::cout << "WidgetSessionCoordinatorTests passed (13 scenarios)\n";
}
