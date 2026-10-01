using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class IntentDeliveryScenarios
{
    internal static async Task DuplicateAndLifecycle()
    {
        var widget = new IntentProbeWidget();
        await widget.InitializeAsync(default);
        try
        {
            Check(await widget.ApplyIntentAsync(1, Request(), default) == WidgetIntentResult.Rejected);
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, default);
            Check(await widget.ApplyIntentAsync(1, Request(), default) == WidgetIntentResult.Accepted);
            Check(await widget.ApplyIntentAsync(1, Request(), default) == WidgetIntentResult.Rejected);
            Check(widget.Calls == 1);
            Check(await widget.ApplyIntentAsync(3, Request(), default) == WidgetIntentResult.Accepted);
            Check(await widget.ApplyIntentAsync(2, Request(), default) == WidgetIntentResult.Rejected);
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default);
            Check(await widget.ApplyIntentAsync(4, Request(), default) == WidgetIntentResult.Rejected);
            Check(widget.Calls == 2);
        }
        finally { await widget.DestroyAsync(default); }
    }

    internal static async Task CancellationAndFailure()
    {
        var widget = new IntentProbeWidget { Block = true };
        await widget.InitializeAsync(default);
        await widget.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, default);
        try
        {
            var pending = widget.ApplyIntentAsync(1, Request(), default).AsTask();
            await widget.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default);
            try { await pending; throw new Exception("Expected cancellation."); } catch (OperationCanceledException) { }
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, default);
            widget.Block = false;
            Check(await widget.ApplyIntentAsync(1, Request(), default) == WidgetIntentResult.Rejected);
            widget.Fail = true;
            try { await widget.ApplyIntentAsync(2, Request(), default); throw new Exception("Expected failure."); }
            catch (InvalidOperationException) { }
            widget.Fail = false;
            Check(await widget.ApplyIntentAsync(2, Request(), default) == WidgetIntentResult.Rejected);
            Check(await widget.ApplyIntentAsync(3, Request(), default) == WidgetIntentResult.Accepted);
        }
        finally { await widget.DestroyAsync(default); }
    }

    internal static async Task ProcessBoundary()
    {
        await using var client = new WidgetProcessClient(new WidgetProcessOptions
        {
            ExecutablePath = Environment.ProcessPath!, Arguments = ["--intent-probe"],
            WidgetInstanceId = "runtime.intent", RequestTimeout = TimeSpan.FromSeconds(5),
        });
        try { await client.DeliverIntentAsync(Request(), 1, default); throw new Exception("Delivery started a worker."); }
        catch (WidgetInputWorkerRetiredException) { }
        Check(client.Starts == 0);
        _ = await client.GetSnapshotAsync();
        var ordinal = client.Starts;
        Check(await client.DeliverIntentAsync(Request(), ordinal, default) == WidgetIntentResult.Rejected);
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        Check(await client.DeliverIntentAsync(Request(), ordinal, default) == WidgetIntentResult.Accepted);
        var snapshot = await client.GetSnapshotAsync();
        Check(snapshot.Root.Children[0].Text == "https://example.com/guide");
        using var cancel = new CancellationTokenSource();
        using var waitDocument = JsonDocument.Parse("""{"url":"https://example.com/wait"}""");
        var waiting = client.DeliverIntentAsync(WidgetIntentRequest.Create(WidgetIntentContracts.Web, waitDocument.RootElement), ordinal, cancel.Token);
        var entered = false;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await client.GetSnapshotAsync()).Root.Children[0].Text == "Waiting") { entered = true; break; }
            await Task.Delay(10);
        }
        Check(entered);
        cancel.Cancel();
        try { await waiting; throw new Exception("IPC request ignored cancellation."); } catch (OperationCanceledException) { }
        Check(client.IsRunning && client.Starts == ordinal);
        Check(await client.DeliverIntentAsync(Request(), ordinal, default) == WidgetIntentResult.Accepted);
        await client.StopAsync();
        try { await client.DeliverIntentAsync(Request(), ordinal, default); throw new Exception("Delivery recovered a retired worker."); }
        catch (WidgetInputWorkerRetiredException) { }
        Check(client.Starts == ordinal);
    }

    internal static WidgetIntentRequest Request()
    {
        using var document = JsonDocument.Parse("""{"url":"https://example.com/guide"}""");
        return WidgetIntentRequest.Create(WidgetIntentContracts.Web, document.RootElement);
    }
    private static void Check(bool value) { if (!value) throw new Exception("Intent assertion failed."); }
}

internal sealed class IntentProbeWidget : Widget
{
    internal int Calls;
    internal bool Block, Fail;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _url = "No request";
    public override WidgetView Render() => new(UI.Stack("root", UI.Text(_url, "url")));
    public override async ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        Entered.TrySetResult();
        if (request.Payload.GetProperty("url").GetString()!.EndsWith("/wait", StringComparison.Ordinal))
        {
            _url = "Waiting";
            Invalidate();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        if (Block) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (Fail) throw new InvalidOperationException("Synthetic receiver failure.");
        _url = request.Payload.GetProperty("url").GetString()!;
        return WidgetIntentResult.Accepted;
    }
}
