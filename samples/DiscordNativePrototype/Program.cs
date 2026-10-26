using System.Text.Json;
using WidgetRail.Samples.DiscordPrototype;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

if (args is ["--probe-watch"])
{
    using var client = new AdapterClient();
    var state = (await client.InvokeAsync("snapshot", null, CancellationToken.None)).Deserialize<DiscordState>(AdapterClient.Json)!;
    state.Validate();
    if (!state.Ready || state.Streams.Length != 1 || state.ChannelId is null) throw new InvalidOperationException("Exactly one test stream required.");
    var lease = Guid.NewGuid().ToString("N");
    try
    {
        var offer = await client.InvokeAsync("watch", new { account = state.Account, channelId = state.ChannelId, key = state.Streams[0].Key, lease }, CancellationToken.None);
        Console.WriteLine("PASS: incoming track acquired; WebRTC offer bytes=" + offer.GetProperty("offer").GetProperty("sdp").GetString()!.Length);
        return 0;
    }
    catch (InvalidOperationException e) { Console.Error.WriteLine("Watch probe: " + e.Message); Console.Error.WriteLine(await client.InvokeAsync("diagnostics", null, CancellationToken.None)); return 1; }
    finally { await client.InvokeAsync("unwatch", new { account = state.Account, lease }, CancellationToken.None); }
}

if (args is ["--validate-live"])
{
    var widget = new DiscordPrototypeWidget();
    try
    {
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        for (var i = 0; i < 30; ++i)
        {
            var snapshot = widget.Render().CreateSnapshot("discord.prototype.validation", i);
            var errors = WidgetTestHost.ValidateWinUiPresentation(snapshot);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => e.Code)));
            await Task.Delay(100);
        }
        Console.WriteLine("PASS: 30 live native declarations and pinned layouts validated; no call actions sent.");
        return 0;
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

if (args is ["--probe"])
{
    try
    {
        using var client = new AdapterClient();
        var state = await client.InvokeAsync("snapshot", null, CancellationToken.None);
        // Diagnostics intentionally omit account/channel names and identifiers.
        Console.WriteLine(JsonSerializer.Serialize(new { ready = state.GetProperty("ready"),
            capabilities = state.GetProperty("capabilities"), channels = state.GetProperty("channels").GetArrayLength(),
            participants = state.GetProperty("participants").GetArrayLength(),
            streams = state.GetProperty("streams").GetArrayLength(), sharing = state.GetProperty("sharing"), relay = state.GetProperty("relayState") }));
        return 0;
    }
    catch (Exception error) { Console.Error.WriteLine("Probe failed: " + error.GetType().Name); return 1; }
}
return await WidgetApplicationBootstrap.RunAsync(args, () => new DiscordPrototypeWidget());
