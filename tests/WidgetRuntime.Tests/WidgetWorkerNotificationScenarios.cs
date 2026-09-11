using System.Collections.Concurrent;
using System.IO.Pipes;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class WidgetWorkerNotificationScenarios
{
    internal static async Task LifecycleFirstCursorActionReachesPipe()
    {
        var pipeName = $"widgetrail-runtime-lifecycle-cursor-{Guid.NewGuid():N}";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using var host = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var widget = new DiagnosticCursorNotificationWidget(loadOnActivation: true);
        var server = new WidgetWorkerServer(
            widget,
            "notification.lifecycle.cursor",
            pipeName,
            WidgetRuntimeProtocol.DefaultMaximumMessageBytes,
            capabilityClient: null,
            sessionNonce: new string('c', 64)).RunAsync(deadline.Token);

        await host.WaitForConnectionAsync(deadline.Token);
        var channel = new LengthPrefixedJsonChannel(
            host, WidgetRuntimeProtocol.DefaultMaximumMessageBytes);
        var hello = await channel.ReadAsync(deadline.Token);
        NotificationAssert.Equal(MessageTypes.Hello, hello.Type);
        await channel.WriteAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.HelloAccepted,
            Payload = RuntimeJson.ToElement(new { }),
        }, deadline.Token);

        var lifecycleEvents = new Queue<RuntimeEnvelope>();
        await channel.WriteAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.SetWidgetLifecycle,
            RequestId = 1,
            Payload = RuntimeJson.ToElement(
                new WidgetLifecyclePayload(WidgetLifecycleState.Visible)),
        }, deadline.Token);
        var lifecycle = await ReadResponseWithEventsAsync(
            channel, requestId: 1, lifecycleEvents, deadline.Token);
        NotificationAssert.Equal(MessageTypes.Acknowledged, lifecycle.Type);
        await widget.InitialLoadCompleted.WaitAsync(deadline.Token);
        var readyRevision = widget.Revision;
        var initialRevision = await ReadThroughInvalidationAsync(
            channel, lifecycleEvents, readyRevision, deadline.Token);

        var renderEvents = new Queue<RuntimeEnvelope>();
        await channel.WriteAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.Render,
            RequestId = 2,
            Payload = RuntimeJson.ToElement(new RenderPayload
            {
                UpdateCapabilities = PresentationUpdateCapabilities.None,
                BaseSequence = 0,
                RequireCheckpoint = true,
            }),
        }, deadline.Token);
        var render = await ReadResponseWithEventsAsync(
            channel, requestId: 2, renderEvents, deadline.Token);
        NotificationAssert.Equal(MessageTypes.Snapshot, render.Type);
        NotificationAssert.Equal(0, renderEvents.Count);
        var first = SnapshotJson.Deserialize(
            System.Text.Encoding.UTF8.GetBytes(render.Payload.GetRawText()));
        NotificationAssert.Equal(4, first.Root.Children.Count);
        NotificationAssert.Equal(1L, first.Root.CollectionGeneration);
        NotificationAssert.True(first.Root.VirtualCollectionWindow is
        {
            RequestGeneration: 1,
            FirstItemIndex: 0,
            TotalItemCount: 12,
            HasBefore: false,
            HasAfter: true,
            Change: VirtualCollectionWindowChange.Replace,
        }, "Lifecycle-first cursor did not render its exact Ready generation-1 window.");
        var pageAction = first.Root.ScrollNearEndActionId
            ?? throw new InvalidOperationException(
                "Lifecycle-first cursor omitted its generated near-end action.");

        var postActionEvents = new Queue<RuntimeEnvelope>();
        await channel.WriteAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.Action,
            RequestId = 3,
            Payload = RuntimeJson.ToElement(new WidgetActionEvent(
                pageAction, first.Root.Id)),
        }, deadline.Token);
        var action = await ReadResponseWithEventsAsync(
            channel, requestId: 3, postActionEvents, deadline.Token);
        NotificationAssert.Equal(MessageTypes.Acknowledged, action.Type);
        NotificationAssert.Equal(
            WidgetOperationAdmission.Enqueued,
            RuntimeJson.FromElement<ActionAdmissionPayload>(action.Payload).Admission);

        var completion = await widget.CursorForwardCompleted.WaitAsync(deadline.Token);
        NotificationAssert.Equal(pageAction, completion.Action.ActionId);
        NotificationAssert.Equal(first.Root.Id, completion.Action.SourceElementId);
        NotificationAssert.True(completion.Handled,
            "The generated pagination action was not handled by its cursor resource.");
        NotificationAssert.Equal(
            WidgetOperationAdmission.Started, completion.Admission);
        NotificationAssert.Equal(WidgetOperationStatus.Succeeded, completion.Status);
        NotificationAssert.Equal<string?>(null, completion.ExceptionType);

        RuntimeEnvelope firstEvent;
        try
        {
            firstEvent = postActionEvents.Count != 0
                ? postActionEvents.Dequeue()
                : await channel.ReadAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(
                "No event followed successful lifecycle-first cursor completion " +
                "before the unchanged worker read deadline.");
        }
        NotificationAssert.Equal(0L, firstEvent.RequestId);
        if (firstEvent.Type == MessageTypes.Invalidated)
        {
            var revision = RuntimeJson.FromElement<InvalidationPayload>(
                firstEvent.Payload).Revision;
            NotificationAssert.True(revision > initialRevision,
                "The first post-action worker invalidation was not forward.");
        }
        else if (firstEvent.Type == MessageTypes.ControllerActionFailed)
        {
            var failure = RuntimeJson.FromElement<ControllerActionFailurePayload>(
                firstEvent.Payload);
            NotificationAssert.Equal(pageAction, failure.ActionId);
            NotificationAssert.Equal(first.Root.Id, failure.SourceElementId);
            throw new InvalidOperationException(
                $"Lifecycle-first cursor action failed action={failure.ActionId} " +
                $"source={failure.SourceElementId} message={failure.Message}");
        }
        else
        {
            throw new InvalidOperationException(
                $"Lifecycle-first cursor received unexpected first event '{firstEvent.Type}'.");
        }

        await channel.WriteAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.Stop,
            RequestId = 4,
            Payload = RuntimeJson.ToElement(new { }),
        }, deadline.Token);
        _ = await ReadResponseWithEventsAsync(
            channel, requestId: 4, new Queue<RuntimeEnvelope>(), deadline.Token);
        await server.WaitAsync(deadline.Token);
    }

    internal static async Task WorkerActionCursorCompletionReachesPipe()
    {
        var pipeName = $"widgetrail-runtime-cursor-{Guid.NewGuid():N}";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using var host = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var widget = new DiagnosticCursorNotificationWidget();
        var server = new WidgetWorkerServer(
            widget,
            "notification.cursor",
            pipeName,
            WidgetRuntimeProtocol.DefaultMaximumMessageBytes,
            capabilityClient: null,
            sessionNonce: new string('b', 64)).RunAsync(deadline.Token);

        await host.WaitForConnectionAsync(deadline.Token);
        var channel = new LengthPrefixedJsonChannel(
            host, WidgetRuntimeProtocol.DefaultMaximumMessageBytes);
        var hello = await channel.ReadAsync(deadline.Token);
        NotificationAssert.Equal(MessageTypes.Hello, hello.Type);
        await channel.WriteAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.HelloAccepted,
            Payload = RuntimeJson.ToElement(new { }),
        }, deadline.Token);

        await channel.WriteAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.SetWidgetLifecycle,
            RequestId = 1,
            Payload = RuntimeJson.ToElement(
                new WidgetLifecyclePayload(WidgetLifecycleState.Visible)),
        }, deadline.Token);
        var lifecycleNotifications = new List<long>();
        var lifecycle = await ReadResponseAsync(
            channel, requestId: 1, lifecycleNotifications, deadline.Token);
        NotificationAssert.Equal(MessageTypes.Acknowledged, lifecycle.Type);
        var baselineRevision = widget.Revision;

        await channel.WriteAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.Action,
            RequestId = 2,
            Payload = RuntimeJson.ToElement(new WidgetActionEvent(
                DiagnosticCursorNotificationWidget.CursorForwardAction,
                "diagnostic.action")),
        }, deadline.Token);
        var actionNotifications = new List<long>();
        var action = await ReadResponseAsync(
            channel, requestId: 2, actionNotifications, deadline.Token);
        NotificationAssert.Equal(MessageTypes.Acknowledged, action.Type);
        NotificationAssert.Equal(
            WidgetOperationAdmission.Enqueued,
            RuntimeJson.FromElement<ActionAdmissionPayload>(action.Payload).Admission);
        _ = await widget.CursorForwardCompleted.WaitAsync(deadline.Token);

        while (!actionNotifications.Any(revision => revision > baselineRevision))
        {
            var notification = await channel.ReadAsync(deadline.Token);
            NotificationAssert.Equal(0L, notification.RequestId);
            NotificationAssert.Equal(MessageTypes.Invalidated, notification.Type);
            actionNotifications.Add(
                RuntimeJson.FromElement<InvalidationPayload>(notification.Payload).Revision);
        }
        NotificationAssert.True(
            actionNotifications.Any(revision => revision > baselineRevision),
            "The completed cursor action did not publish a forward invalidation.");

        await channel.WriteAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.Stop,
            RequestId = 3,
            Payload = RuntimeJson.ToElement(new { }),
        }, deadline.Token);
        _ = await ReadResponseAsync(
            channel, requestId: 3, new List<long>(), deadline.Token);
        await server.WaitAsync(deadline.Token);
    }

    internal static async Task LaneCoalescesOrdersAndDrainsBoundedly()
    {
        await CoalescingAndMixedOrderingAreExact();
        await FullAdmissionClosesExactly();
        await GracefulCloseAndCancellationAreBounded();
    }

    internal static async Task TransportFailureHasOneRequestLoopOutcome()
    {
        var pipeName = $"widgetrail-runtime-notification-{Guid.NewGuid():N}";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using var host = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var widget = new DisconnectNotificationWidget();
        var server = new WidgetWorkerServer(
            widget,
            "notification.transport",
            pipeName,
            WidgetRuntimeProtocol.DefaultMaximumMessageBytes,
            capabilityClient: null,
            sessionNonce: new string('a', 64)).RunAsync(deadline.Token);

        await host.WaitForConnectionAsync(deadline.Token);
        var channel = new LengthPrefixedJsonChannel(
            host, WidgetRuntimeProtocol.DefaultMaximumMessageBytes);
        var hello = await channel.ReadAsync(deadline.Token);
        NotificationAssert.Equal(MessageTypes.Hello, hello.Type);
        await channel.WriteAsync(new RuntimeEnvelope
        {
            Type = MessageTypes.HelloAccepted,
            Payload = RuntimeJson.ToElement(new { }),
        }, deadline.Token);
        await widget.Initialized.WaitAsync(deadline.Token);

        host.Dispose();
        await widget.DestroyStarted.WaitAsync(deadline.Token);
        widget.RaiseInvalidation();
        widget.ReleaseDestroy();

        var first = await NotificationAssert.CaptureAsync(server);
        NotificationAssert.True(first is IOException,
            $"The request-loop owner reported '{first.GetType().Name}' instead of one transport failure.");
        var second = await NotificationAssert.CaptureAsync(server);
        NotificationAssert.True(ReferenceEquals(first, second),
            "Repeated observation did not retain the request loop's single terminal outcome.");
        NotificationAssert.Equal(1, widget.InitializeCount);
        NotificationAssert.Equal(1, widget.DestroyCount);
        widget.RaiseInvalidation();
    }

    private static async Task CoalescingAndMixedOrderingAreExact()
    {
        var sender = new ControlledNotificationSender();
        var lane = new WidgetWorkerNotificationLane(sender.SendAsync, CancellationToken.None);
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.Enqueued,
            lane.EnqueueInvalidation(1));
        await sender.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(2));

        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.Enqueued,
            lane.EnqueueActionFailure(Failure("failure.one")));
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.Enqueued,
            lane.EnqueueActionTerminal(new ActionTerminalPayload(
                1, WidgetActionExecutionOutcome.Succeeded)));
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.Enqueued,
            lane.EnqueueInvalidation(2));
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.Enqueued,
            lane.EnqueueActionFailure(Failure("failure.two")));
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.Coalesced,
            lane.EnqueueInvalidation(3));
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.RejectedStale,
            lane.EnqueueInvalidation(2));

        sender.Release();
        lane.Close();
        await lane.DrainAsync(TimeSpan.FromSeconds(2));
        NotificationAssert.SequenceEqual(
            new[]
            {
                "invalidated:1",
                "failure:failure.one",
                "terminal:1:Succeeded",
                "failure:failure.two",
                "invalidated:3",
            },
            sender.Messages);
        NotificationAssert.Equal(1, sender.MaximumConcurrentSends);
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.RejectedClosed,
            lane.EnqueueInvalidation(4));
    }

    private static async Task FullAdmissionClosesExactly()
    {
        var sender = new ControlledNotificationSender();
        var lane = new WidgetWorkerNotificationLane(sender.SendAsync, CancellationToken.None);
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.Enqueued,
            lane.EnqueueInvalidation(1));
        await sender.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(2));
        for (var index = 0; index < 8; index++)
        {
            NotificationAssert.Equal(
                WidgetWorkerNotificationLane.Admission.Enqueued,
                lane.EnqueueActionFailure(Failure($"failure.{index}")));
        }
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.RejectedFull,
            lane.EnqueueActionFailure(Failure("failure.overflow")));
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.RejectedClosed,
            lane.EnqueueActionFailure(Failure("failure.closed")));
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.RejectedClosed,
            lane.EnqueueInvalidation(2));

        sender.Release();
        var failure = await NotificationAssert.ThrowsAsync<IOException>(
            () => lane.DrainAsync(TimeSpan.FromSeconds(2)));
        NotificationAssert.Equal(
            "The bounded worker action-failure notification queue is full.",
            failure.Message);
        NotificationAssert.Equal(1, sender.MaximumConcurrentSends);
    }

    private static async Task GracefulCloseAndCancellationAreBounded()
    {
        var gracefulSender = new ControlledNotificationSender();
        var graceful = new WidgetWorkerNotificationLane(
            gracefulSender.SendAsync, CancellationToken.None);
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.Enqueued,
            graceful.EnqueueInvalidation(7));
        await gracefulSender.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(2));
        var drain = graceful.DrainAsync(TimeSpan.FromSeconds(2));
        NotificationAssert.True(!drain.IsCompleted,
            "Graceful close returned before its admitted notification drained.");
        gracefulSender.Release();
        await drain;
        NotificationAssert.SequenceEqual(new[] { "invalidated:7" }, gracefulSender.Messages);

        using var cancellation = new CancellationTokenSource();
        var canceledSender = new ControlledNotificationSender();
        var canceled = new WidgetWorkerNotificationLane(
            canceledSender.SendAsync, cancellation.Token);
        NotificationAssert.Equal(
            WidgetWorkerNotificationLane.Admission.Enqueued,
            canceled.EnqueueInvalidation(9));
        await canceledSender.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        _ = await NotificationAssert.ThrowsAsync<OperationCanceledException>(
            () => canceled.DrainAsync(TimeSpan.FromSeconds(2)));
        NotificationAssert.Equal(1, canceledSender.MaximumConcurrentSends);
    }

    private static ControllerActionFailurePayload Failure(string actionId) =>
        new(actionId, "source", "Action failed.");

    private static async Task<RuntimeEnvelope> ReadResponseAsync(
        LengthPrefixedJsonChannel channel,
        long requestId,
        ICollection<long> invalidations,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var envelope = await channel.ReadAsync(cancellationToken);
            if (envelope.RequestId == requestId) return envelope;
            NotificationAssert.Equal(0L, envelope.RequestId);
            NotificationAssert.Equal(MessageTypes.Invalidated, envelope.Type);
            invalidations.Add(
                RuntimeJson.FromElement<InvalidationPayload>(envelope.Payload).Revision);
        }
    }

    private static async Task<RuntimeEnvelope> ReadResponseWithEventsAsync(
        LengthPrefixedJsonChannel channel,
        long requestId,
        Queue<RuntimeEnvelope> events,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var envelope = await channel.ReadAsync(cancellationToken);
            if (envelope.RequestId == requestId) return envelope;
            NotificationAssert.Equal(0L, envelope.RequestId);
            events.Enqueue(envelope);
        }
    }

    private static async Task<long> ReadThroughInvalidationAsync(
        LengthPrefixedJsonChannel channel,
        Queue<RuntimeEnvelope> events,
        long targetRevision,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var envelope = events.Count != 0
                ? events.Dequeue()
                : await channel.ReadAsync(cancellationToken);
            NotificationAssert.Equal(0L, envelope.RequestId);
            NotificationAssert.Equal(MessageTypes.Invalidated, envelope.Type);
            var revision = RuntimeJson.FromElement<InvalidationPayload>(
                envelope.Payload).Revision;
            NotificationAssert.True(revision <= targetRevision,
                "Initial cursor invalidation advanced beyond its Ready revision.");
            if (revision == targetRevision) return revision;
        }
    }

    private sealed class ControlledNotificationSender
    {
        private readonly TaskCompletionSource _firstSendStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<string> _messages = new();
        private int _activeSends;
        private int _maximumConcurrentSends;

        internal Task FirstSendStarted => _firstSendStarted.Task;
        internal IReadOnlyList<string> Messages => _messages.ToArray();
        internal int MaximumConcurrentSends => Volatile.Read(ref _maximumConcurrentSends);

        internal async Task SendAsync(
            RuntimeEnvelope envelope,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeSends);
            UpdateMaximum(active);
            try
            {
                _messages.Enqueue(Describe(envelope));
                _firstSendStarted.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeSends);
            }
        }

        internal void Release() => _release.TrySetResult();

        private void UpdateMaximum(int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumConcurrentSends);
                if (current >= candidate) return;
                if (Interlocked.CompareExchange(
                        ref _maximumConcurrentSends, candidate, current) == current)
                    return;
            }
        }

        private static string Describe(RuntimeEnvelope envelope) => envelope.Type switch
        {
            MessageTypes.Invalidated =>
                $"invalidated:{RuntimeJson.FromElement<InvalidationPayload>(envelope.Payload).Revision}",
            MessageTypes.ControllerActionFailed =>
                $"failure:{RuntimeJson.FromElement<ControllerActionFailurePayload>(envelope.Payload).ActionId}",
            MessageTypes.ActionTerminal =>
                $"terminal:{RuntimeJson.FromElement<ActionTerminalPayload>(envelope.Payload).ExecutionId}:" +
                RuntimeJson.FromElement<ActionTerminalPayload>(envelope.Payload).Outcome,
            _ => throw new InvalidOperationException(
                $"Unexpected notification type '{envelope.Type}'."),
        };
    }

    private sealed class DisconnectNotificationWidget : Widget
    {
        private readonly TaskCompletionSource _initialized = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _destroyStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseDestroy = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _initializeCount;
        private int _destroyCount;

        internal Task Initialized => _initialized.Task;
        internal Task DestroyStarted => _destroyStarted.Task;
        internal int InitializeCount => Volatile.Read(ref _initializeCount);
        internal int DestroyCount => Volatile.Read(ref _destroyCount);

        protected override ValueTask OnCreatedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _initializeCount);
            _initialized.TrySetResult();
            return ValueTask.CompletedTask;
        }

        protected override async ValueTask OnDestroyingAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _destroyCount);
            _destroyStarted.TrySetResult();
            await _releaseDestroy.Task.WaitAsync(cancellationToken);
        }

        internal void RaiseInvalidation() => Invalidate();
        internal void ReleaseDestroy() => _releaseDestroy.TrySetResult();

        public override WidgetView Render() => new(
            UI.Stack("notification.test", UI.Text("status", "Ready")));
    }

    private static class NotificationAssert
    {
        internal static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        internal static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(
                    $"Expected '{expected}', got '{actual}'.");
        }

        internal static void SequenceEqual<T>(
            IEnumerable<T> expected,
            IEnumerable<T> actual)
        {
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException(
                    $"Expected [{string.Join(", ", expected)}], " +
                    $"got [{string.Join(", ", actual)}].");
        }

        internal static async Task<T> ThrowsAsync<T>(Func<Task> action)
            where T : Exception
        {
            try
            {
                await action();
            }
            catch (T exception)
            {
                return exception;
            }
            throw new InvalidOperationException(
                $"Expected {typeof(T).Name} was not thrown.");
        }

        internal static async Task<Exception> CaptureAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                return exception;
            }
            throw new InvalidOperationException(
                "Expected a terminal request-loop failure was not thrown.");
        }
    }
}

internal sealed record DiagnosticCursorItem(int Index);
internal sealed record DiagnosticCursorCompletion(
    WidgetActionEvent Action,
    bool Handled,
    WidgetOperationAdmission Admission,
    WidgetOperationStatus Status,
    string? ExceptionType);

internal sealed class DiagnosticCursorNotificationWidget : Widget
{
    internal const string CursorForwardAction = "diagnostic.cursor.forward";
    internal const string SingleInvalidationAction = "diagnostic.single.invalidate";
    private readonly WidgetCursorResource<DiagnosticCursorItem> _items;
    private readonly bool _loadOnActivation;
    private readonly TaskCompletionSource<DiagnosticCursorCompletion> _cursorForwardCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _initialLoadCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal DiagnosticCursorNotificationWidget(bool loadOnActivation = false)
    {
        _loadOnActivation = loadOnActivation;
        _items = CreateCursorResource<DiagnosticCursorItem>("diagnostic.items", new()
        {
            PageSize = 4,
            MaximumRetainedItems = 8,
            PaginationThreshold = 1,
            LoadPage = (cursor, _, limit, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var start = cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1));
                var count = Math.Min(limit, 12 - start);
                return ValueTask.FromResult(new WidgetCursorPage<DiagnosticCursorItem>(
                    Enumerable.Range(start, count)
                        .Select(index => new DiagnosticCursorItem(index))
                        .ToArray(),
                    start == 0 ? null : new WidgetCollectionCursor($"p{Math.Max(0, start - limit)}"),
                    start + count < 12 ? new WidgetCollectionCursor($"p{start + count}") : null)
                {
                    FirstItemIndex = start,
                    TotalItemCount = 12,
                });
            },
            MapError = _ => new WidgetResourceError(
                "diagnostic_page_failed", "Diagnostic cursor page failed."),
            Viewports =
            [
                new WidgetCursorViewport<DiagnosticCursorItem>(
                    "diagnostic.scroll",
                    item => new WidgetCollectionItemKey($"item.{item.Index}"),
                    item => $"diagnostic.item.{item.Index}")
                {
                    EstimatedItemExtent = 40,
                },
            ],
        });
        Operations.BusyChanged += (_, changed) =>
        {
            if (_loadOnActivation &&
                !changed.IsBusy &&
                string.Equals(changed.Key, "diagnostic.items", StringComparison.Ordinal))
                _initialLoadCompleted.TrySetResult();
        };
    }

    internal Task<DiagnosticCursorCompletion> CursorForwardCompleted =>
        _cursorForwardCompleted.Task;
    internal Task InitialLoadCompleted => _initialLoadCompleted.Task;

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        activeLifetime.ThrowIfCancellationRequested();
        if (_loadOnActivation) _ = _items.EnsureLoaded();
        return ValueTask.CompletedTask;
    }

    public override WidgetView Render()
    {
        var capture = _items.Capture();
        var rows = capture.Snapshot.Items.Select(item => capture.PresentItem(
            item,
            UI.Button(
                $"Item {item.Index}",
                "diagnostic.open",
                $"diagnostic.item.{item.Index}")))
            .ToArray();
        return new WidgetView(
            capture.Present(UI.VerticalScroll("diagnostic.scroll", rows)),
            rows.FirstOrDefault()?.Id);
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        if (action.ActionId == SingleInvalidationAction)
        {
            Invalidate();
            return;
        }
        WidgetActionEvent paginationAction;
        if (action.ActionId == CursorForwardAction)
        {
            var initialResult = await _items.EnsureLoaded().Completion.ConfigureAwait(false);
            if (initialResult.Status != WidgetOperationStatus.Succeeded)
                throw new InvalidOperationException(
                    $"Initial diagnostic cursor load completed with '{initialResult.Status}'.",
                    initialResult.Exception);
            var scroll = (ScrollElement)Render().Root;
            var nextAction = scroll.NearEndActionId
                ?? throw new InvalidOperationException("Diagnostic cursor omitted its forward action.");
            paginationAction = new WidgetActionEvent(nextAction, scroll.Id);
        }
        else
        {
            paginationAction = action;
        }

        var handled = _items.TryHandlePagination(paginationAction, out var operation);
        if (!handled)
            throw new InvalidOperationException("Diagnostic cursor did not admit its forward action.");
        var result = await operation.Completion.ConfigureAwait(false);
        _cursorForwardCompleted.TrySetResult(new(
            action,
            handled,
            operation.Admission,
            result.Status,
            result.Exception?.GetType().FullName));
        if (result.Status != WidgetOperationStatus.Succeeded)
            throw new InvalidOperationException(
                $"Diagnostic cursor pagination completed with '{result.Status}'.",
                result.Exception);
    }
}
