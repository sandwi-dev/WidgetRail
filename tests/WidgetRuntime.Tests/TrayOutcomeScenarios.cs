using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class TrayOutcomeScenarios
{
    internal static async Task RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        await using var client = new WidgetProcessClient(new WidgetProcessOptions
        {
            ExecutablePath = Environment.ProcessPath!, Arguments = ["--tray-outcome-probe"],
            WidgetInstanceId = "runtime.tray", RequestTimeout = TimeSpan.FromSeconds(5)
        });
        var snapshot = await client.GetSnapshotAsync(token);
        long sequence = 0;
        Task<WidgetProcessClient.InputDispatch> Send(string id, bool tracked)
        {
            var action = snapshot.QuickActions.Single(item => item.TrayOffer?.Id == id);
            return client.SendControllerInputForWorkerAsync(new(action.Button, ControllerEventPhase.Pressed,
                ControllerInputContext.TrayQuickAction, Sequence: ++sequence, SnapshotSequence: snapshot.Sequence)
                { TrayOffer = action.TrayOffer, TrackTrayWork = tracked }, null, token, client.Starts);
        }
        foreach (var tracked in new[] { false, true })
        {
            var immediate = await Send("fail", tracked);
            if (immediate.Handled || await immediate.Completion!.WaitAsync(token) != TrayOutcomeProbe.Rejection)
                throw new Exception("An immediate rejection lost its reason.");
        }
        var waiting = await Send("wait", true);
        if (!waiting.Handled || waiting.Completion!.IsCompleted) throw new Exception("Deferred action was not pending.");
        var release = await Send("release", true);
        if (await release.Completion!.WaitAsync(token) != TrayActionResult.Success ||
            await waiting.Completion.WaitAsync(token) != TrayOutcomeProbe.Rejection)
            throw new Exception("Overlapping presses exchanged results.");
        var retired = await Send("hold", true);
        await client.StopAsync(token);
        if ((await retired.Completion!.WaitAsync(token)).Handled)
            throw new Exception("Retired worker reported successful completion.");
        if (client.Starts != 1) throw new Exception("Reading a completion woke a replacement worker.");
    }
}

internal sealed class TrayOutcomeProbe : Widget
{
    internal static readonly TrayActionResult Rejection = TrayActionResult.Unavailable("The test player disconnected.");
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override WidgetView Render() => new(UI.Text("Ready", "root"), QuickActions:
        new[] { "fail", "wait", "release", "hold" }.Select(id => WidgetQuickAction.ForTray(id, new(id, id))).ToArray());

    protected override async ValueTask<TrayActionResult> OnTrayActionAsync(TrayActionOffer offer, CancellationToken token)
    {
        switch (offer.Id)
        {
            case "release":
                release.TrySetResult();
                return TrayActionResult.Success;
            case "wait":
                await release.Task.WaitAsync(token);
                break;
            case "hold":
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                break;
        }
        return Rejection;
    }
}
