using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class BridgeClientRegistryScenarios
{
    internal static async Task TextSuggestions()
    {
        var configured = Widget("suggestions", 's', 's');
        var source = new TextSuggestionSource("gems") { TimeoutMilliseconds = 5000 };
        var field = new ViewNode { Id = "entry", Kind = ViewNodeKind.TextEntry, ActionId = "commit",
            TextEntryValue = "", TextEntryMaximumLength = 100, TextEntrySuggestions = source };
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<TextSuggestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new RegistryFixture(Catalog(configured), configure: (_, client) =>
        {
            client.SnapshotFactory = sequence => new ViewSnapshot { Sequence = sequence, WidgetInstanceId = configured.InstanceId,
                ActiveInputScopeId = "root", Root = new ViewNode { Id = "root", Kind = ViewNodeKind.Stack, Children = [field] } };
            client.TextSuggestions = async (_, token) =>
            {
                calls++; entered.TrySetResult();
                using var cancellation = token.Register(() => cancelled.TrySetResult());
                return await reply.Task; // Deliberately ignores cancellation to exercise admission.
            };
        });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        await fixture.GetSnapshotAsync(configured.Id);
        var descriptor = configured.PublicDescriptor();
        var query = new TextSuggestionQuery("entry", source.Key, Guid.NewGuid().ToString("N"), 1, "fi", 6, 100)
            { TimeoutMilliseconds = source.TimeoutMilliseconds };
        var request = new BridgeTextSuggestionsRequest(configured.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, query);
        var operation = fixture.Registry.SuggestTextAsync(request, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using (var busy = await fixture.Registry.SuggestTextAsync(request with { Query = query with { Revision = 2 } }, default))
            RegistryAssert.True(busy.Value.Unavailable);
        RegistryAssert.Equal(1, calls);
        await fixture.Registry.CancelTextSuggestionsAsync(request with { Query = query with { Revision = 9 } });
        RegistryAssert.True(!cancelled.Task.IsCompleted);
        var cancel = fixture.Registry.CancelTextSuggestionsAsync(request);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.True(!cancel.IsCompleted);
        reply.SetResult(new([new("fireball", "Fireball", "Fireball")]));
        using (var result = await operation) RegistryAssert.Equal("Fireball", result.Value.Items.Single().Value);
        await cancel.WaitAsync(TimeSpan.FromSeconds(2));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => fixture.Registry.SuggestTextAsync(
            request with { Query = query with { SourceKey = "old-catalog" } }, default));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => fixture.Registry.SuggestTextAsync(
            request with { Query = query with { MaximumValueLength = 512 } }, default));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => fixture.Registry.SuggestTextAsync(
            request with { RuntimeGeneration = "retired" }, default));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => fixture.Registry.SuggestTextAsync(
            request with { Query = query with { TimeoutMilliseconds = 2000 } }, default));

        reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = fixture.Registry.SuggestTextAsync(request, default);
        field = field with { TextEntrySuggestions = source with { TimeoutMilliseconds = 3000 } };
        await fixture.GetSnapshotAsync(configured.Id);
        reply.SetResult(TextSuggestionResult.Empty);
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => stale);
        var currentRequest = request with { Query = query with { TimeoutMilliseconds = 3000 } };
        fixture.Clients[0].TextSuggestions = (_, _) => Task.FromCanceled<TextSuggestionResult>(new CancellationToken(true));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => fixture.Registry.SuggestTextAsync(currentRequest, default));
        using (var shutdown = new CancellationTokenSource())
        {
            shutdown.Cancel();
            await RegistryAssert.ThrowsAsync<OperationCanceledException>(() => fixture.Registry.SuggestTextAsync(currentRequest, shutdown.Token));
        }
        fixture.Clients[0].TextSuggestions = (_, _) => Task.FromResult(TextSuggestionResult.Empty);
        using (var recovered = await fixture.Registry.SuggestTextAsync(currentRequest, default))
            RegistryAssert.True(!recovered.Value.Unavailable);
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Background);
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => fixture.Registry.SuggestTextAsync(
            request with { Query = query with { SourceKey = "replacement" } }, default));
        RegistryAssert.Equal(2, calls);
    }
}
