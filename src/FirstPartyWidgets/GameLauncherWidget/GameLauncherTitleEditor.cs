using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal sealed record GameLauncherTitleEditorState(
    GameLauncherDetailsSelection Selection,
    string ProviderTitle,
    string? OverrideTitle,
    bool Interactive,
    bool Busy);

internal static class GameLauncherTitleEditor
{
    internal const string ScopeId = "game-launcher.title.scope";
    internal const string CommitAction = "game-launcher.title.commit";
    internal const string ResetAction = "game-launcher.title.reset";
    internal const string CloseAction = "game-launcher.title.close";
    internal const string EntryId = "game-launcher.title.entry";

    internal static GameLauncherTitleEditorState Project(
        GameLauncherDetailsSelection selection,
        GameLauncherPrivateState organization,
        bool interactive,
        bool busy)
    {
        var providerTitle = organization.Items.FirstOrDefault(item =>
            item.SavedId == selection.SavedId)?.DisplayName ?? selection.DisplayName;
        var overrideTitle = organization.TitleOverrides.FirstOrDefault(item =>
            item.SavedId == selection.SavedId)?.Title;
        return new(selection, providerTitle, overrideTitle, interactive, busy);
    }

    internal static WidgetView Render(GameLauncherTitleEditorState state)
    {
        var enabled = state.Interactive && !state.Busy;
        var display = state.OverrideTitle ?? state.ProviderTitle;
        var root = UI.Stack("game-launcher.title.editor",
                UI.SectionHeader("Edit game title", "game-launcher.title.header",
                    description: "Presentation and search only · launch identity is unchanged"),
                UI.Text($"Provider title · {state.ProviderTitle}",
                    "game-launcher.title.provider", "Provider title"),
                UI.TextEntry(display, "Custom title", CommitAction, EntryId,
                        GameLauncherPrivateState.MaximumTitleLength)
                    .Disabled(!enabled),
                UI.Row("game-launcher.title.actions",
                    UI.Button("Reset title", ResetAction, ResetAction)
                        .Disabled(!enabled || state.OverrideTitle is null),
                    UI.Button("Cancel", CloseAction, CloseAction)
                        .Disabled(!state.Interactive)))
            .InputScope(ScopeId)
            .Shortcut(ControllerButton.B, actionId: CloseAction)
            .Classes("game-launcher-widget", "game-launcher-content");
        return new WidgetView(root, EntryId, ActiveInputScopeId: ScopeId,
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Compact,
                PreferredWidth = 560,
                PreferredHeight = 420,
                MinimumWidth = 320,
                MinimumHeight = 300,
            });
    }
}
