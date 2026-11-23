using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

[Flags]
internal enum PlayingCardKind
{
    Details = 1, Help = 2, News = 4, Sessions = 8, Achievements = 16, Capture = 32,
    All = Details | Help | News | Sessions | Achievements | Capture,
}

public sealed partial class PlayniteLibraryWidget
{
    private const string CardPreferences = "playnite.card-preferences";
    internal const string ManageCards = CardPreferences + ".open";
    private static readonly (PlayingCardKind Kind, string Label)[] CardChoices =
    [
        (PlayingCardKind.Details, "Resume playing"), (PlayingCardKind.Capture, "Latest capture"),
        (PlayingCardKind.Achievements, "Achievements"), (PlayingCardKind.News, "Game updates"),
        (PlayingCardKind.Help, "Ask Game Help"), (PlayingCardKind.Sessions, "Sessions"),
    ];

    private PlayingCardKind HiddenCards { get { lock (_gate) return _organization.HiddenPlayingCards; } }

    private WidgetElement[] PlayingCardSwitches()
    {
        var hidden = HiddenCards;
        return CardChoices.Select(choice =>
        {
            var shown = !hidden.HasFlag(choice.Kind);
            return UI.Switch(choice.Label, shown, CardPreferences + (shown ? ".off." : ".on.") + choice.Kind,
                CardPreferences + ".row." + choice.Kind);
        }).ToArray();
    }

    private WidgetModal PlayingCardManager()
    {
        var switches = PlayingCardSwitches();
        return new(CardPreferences + ".dialog", "Currently Playing cards",
            UI.Stack(CardPreferences + ".body",
                UI.Text("Show these cards when their content is available.", CardPreferences + ".description")
                    .Classes("playing-card-meta"),
                UI.Stack(CardPreferences + ".switches", switches).Classes("playing-card-switches")),
            switches[0].Id, CardPreferences + ".close")
        { StyleClass = "playing-card-dialog", HeaderActions = UI.IconButton(PlayniteIcons.Back,
            CardPreferences + ".close", CardPreferences + ".back", "Close") };
    }

    private async ValueTask<bool> HandlePlayingCardPreferenceAsync(WidgetActionEvent action, CancellationToken token)
    {
        if (!action.ActionId.StartsWith(CardPreferences + ".", StringComparison.Ordinal)) return false;
        var route = _navigation.Value;
        var settings = route.Route == PlayniteLibraryRoute.Settings;
        if (LifecycleState != WidgetLifecycleState.Interactive || !settings && route.Route is not (PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Browse)) return true;
        if (action.ActionId == ManageCards)
        {
            if (settings) return true;
            _model.Update(s => s with { ManagingPlayingCards = true, PlayingEntry = null, HiddenCardFocusOrigin = null });
            return true;
        }
        if (action.ActionId == CardPreferences + ".close")
        {
            _model.Update(s => s with { ManagingPlayingCards = false });
            return true;
        }

        var command = action.ActionId[(CardPreferences.Length + 1)..];
        var shelfHide = command.StartsWith("hide.", StringComparison.Ordinal);
        var game = PlayingId(_model.Value);
        var prefix = shelfHide ? "hide." + game + "." : command.StartsWith("on.", StringComparison.Ordinal) ? "on." : "off.";
        if ((shelfHide ? game is null || route.Route != PlayniteLibraryRoute.Library : !settings && !_model.Value.ManagingPlayingCards) ||
            !command.StartsWith(prefix, StringComparison.Ordinal)) return true;
        var choice = CardChoices.FirstOrDefault(c => c.Kind.ToString() == command[prefix.Length..]);
        if (choice.Label is null) return true;
        var show = prefix == "on.";
        var result = await SaveStateAsync(state => PlayniteLibraryStateMutation.Apply(state with
        {
            HiddenPlayingCards = show ? state.HiddenPlayingCards & ~choice.Kind : state.HiddenPlayingCards | choice.Kind,
        }), token);
        if (route.RouteCancellationToken.IsCancellationRequested) return true;
        if (!result.Saved) ShowActionToast("Card visibility", "Could not save this change. Try again.", false);
        else
        {
            LoadPlaying();
            if (shelfHide)
            {
                var focus = CreateFocusRequest();
                _model.Update(s => s with { PlayingEntry = focus, HiddenCardFocusOrigin = choice.Kind.ToString(),
                    ContentEntryRequestId = focus.Id, ContentEntryScopeId = route.InputScopeId });
            }
            Invalidate();
        }
        return true;
    }
}
