using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryTitleEditorState(
    PlayniteLibraryDetailsSelection Selection,
    string ProviderTitle,
    string? OverrideTitle,
    bool Interactive,
    bool Busy);

internal static class PlayniteLibraryTitleEditor
{
    internal const string ScopeId = "playnite-library.title.scope";
    internal const string CommitAction = "playnite-library.title.commit";
    internal const string ResetAction = "playnite-library.title.reset";
    internal const string CloseAction = "playnite-library.title.close";
    internal const string EntryId = "playnite-library.title.entry";

    internal static PlayniteLibraryTitleEditorState Project(
        PlayniteLibraryDetailsSelection selection,
        PlayniteLibraryPrivateState organization,
        bool interactive,
        bool busy)
    {
        var providerTitle = organization.Items.FirstOrDefault(item =>
            item.SavedId == selection.SavedId)?.DisplayName ?? selection.DisplayName;
        var overrideTitle = organization.TitleOverrides.FirstOrDefault(item =>
            item.SavedId == selection.SavedId)?.Title;
        return new(selection, providerTitle, overrideTitle, interactive, busy);
    }

    internal static WidgetView Render(PlayniteLibraryTitleEditorState state)
    {
        var enabled = state.Interactive && !state.Busy;
        var display = state.OverrideTitle ?? state.ProviderTitle;
        var root = UI.Stack("playnite-library.title.editor",
                UI.SectionHeader("Edit game title", "playnite-library.title.header",
                    description: "Presentation and search only · launch identity is unchanged"),
                UI.Text($"Provider title · {state.ProviderTitle}",
                    "playnite-library.title.provider", "Provider title"),
                UI.TextEntry(display, "Custom title", CommitAction, EntryId,
                        PlayniteLibraryPrivateState.MaximumTitleLength)
                    .Disabled(!enabled),
                UI.Row("playnite-library.title.actions",
                    UI.Button("Reset title", ResetAction, ResetAction)
                        .Disabled(!enabled || state.OverrideTitle is null),
                    UI.Button("Cancel", CloseAction, CloseAction)
                        .Disabled(!state.Interactive)))
            .InputScope(ScopeId)
            .Shortcut(ControllerButton.B, actionId: CloseAction)
            .Classes("playnite-library-widget", "playnite-library-content");
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
