using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal static class GameLauncherActionSheet
{
    internal const string ScopeId = "game-launcher.actions.scope";
    internal const string CloseAction = "game-launcher.actions.close";
    internal const string OpenAction = "game-launcher.actions.open";
    internal const string RefreshSourceAction = "game-launcher.actions.refresh-source";
    internal const string InitialFocusId = "game-launcher.actions.favorite";

    internal static WidgetView Render(GameLauncherDetailsState state)
    {
        var available = state.Interactive && state.Resolved && !state.Busy;
        var source = string.IsNullOrWhiteSpace(state.SourceAttribution)
            ? "game source"
            : state.SourceAttribution + " source";
        var sheet = UI.ActionSheet(
            $"Actions for {state.DisplayName}",
            "game-launcher.actions.sheet",
            ScopeId,
            CloseAction,
            [
                new ActionSheetItem(
                    InitialFocusId,
                    state.Favorite ? "Remove favorite" : "Add favorite",
                    "game-launcher.favorite",
                    WidgetGlyph.Like,
                    state.Favorite ? "Remove from favorites" : "Add to favorites",
                    IsDisabled: !available),
                new ActionSheetItem(
                    "game-launcher.actions.hide",
                    "Hide game",
                    "game-launcher.hide",
                    WidgetGlyph.Warning,
                    "Hide this exact game from the library",
                    ActionSheetItemTone.Danger,
                    IsDisabled: !available),
                new ActionSheetItem(
                    "game-launcher.actions.variant",
                    state.VariantActionLabel,
                    "game-launcher.variant",
                    WidgetGlyph.Settings,
                    state.VariantActionLabel,
                    IsDisabled: !available || !state.VariantActionEnabled),
                new ActionSheetItem(
                    "game-launcher.actions.prefer",
                    state.Preferred ? "Preferred variant" : "Prefer variant",
                    "game-launcher.prefer",
                    WidgetGlyph.Check,
                    state.Preferred ? "Already the preferred variant" : "Prefer this variant",
                    IsDisabled: !available || state.GroupSize < 2 || state.Preferred),
                new ActionSheetItem(
                    "game-launcher.actions.refresh-source",
                    $"Refresh {source}",
                    RefreshSourceAction,
                    WidgetGlyph.Refresh,
                    $"Refresh current installed evidence for {state.SourceAttribution}",
                    IsDisabled: !state.Interactive || state.Busy),
            ],
            $"{state.SourceAttribution} · {state.Availability}. View opens full game details.");
        return new WidgetView(sheet, InitialFocusId,
            ActiveInputScopeId: ScopeId,
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Compact,
                PreferredWidth = 560,
                PreferredHeight = 620,
                MinimumWidth = 320,
                MinimumHeight = 360,
            });
    }
}
