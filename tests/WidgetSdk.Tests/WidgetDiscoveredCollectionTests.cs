using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetDiscoveredCollectionTests
{
    internal static async Task Run()
    {
        await PrefixAuthority(); await RetryAndLimit(); await CancellationAndReplacement(); await IgnoredCancellationIsBounded();
        await DuplicatePolicies(); await RepeatedContinuation(); await ReplacementProviderBudget(); ProtocolValidation();
    }
    private static async Task PrefixAuthority()
    {
        await using var widget = new Fixture((query, token, _, _) => ValueTask.FromResult(token is null
            ? new WidgetDiscoveredPage<string>(["one", "two"], "opaque-next") : new(["three"], null)));
        await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "discovery");
        Equal(ProtocolConstants.CurrentVersion, host.CurrentSnapshot.ProtocolVersion);
        Equal(0, ViewSnapshotValidator.Validate(host.CurrentSnapshot with { ProtocolVersion = ProtocolConstants.DiscoveredCollectionVersion }).Count);
        Equal(0, widget.Source.Descriptor.Count);
        True(widget.Source.Descriptor.Discovery!.HasMore);
        await host.ContinueAsync("items"); host.PublishSnapshot();
        var first = widget.Source.Descriptor;
        using var original = await host.AcquireAsync("items", 0, 2);
        Equal(2, original.Range.Items.Count);
        await host.ContinueAsync("items"); host.PublishSnapshot();
        Equal(first.QueryGeneration, widget.Source.Descriptor.QueryGeneration);
        Equal(3, widget.Source.Descriptor.Count);
        True(!widget.Source.Descriptor.Discovery!.HasMore);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, original.RouteAction("two", ControllerButton.A));
        Equal("initial:two", await widget.Next());
        using var tail = await host.AcquireAsync("items", 2, 1);
        Equal("three", tail.Range.Items[0].Key);
        widget.Source.ReplaceQuery("replacement"); host.PublishSnapshot();
        Equal<WidgetOperationAdmission?>(null, original.RouteAction("one", ControllerButton.A));
        await host.ContinueAsync("items"); host.PublishSnapshot();
        using var next = await host.AcquireAsync("items", 0, 1);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, next.RouteAction("one", ControllerButton.A));
        Equal("replacement:one", await widget.Next());
    }
    private static async Task RetryAndLimit()
    {
        var calls = 0;
        await using var widget = new Fixture((_, token, count, _) =>
        {
            ++calls;
            if (calls == 1) throw new IOException("provider detail");
            return ValueTask.FromResult(token is null ? new WidgetDiscoveredPage<string>([], "empty-page") : new(["one", "two"], "more"));
        }, maximum: 2);
        await widget.Start(); using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "retry");
        await host.ContinueAsync("items"); host.PublishSnapshot();
        Equal(DiscoveredCollectionStatus.Failed, widget.Source.Descriptor.Discovery!.Status);
        await host.ContinueAsync("items"); Equal(1, calls);
        await host.ContinueAsync("items", retry: true); host.PublishSnapshot();
        Equal(0, widget.Source.Descriptor.Count); True(widget.Source.Descriptor.Discovery!.HasMore);
        await host.ContinueAsync("items"); host.PublishSnapshot();
        Equal(2, widget.Source.Descriptor.Count);
        Equal(DiscoveredCollectionStatus.LimitReached, widget.Source.Descriptor.Discovery!.Status);
        True(widget.Source.Descriptor.Discovery.HasMore);
        await host.ContinueAsync("items", retry: true); Equal(3, calls);
        using var retained = await host.AcquireAsync("items", 0, 2); Equal("one", retained.Range.Items[0].Key);
    }
    private static async Task CancellationAndReplacement()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource<WidgetDiscoveredPage<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var widget = new Fixture(async (query, _, _, token) =>
        {
            if (query != "initial") return new(["replacement"], null);
            entered.SetResult(); return await blocked.Task.WaitAsync(token);
        });
        await widget.Start(); using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "replacement");
        var pending = host.ContinueAsync("items").AsTask(); await entered.Task;
        widget.Source.ReplaceQuery("new"); host.PublishSnapshot();
        try { await pending; } catch (Exception error) when (error is ArgumentException or OperationCanceledException) { }
        blocked.TrySetResult(new(["stale"], null));
        await host.ContinueAsync("items"); host.PublishSnapshot();
        using var result = await host.AcquireAsync("items", 0, 1); Equal("replacement", result.Range.Items[0].Key);
    }
    private static async Task IgnoredCancellationIsBounded()
    {
        var calls = 0;
        var blocked = new TaskCompletionSource<WidgetDiscoveredPage<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var widget = new Fixture((_, _, _, _) => { ++calls; return new(blocked.Task); }, timeout: TimeSpan.FromMilliseconds(100));
        await widget.Start(); using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "ignored");
        await host.ContinueAsync("items"); host.PublishSnapshot();
        Equal(DiscoveredCollectionStatus.Failed, widget.Source.Descriptor.Discovery!.Status);
        Equal("discovery_timeout", widget.Source.Descriptor.Discovery.ErrorCode);
        await host.ContinueAsync("items", retry: true); host.PublishSnapshot();
        Equal(1, calls);
        Equal("discovery_busy", widget.Source.Descriptor.Discovery!.ErrorCode);
        blocked.SetResult(new(["late"], null)); await Task.Delay(20);
        Equal(0, widget.Source.Descriptor.Count);
    }
    private static async Task DuplicatePolicies()
    {
        foreach (var policy in new[] { WidgetDiscoveredDuplicatePolicy.Reject, WidgetDiscoveredDuplicatePolicy.KeepFirst })
        {
            await using var widget = new Fixture((_, token, _, _) => ValueTask.FromResult(token is null
                ? new WidgetDiscoveredPage<string>(["one", "two"], "next") : new(["two", "three"], null)), duplicatePolicy: policy);
            await widget.Start(); using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "duplicates");
            await host.ContinueAsync("items"); host.PublishSnapshot();
            using var prefix = await host.AcquireAsync("items", 0, 2);
            await host.ContinueAsync("items"); host.PublishSnapshot();
            Equal(policy == WidgetDiscoveredDuplicatePolicy.Reject ? 2 : 3, widget.Source.Descriptor.Count);
            Equal(policy == WidgetDiscoveredDuplicatePolicy.Reject ? DiscoveredCollectionStatus.Failed : DiscoveredCollectionStatus.Ready,
                widget.Source.Descriptor.Discovery!.Status);
            Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, prefix.RouteAction("two", ControllerButton.A));
            Equal("initial:two", await widget.Next());
        }
    }
    private static async Task RepeatedContinuation()
    {
        await using var widget = new Fixture((_, token, _, _) => ValueTask.FromResult(token is null
            ? new WidgetDiscoveredPage<string>(["one"], "repeated") : new(["two"], "repeated")));
        await widget.Start(); using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "loop");
        await host.ContinueAsync("items"); host.PublishSnapshot();
        await host.ContinueAsync("items"); host.PublishSnapshot();
        Equal(1, widget.Source.Descriptor.Count);
        Equal(DiscoveredCollectionStatus.Failed, widget.Source.Descriptor.Discovery!.Status);
    }
    private static async Task ReplacementProviderBudget()
    {
        var calls = 0;
        var blocked = new TaskCompletionSource<WidgetDiscoveredPage<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var widget = new Fixture((_, _, _, _) => { Interlocked.Increment(ref calls); return new(blocked.Task); }, timeout: TimeSpan.FromMilliseconds(100));
        await widget.Start(); using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "replacement-budget");
        for (var query = 0; query < 5; ++query)
        {
            widget.Source.ReplaceQuery("query-" + query); host.PublishSnapshot();
            await host.ContinueAsync("items"); host.PublishSnapshot();
        }
        Equal(4, calls);
        Equal("discovery_busy", widget.Source.Descriptor.Discovery!.ErrorCode);
        Equal(0, widget.Source.Descriptor.Count);
        blocked.SetResult(new(["current-only"], null));
        for (var attempt = 0; attempt < 100 && widget.Source.Descriptor.Count == 0; ++attempt)
        {
            await Task.Delay(5);
            await host.ContinueAsync("items", retry: true); host.PublishSnapshot();
        }
        Equal(5, calls);
        Equal(1, widget.Source.Descriptor.Count);
    }
    private static void ProtocolValidation()
    {
        var valid = new IndexedCollectionDescriptor("source", 1, 0, 1) { Discovery = new(1, true, DiscoveredCollectionStatus.Ready, 16) };
        IndexedCollectionContract.ValidateDescriptor(valid);
        IndexedCollectionContract.ValidateRequest(new("items", valid, 1, 0, "next") { Kind = IndexedCollectionRequestKind.Continue });
        var invalid = new[]
        {
            valid with { Count = 17 }, valid with { Discovery = valid.Discovery! with { MaximumItems = 4097 } },
            valid with { Discovery = valid.Discovery! with { Status = DiscoveredCollectionStatus.Failed } },
            valid with { Discovery = valid.Discovery! with { Status = DiscoveredCollectionStatus.LimitReached, HasMore = false } },
        };
        foreach (var source in invalid) Throws(() => IndexedCollectionContract.ValidateDescriptor(source));
        Throws(() => IndexedCollectionContract.ValidateRequest(new("items", valid, 0, 0, "next") { Kind = IndexedCollectionRequestKind.Continue }));
        Throws(() => IndexedCollectionContract.ValidateRequest(new("items", valid with { Discovery = null }, 1, 0, "next") { Kind = IndexedCollectionRequestKind.Continue }));
        Throws(() => IndexedCollectionContract.ValidateGroups([new("group", "Heading", 1)], valid, ScrollAxis.Vertical));
    }
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected invalid discovery contract rejection.");
    }
    private sealed class Fixture : Widget, IAsyncDisposable
    {
        internal readonly WidgetDiscoveredCollection<string, string> Source;
        private readonly Channel<string> actions = Channel.CreateUnbounded<string>();
        internal Fixture(Func<string, string?, int, CancellationToken, ValueTask<WidgetDiscoveredPage<string>>> load,
            int maximum = 16, TimeSpan? timeout = null, WidgetDiscoveredDuplicatePolicy duplicatePolicy = WidgetDiscoveredDuplicatePolicy.Reject)
        {
            Source = CreateDiscoveredCollection("source", "initial", new WidgetDiscoveredCollectionOptions<string, string>
            {
                PageSize = 2, MaximumItems = maximum, ReadTimeout = timeout ?? TimeSpan.FromSeconds(2), LoadNext = load, DuplicatePolicy = duplicatePolicy,
                ItemKey = item => new(item), RenderItem = (_, item, context) => UI.Button(item, "open", context.Id("row")),
                OnAction = (query, item, _, token) => actions.Writer.WriteAsync(query + ":" + item, token),
            });
        }
        internal async Task Start() { await WidgetTestHost.InitializeAsync(this); await WidgetTestHost.SetLifecycleStateAsync(this, WidgetLifecycleState.Interactive); }
        internal Task<string> Next() => actions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        public override WidgetView Render() => new(UI.Stack("root", UI.CollectionList("items", Source, 80, "Items")), "items");
        public async ValueTask DisposeAsync() => await WidgetTestHost.DestroyAsync(this);
    }
    private static void True(bool condition) { if (!condition) throw new InvalidOperationException("Discovery assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, actual {actual}."); }
}
