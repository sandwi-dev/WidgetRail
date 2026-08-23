#include "WidgetSessionCoordinator.h"
#include "WidgetAdmissionTrace.h"

#ifdef NDEBUG
#undef NDEBUG
#endif
#include <cassert>
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <future>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace {

using namespace std::chrono_literals;
using widgetrail::WidgetDescriptor;
using widgetrail::WidgetAdmissionTrace;
using widgetrail::WidgetAdmissionTraceStage;
using widgetrail::WidgetLifecycleState;
using widgetrail::WidgetPresentationAuthority;
using widgetrail::WidgetPresentationPublication;
using widgetrail::WidgetPresentationTransactionKind;
using widgetrail::WidgetRefreshState;
using widgetrail::WidgetSessionCoordinator;
using widgetrail::WidgetSessionEventKind;
using widgetrail::WidgetSessionFailureStage;
using widgetrail::WidgetSessionOperationResult;
using widgetrail::WidgetSessionOperations;
using widgetrail::WidgetSnapshot;
using widgetrail::VirtualCollectionWindowChange;

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

WidgetSnapshot Snapshot(
    const wchar_t* instance,
    const long long sequence,
    const double width = 560.0,
    const double height = 645.0) {
    WidgetSnapshot result;
    result.instanceId = instance;
    result.sequence = sequence;
    result.root.id = L"root";
    result.root.kind = L"stack";
    result.surface.emplace();
    result.surface->preferredWidth = width;
    result.surface->preferredHeight = height;
    return result;
}

WidgetSnapshot VirtualSnapshot(
    const wchar_t* instance,
    const long long sequence,
    const std::uint64_t generation,
    const int first,
    const widgetrail::VirtualCollectionWindowChange change =
        widgetrail::VirtualCollectionWindowChange::Replace) {
    auto result = Snapshot(instance, sequence);
    result.protocolVersion = 19;
    result.root.id = L"virtual.list";
    result.root.kind = L"scroll";
    result.root.scrollAxis = L"vertical";
    result.root.collectionAnchorKey = L"key." + std::to_wstring(first);
    result.root.virtualCollectionWindow = widgetrail::VirtualCollectionWindow{
        generation,
        change,
        static_cast<std::uint64_t>(first),
        10'000,
        first > 0,
        first + 2 < 10'000,
        52.0,
    };
    for (int index = first; index < first + 2; ++index) {
        widgetrail::WidgetNode item;
        item.id = L"item.node." + std::to_wstring(index);
        item.kind = L"button";
        item.text = L"Item";
        item.actionId = L"select";
        item.collectionItemKey = L"key." + std::to_wstring(index);
        result.root.children.push_back(std::move(item));
    }
    return result;
}

struct FakeBridge final {
    struct PresentationRequest final {
        long long baseSequence{};
        WidgetPresentationTransactionKind transactionKind{
            WidgetPresentationTransactionKind::OrdinaryCheckpoint};
        long long recoveryOriginSequence{};
        friend bool operator==(const PresentationRequest&, const PresentationRequest&) = default;
    };
    std::mutex mutex;
    std::condition_variable_any changed;
    std::vector<WidgetDescriptor> catalog;
    std::unordered_map<std::wstring, WidgetSnapshot> snapshots;
    std::optional<WidgetSnapshot> snapshotAfterNextRead;
    std::wstring stalledWidget;
    std::wstring ignoreCancellationWidget;
    std::wstring failingWidget;
    bool releaseStall{};
    bool startFails{};
    bool stallStart{};
    bool releaseStart{};
    int startFailuresRemaining{};
    int snapshotFailuresRemaining{};
    bool protocolMismatch{};
    bool publishUpdate{};
    bool rejectPublishedUpdate{};
    int stalePresentationFailuresRemaining{};
    bool failAllPresentationsAsStale{};
    bool stallBaseZeroPresentation{};
    bool baseZeroPresentationStarted{};
    bool releaseBaseZeroPresentation{};
    std::optional<WidgetPresentationTransactionKind> returnedTransactionKind;
    long long returnedRecoveryOriginDelta{};
    int ensureCalls{};
    int catalogCalls{};
    int snapshotCalls{};
    std::unordered_map<std::wstring, int> snapshotCallsByWidget;
    std::vector<PresentationRequest> presentationRequests;
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
                   WidgetLifecycleState, const long long baseSequence,
                   const WidgetPresentationTransactionKind transactionKind,
                   const long long recoveryOriginSequence) {
                return GetPresentation(
                    token, widgetId, baseSequence, transactionKind,
                    recoveryOriginSequence);
            },
            [this](std::stop_token, std::wstring_view, WidgetLifecycleState) {
                std::scoped_lock lock(mutex);
                ++lifecycleCalls;
                return WidgetSessionOperationResult<bool>::Success(true);
            },
            [this](std::stop_token token, std::wstring_view widgetId,
                   const long long baseSequence,
                   const WidgetPresentationTransactionKind transactionKind,
                   const long long recoveryOriginSequence) {
                return GetPresentation(
                    token, widgetId, baseSequence, transactionKind,
                    recoveryOriginSequence);
            },
            [](const WidgetSnapshot& checkpoint,
               const widgetrail::WidgetPresentationUpdate& update,
               const std::wstring_view generation) {
                if (checkpoint.instanceId != update.widgetInstanceId ||
                    checkpoint.sequence != update.baseSequence ||
                    update.presentationGeneration != generation ||
                    update.sequence <= update.baseSequence) {
                    return WidgetSessionOperationResult<
                        widgetrail::WidgetPresentationMaterialization>::Failure(
                        WidgetSessionFailureStage::Protocol,
                        L"update base mismatch");
                }
                auto candidate = checkpoint;
                candidate.sequence = update.sequence;
                widgetrail::WidgetPresentationMaterialization materialization;
                materialization.snapshot = std::move(candidate);
                return WidgetSessionOperationResult<
                    widgetrail::WidgetPresentationMaterialization>::Success(
                        std::move(materialization));
            },
            [this](std::stop_token, std::wstring_view) {
                std::scoped_lock lock(mutex);
                ++restartCalls;
                return WidgetSessionOperationResult<bool>::Success(true);
            },
        };
    }

    WidgetSessionOperationResult<WidgetPresentationPublication> GetPresentation(
        const std::stop_token token,
        const std::wstring_view widgetId,
        const long long baseSequence,
        const WidgetPresentationTransactionKind transactionKind,
        const long long recoveryOriginSequence) {
        {
            std::unique_lock lock(mutex);
            presentationRequests.push_back(
                {baseSequence, transactionKind, recoveryOriginSequence});
            changed.notify_all();
            if (baseSequence == 0 && stallBaseZeroPresentation) {
                baseZeroPresentationStarted = true;
                changed.notify_all();
                changed.wait(lock, token, [&] {
                    return releaseBaseZeroPresentation;
                });
                if (token.stop_requested()) {
                    return WidgetSessionOperationResult<
                        WidgetPresentationPublication>::Failure(
                            WidgetSessionFailureStage::Snapshot,
                            L"checkpoint cancelled");
                }
            }
            if (failAllPresentationsAsStale ||
                (transactionKind == WidgetPresentationTransactionKind::IncrementalUpdate &&
                    stalePresentationFailuresRemaining > 0)) {
                if (stalePresentationFailuresRemaining > 0)
                    --stalePresentationFailuresRemaining;
                return WidgetSessionOperationResult<
                    WidgetPresentationPublication>::Failure(
                        WidgetSessionFailureStage::Protocol,
                        L"stale presentation base",
                        widgetrail::WidgetBridgeRequestFailureCategory::
                            StalePresentationBase);
            }
            if (transactionKind == WidgetPresentationTransactionKind::IncrementalUpdate &&
                publishUpdate) {
                publishUpdate = false;
                ++snapshotCalls;
                ++snapshotCallsByWidget[std::wstring(widgetId)];
                const auto found = snapshots.find(std::wstring(widgetId));
                if (found == snapshots.end()) {
                    return WidgetSessionOperationResult<
                        WidgetPresentationPublication>::Failure(
                            WidgetSessionFailureStage::Snapshot,
                            L"snapshot unavailable");
                }
                widgetrail::WidgetPresentationUpdate update;
                update.protocolVersion = 18;
                update.widgetInstanceId = found->second.instanceId;
                update.presentationGeneration = catalog.front().presentationGeneration;
                update.baseSequence = rejectPublishedUpdate
                    ? baseSequence - 1 : baseSequence;
                update.sequence = baseSequence + 1;
                WidgetPresentationPublication publication;
                publication.transactionKind = returnedTransactionKind.value_or(
                    transactionKind);
                publication.requestBaseSequence = baseSequence;
                publication.recoveryOriginSequence =
                    recoveryOriginSequence + returnedRecoveryOriginDelta;
                publication.update = std::move(update);
                return WidgetSessionOperationResult<
                    WidgetPresentationPublication>::Success(
                        std::move(publication));
            }
        }
        auto snapshot = GetSnapshot(token, widgetId);
        if (!snapshot.value) {
            return WidgetSessionOperationResult<
                WidgetPresentationPublication>::Failure(
                    snapshot.failureStage, std::move(snapshot.safeError));
        }
        WidgetPresentationPublication publication;
        publication.transactionKind = returnedTransactionKind.value_or(
            transactionKind);
        publication.requestBaseSequence = baseSequence;
        publication.recoveryOriginSequence =
            recoveryOriginSequence + returnedRecoveryOriginDelta;
        publication.checkpoint = std::move(*snapshot.value);
        return WidgetSessionOperationResult<WidgetPresentationPublication>::Success(
            std::move(publication));
    }

    WidgetSessionOperationResult<WidgetSnapshot> GetSnapshot(
        const std::stop_token token,
        const std::wstring_view widgetId) {
        std::unique_lock lock(mutex);
        ++snapshotCalls;
        ++snapshotCallsByWidget[std::wstring(widgetId)];
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
        if (snapshotFailuresRemaining > 0) {
            --snapshotFailuresRemaining;
            return WidgetSessionOperationResult<WidgetSnapshot>::Failure(
                WidgetSessionFailureStage::Snapshot, L"delayed snapshot failed");
        }
        if (widgetId == failingWidget) {
            return WidgetSessionOperationResult<WidgetSnapshot>::Failure(
                WidgetSessionFailureStage::Snapshot, L"delayed snapshot failed");
        }
        auto found = snapshots.find(std::wstring(widgetId));
        if (found == snapshots.end()) {
            return WidgetSessionOperationResult<WidgetSnapshot>::Failure(
                WidgetSessionFailureStage::Snapshot, L"snapshot unavailable");
        }
        auto result = found->second;
        if (snapshotAfterNextRead) {
            found->second = std::move(*snapshotAfterNextRead);
            snapshotAfterNextRead.reset();
        }
        if (protocolMismatch) result.instanceId = L"wrong.instance";
        return WidgetSessionOperationResult<WidgetSnapshot>::Success(std::move(result));
    }
};

struct BlockingPipeRead final {
    HANDLE readHandle{INVALID_HANDLE_VALUE};
    HANDLE writeHandle{INVALID_HANDLE_VALUE};
    HANDLE entered{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    HANDLE completed{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    std::atomic_bool succeeded{};
    std::atomic<DWORD> error{ERROR_SUCCESS};

    BlockingPipeRead() {
        assert(entered && completed);
        assert(CreatePipe(&readHandle, &writeHandle, nullptr, 0));
    }

    ~BlockingPipeRead() {
        if (readHandle != INVALID_HANDLE_VALUE) CloseHandle(readHandle);
        if (writeHandle != INVALID_HANDLE_VALUE) CloseHandle(writeHandle);
        if (entered) CloseHandle(entered);
        if (completed) CloseHandle(completed);
    }

    bool Read() {
        assert(SetEvent(entered));
        unsigned char value{};
        DWORD read{};
        const bool result = ReadFile(readHandle, &value, 1, &read, nullptr) && read == 1;
        succeeded.store(result);
        if (!result) error.store(GetLastError());
        assert(SetEvent(completed));
        return result;
    }

    void Release() const {
        const unsigned char value = 1;
        DWORD written{};
        assert(WriteFile(writeHandle, &value, 1, &written, nullptr) && written == 1);
    }
};

template <typename Predicate>
std::vector<widgetrail::WidgetSessionEvent> WaitEvents(
    WidgetSessionCoordinator& coordinator,
    Predicate predicate) {
    const auto deadline = std::chrono::steady_clock::now() + 2s;
    std::vector<widgetrail::WidgetSessionEvent> collected;
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
        bridge.snapshots[L"fast"] = Snapshot(L"fast.one", 2);
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

void SerializedTransportIsInterruptedOnlyAtShutdown() {
    for (const bool revoke : {false, true}) {
        FakeBridge bridge;
        bridge.catalog = {
            Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
        };
        bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 1);
        BlockingPipeRead pipe;
        auto operations = bridge.Operations();
        operations.getSnapshot = [&pipe](std::stop_token, std::wstring_view,
                                         const long long baseSequence,
                                         WidgetPresentationTransactionKind transactionKind,
                                         const long long recoveryOriginSequence) {
            if (!pipe.Read()) {
                return WidgetSessionOperationResult<
                    WidgetPresentationPublication>::Failure(
                        WidgetSessionFailureStage::Snapshot,
                        L"serialized read interrupted");
            }
            WidgetPresentationPublication publication;
            publication.transactionKind = transactionKind;
            publication.requestBaseSequence = baseSequence;
            publication.recoveryOriginSequence = recoveryOriginSequence;
            publication.checkpoint = Snapshot(
                L"alpha.one", baseSequence > 0 ? baseSequence + 1 : 2);
            return WidgetSessionOperationResult<
                WidgetPresentationPublication>::Success(std::move(publication));
        };
        WidgetSessionCoordinator coordinator(std::move(operations));
        assert(coordinator.EstablishCatalog());
        coordinator.SetLifecycleTargets({
            {L"alpha", WidgetLifecycleState::Visible},
        });
        (void)WaitEvents(coordinator, [](const auto& events) {
            return std::any_of(events.begin(), events.end(), [](const auto& event) {
                return event.widgetId == L"alpha" &&
                       event.kind == WidgetSessionEventKind::SnapshotAdmitted;
            });
        });
        assert(coordinator.RequestSnapshot(L"alpha"));
        assert(WaitForSingleObject(pipe.entered, 1'000) == WAIT_OBJECT_0);

        if (revoke) {
            coordinator.RemoveSnapshot(L"alpha");
        } else {
            {
                std::scoped_lock lock(bridge.mutex);
                bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 2);
            }
            coordinator.SetLifecycleTargets({
                {L"alpha", WidgetLifecycleState::Interactive},
            });
        }
        assert(WaitForSingleObject(pipe.completed, 100) == WAIT_TIMEOUT);
        pipe.Release();
        const auto events = WaitEvents(coordinator, [revoke](const auto& value) {
            return std::any_of(value.begin(), value.end(), [revoke](const auto& event) {
                return event.widgetId == L"alpha" &&
                    event.kind == (revoke
                        ? WidgetSessionEventKind::StaleCompletionRejected
                        : WidgetSessionEventKind::SnapshotAdmitted);
            });
        });
        assert(pipe.succeeded.load());
        assert(std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::StaleCompletionRejected;
        }));
        assert(std::none_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::Failed;
        }));
    }

    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 1);
    BlockingPipeRead pipe;
    auto operations = bridge.Operations();
    operations.getSnapshot = [&pipe](std::stop_token, std::wstring_view,
                                     const long long baseSequence,
                                     WidgetPresentationTransactionKind transactionKind,
                                     const long long recoveryOriginSequence) {
        if (!pipe.Read()) {
            return WidgetSessionOperationResult<
                WidgetPresentationPublication>::Failure(
                    WidgetSessionFailureStage::Snapshot,
                    L"serialized read interrupted");
        }
        WidgetPresentationPublication publication;
        publication.transactionKind = transactionKind;
        publication.requestBaseSequence = baseSequence;
        publication.recoveryOriginSequence = recoveryOriginSequence;
        publication.checkpoint = Snapshot(
            L"alpha.one", baseSequence > 0 ? baseSequence + 1 : 2);
        return WidgetSessionOperationResult<
            WidgetPresentationPublication>::Success(std::move(publication));
    };
    WidgetSessionCoordinator coordinator(std::move(operations));
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Visible},
    });
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.RequestSnapshot(L"alpha"));
    assert(WaitForSingleObject(pipe.entered, 1'000) == WAIT_OBJECT_0);
    auto shutdown = std::async(std::launch::async, [&] { coordinator.Shutdown(); });
    const auto shutdownStatus = shutdown.wait_for(2s);
    if (shutdownStatus != std::future_status::ready) pipe.Release();
    shutdown.wait();
    assert(shutdownStatus == std::future_status::ready);
    assert(WaitForSingleObject(pipe.completed, 1'000) == WAIT_OBJECT_0);
    assert(!pipe.succeeded.load());
    assert(pipe.error.load() == ERROR_OPERATION_ABORTED);
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

void RefreshDemandQueuesAgainstCurrentLifecycle() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 1, 592.0, 698.0);
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Visible},
    });
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.Lifecycle(L"alpha") == WidgetLifecycleState::Visible);
    assert(coordinator.RefreshState(L"alpha") == WidgetRefreshState::Current);

    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 2, 760.0, 385.0);
    bridge.stalledWidget = L"alpha";
    coordinator.MarkRefreshRequested(L"alpha");
    const auto retained = coordinator.Presentation(L"alpha");
    assert(retained.snapshot && retained.snapshot->sequence == 1 &&
           retained.snapshot->surface->preferredWidth == 592.0 &&
           retained.authority == WidgetPresentationAuthority::RefreshRetained);
    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Visible},
    });
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] {
            return bridge.snapshotCalls == 2;
        }));
    }
    assert(coordinator.RefreshState(L"alpha") ==
           WidgetRefreshState::RefreshInFlight);
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 1);
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.releaseStall = true;
    }
    bridge.changed.notify_all();
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.RefreshState(L"alpha") == WidgetRefreshState::Current);
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 2 &&
           coordinator.Snapshot(L"alpha")->surface->preferredWidth == 760.0);

    {
        std::scoped_lock lock(bridge.mutex);
        bridge.releaseStall = false;
        bridge.snapshots[L"alpha"] = Snapshot(
            L"alpha.one", 3, 760.0, 385.0);
        bridge.snapshotAfterNextRead = Snapshot(
            L"alpha.one", 4, 760.0, 385.0);
    }
    assert(coordinator.RequestSnapshot(L"alpha"));
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] {
            return bridge.snapshotCalls == 3;
        }));
    }
    assert(coordinator.RefreshState(L"alpha") ==
           WidgetRefreshState::RefreshInFlight);
    coordinator.MarkRefreshRequested(L"alpha");
    assert(coordinator.RequestSnapshot(L"alpha"));
    assert(coordinator.RefreshState(L"alpha") ==
           WidgetRefreshState::RefreshRequested);
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.releaseStall = true;
    }
    bridge.changed.notify_all();
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    const auto coalescedState = coordinator.RefreshState(L"alpha");
    assert(coalescedState == WidgetRefreshState::RefreshRequested ||
           coalescedState == WidgetRefreshState::RefreshInFlight ||
           coalescedState == WidgetRefreshState::Current);
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 3);
    if (coalescedState != WidgetRefreshState::Current) {
        (void)WaitEvents(coordinator, [&](const auto&) {
            return coordinator.RefreshState(L"alpha") ==
                   WidgetRefreshState::Current;
        });
    }
    assert(coordinator.RefreshState(L"alpha") == WidgetRefreshState::Current);

    bridge.stalledWidget.clear();
    bridge.failingWidget = L"alpha";
    coordinator.MarkRefreshRequested(L"alpha");
    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Visible},
    });
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::Failed;
        });
    });
    const auto failed = coordinator.Presentation(L"alpha");
    assert(failed.snapshot && failed.snapshot->sequence == 4 &&
           failed.authority == WidgetPresentationAuthority::FailureRetained &&
           coordinator.RefreshState(L"alpha") ==
               WidgetRefreshState::RefreshRequested);
}

void EightWidgetRetentionDoesNotWakeBackgroundWorkers() {
    FakeBridge bridge;
    constexpr std::array<std::pair<double, double>, 8> extents{{
        {592.0, 698.0}, {632.0, 878.0}, {760.0, 385.0}, {735.0, 847.0},
        {560.0, 645.0}, {880.0, 520.0}, {480.0, 340.0}, {978.0, 466.0},
    }};
    std::array<std::wstring, 8> ids;
    for (std::size_t index = 0; index < ids.size(); ++index) {
        ids[index] = L"fixture-" + std::to_wstring(index);
        const auto instance = ids[index] + L".one";
        WidgetDescriptor descriptor;
        descriptor.id = ids[index];
        descriptor.name = ids[index];
        descriptor.instanceId = instance;
        descriptor.runtimeGeneration = L"runtime-1";
        descriptor.presentationGeneration = L"view-1";
        bridge.catalog.push_back(std::move(descriptor));
        bridge.snapshots[ids[index]] = Snapshot(
            bridge.catalog.back().instanceId.c_str(), 1,
            extents[index].first, extents[index].second);
    }
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    for (const auto& id : ids) {
        assert(coordinator.RequestSnapshot(id));
        (void)WaitEvents(coordinator, [&](const auto& events) {
            return std::any_of(events.begin(), events.end(), [&](const auto& event) {
                return event.widgetId == id &&
                       event.kind == WidgetSessionEventKind::SnapshotAdmitted;
            });
        });
    }
    assert(coordinator.RetainedCheckpointCount() == ids.size());
    int callsBeforeInvalidation{};
    {
        std::scoped_lock lock(bridge.mutex);
        callsBeforeInvalidation = bridge.snapshotCalls;
    }

    coordinator.MarkAllRefreshRequested();
    assert(coordinator.RetainedCheckpointCount() == ids.size());
    for (std::size_t index = 0; index < ids.size(); ++index) {
        const auto presentation = coordinator.Presentation(ids[index]);
        assert(presentation.snapshot && presentation.snapshot->sequence == 1 &&
               presentation.snapshot->surface->preferredWidth ==
                   extents[index].first &&
               presentation.snapshot->surface->preferredHeight ==
                   extents[index].second &&
               presentation.authority ==
                   WidgetPresentationAuthority::RefreshRetained &&
               coordinator.RefreshState(ids[index]) ==
                   WidgetRefreshState::RefreshRequested);
    }
    {
        std::scoped_lock lock(bridge.mutex);
        assert(bridge.snapshotCalls == callsBeforeInvalidation);
    }

    const auto retainedLookupStarted = std::chrono::steady_clock::now();
    const auto* immediate = coordinator.Snapshot(ids[3]);
    const auto retainedLookupElapsed =
        std::chrono::steady_clock::now() - retainedLookupStarted;
    assert(immediate && immediate->surface->preferredWidth == extents[3].first &&
           retainedLookupElapsed < 20ms);

    bridge.snapshots[ids[3]] = Snapshot(
        bridge.catalog[3].instanceId.c_str(), 2,
        extents[3].first, extents[3].second);
    coordinator.SetLifecycleTargets({
        {ids[3], WidgetLifecycleState::Visible},
    });
    (void)WaitEvents(coordinator, [&](const auto& events) {
        return std::any_of(events.begin(), events.end(), [&](const auto& event) {
            return event.widgetId == ids[3] &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.Snapshot(ids[3]) &&
           coordinator.Snapshot(ids[3])->sequence == 2 &&
           coordinator.RefreshState(ids[3]) == WidgetRefreshState::Current);

    bridge.stalledWidget = ids[4];
    coordinator.SetLifecycleTargets({
        {ids[4], WidgetLifecycleState::Visible},
    });
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] {
            return bridge.snapshotCallsByWidget[ids[4]] == 2;
        }));
    }
    assert(coordinator.RefreshState(ids[4]) ==
           WidgetRefreshState::RefreshInFlight);
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.snapshots[ids[5]] = Snapshot(
            bridge.catalog[5].instanceId.c_str(), 2,
            extents[5].first, extents[5].second);
    }
    coordinator.SetLifecycleTargets({
        {ids[5], WidgetLifecycleState::Interactive},
    });
    const auto switched = WaitEvents(coordinator, [&](const auto& events) {
        return std::any_of(events.begin(), events.end(), [&](const auto& event) {
            return event.widgetId == ids[5] &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(std::any_of(switched.begin(), switched.end(), [&](const auto& event) {
        return event.widgetId == ids[4] &&
               event.kind == WidgetSessionEventKind::StaleCompletionRejected;
    }));
    assert(coordinator.Snapshot(ids[4]) &&
           coordinator.Snapshot(ids[4])->sequence == 1 &&
           coordinator.RefreshState(ids[4]) ==
               WidgetRefreshState::RefreshRequested &&
           coordinator.Presentation(ids[4]).authority ==
               WidgetPresentationAuthority::RefreshRetained);

    assert(coordinator.RequestRestart(ids[6]));
    assert(!coordinator.Snapshot(ids[6]) &&
           coordinator.Presentation(ids[6]).authority ==
               WidgetPresentationAuthority::Unavailable);
    (void)WaitEvents(coordinator, [&](const auto& events) {
        return std::any_of(events.begin(), events.end(), [&](const auto& event) {
            return event.widgetId == ids[6] &&
                   event.kind == WidgetSessionEventKind::Restarted;
        });
    });

    bridge.protocolMismatch = true;
    assert(coordinator.RequestSnapshot(ids[7]));
    (void)WaitEvents(coordinator, [&](const auto& events) {
        return std::any_of(events.begin(), events.end(), [&](const auto& event) {
            return event.widgetId == ids[7] &&
                   event.kind == WidgetSessionEventKind::Failed;
        });
    });
    assert(!coordinator.Snapshot(ids[7]) &&
           coordinator.Presentation(ids[7]).authority ==
               WidgetPresentationAuthority::Unavailable);
    assert(coordinator.RetainedCheckpointCount() == ids.size() - 2);
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

void VisibleTargetSupersedesBackgroundSnapshotAndRetainsCheckpoint() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 1, 592.0, 698.0);
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    assert(coordinator.RequestSnapshot(L"alpha"));
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 2, 760.0, 385.0);
    bridge.stalledWidget = L"alpha";
    bridge.ignoreCancellationWidget = L"alpha";
    bridge.snapshotFailuresRemaining = 1;
    assert(coordinator.RequestSnapshot(L"alpha"));
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] {
            return bridge.snapshotCalls == 2;
        }));
    }

    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Visible},
    });
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 1 &&
           coordinator.Snapshot(L"alpha")->surface->preferredWidth == 592.0);
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.releaseStall = true;
    }
    bridge.changed.notify_all();
    const auto events = WaitEvents(coordinator, [](const auto& value) {
        return std::any_of(value.begin(), value.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted &&
                   event.lifecycle == WidgetLifecycleState::Visible;
        });
    });
    assert(std::count_if(events.begin(), events.end(), [](const auto& event) {
        return event.widgetId == L"alpha" &&
               event.kind == WidgetSessionEventKind::StaleCompletionRejected &&
               event.requestKind == widgetrail::WidgetSessionRequestKind::Snapshot &&
               event.lifecycle == WidgetLifecycleState::Background &&
               event.completionDisposition ==
                   widgetrail::WidgetSessionCompletionDisposition::Cancelled;
    }) == 1);
    assert(std::none_of(events.begin(), events.end(), [](const auto& event) {
        return event.widgetId == L"alpha" &&
               event.kind == WidgetSessionEventKind::Failed;
    }));
    assert(coordinator.Lifecycle(L"alpha") == WidgetLifecycleState::Visible);
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 2 &&
           coordinator.Snapshot(L"alpha")->surface->preferredWidth == 760.0);
    assert(!coordinator.Failure(L"alpha"));
}

void WrongLifecycleFailureCannotReplaceNewerVisibleIntent() {
    FakeBridge bridge;
    bridge.catalog = {
        Descriptor(L"alpha", L"alpha.one", L"runtime-1", L"view-1"),
    };
    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 9, 760.0, 385.0);
    bridge.stalledWidget = L"alpha";
    bridge.snapshotFailuresRemaining = 1;
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Visible},
    });
    {
        std::unique_lock lock(bridge.mutex);
        assert(bridge.changed.wait_for(lock, 1s, [&] {
            return bridge.snapshotCalls == 1;
        }));
    }

    coordinator.SetLifecycleTargets({
        {L"alpha", WidgetLifecycleState::Interactive},
    });
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.releaseStall = true;
    }
    bridge.changed.notify_all();
    const auto events = WaitEvents(coordinator, [](const auto& value) {
        return std::any_of(value.begin(), value.end(), [](const auto& event) {
            return event.widgetId == L"alpha" &&
                   event.kind == WidgetSessionEventKind::SnapshotAdmitted &&
                   event.lifecycle == WidgetLifecycleState::Interactive;
        });
    });
    assert(std::count_if(events.begin(), events.end(), [](const auto& event) {
        return event.widgetId == L"alpha" &&
               event.kind == WidgetSessionEventKind::StaleCompletionRejected &&
               event.lifecycle == WidgetLifecycleState::Visible &&
               event.completionDisposition ==
                   widgetrail::WidgetSessionCompletionDisposition::WrongLifecycle;
    }) == 1);
    assert(std::none_of(events.begin(), events.end(), [](const auto& event) {
        return event.widgetId == L"alpha" &&
               event.kind == WidgetSessionEventKind::Failed;
    }));
    assert(coordinator.Lifecycle(L"alpha") == WidgetLifecycleState::Interactive);
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 9);
    assert(!coordinator.Failure(L"alpha"));
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
        widgetrail::WidgetSessionTraceStage::LifecycleDecision,
        widgetrail::WidgetSessionTraceAction::Queued,
        widgetrail::WidgetSessionTraceReason::None,
        widgetrail::WidgetSessionCompletionDisposition::None,
        7,
        3,
        widgetrail::WidgetSessionRequestKind::Establish,
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
    std::vector<widgetrail::WidgetSessionTraceEvent> trace;
    std::atomic<std::uint64_t> now{100};
    WidgetSessionCoordinator coordinator(
        bridge.Operations(), {},
        [&](const widgetrail::WidgetSessionTraceEvent& event) {
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
           widgetrail::WidgetSessionCompletionDisposition::Admitted);

    std::scoped_lock lock(traceMutex);
    assert(std::any_of(trace.begin(), trace.end(), [](const auto& event) {
        return event.correlationId == 40 &&
               event.stage == widgetrail::WidgetSessionTraceStage::LifecycleDecision &&
               event.action == widgetrail::WidgetSessionTraceAction::Skipped &&
               event.reason == widgetrail::WidgetSessionTraceReason::Deferred;
    }));
    const auto queued = std::find_if(trace.begin(), trace.end(), [](const auto& event) {
        return event.correlationId == 41 &&
               event.stage == widgetrail::WidgetSessionTraceStage::RequestQueued;
    });
    const auto started = std::find_if(trace.begin(), trace.end(), [](const auto& event) {
        return event.correlationId == 41 &&
               event.stage == widgetrail::WidgetSessionTraceStage::RequestStarted;
    });
    const auto completed = std::find_if(trace.begin(), trace.end(), [](const auto& event) {
        return event.correlationId == 41 &&
               event.stage == widgetrail::WidgetSessionTraceStage::RequestCompleted;
    });
    assert(queued != trace.end() && started != trace.end() && completed != trace.end());
    assert(queued->requestId == started->requestId &&
           started->requestId == completed->requestId);
    assert(queued->generation == completed->generation);
    assert(queued->queuedAt <= started->startedAt &&
           started->startedAt <= completed->completedAt);
    assert(completed->disposition ==
           widgetrail::WidgetSessionCompletionDisposition::Admitted);
}

void SelectedTraceRejectsPinnedAndSupersededAdmissions() {
    WidgetAdmissionTrace trace;
    const auto selected = trace.BeginSelection(
        L"selected", L"selected", true, false, 1000);
    trace.RecordSession({
        selected,
        widgetrail::WidgetSessionTraceStage::RequestCompleted,
        widgetrail::WidgetSessionTraceAction::None,
        widgetrail::WidgetSessionTraceReason::None,
        widgetrail::WidgetSessionCompletionDisposition::Failed,
        1, 1, widgetrail::WidgetSessionRequestKind::Establish,
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
        widgetrail::WidgetSessionTraceStage::RequestCompleted,
        widgetrail::WidgetSessionTraceAction::None,
        widgetrail::WidgetSessionTraceReason::NewerTarget,
        widgetrail::WidgetSessionCompletionDisposition::Cancelled,
        2, 2, widgetrail::WidgetSessionRequestKind::Snapshot,
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
    std::vector<widgetrail::WidgetSessionTraceEvent> trace;
    WidgetSessionCoordinator coordinator(
        bridge.Operations(), {},
        [&](const widgetrail::WidgetSessionTraceEvent& event) {
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

void AtomicUpdateAdmissionAndCheckpointFallback() {
    FakeBridge bridge;
    bridge.catalog = {Descriptor(
        L"alpha", L"alpha.one", L"runtime-1",
        L"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")};
    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 1);
    bridge.snapshots[L"alpha"].documentJson = L"retained";
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({{L"alpha", WidgetLifecycleState::Visible}});
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 1);

    bridge.publishUpdate = true;
    assert(coordinator.RequestSnapshot(L"alpha"));
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.Snapshot(L"alpha")->sequence == 2);
    assert((!bridge.presentationRequests.empty() &&
           bridge.presentationRequests.back() == FakeBridge::PresentationRequest{
               1, WidgetPresentationTransactionKind::IncrementalUpdate, 0}));

    bridge.snapshots[L"alpha"] = Snapshot(L"alpha.one", 3);
    bridge.snapshots[L"alpha"].documentJson = L"replacement";
    bridge.publishUpdate = true;
    bridge.rejectPublishedUpdate = true;
    assert(coordinator.RequestSnapshot(L"alpha"));
    const auto recovered = WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(std::none_of(recovered.begin(), recovered.end(), [](const auto& event) {
        return event.kind == WidgetSessionEventKind::Failed;
    }));
    assert(coordinator.Snapshot(L"alpha")->sequence == 3);
    assert(bridge.presentationRequests.size() >= 3);
    const auto requestCount = bridge.presentationRequests.size();
    assert((bridge.presentationRequests[requestCount - 2] ==
           FakeBridge::PresentationRequest{
               2, WidgetPresentationTransactionKind::IncrementalUpdate, 0}));
    assert((bridge.presentationRequests[requestCount - 1] ==
           FakeBridge::PresentationRequest{
               0, WidgetPresentationTransactionKind::OrdinaryCheckpoint, 0}));
}

enum class PublicationMatrixIntent {
    OrdinaryCheckpoint,
    OrdinaryIncremental,
    TypedRecoveryCheckpoint,
};

struct PublicationInvariantRow final {
    const char* name;
    long long priorHostSequence;
    long long bridgeRequestBase;
    WidgetLifecycleState lifecycleAuthority;
    PublicationMatrixIntent intent;
    std::uint64_t collectionGeneration;
    VirtualCollectionWindowChange marker;
    long long responseSequence;
    bool expectedAdmission;
    long long expectedCommittedSequence;
};

void PublicationInvariantMatrixOwnsRecoveryCheckpointAdmission() {
    const std::array rows{
        PublicationInvariantRow{
            "ordinary checkpoint", 0, 0, WidgetLifecycleState::Visible,
            PublicationMatrixIntent::OrdinaryCheckpoint, 7,
            VirtualCollectionWindowChange::Replace, 1, true, 1},
        PublicationInvariantRow{
            "ordinary exact-base increment", 10, 10, WidgetLifecycleState::Visible,
            PublicationMatrixIntent::OrdinaryIncremental, 7,
            VirtualCollectionWindowChange::Replace, 11, true, 11},
        PublicationInvariantRow{
            "typed stale-base recovery checkpoint", 10, 10,
            WidgetLifecycleState::Visible,
            PublicationMatrixIntent::TypedRecoveryCheckpoint, 7,
            VirtualCollectionWindowChange::Replace, 11, true, 11},
        PublicationInvariantRow{
            "directional recovery checkpoint", 10, 10,
            WidgetLifecycleState::Visible,
            PublicationMatrixIntent::TypedRecoveryCheckpoint, 8,
            VirtualCollectionWindowChange::Append, 11, false, 10},
        PublicationInvariantRow{
            "non-forward recovery checkpoint", 10, 10,
            WidgetLifecycleState::Visible,
            PublicationMatrixIntent::TypedRecoveryCheckpoint, 8,
            VirtualCollectionWindowChange::Replace, 10, false, 10},
    };

    for (const auto& row : rows) {
        FakeBridge bridge;
        bridge.catalog = {Descriptor(
            L"alpha", L"alpha.one", L"runtime-1", L"view-1")};
        WidgetSessionCoordinator coordinator(bridge.Operations());
        assert(coordinator.EstablishCatalog());

        if (row.priorHostSequence > 0) {
            auto prior = VirtualSnapshot(
                L"alpha.one", row.priorHostSequence, 7, 100);
            prior.documentJson = L"{}";
            bridge.snapshots[L"alpha"] = std::move(prior);
            coordinator.SetLifecycleTargets({{L"alpha", row.lifecycleAuthority}});
            (void)WaitEvents(coordinator, [](const auto& events) {
                return std::any_of(events.begin(), events.end(), [](const auto& event) {
                    return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
                });
            });
        }

        auto candidate = VirtualSnapshot(
            L"alpha.one", row.responseSequence, row.collectionGeneration,
            row.intent == PublicationMatrixIntent::OrdinaryIncremental ? 100 : 200,
            row.marker);
        candidate.documentJson = L"{}";
        bridge.snapshots[L"alpha"] = std::move(candidate);
        bridge.publishUpdate =
            row.intent == PublicationMatrixIntent::OrdinaryIncremental;
        bridge.stalePresentationFailuresRemaining =
            row.intent == PublicationMatrixIntent::TypedRecoveryCheckpoint ? 1 : 0;

        if (row.intent == PublicationMatrixIntent::OrdinaryCheckpoint) {
            coordinator.SetLifecycleTargets({{L"alpha", row.lifecycleAuthority}});
        } else {
            assert(coordinator.RequestSnapshot(L"alpha"));
        }
        const auto events = WaitEvents(coordinator, [&](const auto& available) {
            return std::any_of(available.begin(), available.end(), [&](const auto& event) {
                return row.expectedAdmission
                    ? event.kind == WidgetSessionEventKind::SnapshotAdmitted
                    : event.kind == WidgetSessionEventKind::Failed;
            });
        });
        assert(!events.empty());
        assert(coordinator.Snapshot(L"alpha"));
        assert(coordinator.Snapshot(L"alpha")->sequence ==
               row.expectedCommittedSequence);
        assert(coordinator.Lifecycle(L"alpha") == row.lifecycleAuthority);
        assert(!bridge.presentationRequests.empty());
        if (row.intent == PublicationMatrixIntent::TypedRecoveryCheckpoint) {
            assert(bridge.presentationRequests.size() == 3);
            assert((bridge.presentationRequests[1] ==
                   FakeBridge::PresentationRequest{
                       row.bridgeRequestBase,
                       WidgetPresentationTransactionKind::IncrementalUpdate, 0}));
            assert((bridge.presentationRequests[2] ==
                   FakeBridge::PresentationRequest{
                       0, WidgetPresentationTransactionKind::RecoveryCheckpoint,
                       row.bridgeRequestBase}));
        } else {
            assert((bridge.presentationRequests.back() == FakeBridge::PresentationRequest{
                row.bridgeRequestBase,
                row.intent == PublicationMatrixIntent::OrdinaryIncremental
                    ? WidgetPresentationTransactionKind::IncrementalUpdate
                    : WidgetPresentationTransactionKind::OrdinaryCheckpoint,
                0}));
        }
        if (row.expectedAdmission) {
            const auto& committed = *coordinator.Snapshot(L"alpha");
            assert(committed.root.collectionAnchorKey ==
                   (row.intent == PublicationMatrixIntent::OrdinaryIncremental
                       ? L"key.100" :
                       row.intent == PublicationMatrixIntent::OrdinaryCheckpoint
                           ? L"key.200" : L"key.200"));
            assert(committed.root.virtualCollectionWindow->requestGeneration ==
                   (row.intent == PublicationMatrixIntent::OrdinaryIncremental
                       ? 7 : row.collectionGeneration));
            assert(committed.root.virtualCollectionWindow->change ==
                   VirtualCollectionWindowChange::Replace);
        }
        (void)row.name;
    }
}

void RecoveryOriginAndRetryBoundsRemainExact() {
    FakeBridge bridge;
    bridge.catalog = {Descriptor(
        L"alpha", L"alpha.one", L"runtime-1", L"view-1")};
    auto initial = VirtualSnapshot(L"alpha.one", 30, 4, 100);
    initial.documentJson = L"{}";
    bridge.snapshots[L"alpha"] = std::move(initial);
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({{L"alpha", WidgetLifecycleState::Visible}});
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });

    bridge.stalePresentationFailuresRemaining = 1;
    bridge.stallBaseZeroPresentation = true;
    auto replacementSnapshot = VirtualSnapshot(L"alpha.one", 31, 5, 200);
    replacementSnapshot.documentJson = L"{}";
    bridge.snapshots[L"alpha"] = std::move(replacementSnapshot);
    assert(coordinator.RequestSnapshot(L"alpha"));
    const auto recoveryDeadline = std::chrono::steady_clock::now() + 2s;
    while (std::chrono::steady_clock::now() < recoveryDeadline) {
        (void)coordinator.TakeEvents();
        {
            std::scoped_lock lock(bridge.mutex);
            if (bridge.baseZeroPresentationStarted) break;
        }
        std::this_thread::yield();
    }
    {
        std::scoped_lock lock(bridge.mutex);
        assert(bridge.baseZeroPresentationStarted);
    }
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.stallBaseZeroPresentation = false;
    }
    const auto replacement = coordinator.EstablishPresentationForProbe(
        L"alpha", WidgetLifecycleState::Visible);
    assert(replacement && replacement->sequence == 31);
    {
        std::scoped_lock lock(bridge.mutex);
        bridge.releaseBaseZeroPresentation = true;
        bridge.changed.notify_all();
    }
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::Failed;
        });
    });
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 31);

    bridge.failAllPresentationsAsStale = true;
    const auto before = bridge.presentationRequests.size();
    assert(coordinator.RequestSnapshot(L"alpha"));
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::Failed;
        });
    });
    assert(bridge.presentationRequests.size() == before + 2);
    assert((bridge.presentationRequests[before] == FakeBridge::PresentationRequest{
        31, WidgetPresentationTransactionKind::IncrementalUpdate, 0}));
    assert((bridge.presentationRequests[before + 1] == FakeBridge::PresentationRequest{
        0, WidgetPresentationTransactionKind::RecoveryCheckpoint, 31}));
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 31);
}

void MismatchedTypedPublicationCannotMutateRetainedState() {
    for (const auto mismatch : {
            WidgetPresentationTransactionKind::OrdinaryCheckpoint,
            WidgetPresentationTransactionKind::RecoveryCheckpoint}) {
        FakeBridge bridge;
        bridge.catalog = {Descriptor(
            L"alpha", L"alpha.one", L"runtime-1", L"view-1")};
        auto retained = VirtualSnapshot(L"alpha.one", 20, 4, 100);
        retained.documentJson = L"{}";
        bridge.snapshots[L"alpha"] = retained;
        WidgetSessionCoordinator coordinator(bridge.Operations());
        assert(coordinator.EstablishCatalog());
        coordinator.SetLifecycleTargets({{L"alpha", WidgetLifecycleState::Visible}});
        (void)WaitEvents(coordinator, [](const auto& events) {
            return std::any_of(events.begin(), events.end(), [](const auto& event) {
                return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
            });
        });

        bridge.returnedTransactionKind = mismatch;
        auto candidate = VirtualSnapshot(L"alpha.one", 21, 5, 200);
        candidate.documentJson = L"{}";
        bridge.snapshots[L"alpha"] = std::move(candidate);
        assert(coordinator.RequestSnapshot(L"alpha"));
        const auto events = WaitEvents(coordinator, [](const auto& available) {
            return std::any_of(available.begin(), available.end(), [](const auto& event) {
                return event.kind == WidgetSessionEventKind::Failed;
            });
        });
        assert(!events.empty());
        assert(coordinator.Snapshot(L"alpha"));
        assert(coordinator.Snapshot(L"alpha")->sequence == 20);
        assert(coordinator.Lifecycle(L"alpha") == WidgetLifecycleState::Visible);
        assert(coordinator.RefreshState(L"alpha") ==
               WidgetRefreshState::RefreshRequested);

        bridge.returnedTransactionKind.reset();
        auto retry = VirtualSnapshot(L"alpha.one", 22, 6, 300);
        retry.documentJson = L"{}";
        bridge.snapshots[L"alpha"] = std::move(retry);
        assert(coordinator.RequestSnapshot(L"alpha"));
        (void)WaitEvents(coordinator, [](const auto& available) {
            return std::any_of(available.begin(), available.end(), [](const auto& event) {
                return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
            });
        });
        assert(coordinator.Snapshot(L"alpha") &&
               coordinator.Snapshot(L"alpha")->sequence == 22);
        assert(coordinator.Lifecycle(L"alpha") == WidgetLifecycleState::Visible);
        assert(coordinator.RefreshState(L"alpha") == WidgetRefreshState::Current);
    }
}

void VirtualWindowAdmissionRejectsStaleAndRetainsCheckpoint() {
    FakeBridge bridge;
    bridge.catalog = {Descriptor(
        L"alpha", L"alpha.one", L"runtime-1", L"view-1")};
    bridge.snapshots[L"alpha"] = VirtualSnapshot(L"alpha.one", 1, 2, 100);
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({{L"alpha", WidgetLifecycleState::Visible}});
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 1);

    bridge.snapshots[L"alpha"] = VirtualSnapshot(L"alpha.one", 2, 1, 98);
    assert(coordinator.RequestSnapshot(L"alpha"));
    const auto stale = WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::Failed;
        });
    });
    assert(std::any_of(stale.begin(), stale.end(), [](const auto& event) {
        return event.failure.stage == WidgetSessionFailureStage::Protocol;
    }));
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 1 &&
           coordinator.Snapshot(L"alpha")->root.virtualCollectionWindow->
               requestGeneration == 2);

    bridge.snapshots[L"alpha"] = VirtualSnapshot(
        L"alpha.one", 3, 3, 101, VirtualCollectionWindowChange::Append);
    assert(coordinator.RequestSnapshot(L"alpha", true));
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->sequence == 3 &&
           coordinator.Snapshot(L"alpha")->root.virtualCollectionWindow->
               requestGeneration == 3);
}

void VirtualWindowAdmissionEnforcesDirectionalAuthority() {
    FakeBridge bridge;
    bridge.catalog = {Descriptor(
        L"alpha", L"alpha.one", L"runtime-1", L"view-1")};
    bridge.snapshots[L"alpha"] = VirtualSnapshot(L"alpha.one", 1, 1, 100);
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({{L"alpha", WidgetLifecycleState::Visible}});
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    const auto rejects = [&](WidgetSnapshot candidate) {
        bridge.snapshots[L"alpha"] = std::move(candidate);
        assert(coordinator.RequestSnapshot(L"alpha", true));
        const auto events = WaitEvents(coordinator, [](const auto& available) {
            return std::any_of(available.begin(), available.end(), [](const auto& event) {
                return event.kind == WidgetSessionEventKind::Failed;
            });
        });
        assert(std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.failure.stage == WidgetSessionFailureStage::Protocol;
        }));
    };
    const auto admits = [&](WidgetSnapshot candidate) {
        const auto sequence = candidate.sequence;
        bridge.snapshots[L"alpha"] = std::move(candidate);
        assert(coordinator.RequestSnapshot(L"alpha", true));
        (void)WaitEvents(coordinator, [](const auto& available) {
            return std::any_of(available.begin(), available.end(), [](const auto& event) {
                return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
            });
        });
        assert(coordinator.Snapshot(L"alpha") &&
               coordinator.Snapshot(L"alpha")->sequence == sequence);
    };

    rejects(VirtualSnapshot(
        L"alpha.one", 2, 2, 99, VirtualCollectionWindowChange::Append));
    assert(coordinator.Snapshot(L"alpha")->sequence == 1);

    auto conflictingOverlap = VirtualSnapshot(
        L"alpha.one", 3, 2, 101, VirtualCollectionWindowChange::Append);
    conflictingOverlap.root.children[0].collectionItemKey = L"key.changed";
    conflictingOverlap.root.collectionAnchorKey = L"key.102";
    rejects(std::move(conflictingOverlap));
    assert(coordinator.Snapshot(L"alpha")->sequence == 1);

    auto incompatibleTotal = VirtualSnapshot(
        L"alpha.one", 4, 2, 101, VirtualCollectionWindowChange::Append);
    incompatibleTotal.root.virtualCollectionWindow->totalItemCount = 10'001;
    rejects(std::move(incompatibleTotal));
    assert(coordinator.Snapshot(L"alpha")->sequence == 1);

    admits(VirtualSnapshot(
        L"alpha.one", 5, 2, 101, VirtualCollectionWindowChange::Append));
    rejects(VirtualSnapshot(
        L"alpha.one", 6, 3, 102, VirtualCollectionWindowChange::Prepend));
    assert(coordinator.Snapshot(L"alpha")->sequence == 5);
    admits(VirtualSnapshot(
        L"alpha.one", 7, 3, 100, VirtualCollectionWindowChange::Prepend));
}

void VirtualWindowReplacementOwnsMutationAndUnknownPosition() {
    FakeBridge bridge;
    bridge.catalog = {Descriptor(
        L"alpha", L"alpha.one", L"runtime-1", L"view-1")};
    bridge.snapshots[L"alpha"] = VirtualSnapshot(L"alpha.one", 1, 1, 100);
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({{L"alpha", WidgetLifecycleState::Visible}});
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    const auto admits = [&](WidgetSnapshot candidate) {
        const auto sequence = candidate.sequence;
        bridge.snapshots[L"alpha"] = std::move(candidate);
        assert(coordinator.RequestSnapshot(L"alpha", true));
        (void)WaitEvents(coordinator, [](const auto& events) {
            return std::any_of(events.begin(), events.end(), [](const auto& event) {
                return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
            });
        });
        assert(coordinator.Snapshot(L"alpha") &&
               coordinator.Snapshot(L"alpha")->sequence == sequence);
    };

    auto inserted = VirtualSnapshot(L"alpha.one", 2, 2, 100);
    inserted.root.children[0].collectionItemKey = L"key.inserted";
    inserted.root.collectionAnchorKey = L"key.inserted";
    admits(std::move(inserted));
    auto removed = VirtualSnapshot(L"alpha.one", 3, 3, 100);
    removed.root.children[1].collectionItemKey = L"key.102";
    admits(std::move(removed));
    auto moved = VirtualSnapshot(L"alpha.one", 4, 4, 100);
    std::swap(moved.root.children[0].collectionItemKey,
              moved.root.children[1].collectionItemKey);
    moved.root.collectionAnchorKey = moved.root.children[0].collectionItemKey;
    admits(std::move(moved));

    auto unknown = VirtualSnapshot(L"alpha.one", 5, 5, 0);
    unknown.root.virtualCollectionWindow->firstItemIndex.reset();
    unknown.root.virtualCollectionWindow->totalItemCount.reset();
    unknown.root.virtualCollectionWindow->change =
        VirtualCollectionWindowChange::Append;
    bridge.snapshots[L"alpha"] = unknown;
    assert(coordinator.RequestSnapshot(L"alpha", true));
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::Failed;
        });
    });
    assert(coordinator.Snapshot(L"alpha")->sequence == 4);
    unknown.root.virtualCollectionWindow->change =
        VirtualCollectionWindowChange::Replace;
    admits(std::move(unknown));
}

void VirtualWindowFreshSessionRequiresReplacement() {
    FakeBridge bridge;
    bridge.catalog = {Descriptor(
        L"alpha", L"alpha.one", L"runtime-1", L"view-1")};
    bridge.snapshots[L"alpha"] = VirtualSnapshot(
        L"alpha.one", 1, 42, 100, VirtualCollectionWindowChange::Append);
    WidgetSessionCoordinator coordinator(bridge.Operations());
    assert(coordinator.EstablishCatalog());
    coordinator.SetLifecycleTargets({{L"alpha", WidgetLifecycleState::Visible}});
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::Failed;
        });
    });
    assert(!coordinator.Snapshot(L"alpha"));

    bridge.snapshots[L"alpha"] = VirtualSnapshot(L"alpha.one", 2, 42, 100);
    assert(coordinator.RequestSnapshot(L"alpha", true));
    (void)WaitEvents(coordinator, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(coordinator.Snapshot(L"alpha") &&
           coordinator.Snapshot(L"alpha")->root.virtualCollectionWindow->
               requestGeneration == 42);

    FakeBridge restartedBridge;
    restartedBridge.catalog = bridge.catalog;
    restartedBridge.snapshots[L"alpha"] = VirtualSnapshot(L"alpha.one", 1, 43, 100);
    WidgetSessionCoordinator restarted(restartedBridge.Operations());
    assert(restarted.EstablishCatalog());
    restarted.SetLifecycleTargets({{L"alpha", WidgetLifecycleState::Visible}});
    (void)WaitEvents(restarted, [](const auto& events) {
        return std::any_of(events.begin(), events.end(), [](const auto& event) {
            return event.kind == WidgetSessionEventKind::SnapshotAdmitted;
        });
    });
    assert(restarted.Snapshot(L"alpha") &&
           restarted.Snapshot(L"alpha")->root.virtualCollectionWindow->
               requestGeneration == 43);
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
    SerializedTransportIsInterruptedOnlyAtShutdown();
    SelectionRevokesNeverCompletingRequest();
    DelayedSuccessRetainsLastGoodSnapshot();
    RefreshDemandQueuesAgainstCurrentLifecycle();
    EightWidgetRetentionDoesNotWakeBackgroundWorkers();
    HideAndWorkerExitRevokePendingSnapshots();
    CancellationIgnoringLateResultsAreStale();
    VisibleTargetSupersedesBackgroundSnapshotAndRetainsCheckpoint();
    WrongLifecycleFailureCannotReplaceNewerVisibleIntent();
    CorrelatedAdmissionTraceIsBoundedAndSanitized();
    CoordinatorEmitsCorrelatedLifecycleAndRequestStages();
    SelectedTraceRejectsPinnedAndSupersededAdmissions();
    SelectedLifecycleCorrelationExcludesPinnedTarget();
    AtomicUpdateAdmissionAndCheckpointFallback();
    PublicationInvariantMatrixOwnsRecoveryCheckpointAdmission();
    RecoveryOriginAndRetryBoundsRemainExact();
    MismatchedTypedPublicationCannotMutateRetainedState();
    VirtualWindowAdmissionRejectsStaleAndRetainsCheckpoint();
    VirtualWindowAdmissionEnforcesDirectionalAuthority();
    VirtualWindowReplacementOwnsMutationAndUnknownPosition();
    VirtualWindowFreshSessionRequiresReplacement();
    std::cout << "WidgetSessionCoordinatorTests passed (28 scenarios)\n";
}
