using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryConnectionState(
    PlayniteBridgeConnectionKind Kind,
    bool Busy,
    string Code,
    bool Interactive,
    PlayniteLibraryConnectionFeedback? Feedback);

internal sealed class PlayniteLibraryConnectionFeedback(
    string title,
    string message,
    ToastTone tone)
{
    internal string Title { get; } = title;
    internal string Message { get; } = message;
    internal ToastTone Tone { get; } = tone;
}

internal static class PlayniteLibraryConnectionPresentation
{
    internal static WidgetView Render(PlayniteLibraryConnectionState state)
    {
        var configured = state.Kind is not (
            PlayniteBridgeConnectionKind.NotConfigured or
            PlayniteBridgeConnectionKind.AuthenticationRequired);
        var (title, detail, tone) = state.Kind switch
        {
            PlayniteBridgeConnectionKind.Connected => (
                "Playnite Bridge connected",
                "Your library is ready to browse.", AlertTone.Success),
            PlayniteBridgeConnectionKind.AuthenticationRequired => (
                "Authentication required",
                "The saved token was rejected. Enter the current Playnite Bridge token.",
                AlertTone.Warning),
            PlayniteBridgeConnectionKind.Incompatible => (
                "Playnite Bridge is incompatible",
                "Update Playnite Bridge to a compatible version, then try again.",
                AlertTone.Warning),
            PlayniteBridgeConnectionKind.Malformed => (
                "Playnite Bridge returned invalid data",
                "Restart Playnite and test the connection again.",
                AlertTone.Danger),
            PlayniteBridgeConnectionKind.Unavailable when state.Code == "permission_required" => (
                "Permission required",
                "Allow local port 19821 and private-secret access in WidgetRail Settings.",
                AlertTone.Warning),
            PlayniteBridgeConnectionKind.Unavailable => (
                "Playnite Bridge unavailable",
                "Start Playnite Bridge locally, then test the connection again.",
                AlertTone.Warning),
            _ => (
                "Token required",
                "Copy the token from Playnite Bridge settings and save it here.",
                AlertTone.Info),
        };
        var enabled = state.Interactive && !state.Busy;
        var token = UI.SensitiveTextEntry(
                configured ? "Enter a replacement Playnite Bridge token" :
                    "Enter the Playnite Bridge token",
                PlayniteLibraryWidget.PlayniteSaveActionId,
                "playnite-library.playnite.token",
                ProtocolConstants.MaximumTextEntryLength)
            .Disabled(!enabled)
            .AddClasses("playnite-library-control", "playnite-library-playnite-token");
        var actions = new List<WidgetElement>
        {
            UI.Button("Test connection", PlayniteLibraryWidget.PlayniteRefreshActionId,
                    PlayniteLibraryWidget.PlayniteRefreshActionId)
                .Disabled(!enabled || !configured)
                .AddClasses("playnite-library-control"),
        };
        if (configured)
            actions.Add(UI.Button("Remove saved token", PlayniteLibraryWidget.PlayniteDeleteActionId,
                    PlayniteLibraryWidget.PlayniteDeleteActionId)
                .Disabled(!enabled)
                .AddClasses("playnite-library-control"));
        var children = new List<WidgetElement>
        {
            UI.Row("playnite-library.playnite.header",
                    UI.Stack("playnite-library.playnite.heading",
                        UI.Text("PLAYNITE LIBRARY", "playnite-library.playnite.eyebrow")
                            .Classes("playnite-library-eyebrow"),
                        UI.Text("Playnite connection", "playnite-library.playnite.title")
                            .Classes("playnite-library-title"))
                    .Classes("playnite-library-playnite-heading"),
                    UI.Button("Back", PlayniteLibraryWidget.PlayniteBackActionId,
                            PlayniteLibraryWidget.PlayniteBackActionId)
                        .Disabled(!state.Interactive)
                        .AddClasses("playnite-library-control"))
                .Classes("playnite-library-header", "playnite-library-playnite-header"),
            UI.Text("Local service · localhost:19821",
                    "playnite-library.playnite.endpoint",
                    "Playnite Bridge local service on port 19821")
                .Classes("playnite-library-source-summary"),
            UI.Alert(title, detail, tone, "playnite-library.playnite.status"),
            UI.Stack("playnite-library.playnite.credential",
                    UI.SectionHeader("Protected token", "playnite-library.playnite.credential.header",
                        description: "Saved securely in Windows Credential Manager."),
                    token)
                .Classes("playnite-library-playnite-card"),
            UI.Row("playnite-library.playnite.actions", actions.ToArray())
                .Classes("playnite-library-actions", "playnite-library-playnite-actions"),
        };
        if (state.Feedback is { } feedback)
            children.Add(UI.Toast(feedback.Title, feedback.Message, feedback.Tone,
                "playnite-library.playnite.feedback"));
        if (state.Busy)
            children.Add(UI.Stack("playnite-library.playnite.busy",
                    UI.LoadingIndicator("playnite-library.playnite.busy.indicator",
                        "Checking Playnite Bridge"),
                    UI.Text("Checking the local connection…",
                        "playnite-library.playnite.busy.text"))
                .Classes("playnite-library-playnite-card"));
        var shell = UI.Stack("playnite-library.playnite.shell",
                children[0],
                UI.VerticalScroll("playnite-library.playnite.content", children.Skip(1).ToArray())
                    .Classes("playnite-library-playnite-content"))
            .Classes("playnite-library-playnite-shell");
        var root = UI.Stack("playnite-library.playnite.root", shell)
            .Classes("playnite-library-playnite");
        return new WidgetView(root,
            configured ? PlayniteLibraryWidget.PlayniteRefreshActionId :
                "playnite-library.playnite.token",
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Standard,
                WidthMode = WidgetSurfaceAxisMode.FillAvailable,
                HeightMode = WidgetSurfaceAxisMode.FillAvailable,
                PreferredWidth = 900,
                PreferredHeight = 620,
                MinimumWidth = 420,
                MinimumHeight = 340,
                Appearance = WidgetSurfaceAppearance.Transparent,
            });
    }
}

public sealed partial class PlayniteLibraryWidget
{
    internal const string PlayniteOpenActionId = "playnite-library.playnite.open";
    internal const string PlayniteBackActionId = "playnite-library.playnite.back";
    internal const string PlayniteRefreshActionId = "playnite-library.playnite.refresh";
    internal const string PlayniteSaveActionId = "playnite-library.playnite.save";
    internal const string PlayniteDeleteActionId = "playnite-library.playnite.delete";

    private IPlayniteBridgeClient? _playniteClient;

    private IPlayniteBridgeClient PlayniteClient =>
        _playniteClient ??= PlayniteBridgeClient.CreateDefault();

    private PlayniteLibraryConnectionState CapturePlayniteConnection(
        PlayniteLibraryRenderState state)
    {
        var probe = _playniteConnection.Snapshot;
        return new(state.PlayniteKind,
            state.PlayniteBusy || probe.Status is WidgetResourceStatus.Loading or
                WidgetResourceStatus.Refreshing,
            state.PlayniteCode,
            LifecycleState == WidgetLifecycleState.Interactive,
            state.PlayniteFeedback);
    }

    private async ValueTask<PlayniteBridgeConnectionResult> LoadPlayniteConnectionAsync(
        CancellationToken activeCancellationToken)
    {
        var route = _navigation.Value;
        if (route.Route != PlayniteLibraryRoute.PlayniteConnection)
            throw new OperationCanceledException("The Playnite connection route is inactive.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            activeCancellationToken, route.RouteCancellationToken);
        PlayniteBridgeConnectionResult result;
        try
        {
            result = await PlayniteClient.ProbeAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (PlayniteCredentialException)
        {
            result = new(PlayniteBridgeConnectionKind.Unavailable,
                "secret_store_unavailable");
        }
        var currentRoute = _navigation.Value;
        if (currentRoute.Route != PlayniteLibraryRoute.PlayniteConnection ||
            currentRoute.Revision != route.Revision)
            throw new OperationCanceledException(
                "The Playnite connection observation is stale.", lifetime.Token);
        _model.Update(state => state with
        {
            PlayniteKind = result.Kind,
            PlayniteCode = result.Code,
        });
        return result;
    }

    private async ValueTask RefreshPlayniteConnectionAsync(CancellationToken cancellationToken)
    {
        if (_navigation.Value.Route != PlayniteLibraryRoute.PlayniteConnection) return;
        _playniteFeedbackExpiry.Cancel();
        _model.Update(state => state with { PlayniteFeedback = null });
        var operation = _playniteConnection.Refresh();
        var completion = await operation.Completion.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (_navigation.Value.Route != PlayniteLibraryRoute.PlayniteConnection) return;
        if (completion.Status != WidgetOperationStatus.Succeeded)
        {
            ShowPlayniteFeedback(new("Connection test failed",
                "Playnite Bridge could not be checked.", ToastTone.Danger));
            return;
        }
        var result = _playniteConnection.Snapshot.Value;
        if (result is null) return;
        ShowPlayniteFeedback(result.Kind == PlayniteBridgeConnectionKind.Connected
            ? new("Connection confirmed", "Playnite Bridge is ready.", ToastTone.Success)
            : new("Connection test failed", ConnectionFeedbackMessage(result),
                result.Kind == PlayniteBridgeConnectionKind.AuthenticationRequired
                    ? ToastTone.Warning
                    : ToastTone.Danger));
    }

    private async ValueTask SavePlayniteCredentialAsync(
        string token, CancellationToken cancellationToken)
    {
        if (!TryBeginPlayniteCommand()) return;
        var routeToken = _navigation.Value.RouteCancellationToken;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, routeToken);
        try
        {
            try { await PlayniteClient.SaveCredentialAsync(token, lifetime.Token)
                    .ConfigureAwait(false); }
            catch (ArgumentException)
            {
                _model.Update(state => state with
                {
                    PlayniteKind = PlayniteBridgeConnectionKind.AuthenticationRequired,
                    PlayniteCode = "credential_invalid",
                    PlayniteBusy = false,
                });
                ShowPlayniteFeedback(new("Token was not saved",
                    "Enter a valid Playnite Bridge token.", ToastTone.Warning));
                return;
            }
            _playniteFeedbackExpiry.Cancel();
            _model.Update(state => state with { PlayniteFeedback = null });
            _playniteConnection.Reset();
            var probe = _playniteConnection.Refresh();
            await probe.Completion.WaitAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (PlayniteCredentialException)
        {
            _model.Update(state => state with
            {
                PlayniteKind = PlayniteBridgeConnectionKind.Unavailable,
                PlayniteCode = "secret_store_unavailable",
            });
            ShowPlayniteFeedback(new("Token was not saved",
                "Windows Credential Manager is unavailable.", ToastTone.Danger));
        }
        finally
        {
            _model.Update(state => state with { PlayniteBusy = false });
            // CommittedText belongs to the action callback only. This widget never stores it.
            token = string.Empty;
        }
    }

    private async ValueTask DeletePlayniteCredentialAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginPlayniteCommand()) return;
        var routeToken = _navigation.Value.RouteCancellationToken;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, routeToken);
        try
        {
            await PlayniteClient.DeleteCredentialAsync(lifetime.Token).ConfigureAwait(false);
            _playniteConnection.Reset();
            _model.Update(state => state with
            {
                PlayniteKind = PlayniteBridgeConnectionKind.NotConfigured,
                PlayniteCode = "credential_missing",
            });
            ShowPlayniteFeedback(new("Saved token removed",
                "Playnite Library now requires setup.", ToastTone.Success));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (PlayniteCredentialException)
        {
            _model.Update(state => state with
            {
                PlayniteKind = PlayniteBridgeConnectionKind.Unavailable,
                PlayniteCode = "secret_store_unavailable",
            });
            ShowPlayniteFeedback(new("Token was not removed",
                "Windows Credential Manager is unavailable.", ToastTone.Danger));
        }
        finally { _model.Update(state => state with { PlayniteBusy = false }); }
    }

    private bool TryBeginPlayniteCommand()
    {
        var admitted = _model.Update(state => state.PlayniteBusy
            ? (state, false)
            : (state with { PlayniteBusy = true }, true));
        return admitted.Result;
    }

    private void RetirePlayniteConnection(bool clearPresentation = true)
    {
        _playniteConnection.Reset();
        _playniteFeedbackExpiry.Cancel();
        if (clearPresentation)
            _model.Update(state => state with
            {
                PlayniteBusy = false,
                PlayniteFeedback = null,
            });
    }

    private void ShowPlayniteFeedback(PlayniteLibraryConnectionFeedback feedback)
    {
        var route = _navigation.Value;
        if (route.Route != PlayniteLibraryRoute.PlayniteConnection) return;
        _model.Update(state => state with { PlayniteFeedback = feedback });
        _ = _playniteFeedbackExpiry.ScheduleLatest(
            UI.DefaultToastDuration,
            () => _model.Update(state => ReferenceEquals(
                    state.PlayniteFeedback, feedback)
                ? state with { PlayniteFeedback = null }
                : state),
            route.RouteCancellationToken);
    }

    private static string ConnectionFeedbackMessage(PlayniteBridgeConnectionResult result) =>
        result.Kind switch
        {
            PlayniteBridgeConnectionKind.AuthenticationRequired =>
                "The saved token was rejected.",
            PlayniteBridgeConnectionKind.Incompatible =>
                "The local service does not expose the required games API.",
            PlayniteBridgeConnectionKind.Malformed =>
                "The local service returned an invalid bounded response.",
            PlayniteBridgeConnectionKind.NotConfigured =>
                "Save the Playnite Bridge token before testing.",
            _ => "Playnite Bridge is not available on localhost:19821.",
        };
}
