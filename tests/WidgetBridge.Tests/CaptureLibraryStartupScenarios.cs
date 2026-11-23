using WidgetRail.FirstPartyWidgets.CaptureLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class CaptureLibraryStartupScenarios
{
    internal static async Task StartupPreferencesRetryAfterCancellationAndFailure()
    {
        foreach (var cancel in new[] { true, false })
        {
            var item = new CaptureLibraryItem(new string('a', 32), new(new string('b', 32), "video/mp4", 1280, 720, 12, 5, long.MaxValue), 1);
            var services = new WidgetTestHostServicesBuilder().WithPrivateState(new())
                .WithEvents(WidgetCaptureLibraryCapabilities.Changed, Array.Empty<CaptureLibraryChanged>())
                .WithResponse(WidgetCaptureLibraryCapabilities.Applications, new CaptureLibraryApplications([], false))
                .WithHandler(WidgetCaptureLibraryCapabilities.Folders,(request,_)=>ValueTask.FromResult(new CaptureLibraryFolders([new("", "WidgetRail captures", 0)],request.Revision ?? "a")))
                .WithResponse(WidgetCaptureLibraryCapabilities.QueryContinuity,
                    new CaptureLibraryContinuityPage(new([item], 0, 1, 12, "1"), "1"))
                .WithResponse(WidgetCaptureLibraryCapabilities.Groups, new CaptureLibraryGroups([new("unknown", 1)], 1, "1")).Build();
            var client = new FirstPreferenceFailureClient(services.Capabilities);
            var widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), new WidgetHostServices(client));
            try
            {
                await WidgetTestHost.InitializeAsync(widget);
                await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
                await client.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (cancel) await widget.OnActionAsync(new("capture-library.cancel", "capture-library.cancel"));
                else client.Fail.TrySetResult();
                await client.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await Wait(() => !Nodes().Any(node => node.IsBusy == true));
                if (client.Reads != 1 || Nodes().Any(node => node.Id == "capture-library.gallery.preview"))
                    throw new InvalidOperationException("Failed/cancelled preferences were marked loaded or retried without a new activation.");
                await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
                await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
                await Wait(() => !Nodes().Any(node => node.IsBusy == true) &&
                    Nodes().Any(node => node.Id == "capture-library.gallery.preview") &&
                    Nodes().Any(node => node.Id == "capture-library.entries" && node.IndexedCollection?.Count == 1));
                if (client.Reads != 2) throw new InvalidOperationException("Reactivation did not make exactly one successful preference retry.");
            }
            finally { client.Fail.TrySetResult(); await WidgetTestHost.DestroyAsync(widget); }
            IEnumerable<ViewNode> Nodes() => Walk(widget.RenderSnapshot("startup-retry", 1).Root);
        }
        static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
        static async Task Wait(Func<bool> ready)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (!ready()) await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FirstPreferenceFailureClient(IWidgetCapabilityClient inner) : IWidgetCapabilityClient
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Fail = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads;
        public bool IsAvailable => inner.IsAvailable;
        public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(WidgetCapabilityOperation<TRequest, TResponse> operation,
            TRequest request, CancellationToken cancellationToken = default)
        {
            if (operation.OperationId == "private-state.read" && Interlocked.Increment(ref Reads) == 1)
            {
                Entered.TrySetResult();
                try
                {
                    await Fail.Task.WaitAsync(cancellationToken);
                    throw new WidgetCapabilityException("private_state_unavailable", "Controlled temporary state failure.");
                }
                finally { Returned.TrySetResult(); }
            }
            return await inner.InvokeAsync(operation, request, cancellationToken);
        }
        public ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(WidgetCapabilityEvent<TPayload> platformEvent,
            CancellationToken cancellationToken = default) => inner.OpenSubscriptionAsync(platformEvent, cancellationToken);
    }

    internal static async Task StartupPreferencesCannotDiscardAcknowledgedRefresh()
    {
        var firstQuery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latestGroups = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishGroups = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preferencesReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries = 0;
        var item = new CaptureLibraryItem(new string('a', 32), new(new string('b', 32), "video/mp4", 1280, 720, 12, 5, long.MaxValue), 1);
        var services = new WidgetTestHostServicesBuilder().WithPrivateState(new())
            .WithEvents(WidgetCaptureLibraryCapabilities.Changed, Array.Empty<CaptureLibraryChanged>())
            .WithResponse(WidgetCaptureLibraryCapabilities.Applications, new CaptureLibraryApplications([], false))
            .WithHandler(WidgetCaptureLibraryCapabilities.Folders,(request,_)=>ValueTask.FromResult(new CaptureLibraryFolders([new("", "WidgetRail captures", 0)],request.Revision ?? "a")))
            .WithHandler(WidgetCaptureLibraryCapabilities.QueryContinuity, async (_, token) =>
            {
                if (Interlocked.Increment(ref queries) == 1)
                {
                    firstQuery.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
                return new CaptureLibraryContinuityPage(new([item], 0, 1, 12, "1"), "1");
            })
            .WithHandler(WidgetCaptureLibraryCapabilities.Groups, async (_, token) =>
            {
                latestGroups.TrySetResult();
                await finishGroups.Task.WaitAsync(token);
                return new CaptureLibraryGroups([new("unknown", 1)], 1, "1");
            }).Build();
        var client = new StartupOrderingClient(services.Capabilities, firstQuery, latestGroups, preferencesReturned, () => Volatile.Read(ref queries));
        var widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), new WidgetHostServices(client));
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await latestGroups.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await preferencesReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => !Walk(widget.RenderSnapshot("startup-preferences", 1).Root).Any(node => node.IsBusy == true));
            finishGroups.TrySetResult();
            await Until(() => Walk(widget.RenderSnapshot("startup-preferences", 2).Root)
                .Any(node => node.Id == "capture-library.entries" && node.IndexedCollection?.Count == 1));
            var snapshot = widget.RenderSnapshot("startup-preferences", 3);
            Check(Walk(snapshot.Root).Any(node => node.Id == "capture-library.gallery.preview"),
                "Initial preference and newer acknowledged snapshot did not both reach the gallery.");
            Check(queries == 2, "Startup must not require an explicit refresh or unbounded retry to publish its acknowledged snapshot.");
        }
        finally { finishGroups.TrySetResult(); await WidgetTestHost.DestroyAsync(widget); }

        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
        static async Task Until(Func<bool> ready)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (!ready()) await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StartupOrderingClient(IWidgetCapabilityClient inner, TaskCompletionSource firstQuery,
        TaskCompletionSource latestGroups, TaskCompletionSource preferencesReturned, Func<int> queries) : IWidgetCapabilityClient
    {
        public bool IsAvailable => inner.IsAvailable;
        public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(WidgetCapabilityOperation<TRequest, TResponse> operation,
            TRequest request, CancellationToken cancellationToken = default)
        {
            // If preferences load after the initial read was superseded, force
            // them to complete while the acknowledged replacement is in flight.
            if (operation.OperationId == "private-state.read" && queries() > 0)
                await latestGroups.Task.WaitAsync(cancellationToken);
            var result = await inner.InvokeAsync(operation, request, cancellationToken);
            if (operation.OperationId == "private-state.read") preferencesReturned.TrySetResult();
            return result;
        }
        public async ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(WidgetCapabilityEvent<TPayload> platformEvent,
            CancellationToken cancellationToken = default)
        {
            await firstQuery.Task.WaitAsync(cancellationToken);
            return await inner.OpenSubscriptionAsync(platformEvent, cancellationToken);
        }
    }
}
