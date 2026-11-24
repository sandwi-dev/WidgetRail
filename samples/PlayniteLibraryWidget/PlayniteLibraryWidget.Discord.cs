using System.Globalization;
using System.Text.Json;
using WidgetRail.Samples.Integrations;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string DiscordPermissionAction = "playnite.settings.discord-access";
    private const string DiscordReadRetry = "playnite.discord.retry-read";
    private const string DiscordStart = "playnite.discord.start";
    private const string DiscordObserver = "playnite.discord-summary";
    private sealed record DiscordShelfVisit(string Game, CancellationToken Route);
    private DiscordShelfVisit? discordDemand;

    private void ClearDiscordShelf()
    {
        lock (_gate)
        {
            discordDemand = null;
            Operations.Cancel(DiscordObserver);
            _model.Update(s => s.DiscordSummary is null && s.DiscordPanelGame is null && s.DiscordStart == DiscordStartState.Idle ? s :
                s with { DiscordSummary = null, DiscordPanelGame = null, DiscordStart = DiscordStartState.Idle });
        }
    }

    private void ObserveDiscordShelf()
    {
        var route = _navigation.Value;
        if (!IsActive || route.Route != PlayniteLibraryRoute.Library || PlayingId(_model.Value) is not { } game ||
            HiddenCards.HasFlag(PlayingCardKind.Discord))
        {
            ClearDiscordShelf();
            return;
        }
        var demand = new DiscordShelfVisit(game, route.RouteCancellationToken);
        lock (_gate)
        {
            if (discordDemand == demand) return;
            discordDemand = demand;
            var observer = Operations.RunLatest(DiscordObserver, context => ReadDiscordShelfAsync(demand, context));
            if (!observer.IsAccepted) discordDemand = null;
        }
    }

    private async ValueTask ReadDiscordShelfAsync(DiscordShelfVisit demand, WidgetOperationContext context)
    {
        using var visit = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, demand.Route);
        void Set(DiscordActivitySummary? summary, bool accessLost = false, bool running = false, bool checkedProvider = true)
        {
            lock (_gate)
            {
                if (!context.IsCurrent || !ReferenceEquals(discordDemand, demand) || visit.IsCancellationRequested) return;
                _model.Update(s => s with
                {
                    DiscordActionNotice = summary?.Session == s.DiscordSummary?.Session ? s.DiscordActionNotice : null,
                    DiscordStart = summary is not null || accessLost ? DiscordStartState.Idle :
                        !checkedProvider ? DiscordStartState.ReadFailed :
                        s.DiscordProviderRunning && !running ? DiscordStartState.Retry : s.DiscordStart,
                    DiscordSummary = summary, DiscordWide = summary is { Count: > 2 },
                    DiscordNeedsConsent = accessLost, DiscordProviderRunning = running, DiscordSummaryChecked = checkedProvider,
                    DiscordPanelGame = summary is null or { Mode: "setup" } or { ConnectionFailed: true } || summary.Session != s.DiscordSummary?.Session
                        ? null : s.DiscordPanelGame
                });
            }
        }
        try
        {
            var capabilities = HostServices.Capabilities;
            await using var changes = await capabilities.OpenSubscriptionAsync(WidgetSummaryCapabilities.ChangedFrom(DiscordActivityIntegration.Provider), visit.Token);
            async ValueTask Read()
            {
                var reply = await capabilities.InvokeAsync(WidgetSummaryCapabilities.ReadFrom(DiscordActivityIntegration.Provider),
                    new(DiscordActivityIntegration.Provider), visit.Token);
                Set(reply?.Value?.Deserialize<DiscordActivitySummary>() is { } value && ValidDiscordSummary(value) &&
                    Guid.TryParseExact(reply.ReaderProof, "N", out _) ? value with { ReaderProof = reply.ReaderProof } : null, running: reply?.ProviderRunning == true, checkedProvider: reply is not null);
            }
            await Read();
            await foreach (var _ in changes.ReadAllAsync(visit.Token)) await Read();
            Set(null, accessLost: true);
        }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or JsonException or InvalidOperationException or NotSupportedException)
        {
            Set(null, accessLost: error is WidgetCapabilityException { ErrorCode: "permission_denied" or "capability_revoked" }, checkedProvider: false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(discordDemand, demand) && visit.IsCancellationRequested)
                {
                    discordDemand = null;
                    _model.Update(s => s with { DiscordSummary = null, DiscordPanelGame = null, DiscordStart = DiscordStartState.Idle });
                }
            }
        }
    }

    private static bool ValidDiscordSummary(DiscordActivitySummary value) =>
        Guid.TryParseExact(value.Session, "N", out _) && value.Mode is "friends" or "call" or "setup" or "unavailable" &&
        ShortText(value.Title, 240) && value.Count is >= 0 and <= 100 &&
        value.People is { Length: <= 6 } people && people.Length == Math.Min(value.Count, 6) &&
        people.All(p => p is not null && p.Key is { Length: > 0 and <= 32 } && p.Key.All(char.IsAsciiDigit) &&
            ShortText(p.Name, 120) && ShortText(p.Detail, 600) &&
            (p.Action is null && p.Token is null || p.Action is { } action && Enum.IsDefined(action) && Guid.TryParseExact(p.Token, "N", out _))) &&
        people.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() == people.Length;

    private static bool ShortText(string? text, int max) => text is not null && text.Length <= max && !text.Any(char.IsControl);

    private static WidgetElement DiscordAvatarView(DiscordActivityPerson person, string id)
    {
        var layers = new List<WidgetElement> { UI.Text(string.IsNullOrWhiteSpace(person.Name) ? "?" : StringInfo.GetNextTextElement(person.Name.Trim()),
            id + ".initial", person.Name).Classes("playing-discord-initial").AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center) };
        if (person.Avatar is { Length: <= 160 } avatar && Uri.TryCreate(avatar, UriKind.Absolute, out var uri) &&
            uri is { Scheme: "https", Host: "cdn.discordapp.com", IsDefaultPort: true, UserInfo.Length: 0, Fragment.Length: 0 } &&
            (uri.AbsolutePath.StartsWith("/avatars/" + person.Key + "/", StringComparison.Ordinal) ||
             uri.AbsolutePath.StartsWith("/embed/avatars/", StringComparison.Ordinal)))
            layers.Add(UI.PublicImage(avatar, id + ".photo", person.Name).Classes("playing-discord-photo"));
        return UI.Grid(id, [GridTrack.Star()], [GridTrack.Star()], layers.ToArray()).Classes("playing-discord-avatar", "playing-discord-avatar-fallback");
    }

    private static ActionSurfaceElement DiscordShelfCard(string id, PlayniteLibraryRenderState state)
    {
        var summary = state.DiscordSummary;
        var wide = state.DiscordWide;
        var people = summary?.People ?? [];
        var title = summary?.ConnectionFailed == true ?
            state.DiscordStart == DiscordStartState.Starting ? "Connecting to Discord" : "Retry Discord connection" : summary?.Title ?? (state.DiscordNeedsConsent ? "Connect Discord" :
            state.DiscordStart is DiscordStartState.Retry or DiscordStartState.ReadFailed ? "Retry Discord connection" :
            state.DiscordStart == DiscordStartState.Starting || !state.DiscordSummaryChecked || state.DiscordProviderRunning
                ? "Connecting to Discord" : "Start Discord");
        if (summary is null or { Mode: "setup" } or { ConnectionFailed: true })
        {
            var launcher = UI.Grid(id + ".face", [GridTrack.Star(), GridTrack.Auto()], [GridTrack.Star()],
                UI.Icon(PlayniteIcons.Discord, id + ".brand", "Discord").Classes("playing-discord-launch-mark")
                    .AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center),
                UI.Text(title, id + ".title").Classes("playing-card-title").InGrid(row: 1))
                .Classes("playing-card-face", "playing-discord-face");
            var action = state.DiscordNeedsConsent ? DiscordPermissionAction :
                state.DiscordStart == DiscordStartState.ReadFailed ? DiscordReadRetry : id;
            var card = UI.ActionSurface(action, id, title, ActionSurfaceOrientation.Vertical, launcher);
            if (summary?.ConnectionFailed == true || summary is null && action == id &&
                (state.DiscordStart != DiscordStartState.Idle || state.DiscordSummaryChecked && !state.DiscordProviderRunning))
                card = card with { Intent = WidgetIntentRequest.Create(DiscordActivityIntegration.Open,
                    DiscordActivityIntegration.OpenPayload, WidgetIntentPresentation.StayInSource) with
                    { PreparationKey = DiscordStart, ReportsResult = true } };
            return card.Classes("playing-action", wide ? "playing-discord-wide" : "playing-discord-compact", "playing-discord-launcher");
        }
        var header = new List<WidgetElement>
        {
            UI.Icon(PlayniteIcons.Discord, id + ".brand", "Discord").Classes("playing-discord-brand"),
            UI.Text(summary.Mode == "call" ? $"{title} · {summary.Count}" : people.Length == 0 ? "Discord" : title, id + ".title")
                .Classes("playing-card-title", "playing-discord-title").InGrid(column: 1)
        };
        var more = summary.Count - Math.Min(people.Length, 4);
        if (more > 0 || summary is { Truncated: true })
        {
            header.AddRange(people.Skip(4).Select((p, i) => DiscordAvatarView(p, id + ".extra." + i)
                .AddClasses("playing-discord-small-avatar").InGrid(column: 2 + i)));
            header.Add(UI.Text(summary!.Truncated ? "…" : "+" + more, id + ".count",
                summary.Truncated ? "More people" : more + " more people").Classes("playing-discord-count").InGrid(column: 4));
        }
        var nodes = new List<WidgetElement> { UI.Grid(id + ".header", [GridTrack.Auto()],
            [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto(), GridTrack.Auto(), GridTrack.Auto(), GridTrack.Pixel(24)], header.ToArray())
            .Spacing(0, 4).Classes("playing-discord-heading").InGrid(columnSpan: wide ? 4 : 2) };
        for (var i = 0; i < Math.Min(4, people.Length); i++)
        {
            var person = people[i];
            var key = id + ".person." + person.Key;
            var row = 1 + 2 * (wide ? i / 2 : i);
            var col = wide ? 2 * (i % 2) : 0;
            nodes.Add(DiscordAvatarView(person, key + ".avatar").InGrid(row, col, rowSpan: 2).AlignInGrid(GridCellAlignment.Start, GridCellAlignment.Center));
            nodes.Add(UI.Text(person.Name, key + ".name").Classes("playing-discord-name")
                .InGrid(row, col + 1).AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.End));
            nodes.Add(UI.Text(DiscordPersonStatus(person, summary.Mode == "call"), key + ".detail").Classes("playing-card-meta")
                .InGrid(row + 1, col + 1).AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.Start));
        }
        if (people.Length == 0)
            nodes.Add(UI.Text(summary.Mode == "call" ? "No participants" :
                summary.Mode == "unavailable" ? "Activity unavailable" : "No friends active", id + ".empty")
                .Classes("playing-card-meta").InGrid(row: 1, columnSpan: wide ? 4 : 2));
        var face = UI.Grid(id + ".face", [GridTrack.Auto(), GridTrack.Star(), GridTrack.Star(), GridTrack.Star(), GridTrack.Star()],
            wide ? [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto(), GridTrack.Star()] : [GridTrack.Auto(), GridTrack.Star()],
            nodes.ToArray()).Spacing(2, 8).Classes("playing-card-face", "playing-discord-face");
        var inCall = summary.Mode == "call";
        var accessible = new List<string> { "Discord", title };
        if (inCall) accessible.Add($"{summary.Count} {(summary.Count == 1 ? "participant" : "participants")}");
        accessible.AddRange(people.Take(4).Select(p => p.Name + ", " + DiscordPersonStatus(p, inCall)));
        if (summary.Truncated) accessible.Add("More people");
        else if (more > 0) accessible.Add($"{more} more");
        return UI.ActionSurface(id, id, string.Join(", ", accessible),
            ActionSurfaceOrientation.Vertical, face).Classes("playing-action", wide ? "playing-discord-wide" : "playing-discord-compact");
    }

    private static WidgetElement DiscordPersonCopy(DiscordActivityPerson p, string key, bool inCall) =>
        UI.Stack(key + ".copy", UI.Text(p.Name, key + ".name").Classes("playing-discord-name"),
            UI.Text(DiscordPersonStatus(p, inCall), key + ".detail").Classes("playing-card-meta")).Classes("playing-discord-copy");

    private static string DiscordPersonStatus(DiscordActivityPerson p, bool inCall) =>
        p.Streaming && inCall ? "Live · " + p.Detail : p.Detail;

    public override ValueTask<JsonElement?> OnPrepareIntentAsync(string key, WidgetIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (key != DiscordStart || !DiscordActivityIntegration.IsOpenRequest(request))
            return base.OnPrepareIntentAsync(key, request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Keep the same declared action during preparation so the host can revalidate the press.
        _model.Update(s => s with { DiscordStart = DiscordStartState.Starting });
        return ValueTask.FromResult<JsonElement?>(request.Payload);
    }

    public override ValueTask<bool> OnIntentCompletedAsync(WidgetIntentFeedback feedback, CancellationToken cancellationToken = default)
    {
        if (DiscordActivityIntegration.IsOpenRequest(feedback.Request))
        {
            if (feedback.Status != WidgetIntentStatus.Accepted)
                _model.Update(s => s.DiscordStart == DiscordStartState.Starting
                    ? s with { DiscordStart = DiscordStartState.Retry } : s);
            return ValueTask.FromResult(false);
        }
        if (feedback.Request.ContractId != DiscordActivityIntegration.Perform.Id ||
            !feedback.Request.Payload.TryGetProperty("reader", out var proof)) return ValueTask.FromResult(false);
        cancellationToken.ThrowIfCancellationRequested();
        _model.Update(s => s.DiscordPanelGame is not null && s.DiscordSummary?.ReaderProof == proof.GetString()
            ? s with { DiscordActionNotice = feedback.Status is WidgetIntentStatus.Rejected or WidgetIntentStatus.Unavailable or WidgetIntentStatus.Failed
                ? feedback.Request.Payload.GetProperty("pin").GetBoolean() ? "This stream could not be pinned." : "Could not join this voice channel."
                : null } : s);
        return ValueTask.FromResult(false);
    }

    private static WidgetModal DiscordShelfPanel(string id, DiscordActivitySummary summary, string? notice)
    {
        var rows = new List<WidgetElement>();
        string? first = null;
        foreach (var p in summary.People)
        {
            var key = id + ".row." + p.Key;
            var contents = new List<WidgetElement> { DiscordAvatarView(p, key + ".avatar"), DiscordPersonCopy(p, key, summary.Mode == "call").InGrid(column: 1) };
            if (p.ActionLabel is { } label) contents.Add(UI.Text(label, key + ".action").Classes("playing-discord-command").InGrid(column: 2)
                .AlignInGrid(GridCellAlignment.End, GridCellAlignment.Center));
            var grid = UI.Grid(key + ".content", [GridTrack.Auto()], [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto()],
                contents.ToArray()).Spacing(0, 12).Classes("playing-discord-row");
            if (p.Token is null) rows.Add(grid);
            else
            {
                first ??= key;
                var intent = WidgetIntentRequest.Create(DiscordActivityIntegration.Perform, JsonSerializer.SerializeToElement(new
                {
                    token = p.Token, reader = summary.ReaderProof,
                    pin = p.Action == DiscordSummaryAction.WatchAndPin, microphone = p.Action == DiscordSummaryAction.JoinVoice
                }), WidgetIntentPresentation.StayInSource) with { ReportsResult = true };
                var row = UI.ActionSurface(key, key, p.Name + ", " + DiscordPersonStatus(p, summary.Mode == "call") + ", " + p.ActionLabel,
                    ActionSurfaceOrientation.Vertical, grid) with { Intent = intent };
                rows.Add(row.Classes("playing-discord-row-action"));
            }
        }
        var context = summary.Mode == "call" ? $"In voice · {summary.Count} {(summary.Count == 1 ? "participant" : "participants")}" :
            summary.Count > 0 ? "Discord" : summary.Mode == "unavailable" ? "Open Discord to check activity." : "No friends active";
        if (summary.Count > summary.People.Length || summary.Truncated) context += $" · Showing {summary.People.Length}";
        var footer = UI.Row(id + ".footer",
            UI.Button("Close", id + ".close", id + ".close")).Classes("playing-discord-footer");
        var body = UI.Grid(id + ".body", [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto()], [GridTrack.Star()],
            UI.Text(notice ?? context, id + ".context").Classes("playing-card-meta"),
            UI.VerticalScroll(id + ".scroll", UI.Stack(id + ".people", rows.ToArray()).Classes("playing-discord-rows")).InGrid(row: 1),
            footer.InGrid(row: 2)).Spacing(8, 0);
        return new(id + ".panel", summary.Title, body, first ?? id + ".close", id + ".close")
        { ScrollContent = false, StyleClass = "playing-discord-dialog", HeaderActions = UI.ControllerHint(ControllerButton.B, "Close", id + ".hint") };
    }
}
