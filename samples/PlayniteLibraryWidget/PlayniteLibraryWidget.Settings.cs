using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    internal const string SettingsOpen = "playnite.settings.open";
    private const string CheckIntegrations = "playnite.settings.check";

    private WidgetView RenderSettings(WidgetNavigationSnapshot<PlayniteLibraryRoute> navigation, PlayniteLibraryRenderState state)
    {
        const string id = "playnite.settings";
        var setup = state.Setup;
        var switches = PlayingCardSwitches();
        var connectionStatus = setup.Connected ? "Connected" : setup.Plan?.Title ?? setup.Message ?? "Connection unavailable";
        var body = UI.VerticalScroll(id + ".content",
            UI.SectionHeader("Currently Playing cards", id + ".cards.title", description: "Show these cards when their content is available."),
            UI.ResponsiveGrid(id + ".cards", 220, 2, switches).Classes("playing-card-switches"),
            NextActivityCountSetting(),
            UI.Button("Allow Discord activity", DiscordPermissionAction, id + ".discord-access"),
            UI.SectionHeader("Playnite integrations", id + ".integrations.title"),
            UI.Row(id + ".bridge", UI.Text("WidgetRail for Playnite", id + ".bridge.name").Classes("playnite-library-setup-stage"),
                UI.Text(connectionStatus, id + ".bridge.status").Classes("playnite-library-setup-hint")).Classes("playnite-settings-heading"),
            UI.Button("Connection settings", PlayniteOpenActionId, id + ".connection").Disabled(setup.Busy),
            Addon(PlayniteOptionalAddon.Achievements, "Playnite Achievements", "Achievement progress, unlocks and tracked goals.", id + ".achievements"),
            Addon(PlayniteOptionalAddon.GameActivity, "GameActivity", "Play sessions, playtime charts and session highlights.", id + ".sessions"),
            UI.Button("Check again", CheckIntegrations, id + ".check").Disabled(setup.Busy || _setupService is null))
            .Classes("playnite-library-playnite-content");
        var root = UI.Stack(id,
            UI.Stack(id + ".shell", UI.Row(id + ".header",
                UI.Text("Playnite settings", id + ".title").Classes("playnite-library-title"),
                UI.Button("Back", navigation.BackActionId!, id + ".back")).Classes("playnite-library-playnite-header"),
                body).Classes("playnite-library-playnite-shell")).Classes("playnite-library-playnite");
        var content = state.ActionFeedback is { Succeeded: false } feedback && feedback.ScopeId == navigation.InputScopeId
            ? UI.Stack(id + ".feedback.scope", root, UI.Toast(feedback.Title, feedback.Message, ToastTone.Warning, id + ".feedback"))
            : root;
        var scoped = _navigation.Scope(navigation, content);
        return new(scoped, ResolveInitialFocus(scoped, navigation.InitialFocusId, switches[0].Id, navigation.InputScopeId, false),
            ActiveInputScopeId: navigation.InputScopeId, Surface: new WidgetSurfaceHints
            { Mode = WidgetSurfaceMode.Standard, WidthMode = WidgetSurfaceAxisMode.FillAvailable,
                HeightMode = WidgetSurfaceAxisMode.FillAvailable, Appearance = WidgetSurfaceAppearance.Transparent });

        WidgetElement Addon(PlayniteOptionalAddon addon, string name, string description, string key)
        {
            var status = !setup.Connected || setup.Plan is not { } plan || plan.UncheckedAddons.HasFlag(addon)
                ? "Status unavailable" : plan.MissingAddons.HasFlag(addon) ? "Missing" : "Installed";
            var rows = new List<WidgetElement>
            {
                UI.Row(key + ".heading", UI.Text(name, key + ".name").Classes("playnite-library-setup-stage"),
                    UI.Text(status, key + ".status").Classes("playnite-library-setup-hint")).Classes("playnite-settings-heading"),
                UI.Text(description, key + ".description").Classes("playnite-library-details-copy"),
            };
            if (MissingAddon(setup, addon, key + ".installer") is { } installer) rows.Add(installer);
            return UI.Stack(key + ".section", rows.ToArray()).Classes("playnite-settings-integration");
        }
    }
}
