using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;
internal static class ActivityServiceTests
{
    internal static async Task Contract()
    {
        // Reverting a pending update to the in-flight state must not emit the obsolete state.
        var revertClient = new Client(); var revert = new WidgetHostServices(revertClient).Activity;
        var original = revert.SetAsync("player", new(LocalAudio: true)).AsTask();
        await revertClient.Started.Task;
        var stopped = revert.ClearAsync("player").AsTask();
        var playingAgain = revert.SetAsync("player", new(LocalAudio: true)).AsTask();
        Require((await stopped).Superseded, "Pending stop must be replaced when playback resumes");
        revertClient.Release.SetResult(); await original; await playingAgain;
        Require(revertClient.Values.Count == 1 && revertClient.Values[0].LocalAudio, "Rapid resume must not briefly clear audio or resend unchanged state");
        var burstClient = new Client(); var burstService = new WidgetHostServices(burstClient).Activity;
        var burstFirst = burstService.SetAsync("player", new(LocalAudio: true)).AsTask(); await burstClient.Started.Task;
        var burst = Enumerable.Range(0, 500).Select(i => burstService.SetAsync("player", new(LocalAudio: i % 2 == 0)).AsTask()).ToArray();
        Require(burst.Distinct().Count(task => !task.IsCompleted) <= 2 && burstClient.Values.Count == 1, "Burst queued unbounded live publications");
        burstClient.Release.SetResult(); await burstFirst; await Task.WhenAll(burst);
        Require(burstClient.Values.Count == 2 && !burstClient.Values[^1].LocalAudio, "Burst did not converge directly on the latest state");
        var client = new Client();
        var service = new WidgetHostServices(client).Activity;
        var first = service.SetAsync("player", new(LocalAudio: true)).AsTask();
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var intermediate = service.SetAsync("recorder", new(Recording: true)).AsTask();
        var latest = service.ClearAsync("player").AsTask();
        var superseded = await intermediate;
        Require(!superseded.Accepted && superseded.Superseded, "Replaced state must complete explicitly as superseded");
        client.Release.SetResult();
        Require((await first).Accepted && (await latest).Accepted, "Latest state must eventually reach host");
        Require(client.Values.Count == 2 && client.Values[^1] == new WidgetActivity(Recording: true), "Independent sources must aggregate without stale audio");
        var calls = client.Values.Count;
        await service.SetAsync("second-recorder", new(Recording: true));
        await service.ClearAsync("recorder");
        Require(client.Values.Count == calls, "One of two identical activities stopping must not clear shared badge or send duplicates");
        await service.ClearAsync("second-recorder");
        Require(client.Values[^1] == new WidgetActivity(), "Last source clearing must clear aggregate");
        for (var i = 0; i < 16; i++) await service.SetAsync("source" + i, new(RemotePlayback: true));
        try { await service.SetAsync("overflow", new(LocalAudio: true)); throw new Exception("Unbounded sources accepted"); }
        catch (InvalidOperationException) { }
        try { await service.SetAsync("invalid source", new()); throw new Exception("Invalid source accepted"); }
        catch (ArgumentException) { }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await service.ClearAsync("source0", cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        Require(client.Values[^1].RemotePlayback, "Cancelled operation before submission changes no state");
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Client : IWidgetCapabilityClient
    {
        public bool IsAvailable => true;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<WidgetActivity> Values = [];
        public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(WidgetCapabilityOperation<TRequest, TResponse> operation, TRequest request, CancellationToken cancellationToken = default)
        {
            Require(operation.CapabilityId == WidgetActivityCapabilities.Service, "Incorrect capability");
            Values.Add((WidgetActivity)(object)request!);
            if (Values.Count == 1) { Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return (TResponse)(object)new WidgetActivityReceipt(true);
        }
        public ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(WidgetCapabilityEvent<TPayload> platformEvent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
