using WidgetRail.Playnite;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record CaptureGameChoice(string Id, string Title, string Source);
internal sealed record CaptureGameCatalog(string SuggestionSource, CaptureGameChoice[] Games, TextSuggestionCatalog Suggestions);
internal sealed record CaptureGameChooser(string Opening, string Connection, string Profile,
    IReadOnlyList<SavedCaptureMediaReference> Captures, string Text = "", string? Message = null, CaptureGameChoice? Destination = null)
{
    internal WidgetFocusRequest? FocusRequest { get; init; }
    internal int Linked { get; init; }
    internal int AlreadyLinked { get; init; }
    internal string? ProcessName { get; init; }
    internal bool MixedProcesses { get; init; }
}

public sealed partial class PlayniteLibraryWidget
{
    private const string GamePicker = "playnite-library.capture-game";
    private const string GameResults = GamePicker + ".results";
    private const string GameSearchField = GamePicker + ".search";
    private const string AssociateWork = GamePicker + ".associate";
    private static readonly CompiledWidgetIntentContract captureAssociation = CompiledWidgetIntentContract.Create(WidgetIntentContracts.CaptureAssociation);
    private readonly WidgetQueryResource<string, CaptureGameCatalog> captureGames;
    private readonly WidgetLoadedCollection<string, CaptureGameChoice> gameChoiceRows;
    private readonly WidgetNotificationPublisher gameLinkFeedback;

    private static SavedCaptureMediaReference[]? ReadCaptureOffer(WidgetIntentRequest request)
    {
        if (!request.IsWellFormed() || request.ContractId != captureAssociation.Id || request.Version != captureAssociation.Version ||
            request.SchemaDigest != captureAssociation.SchemaDigest || !captureAssociation.Accepts(request.Payload)) return null;
        var items = new List<SavedCaptureMediaReference>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Payload.GetProperty("captures").EnumerateArray())
        {
            if (!item.GetProperty("revision").TryGetInt64(out var revision)) return null;
            var capture = new SavedCaptureMediaReference(item.GetProperty("id").GetString()!, revision);
            if (!capture.IsWellFormed() || !ids.Add(capture.Id)) return null;
            items.Add(capture);
        }
        return items.ToArray();
    }

    private bool CanOpenGameChooser(PlayniteLibraryRenderState state) =>
        state.GameChooser is null && !state.CaptureSettings.Open && !state.SetupDialogOpen && state.FilterDraft is null &&
        !state.DetailsExtras.OperationBusy && !state.DetailsExtras.ConfirmUninstall && !state.DetailsExtras.ChoosingCompletion &&
        state.DetailsExtras.CaptureViewer is null && state.DetailsExtras.CapturePickerOffset is null &&
        _navigation.Value.Route != PlayniteLibraryRoute.PlayniteConnection && !Operations.IsBusy(CaptureWork);

    private async ValueTask<PlayniteCaptureIntegration?> CaptureChooserConnectionAsync(CancellationToken token)
    {
        try
        {
            var access = await HostServices.Permissions.CheckAsync([WidgetCaptureLibraryCapabilities.ReadPermission,
                WidgetCaptureLibraryCapabilities.ManagePermission], token);
            if (access.Count != 2 || access.Any(permission => permission.State != WidgetPermissionState.Granted)) return null;
            return await _application.GetCaptureIntegrationAsync(token);
        }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or
            PlayniteBridgeDataException or PlayniteBridgeTransportException) { return null; }
    }

    private WidgetQueryResource<string, CaptureGameCatalog> CreateCaptureGames() =>
        CreateQueryResource<string, CaptureGameCatalog>(GamePicker + ".games", new()
        {
            Load = async (opening, token) =>
            {
                var catalog = await _application.CaptureQueryAsync(new(false), new(PlayniteLibraryQueryScope.Library), false, token);
                var games = catalog.Games.Where(game => Guid.TryParseExact(game.Id, "D", out _))
                    .Select(game => new CaptureGameChoice(game.Id, game.Name, game.Source)).ToArray();
                var titles = games.DistinctBy(game => game.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(game => new TextSuggestion(game.Id, RelatedCaptureLabel(game.Title, TextSuggestionContract.MaximumLabelLength), game.Title))
                    .Where(item => TextSuggestionContract.IsValid(new([item]), 1, WidgetAppLibraryQuery.MaximumSearchTextLength))
                    .Take(TextSuggestionCatalog.MaximumEntries);
                return new("playnite.capture.games." + opening, games, new(titles, TextSuggestionMatchField.Value));
            },
            MapError = _ => new("game_search_failed", "The Playnite library could not be searched."),
        });

    private WidgetLoadedCollection<string, CaptureGameChoice> CreateGameChoiceRows() =>
        CreateLoadedCollection<string, CaptureGameChoice>(GameResults + ".source", "closed", "closed", [], new()
        {
            ItemKey = game => new(game.Id),
            RenderItem = (key, game, ids) => UI.ActionSurface(key + ".link", ids.Id("row"), "Link to " + game.Title + ", " + game.Source,
                ActionSurfaceOrientation.Horizontal,
                UI.Stack(ids.Id("labels"),
                    UI.Text(RelatedCaptureLabel(game.Title, 120), ids.Id("name")).Classes("playnite-capture-card-title"),
                    UI.Text(game.Source, ids.Id("source")).Classes("playnite-library-details-meta")).Classes("playnite-capture-card-labels"),
                UI.ControllerHint(ControllerButton.A, "Link", ids.Id("hint")).AddClasses("playnite-game-link-hint"))
                .Classes("playnite-capture-card"),
            OnAction = (key, game, action, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (_model.Value.GameChooser is { Destination: null } chooser && LifecycleState == WidgetLifecycleState.Interactive &&
                    action.ActionId == key + ".link" && GameChoiceKey(chooser) == key && !Operations.IsBusy(AssociateWork))
                {
                    var chosen = chooser with { Destination = game, Message = null, FocusRequest = null };
                    if (_model.Update(current => current.GameChooser == chooser ? current with { GameChooser = chosen } : current).Changed)
                        LinkOfferedCaptures(chosen);
                }
                return ValueTask.CompletedTask;
            },
        });

    private ValueTask<TextSuggestionResult> SuggestCaptureGames(TextSuggestionQuery query, CancellationToken token)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive || _model.Value.GameChooser is not { Destination: null } chooser ||
            captureGames.GetSnapshot(chooser.Opening).Value is not { } catalog || catalog.SuggestionSource != query.SourceKey)
            return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
        var result = catalog.Suggestions.Search(query, token);
        return ValueTask.FromResult(_model.Value.GameChooser == chooser && LifecycleState == WidgetLifecycleState.Interactive &&
            ReferenceEquals(catalog, captureGames.GetSnapshot(chooser.Opening).Value) ? result : TextSuggestionResult.NotAvailable);
    }

    private async ValueTask<WidgetIntentResult> OpenCaptureGamesAsync(WidgetIntentRequest request, CancellationToken token)
    {
        var scope = _navigation.Value.InputScopeId;
        if (!IsActive || !CanOpenGameChooser(_model.Value) || ReadCaptureOffer(request) is not { } items ||
            await CaptureChooserConnectionAsync(token) is not { } connection) return WidgetIntentResult.Rejected;
        token.ThrowIfCancellationRequested();
        if (!IsActive || scope != _navigation.Value.InputScopeId) return WidgetIntentResult.Rejected;
        var state = new CaptureGameChooser(Guid.NewGuid().ToString("N"), connection.ConnectionId, connection.ProfileId, items);
        if (!CanOpenGameChooser(_model.Value)) return WidgetIntentResult.Rejected;
        var opened = _model.Update(current => current.GameChooser is null && !current.CaptureSettings.Open && !current.SetupDialogOpen &&
            current.FilterDraft is null ? current with { GameChooser = state } : current);
        if (!opened.Changed) return WidgetIntentResult.Rejected;
        captureGames.EnsureLoaded(state.Opening, _navigation.Value.RouteCancellationToken);
        return WidgetIntentResult.Accepted;
    }

    private static string GameChoiceKey(CaptureGameChooser state) => GamePicker + "." + CaptureRelatedIdentity.Hash(
        $"{state.Opening}/{state.Text}/{state.Destination?.Id}/{state.Captures.Count}/{state.FocusRequest?.Id}")[..20];

    private WidgetView RenderCaptureGames(WidgetView background, CaptureGameChooser state)
    {
        var key = GameChoiceKey(state);
        var quantity = state.Captures.Count == 1 ? "1 capture" : $"{state.Captures.Count} captures";
        var close = key + ".close";
        if (state.Destination is { } destination)
        {
            var busy = Operations.IsBusy(AssociateWork);
            var canRetry = state.Captures.Count > 0 && !busy;
            var rows = new List<WidgetElement>();
            if (busy) rows.Add(UI.LoadingIndicator(GamePicker + ".progress", size: LoadingIndicatorSize.Compact));
            rows.Add(UI.Text(state.Message ?? (busy ? $"Linking {quantity} to {destination.Title}…" :
                "Linking was interrupted. Retry to finish."), GamePicker + ".message"));
            var retry = GamePicker + ".retry-link";
            if (canRetry) rows.Add(UI.Button("Retry remaining captures", key + ".commit", retry));
            var status = background.WithModal(new(GamePicker + ".status", "Link captures", UI.Stack(GamePicker + ".status.body", rows.ToArray()),
                canRetry ? retry : GamePicker + ".cancel", close)
            {
                HeaderActions = UI.Button(busy ? "Cancel" : "Close", close, GamePicker + ".cancel"),
                StyleClass = "playnite-link-status",
            });
            return canRetry && state.FocusRequest is { } retryFocus
                ? status with { FocusGroupEntryRequest = retryFocus.FocusElement(GamePicker + ".status", retry) }
                : status;
        }

        var snapshot = captureGames.GetSnapshot(state.Opening);
        var field = UI.TextEntry(state.Text, "Search games", key + ".search", GameSearchField, WidgetAppLibraryQuery.MaximumSearchTextLength);
        WidgetElement results;
        if (snapshot.Value is { } catalog)
        {
            field = field.Suggest(catalog.SuggestionSource);
            if (gameChoiceRows.Context != key)
            {
                var matches = state.Text.Length == 0 ? catalog.Games :
                    catalog.Games.Where(game => game.Title.Contains(state.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
                gameChoiceRows.Publish(key, key, matches);
            }
            results = gameChoiceRows.Items.Count == 0 ? UI.Text(state.Text.Length == 0 ? "No games in this library." : "No matching games.", GamePicker + ".empty") :
                UI.CollectionList(GameResults, gameChoiceRows, 64, "Games").FocusUp(GameSearchField).Classes("playnite-library-achievement-list");
            if (gameChoiceRows.Items.Count > 0) field = field.FocusDown(GameResults);
        }
        else if (snapshot.Error is { } error)
            results = UI.Stack(GamePicker + ".failure", UI.Text(error.Message, GamePicker + ".error"),
                UI.Button("Try again", key + ".retry", GamePicker + ".retry"));
        else results = UI.LoadingIndicator(GamePicker + ".loading", size: LoadingIndicatorSize.Compact);
        var body = UI.Grid(GamePicker + ".body", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()], field, results.InGrid(row: 1)).Spacing(12, 0);
        var view = background.WithModal(new(GamePicker + ".dialog", "Link " + quantity + " to a game", body, GameSearchField, close)
        {
            ScrollContent = false, StyleClass = "playnite-link-game-picker",
            HeaderActions = UI.Button("Cancel", close, GamePicker + ".cancel"),
        });
        return results.Id == GameResults && state.FocusRequest is { } focus
            ? view with { FocusGroupEntryRequest = focus.EnterCollection(gameChoiceRows, GameResults) } : view;
    }

    private void CloseGameChooser(CaptureGameChooser state)
    {
        if (!_model.Update(current => current.GameChooser == state ? current with { GameChooser = null } : current).Changed) return;
        captureGames.Reset();
        gameChoiceRows.Publish("closed", "closed", []);
    }

    private void HandleCaptureGameAction(CaptureGameChooser state, WidgetActionEvent action)
    {
        var prefix = GameChoiceKey(state) + ".";
        if (LifecycleState != WidgetLifecycleState.Interactive || !action.ActionId.StartsWith(prefix, StringComparison.Ordinal)) return;
        var suffix = action.ActionId[prefix.Length..];
        if (suffix == "close")
        {
            if (Operations.IsBusy(AssociateWork)) Operations.Cancel(AssociateWork);
            CloseGameChooser(state);
            return;
        }
        if (Operations.IsBusy(AssociateWork)) return;
        if (state.Destination is not null)
        {
            if (suffix == "commit" && state.Captures.Count > 0) LinkOfferedCaptures(state);
            return;
        }
        if (suffix == "search" && action.CommittedText is { Length: <= WidgetAppLibraryQuery.MaximumSearchTextLength } input)
            _model.Update(current => current.GameChooser == state ? current with { GameChooser = state with { Text = input.Trim(), FocusRequest = CreateFocusRequest() } } : current);
        else if (suffix == "retry") captureGames.EnsureLoaded(state.Opening, _navigation.Value.RouteCancellationToken, true);
    }

    private void LinkOfferedCaptures(CaptureGameChooser state)
    {
        var game = state.Destination!;
        if (!Guid.TryParseExact(game.Id, "D", out var id)) return;
        Operations.RunSingleFlight(AssociateWork, async operation =>
        {
            var token = operation.CancellationToken;
            bool Current() => operation.IsCurrent && _model.Value.GameChooser == state;
            var pending = state.Captures.ToList();
            var applied = state.Linked;
            var existing = state.AlreadyLinked;
            var process = state.ProcessName;
            var mixed = state.MixedProcesses;
            try
            {
                var destination = await _application.GetGameDetailsAsync(game.Id, token);
                var connection = await CaptureChooserConnectionAsync(token);
                if (!Current()) return;
                if (destination?.Item.SavedId != game.Id || connection?.ConnectionId != state.Connection || connection.ProfileId != state.Profile)
                {
                    Finish("Playnite or its permissions changed. Reopen the selection from Capture Library.", [], success: false);
                    return;
                }
                var reference = CaptureRelatedIdentity.Game(state.Profile, id);
                foreach (var capture in state.Captures)
                {
                    if (!Current()) return;
                    try
                    {
                        var item = await HostServices.CaptureLibrary.GetMetadataAsync(capture.Id, token);
                        if (!Current()) return;
                        if (!item.Available || item.Id != capture.Id || item.Revision != capture.Revision) continue;
                        var outcome = await AttachGameAsync(item, reference, destination.Item.Presentation.DisplayName, token, explicitSelection: true);
                        if (outcome is not (CaptureContextSaveOutcome.Saved or CaptureContextSaveOutcome.AlreadySaved)) continue;
                        var name = item.Media.SourceApplication?.ProcessName;
                        mixed |= applied + existing > 0 && !string.Equals(name, process, StringComparison.OrdinalIgnoreCase);
                        process = name;
                        pending.Remove(capture);
                        if (outcome == CaptureContextSaveOutcome.Saved) applied++;
                        else existing++;
                    }
                    catch (WidgetCapabilityException error) when (error.ErrorCode is "capture_library_not_found" or "capture_library_context_conflict" or
                        "capture_library_metadata_conflict" or "capture_library_context_limit" or "capture_unavailable") { }
                }
                if (pending.Count == 0 && !mixed && Current()) await RememberCaptureProcessAsync(reference, process, token);
            }
            catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or PlayniteBridgeDataException or PlayniteBridgeTransportException) { }
            var summary = LinkCounts(applied, existing);
            Finish(pending.Count == 0 ? $"{summary} to {game.Title}." :
                $"{summary} to {game.Title}. {pending.Count} could not be linked.", pending);

            void Finish(string message, IReadOnlyList<SavedCaptureMediaReference> remaining, bool success = true)
            {
                if (!Current()) return;
                if (remaining.Count == 0 && success)
                {
                    CloseGameChooser(state);
                    gameLinkFeedback.Publish(new("game-link", "Playnite Library") { Body = message,
                        Retention = WidgetNotificationRetention.BannerOnly, Interaction = WidgetNotificationInteraction.Informational,
                        Visibility = WidgetNotificationVisibility.Always });
                    return;
                }
                _model.Update(current => current.GameChooser == state ? current with { GameChooser = state with {
                    Message = message, Captures = remaining.ToArray(), Destination = game, FocusRequest = CreateFocusRequest(),
                    Linked = applied, AlreadyLinked = existing, ProcessName = process, MixedProcesses = mixed } } : current);
            }
        }, WidgetOperationLifetime.Active);
    }

    private static string LinkCounts(int linked, int existing) => existing == 0 ? $"{linked} linked" :
        linked == 0 ? $"{existing} already linked" : $"{linked} linked, {existing} already linked";
}
