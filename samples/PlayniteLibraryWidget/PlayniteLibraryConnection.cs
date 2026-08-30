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
            .Classes("playnite-library-playnite-token");
        var actions = new List<WidgetElement>
        {
            UI.Button("Back", PlayniteLibraryWidget.PlayniteBackActionId,
                    PlayniteLibraryWidget.PlayniteBackActionId)
                .Disabled(!state.Interactive),
            UI.Button("Test connection", PlayniteLibraryWidget.PlayniteRefreshActionId,
                    PlayniteLibraryWidget.PlayniteRefreshActionId)
                .Disabled(!enabled || !configured),
        };
        if (configured)
            actions.Add(UI.Button("Remove saved token", PlayniteLibraryWidget.PlayniteDeleteActionId,
                    PlayniteLibraryWidget.PlayniteDeleteActionId)
                .Disabled(!enabled));
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
        var root = UI.Stack("playnite-library.playnite.root", children.ToArray())
            .Classes("playnite-library-widget", "playnite-library-playnite");
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
    private PlayniteBridgeConnectionKind _playniteKind =
        PlayniteBridgeConnectionKind.NotConfigured;
    private string _playniteCode = "credential_missing";
    private bool _playniteBusy;
    private long _playniteGeneration;

    private IPlayniteBridgeClient PlayniteClient =>
        _playniteClient ??= PlayniteBridgeClient.CreateDefault();

    private PlayniteLibraryConnectionState CapturePlayniteConnectionLocked() =>
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
                _navigation.Value.Route != PlayniteLibraryRoute.PlayniteConnection) return;
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
