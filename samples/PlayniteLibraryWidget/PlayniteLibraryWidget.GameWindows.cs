using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string WindowAction = "playnite.window.";

    internal static IEnumerable<WidgetContextAction> GameWindowActions(PlayniteBridgeGame? game)
    {
        if (game?.GameWindow is not { } run) yield break;
        var key = WindowAction + run.Session.ToString("N");
        yield return new(key + ".choose", run.Remembered ? "Change game window…" : "Choose game window…");
        if (run.Remembered) yield return new(key + ".automatic", "Use automatic detection");
    }

    private async ValueTask<bool> HandleGameWindowAsync(string action, CancellationToken token)
    {
        if (!action.StartsWith(WindowAction, StringComparison.Ordinal)) return false;
        var route = _navigation.Value.Route;
        PlayniteBridgeGame? Source() => route == PlayniteLibraryRoute.GameDetails ? _model.Value.DetailsExtras.Full?.Game :
            route == PlayniteLibraryRoute.Library && PlayingId(_model.Value) is { } id && !HiddenCards.HasFlag(PlayingCardKind.Details)
                ? playingDetails.GetSnapshot(id).Value?.Game : null;
        var game = Source();
        if (game?.GameWindow is not { } run || !GameWindowActions(game).Any(item => item.ActionId == action)) return true;
        bool Current() => LifecycleState == WidgetLifecycleState.Interactive && route == _navigation.Value.Route &&
            _model.Value.RunningGames?.Contains(game.Id) == true && Source()?.GameWindow?.Session == run.Session;
        if (Current()) await ApplyGameWindowAsync(game.Id, game.Name, run, action.EndsWith(".automatic", StringComparison.Ordinal), Current, token);
        return true;
    }

    private async Task<bool> ApplyGameWindowAsync(string gameId, string title, PlayniteWindowBinding run, bool automatic,
        Func<bool> current, CancellationToken token)
    {
        try
        {
            var permission = WidgetTaskSwitcherCapabilities.Switch.CapabilityId;
            var state = (await HostServices.Permissions.CheckAsync([permission], token)).Single().State;
            if (state != WidgetPermissionState.Granted && (state != WidgetPermissionState.NotDecided ||
                await HostServices.Permissions.RequestAsync([permission], token) != WidgetPermissionOutcome.Granted))
            {
                if (current()) ShowActionToast(title, "Allow window switching in Playnite Library permissions to choose a game window.", false);
                return false;
            }
            if (!current()) return false;
            var caption = "Choose window for " + title;
            var match = automatic ? "automatic" : await HostServices.TaskSwitcher.ChooseWindowAsync(caption[..Math.Min(caption.Length, 120)], token);
            if (match == null || !current()) return false;
            if (!await _application.SetGameWindowAsync(gameId, run, match, token))
            {
                if (current()) ShowActionToast(title, "That game or window is no longer available. Choose the window again.", false);
                return false;
            }
            lock (_gate)
            {
                var details = _model.Value.DetailsExtras.Full;
                if (details?.Game is { } game && game.Id == gameId && game.GameWindow == run)
                    _model.Update(model => model with { DetailsExtras = model.DetailsExtras with
                    { Full = details with { Game = game with { GameWindow = run with { Remembered = !automatic } } } } });
            }
            playingDetails.Reset(invalidate: false);
            LoadPlaying();
            Invalidate();
            return true;
        }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or
            PlayniteBridgeTransportException or PlayniteBridgeDataException)
        {
            if (current()) ShowActionToast(title, "Couldn’t update the game window. Check the Playnite connection and try again.", false);
            return false;
        }
    }
}
