using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryConnectionState(
    PlayniteBridgeConnectionKind Kind,
    bool Busy,
    string Code,
    bool Interactive);

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
                "The bounded local library API is compatible.", AlertTone.Success),
            PlayniteBridgeConnectionKind.AuthenticationRequired => (
                "Authentication required",
                "The saved token was rejected. Enter the current Playnite Bridge token.",
                AlertTone.Warning),
            PlayniteBridgeConnectionKind.Incompatible => (
                "Playnite Bridge is incompatible",
                "The local service does not expose the required bounded games API.",
                AlertTone.Warning),
            PlayniteBridgeConnectionKind.Malformed => (
                "Playnite Bridge returned invalid data",
                "The local service response did not match the bounded games contract.",
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
            UI.Button("Back", PlayniteLibraryWidget.PlayniteBackActionId,
                    PlayniteLibraryWidget.PlayniteBackActionId)
                .Disabled(!state.Interactive)
                .AddClasses("playnite-library-control"),
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
                            .Classes("playnite-library-title")),
                    UI.Row("playnite-library.playnite.actions", actions.ToArray())
                        .Classes("playnite-library-actions"))
                .Classes("playnite-library-header"),
            UI.Text("Local service · localhost:19821",
                    "playnite-library.playnite.endpoint",
                    "Playnite Bridge local service on port 19821")
                .Classes("playnite-library-source-summary"),
            UI.Alert(title, detail, tone, "playnite-library.playnite.status"),
            UI.Stack("playnite-library.playnite.credential",
                    UI.SectionHeader("Protected token", "playnite-library.playnite.credential.header",
                        description: "Stored privately and injected only into this local connection."),
                    token)
                .Classes("playnite-library-playnite-card"),
        };
        if (state.Busy)
            children.Add(UI.Stack("playnite-library.playnite.busy",
                    UI.LoadingIndicator("playnite-library.playnite.busy.indicator",
                        "Checking Playnite Bridge"),
                    UI.Text("Checking the local connection…",
                        "playnite-library.playnite.busy.text"))
                .Classes("playnite-library-playnite-card"));
        var shell = UI.Stack("playnite-library.playnite.shell", children.ToArray())
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
        var observed = probe.Value;
        return new(observed?.Kind ?? state.PlayniteKind,
            state.PlayniteBusy || probe.Status is WidgetResourceStatus.Loading or
                WidgetResourceStatus.Refreshing,
            observed?.Code ?? state.PlayniteCode,
            LifecycleState == WidgetLifecycleState.Interactive);
    }

    private async ValueTask<PlayniteBridgeConnectionResult> LoadPlayniteConnectionAsync(
        CancellationToken activeCancellationToken)
    {
        var route = _navigation.Value;
        if (route.Route != PlayniteLibraryRoute.PlayniteConnection)
            throw new OperationCanceledException("The Playnite connection route is inactive.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            activeCancellationToken, route.RouteCancellationToken);
        try
        {
            return await PlayniteClient.ProbeAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (PlayniteCredentialException)
        {
            return new(PlayniteBridgeConnectionKind.Unavailable,
                "secret_store_unavailable");
        }
    }

    private async ValueTask RefreshPlayniteConnectionAsync(CancellationToken cancellationToken)
    {
        if (_navigation.Value.Route != PlayniteLibraryRoute.PlayniteConnection) return;
        var operation = _playniteConnection.Refresh();
        await operation.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                return;
            }
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
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (PlayniteCredentialException)
        {
            _model.Update(state => state with
            {
                PlayniteKind = PlayniteBridgeConnectionKind.Unavailable,
                PlayniteCode = "secret_store_unavailable",
            });
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
        if (clearPresentation)
            _model.Update(state => state with { PlayniteBusy = false });
    }
}
