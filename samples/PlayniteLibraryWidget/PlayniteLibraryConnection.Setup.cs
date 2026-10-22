using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static partial class PlayniteLibraryConnectionPresentation
{
    internal static WidgetModal SetupDialog(PlayniteSetupView setup)
    {
        var prefix = PlayniteLibraryWidget.SetupPrefix;
        var plan = setup.Plan;
        var canApply = plan?.CanApply == true;
        var primaryAction = plan?.Action == PlayniteSetupAction.Start ? PlayniteLibraryWidget.SetupStartAction : prefix + "apply";
        var primaryLabel = plan?.Action == PlayniteSetupAction.Install && !plan.Installation.Running ? "Install integration and start Playnite"
            : plan?.ActionLabel ?? "Connect Playnite";
        var children = new List<WidgetElement>
        {
            setup.Busy ? SetupProgress(setup, prefix + "stopped.progress") :
                UI.Text(plan?.Message ?? setup.Message ?? "Choose your Playnite profile in connection settings.",
                    prefix + "stopped.message").Classes("playnite-library-setup-path"),
        };
        if (plan is not null)
            children.Add(UI.Text(plan.Installation.DataDirectory, prefix + "stopped.profile").Classes("playnite-library-setup-path"));
        if (!setup.Busy && canApply && plan!.Action != PlayniteSetupAction.Connect)
            children.Add(UI.Text("Playnite may open a window or switch focus during startup. Setup continues; reopen WidgetRail to return here.",
                prefix + "stopped.visibility").Classes("playnite-library-setup-path"));
        if (!setup.Busy && setup.Message is { } message && !setup.Connected && plan is not null)
            children.Add(UI.Alert("Could not connect", message, AlertTone.Warning, prefix + "stopped.error"));
        if (canApply)
            children.Add(UI.Button(primaryLabel, primaryAction, prefix + "stopped.primary")
                .Busy(setup.Busy).Classes("playnite-library-setup-primary"));
        children.Add(UI.Button("Connection settings", PlayniteLibraryWidget.PlayniteOpenActionId, prefix + "stopped.settings")
            .Disabled(setup.Busy));
        return new("playnite-start-dialog", setup.Busy ? "Getting Playnite ready" :
                plan?.Installation.Running == false ? "Playnite isn't running" : "Connect Playnite",
            UI.Stack(prefix + "stopped.content", children.ToArray()).Classes("playnite-library-setup-actions"),
            canApply ? prefix + "stopped.primary" : prefix + "stopped.settings", prefix + "dismiss");
    }

    private static WidgetElement SetupProgress(PlayniteSetupView setup, string id)
    {
        var stage = setup.Progress ?? "Checking Playnite…";
        return UI.Grid(id, [GridTrack.Auto()], [GridTrack.Auto(), GridTrack.Star()],
            UI.LoadingIndicator(id + ".spinner", stage).Classes("playnite-library-setup-spinner"),
            UI.Stack(id + ".copy",
                UI.Text(stage, id + ".stage").Classes("playnite-library-setup-stage"),
                UI.Text("Setup continues if you hide WidgetRail. If Playnite takes focus, reopen the overlay to return here.", id + ".hint")
                    .Classes("playnite-library-setup-hint"))
                .Classes("playnite-library-setup-progress-copy").InGrid(0, 1))
            .Spacing(0, 16).Classes("playnite-library-setup-progress");
    }

    private static WidgetView RenderSetup(PlayniteLibraryConnectionState state)
    {
        var setup = state.Setup!;
        var prefix = PlayniteLibraryWidget.SetupPrefix;
        var enabled = state.Interactive && !setup.Busy && !state.Busy;
        var children = new List<WidgetElement>
        {
            UI.Text("Your Playnite library, ready for your controller.", prefix + "intro").Classes("playnite-library-setup-intro"),
        };
        if (setup.Installations.Count > 1)
        {
            var options = setup.Installations.Select(item => new SelectOption(item.Id, item.DisplayName,
                prefix + "select." + item.Id, IsSelected: setup.Plan?.Installation.Id == item.Id)).ToList();
            if (!options.Any(option => option.IsSelected))
                options.Insert(0, new("choose", "Choose a profile", prefix + "choose", IsSelected: true, IsDisabled: true));
            children.Add(UI.Select("Playnite profile", options, prefix + "installation")
                .Disabled(!enabled).Classes("playnite-library-control"));
        }
        if (setup.Plan is { } plan)
        {
            children.Add(UI.Alert(plan.Title, plan.Message, plan.CanApply ? AlertTone.Info : AlertTone.Warning, prefix + "plan"));
            children.Add(UI.Text(plan.Installation.DataDirectory, prefix + "location", "Playnite data folder: " + plan.Installation.DataDirectory)
                .Classes("playnite-library-setup-path"));
        }
        else if (!setup.Busy)
            children.Add(UI.Alert("Find your Playnite installation", setup.Message ?? "Find Playnite to check its integration and connect without copying a token.", AlertTone.Info, prefix + "plan"));
        if (setup.Busy)
            children.Add(SetupProgress(setup, prefix + "progress"));
        else if (setup.Message is { } message && setup.Plan is not null)
            children.Add(UI.Alert(setup.Connected ? "Connected" : "Setup status", message,
                setup.Connected ? AlertTone.Success : AlertTone.Warning, prefix + "result"));
        children.Add(UI.Stack(prefix + "actions",
            UI.Button(setup.Plan?.ActionLabel ?? "Connect Playnite", prefix + "apply", prefix + "apply")
                .Busy(setup.Busy).Disabled(!state.Interactive || setup.Plan?.CanApply != true).Classes("playnite-library-setup-primary"),
            UI.ResponsiveGrid(prefix + "secondary", 180, 3,
                UI.Button("Find Playnite", prefix + "scan", prefix + "scan").Busy(setup.Busy).Disabled(!state.Interactive),
                UI.DisclosureHeader(prefix + "advanced", setup.Advanced ? "Hide advanced" : "Advanced", setup.Advanced,
                    prefix + "advanced.content", prefix + "advanced.expand", prefix + "advanced.collapse").Disabled(!enabled),
                UI.Button("Get Playnite", prefix + "download", prefix + "download")
                    .OpenIntent(WidgetIntentContracts.Web, JsonSerializer.SerializeToElement(new { url = "https://playnite.link/" })).Disabled(!enabled))
                .Classes("playnite-library-setup-secondary"))
            .Classes("playnite-library-setup-actions"));
        if (setup.Advanced)
        {
            var advanced = new List<WidgetElement>();
            advanced.Add(UI.SectionHeader("Folders and manual connection", prefix + "advanced.title",
                description: "Use these options for a custom installation or an existing integration you manage yourself."));
            advanced.Add(UI.Text("Playnite application folder", prefix + "program.label"));
            advanced.Add((UI.TextEntry(setup.ProgramPath, "Folder containing Playnite.DesktopApp.exe", prefix + "program", prefix + "program", 1024)
                with { InputKind = TextEntryInputKind.Ordinary }).Disabled(!enabled).Classes("playnite-library-control"));
            advanced.Add(UI.Text("Playnite data folder (optional)", prefix + "data.label"));
            advanced.Add((UI.TextEntry(setup.DataPath, "Leave empty to detect the profile", prefix + "data", prefix + "data", 1024)
                with { InputKind = TextEntryInputKind.Ordinary }).Disabled(!enabled).Classes("playnite-library-control"));
            advanced.Add(UI.Button("Use these folders", prefix + "folders", prefix + "folders").Disabled(!enabled || string.IsNullOrWhiteSpace(setup.ProgramPath)));
            advanced.Add(UI.Text("Manual Bridge token · stored in Windows Credential Manager", prefix + "token.label").Classes("playnite-library-setup-path"));
            advanced.Add(UI.SensitiveTextEntry("Enter a Playnite Bridge token", PlayniteLibraryWidget.PlayniteSaveActionId,
                "playnite-library.playnite.token", ProtocolConstants.MaximumTextEntryLength).Disabled(!enabled).Classes("playnite-library-playnite-token"));
            advanced.Add(UI.ResponsiveGrid(prefix + "manual-actions", 180, 2,
                UI.Button("Test connection", PlayniteLibraryWidget.PlayniteRefreshActionId, PlayniteLibraryWidget.PlayniteRefreshActionId).Disabled(!enabled),
                UI.Button("Remove saved token", PlayniteLibraryWidget.PlayniteDeleteActionId, PlayniteLibraryWidget.PlayniteDeleteActionId).Disabled(!enabled))
                .Classes("playnite-library-setup-secondary"));
            if (state.Feedback is { } feedback)
                advanced.Add(UI.Alert(feedback.Title, feedback.Message, state.Kind == PlayniteBridgeConnectionKind.Connected ? AlertTone.Success : AlertTone.Info, prefix + "manual-result"));
            children.Add(UI.Stack(prefix + "advanced.content", advanced.ToArray()).Classes("playnite-library-advanced-content"));
        }
        var header = UI.Row("playnite-library.playnite.header",
            UI.Text("Playnite connection", "playnite-library.playnite.title").Classes("playnite-library-title"),
            UI.Button("Back", PlayniteLibraryWidget.PlayniteBackActionId, PlayniteLibraryWidget.PlayniteBackActionId).Disabled(!state.Interactive))
            .Classes("playnite-library-header", "playnite-library-playnite-header");
        var root = UI.Stack("playnite-library.playnite.root",
            UI.Stack("playnite-library.playnite.shell", header,
                UI.VerticalScroll("playnite-library.playnite.content", children.ToArray()).Classes("playnite-library-playnite-content"))
                .Classes("playnite-library-playnite-shell", "wrail-surface-raised"))
            .Classes("playnite-library-playnite");
        return new(root, setup.Plan?.CanApply == true ? prefix + "apply" : prefix + "scan", Surface: new WidgetSurfaceHints
        {
            Mode = WidgetSurfaceMode.Standard, WidthMode = WidgetSurfaceAxisMode.FillAvailable, HeightMode = WidgetSurfaceAxisMode.FillAvailable,
            PreferredWidth = 900, PreferredHeight = 620, MinimumWidth = 420, MinimumHeight = 340, Appearance = WidgetSurfaceAppearance.Transparent,
        });
    }
}
