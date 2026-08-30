using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameLauncher;

internal sealed record GameLauncherPlayniteConnectionState(
    PlayniteBridgeConnectionKind Kind,
    bool Busy,
    string Code,
    bool Interactive);

internal static class GameLauncherPlayniteConnectionPresentation
{
    internal static WidgetView Render(GameLauncherPlayniteConnectionState state)
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
                GameLauncherWidget.PlayniteSaveActionId,
                "game-launcher.playnite.token",
                ProtocolConstants.MaximumTextEntryLength)
            .Disabled(!enabled)
            .Classes("game-launcher-playnite-token");
        var actions = new List<WidgetElement>
        {
            UI.Button("Back", GameLauncherWidget.PlayniteBackActionId,
                    GameLauncherWidget.PlayniteBackActionId)
                .Disabled(!state.Interactive),
            UI.Button("Test connection", GameLauncherWidget.PlayniteRefreshActionId,
                    GameLauncherWidget.PlayniteRefreshActionId)
                .Disabled(!enabled || !configured),
        };
        if (configured)
            actions.Add(UI.Button("Remove saved token", GameLauncherWidget.PlayniteDeleteActionId,
                    GameLauncherWidget.PlayniteDeleteActionId)
                .Disabled(!enabled));
        var children = new List<WidgetElement>
        {
            UI.Row("game-launcher.playnite.header",
                    UI.Stack("game-launcher.playnite.heading",
                        UI.Text("GAME LAUNCHER", "game-launcher.playnite.eyebrow")
                            .Classes("game-launcher-eyebrow"),
                        UI.Text("Playnite connection", "game-launcher.playnite.title")
                            .Classes("game-launcher-title")),
                    UI.Row("game-launcher.playnite.actions", actions.ToArray())
                        .Classes("game-launcher-actions"))
                .Classes("game-launcher-header"),
            UI.Text("Local service · 127.0.0.1:19821",
                    "game-launcher.playnite.endpoint",
                    "Playnite Bridge local service on port 19821")
                .Classes("game-launcher-source-summary"),
            UI.Alert(title, detail, tone, "game-launcher.playnite.status"),
            UI.Stack("game-launcher.playnite.credential",
                    UI.SectionHeader("Protected token", "game-launcher.playnite.credential.header",
                        description: "Stored privately and injected only into this local connection."),
                    token)
                .Classes("game-launcher-playnite-card"),
        };
        if (state.Busy)
            children.Add(UI.Stack("game-launcher.playnite.busy",
                    UI.LoadingIndicator("game-launcher.playnite.busy.indicator",
                        "Checking Playnite Bridge"),
                    UI.Text("Checking the local connection…",
                        "game-launcher.playnite.busy.text"))
                .Classes("game-launcher-playnite-card"));
        var root = UI.Stack("game-launcher.playnite.root", children.ToArray())
            .Classes("game-launcher-widget", "game-launcher-playnite");
        return new WidgetView(root,
            configured ? GameLauncherWidget.PlayniteRefreshActionId :
                "game-launcher.playnite.token",
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

public sealed partial class GameLauncherWidget
{
    internal const string PlayniteOpenActionId = "game-launcher.playnite.open";
    internal const string PlayniteBackActionId = "game-launcher.playnite.back";
    internal const string PlayniteRefreshActionId = "game-launcher.playnite.refresh";
    internal const string PlayniteSaveActionId = "game-launcher.playnite.save";
    internal const string PlayniteDeleteActionId = "game-launcher.playnite.delete";

    private IPlayniteBridgeClient? _playniteClient;
    private PlayniteBridgeConnectionKind _playniteKind =
        PlayniteBridgeConnectionKind.NotConfigured;
    private string _playniteCode = "credential_missing";
    private bool _playniteBusy;
    private long _playniteGeneration;

    private IPlayniteBridgeClient PlayniteClient =>
        _playniteClient ??= PlayniteBridgeClient.CreateDefault();

    private GameLauncherPlayniteConnectionState CapturePlayniteConnectionLocked() =>
        new(_playniteKind, _playniteBusy, _playniteCode,
            LifecycleState == WidgetLifecycleState.Interactive);

    private async ValueTask RefreshPlayniteConnectionAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginPlayniteOperation(out var generation)) return;
        try
        {
            var result = await PlayniteClient.ProbeAsync(cancellationToken).ConfigureAwait(false);
            CompletePlayniteOperation(generation, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CancelPlayniteOperation(generation);
        }
        catch (PlayniteCredentialException)
        {
            CompletePlayniteOperation(generation,
                new(PlayniteBridgeConnectionKind.Unavailable, "secret_store_unavailable"));
        }
    }

    private async ValueTask SavePlayniteCredentialAsync(
        string token, CancellationToken cancellationToken)
    {
        if (!TryBeginPlayniteOperation(out var generation)) return;
        try
        {
            try { await PlayniteClient.SaveCredentialAsync(token, cancellationToken)
                    .ConfigureAwait(false); }
            catch (ArgumentException)
            {
                CompletePlayniteOperation(generation,
                    new(PlayniteBridgeConnectionKind.AuthenticationRequired,
                        "credential_invalid"));
                return;
            }
            var result = await PlayniteClient.ProbeAsync(cancellationToken).ConfigureAwait(false);
            CompletePlayniteOperation(generation, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CancelPlayniteOperation(generation);
        }
        catch (PlayniteCredentialException)
        {
            CompletePlayniteOperation(generation,
                new(PlayniteBridgeConnectionKind.Unavailable, "secret_store_unavailable"));
        }
        finally
        {
            // CommittedText belongs to the action callback only. This widget never stores it.
            token = string.Empty;
        }
    }

    private async ValueTask DeletePlayniteCredentialAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginPlayniteOperation(out var generation)) return;
        try
        {
            await PlayniteClient.DeleteCredentialAsync(cancellationToken).ConfigureAwait(false);
            CompletePlayniteOperation(generation,
                new(PlayniteBridgeConnectionKind.NotConfigured, "credential_missing"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CancelPlayniteOperation(generation);
        }
        catch (PlayniteCredentialException)
        {
            CompletePlayniteOperation(generation,
                new(PlayniteBridgeConnectionKind.Unavailable, "secret_store_unavailable"));
        }
    }

    private bool TryBeginPlayniteOperation(out long generation)
    {
        lock (_gate)
        {
            if (_playniteBusy)
            {
                generation = 0;
                return false;
            }
            generation = ++_playniteGeneration;
            _playniteBusy = true;
        }
        Invalidate();
        return true;
    }

    private void CompletePlayniteOperation(
        long generation, PlayniteBridgeConnectionResult result)
    {
        lock (_gate)
        {
            if (generation != _playniteGeneration ||
                _navigation.Value.Route != GameLauncherRoute.PlayniteConnection) return;
            _playniteKind = result.Kind;
            _playniteCode = result.Code;
            _playniteBusy = false;
        }
        Invalidate();
    }

    private void CancelPlayniteOperation(long generation)
    {
        lock (_gate)
        {
            if (generation != _playniteGeneration) return;
            _playniteBusy = false;
        }
        Invalidate();
    }

    private void RetirePlayniteConnection()
    {
        lock (_gate)
        {
            _playniteGeneration++;
            _playniteBusy = false;
        }
    }
}
