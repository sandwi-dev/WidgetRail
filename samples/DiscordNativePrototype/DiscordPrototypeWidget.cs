using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordPrototype;

public sealed class DiscordPrototypeWidget : Widget
{
    private sealed record Presentation(string Query = "", string? StreamKey = null, string? StreamName = null,
        string? Account = null, string? Lease = null, WidgetWebSession? Receiver = null, string Notice = "");
    private readonly IDiscordAdapter client;
    private readonly WidgetResource<DiscordState> state;
    private readonly WidgetModel<Presentation> presentation;
    private readonly PinnedLayoutHandle streamPin;
    private readonly PinnedLayoutHandle callPin;

    public DiscordPrototypeWidget() : this(new AdapterClient()) { }

    internal DiscordPrototypeWidget(IDiscordAdapter adapter)
    {
        client = adapter;
        presentation = CreateModel(new Presentation());
        state = CreateResource("discord.read", new WidgetResourceOptions<DiscordState>
        {
            Load = ReadStateAsync,
            MapError = _ => new("discord.unavailable", "Vesktop is unavailable. Start the prototype session, sign in, then Refresh."),
            RetainLastGoodValue = false, Lifetime = WidgetOperationLifetime.Widget,
            CacheDuration = TimeSpan.FromSeconds(1)
        });
        streamPin = CreatePinnedLayoutHandle("discord.stream", "Stream", new()
        {
            PreferredWidth = 560, PreferredHeight = 340, MinimumWidth = 300, MinimumHeight = 200,
            WidthMode = WidgetSurfaceAxisMode.Preferred, HeightMode = WidgetSurfaceAxisMode.Preferred
        });
        callPin = CreatePinnedLayoutHandle("discord.call", "Voice chat", new()
        {
            PreferredWidth = 400, PreferredHeight = 240, MinimumWidth = 300, MinimumHeight = 180,
            WidthMode = WidgetSurfaceAxisMode.Preferred, HeightMode = WidgetSurfaceAxisMode.Preferred
        });
    }

    protected override ValueTask OnCreatedAsync(CancellationToken widgetLifetime)
    {
        Operations.RunSingleFlight("discord.poll", async context =>
        {
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await state.Refresh().Completion;
                var p = presentation.Value;
                if (p.Lease is not null && p.Account is not null && streamPin.IsSelected)
                {
                    try { await client.InvokeAsync("keep", new { account = p.Account, lease = p.Lease }, context.CancellationToken); }
                    catch (Exception) when (!context.CancellationToken.IsCancellationRequested) { /* Read resource supplies disconnect state. */ }
                }
                await Task.Delay(LifecycleState == WidgetLifecycleState.Background && !streamPin.IsSelected ? 5000 : 1200, context.CancellationToken);
            }
        }, WidgetOperationLifetime.Widget);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    { state.EnsureLoaded(); return ValueTask.CompletedTask; }

    private async ValueTask<DiscordState> ReadStateAsync(CancellationToken token)
    {
        var result = (await client.InvokeAsync("snapshot", null, token)).Deserialize<DiscordState>(AdapterClient.Json)
            ?? throw new InvalidDataException();
        result.Validate();
        var p = presentation.Value;
        if (p.Account is not null && p.Account != result.Account)
        {
            presentation.Update(current => current with { StreamKey = null, StreamName = null, Account = null });
            RestartRelay();
        }
        return result;
    }

    public override WidgetView Render()
    {
        var snapshot = state.Snapshot;
        var s = snapshot.HasValue ? snapshot.Value : null;
        var p = presentation.Value;
        var busy = Operations.IsBusy("discord.command");
        var controls = new List<WidgetElement>
        {
            UI.Text("Discord · Native prototype", "discord.title").Classes("heading"),
            UI.Row("discord.tools", UI.Button("Refresh", "refresh", "discord.refresh"),
                UI.Button("Open Vesktop", "show", "discord.show")),
        };
        if (s?.Ready != true)
            controls.Add(UI.Text(snapshot.Error?.Message ?? (s is null ? "Connecting to Vesktop…" : "Sign in through the separate Vesktop window."), "discord.status"));
        else
        {
            controls.Add(CallControls(s, "main", busy));
            if (p.Notice.Length > 0) controls.Add(UI.Text(p.Notice, "discord.notice"));
            if (s.SharePending) controls.Add(UI.Text("Choose a window and audio in Vesktop. Sharing has not been confirmed yet.", "discord.share.pending"));
            if (s.ShareError is not null) controls.Add(UI.Text("Sharing was canceled or could not start. Check Vesktop before trying again.", "discord.share.error"));
            controls.Add(UI.Row("discord.sharing",
                UI.Button(s.Sharing ? "Sharing active" : "Share game…", "share", "discord.share")
                    .Disabled(busy || s.ChannelId is null || s.Sharing || s.SharePending || !s.Capabilities.Share),
                UI.Button(s.SharePending ? "Cancel sharing" : "Stop sharing", "stopShare", "discord.share.stop")
                    .Disabled(busy || !(s.Sharing || s.SharePending) || !s.Capabilities.StopShare)));
            controls.Add(UI.Text("Participants", "discord.participants.title"));
            foreach (var user in s.Participants.Take(30)) controls.Add(UI.Text(user.Name + (user.Speaking ? " · Speaking" : "") +
                (user.Muted ? " · Muted" : "") + (user.Deafened ? " · Deafened" : ""), "discord.participant." + user.Id));
            controls.Add(UI.Text("Incoming streams · Select one, then pin the Stream layout from the tray.", "discord.streams.title"));
            foreach (var stream in s.Streams.Take(20)) controls.Add(UI.Button(stream.Name, "watch." + stream.UserId, "discord.watch." + stream.UserId)
                .Selected(p.StreamKey == stream.Key).Disabled(busy || !s.Capabilities.Watch));
            controls.Add(UI.Button("Stop watching", "unwatch", "discord.unwatch").Disabled(p.StreamKey is null));
            controls.Add(UI.TextEntry(p.Query, "Search voice channels", "search", "discord.search", 120));
            var filtered = s.Channels.Where(c => (c.Guild + " " + c.Name).Contains(p.Query, StringComparison.OrdinalIgnoreCase)).ToArray();
            controls.Add(UI.Text($"Voice channels · {filtered.Length}" + (filtered.Length > 32 ? " · Showing first 32; refine search" : ""), "discord.channels.title"));
            foreach (var c in filtered.Take(32)) controls.Add(UI.Button(c.Guild + " / " + c.Name, "join." + c.Id, "discord.channel." + c.Id)
                .Selected(c.Id == s.ChannelId).Disabled(busy || !s.Capabilities.Voice));
        }
        var pinContent = p.Receiver is { } receiver
            ? (WidgetElement)UI.WebView(receiver.Reference, "discord.video", "Discord shared screen", BrowserInteractionMode.ActivateToInteract)
            : UI.Text(p.StreamName is null ? "Select a stream in Discord controls." : p.Notice.Length > 0 ? p.Notice : "Connecting to " + p.StreamName + "…", "discord.pin.status");
        return new(UI.VerticalScroll("discord.root", controls.ToArray()), "discord.refresh", Surface: new()
        {
            PreferredWidth = 740, PreferredHeight = 690, MinimumWidth = 400, MinimumHeight = 360,
            WidthMode = WidgetSurfaceAxisMode.Preferred, HeightMode = WidgetSurfaceAxisMode.Preferred
        }) { PinnedLayouts = [streamPin.Present(pinContent), callPin.Present(CallControls(s, "pin", busy))] };
    }

    private static WidgetElement CallControls(DiscordState? s, string prefix, bool busy)
    {
        var id = "discord." + prefix;
        var inCall = s?.Ready == true && s.ChannelId is not null;
        return UI.Stack(id + ".call", UI.Text(inCall ? "Voice · " + s!.ChannelName : "Not in a voice channel", id + ".status"),
            UI.Row(id + ".actions",
                UI.Button(s?.Muted == true ? "Unmute" : "Mute", "mute", id + ".mute").Disabled(busy || !inCall || s?.Capabilities.Mute != true),
                UI.Button(s?.Deafened == true ? "Undeafen" : "Deafen", "deafen", id + ".deafen").Disabled(busy || !inCall || s?.Capabilities.Deafen != true),
                UI.Button("Disconnect", "leave", id + ".leave").Disabled(busy || !inCall)));
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        var s = state.Snapshot.Value;
        if (action.ActionId == "refresh") { state.Refresh(); return ValueTask.CompletedTask; }
        if (action.ActionId == "search") { presentation.Update(p => p with { Query = action.CommittedText ?? "" }); return ValueTask.CompletedTask; }
        if (action.ActionId == "unwatch") { presentation.Update(p => p with { StreamKey = null, StreamName = null }); RestartRelay(); return ValueTask.CompletedTask; }
        if (action.ActionId.StartsWith("watch.", StringComparison.Ordinal) && s?.Ready == true)
        {
            var stream = s.Streams.FirstOrDefault(candidate => "watch." + candidate.UserId == action.ActionId);
            if (stream is not null)
            {
                presentation.Update(p => p with { StreamKey = stream.Key, StreamName = stream.Name, Account = s.Account,
                    Notice = "Use the tray’s Pin command and choose Stream to watch." });
                RestartRelay();
            }
            return ValueTask.CompletedTask;
        }
        var method = action.ActionId.StartsWith("join.", StringComparison.Ordinal) ? "join" : action.ActionId;
        if (method is not ("show" or "join" or "leave" or "mute" or "deafen" or "share" or "stopShare")) return ValueTask.CompletedTask;
        if (method != "show" && (s?.Ready != true || state.Snapshot.Status == WidgetResourceStatus.Error)) return ValueTask.CompletedTask;
        var target = method == "join" ? action.ActionId[5..] : null;
        if (target is not null && !s!.Channels.Any(c => c.Id == target)) return ValueTask.CompletedTask;
        Operations.RunSingleFlight("discord.command", async context =>
        {
            presentation.Update(p => p with { Notice = "" });
            try
            {
                await client.InvokeAsync(method, new { account = s?.Account, channelId = s?.ChannelId, target,
                    value = method == "mute" ? s?.Muted != true : s?.Deafened != true }, context.CancellationToken);
                if (context.IsCurrent) presentation.Update(p => p with { Notice = method == "share"
                    ? "Choose what to share in Vesktop. Audio may include other applications; game-only audio is not yet verified."
                    : "Request sent; status below reflects Discord’s reported state." });
            }
            catch (Exception) when (!context.CancellationToken.IsCancellationRequested)
            {
                if (context.IsCurrent) presentation.Update(p => p with { Notice = "Could not confirm the action. Refresh or check Vesktop; it has not been retried." });
            }
            finally { state.Refresh(); }
        }, WidgetOperationLifetime.Widget);
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnPinnedLayoutSelectionChangedAsync(string? layoutId, CancellationToken cancellationToken = default)
    { RestartRelay(); return ValueTask.CompletedTask; }

    private void RestartRelay() => Operations.RunLatest("discord.relay", async context =>
    {
        if (!context.IsCurrent) return;
        await CloseReceiverAsync(context.CancellationToken);
        var p = presentation.Value;
        var s = state.Snapshot.Value;
        if (!streamPin.IsSelected || p.StreamKey is null || s?.Ready != true || s.Account != p.Account || s.ChannelId is null) return;
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, streamPin.SelectionCancellationToken);
        var token = scope.Token;
        WidgetWebSession? receiver = null;
        var lease = Guid.NewGuid().ToString("N");
        var phase = "receiver-start";
        try
        {
            receiver = await HostServices.WebSessions.OpenAsync(new("discord.receiver", "Discord stream", WebSessionSource.Package("web/receiver.html",
                [new() { Path = "web/receiver.html", ContentType = "text/html" }, new() { Path = "web/receiver.js", ContentType = "text/javascript" },
                 new() { Path = "web/web-session.js", ContentType = "text/javascript" }])
                with { AllowAutoplay = true }), token);
            // A request can arrive before the page has loaded its handlers. The
            // explicit ready message below is consumed by the SDK session reader.
            using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            readyTimeout.CancelAfter(TimeSpan.FromSeconds(12));
            phase = "receiver-ready";
            var pageReady = false;
            await foreach (var message in receiver.ReadMessagesAsync(readyTimeout.Token))
                if (message.TryGetProperty("ready", out var ready) && ready.GetBoolean()) { pageReady = true; break; }
            if (!pageReady) throw new IOException("receiver_closed");
            phase = "incoming-track";
            var offer = await client.InvokeAsync("watch", new { account = s.Account, channelId = s.ChannelId, key = p.StreamKey, lease }, token);
            phase = "receiver-answer";
            var answer = await receiver.RequestAsync<JsonElement, JsonElement>("offer", offer, token);
            phase = "sender-answer";
            await client.InvokeAsync("answer", new { account = s.Account, channelId = s.ChannelId, lease, answer = answer.GetProperty("answer") }, token);
            if (!context.IsCurrent || token.IsCancellationRequested) return;
            var published = presentation.Update(current => context.IsCurrent && !token.IsCancellationRequested
                ? (current with { Receiver = receiver, Lease = lease, Notice = "" }, true) : (current, false)).Result;
            if (published) receiver = null; // Model owns the session until replacement/shutdown.
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            Console.Error.WriteLine($"Discord relay failed: {phase}/{error.GetType().Name}");
            if (context.IsCurrent) presentation.Update(current => current with { Notice = $"Stream could not connect ({phase}). Select it again to retry." });
        }
        finally
        {
            if (receiver is not null) await receiver.DisposeAsync();
            if (presentation.Value.Lease != lease)
            {
                try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await client.InvokeAsync("unwatch", new { account = s.Account, lease }, cleanup.Token); }
                catch (Exception) { /* Adapter lease expires even if the connection died. */ }
            }
        }
    }, WidgetOperationLifetime.Widget);

    private async Task CloseReceiverAsync(CancellationToken token)
    {
        var old = presentation.Update(p => (p with { Receiver = null, Lease = null }, p)).Result;
        if (old.Receiver is not null) await old.Receiver.DisposeAsync();
        if (old.Lease is not null)
            try { await client.InvokeAsync("unwatch", new { account = old.Account, lease = old.Lease }, token); }
            catch (Exception) when (!token.IsCancellationRequested) { }
    }

    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        try
        {
            await CloseReceiverAsync(shutdownToken);
            if (state.Snapshot.Value is { Ready: true } s)
                try { await client.InvokeAsync("cancelPendingShare", new { account = s.Account }, shutdownToken); }
                catch (Exception) when (!shutdownToken.IsCancellationRequested) { }
        }
        finally { client.Dispose(); }
    }
}
