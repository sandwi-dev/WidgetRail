using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static partial class PlayniteLibraryConnectionPresentation
{
    internal static WidgetModal StoppedDialog(PlayniteSetupView setup)
    {
        var prefix = PlayniteLibraryWidget.SetupPrefix;
        var needsSetup = setup.Plan is { Action: not (PlayniteSetupAction.Start or PlayniteSetupAction.Connect) };
        var primaryAction = needsSetup ? prefix + "apply" : PlayniteLibraryWidget.SetupStartAction;
        var primaryLabel = setup.Plan?.Action == PlayniteSetupAction.Install ? "Install integration and start Playnite"
            : needsSetup ? setup.Plan!.ActionLabel : "Start Playnite";
        var children = new List<WidgetElement>
        {
            UI.Text(needsSetup ? setup.Plan!.Message : "Start Playnite to use your library. Your normal library updates will run.",
                prefix + "stopped.message").Classes("playnite-library-setup-path"),
            UI.Text("Playnite may open a window or switch focus during startup. Setup continues; reopen WidgetRail to return here.",
                prefix + "stopped.visibility").Classes("playnite-library-setup-path"),
        };
        if (setup.Progress is { } progress)
            children.Add(UI.Text(progress, prefix + "stopped.progress").Classes("playnite-library-setup-path"));
        else if (setup.Message is { } message && !setup.Connected)
            children.Add(UI.Alert("Could not connect", message, AlertTone.Warning, prefix + "stopped.error"));
        children.Add(UI.Button(primaryLabel, primaryAction, prefix + "stopped.primary")
            .Busy(setup.Busy).Disabled(needsSetup && setup.Plan?.CanApply != true).Classes("playnite-library-setup-primary"));
        children.Add(UI.Button("Connection settings", PlayniteLibraryWidget.PlayniteOpenActionId, prefix + "stopped.settings")
            .Disabled(setup.Busy));
        return new("playnite-start-dialog", "Playnite isn't running",
            UI.Stack(prefix + "stopped.content", children.ToArray()).Classes("playnite-library-setup-actions"),
            needsSetup && setup.Plan?.CanApply != true ? prefix + "stopped.settings" : prefix + "stopped.primary", prefix + "dismiss");
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
            children.Add(UI.Select("Playnite profile", setup.Installations.Select(item => new SelectOption(item.Id, item.DisplayName,
                prefix + "select." + item.Id, IsSelected: setup.Plan?.Installation.Id == item.Id)).ToArray(), prefix + "installation")
                .Disabled(!enabled).Classes("playnite-library-control"));
        if (setup.Plan is { } plan)
        {
            children.Add(UI.Alert(plan.Title, plan.Message, plan.CanApply ? AlertTone.Info : AlertTone.Warning, prefix + "plan"));
            children.Add(UI.Text(plan.Installation.DataDirectory, prefix + "location", "Playnite data folder: " + plan.Installation.DataDirectory)
                .Classes("playnite-library-setup-path"));
        }
        else if (!setup.Busy)
            children.Add(UI.Alert("Find your Playnite installation", setup.Message ?? "Find Playnite to check its integration and connect without copying a token.", AlertTone.Info, prefix + "plan"));
        if (setup.Progress is { } progress)
            children.Add(UI.Row(prefix + "progress", UI.LoadingIndicator(prefix + "spinner", progress),
                UI.Text(progress, prefix + "progress.text")).Classes("playnite-library-setup-progress"));
        else if (setup.Message is { } message && setup.Plan is not null)
            children.Add(UI.Alert(setup.Connected ? "Connected" : "Setup status", message,
                setup.Connected ? AlertTone.Success : AlertTone.Warning, prefix + "result"));
        children.Add(UI.Stack(prefix + "actions",
            UI.Button(setup.Plan?.ActionLabel ?? "Connect Playnite", prefix + "apply", prefix + "apply")
                .Busy(setup.Busy).Disabled(!state.Interactive || setup.Plan?.CanApply != true).Classes("playnite-library-setup-primary"),
            UI.ResponsiveGrid(prefix + "secondary", 180, 3,
                UI.Button("Find Playnite", prefix + "scan", prefix + "scan").Busy(setup.Busy).Disabled(!state.Interactive),
                UI.Button(setup.Advanced ? "Hide advanced" : "Advanced", prefix + "advanced", prefix + "advanced").Disabled(!enabled),
                UI.Button("Get Playnite", prefix + "download", prefix + "download")
                    .OpenIntent(WidgetIntentContracts.Web, JsonSerializer.SerializeToElement(new { url = "https://playnite.link/" })).Disabled(!enabled))
                .Classes("playnite-library-setup-secondary"))
            .Classes("playnite-library-setup-actions"));
        if (setup.Advanced)
        {
            children.Add(UI.SectionHeader("Folders and manual connection", prefix + "advanced.title",
                description: "Use these options for a custom installation or an existing integration you manage yourself."));
            children.Add(UI.Text("Playnite application folder", prefix + "program.label"));
            children.Add((UI.TextEntry(setup.ProgramPath, "Folder containing Playnite.DesktopApp.exe", prefix + "program", prefix + "program", 1024)
                with { InputKind = TextEntryInputKind.Ordinary }).Disabled(!enabled).Classes("playnite-library-control"));
            children.Add(UI.Text("Playnite data folder (optional)", prefix + "data.label"));
            children.Add((UI.TextEntry(setup.DataPath, "Leave empty to detect the profile", prefix + "data", prefix + "data", 1024)
                with { InputKind = TextEntryInputKind.Ordinary }).Disabled(!enabled).Classes("playnite-library-control"));
            children.Add(UI.Button("Use these folders", prefix + "folders", prefix + "folders").Disabled(!enabled || string.IsNullOrWhiteSpace(setup.ProgramPath)));
            children.Add(UI.Text("Manual Bridge token · stored in Windows Credential Manager", prefix + "token.label").Classes("playnite-library-setup-path"));
            children.Add(UI.SensitiveTextEntry("Enter a Playnite Bridge token", PlayniteLibraryWidget.PlayniteSaveActionId,
                "playnite-library.playnite.token", ProtocolConstants.MaximumTextEntryLength).Disabled(!enabled).Classes("playnite-library-playnite-token"));
            children.Add(UI.ResponsiveGrid(prefix + "manual-actions", 180, 2,
                UI.Button("Test connection", PlayniteLibraryWidget.PlayniteRefreshActionId, PlayniteLibraryWidget.PlayniteRefreshActionId).Disabled(!enabled),
                UI.Button("Remove saved token", PlayniteLibraryWidget.PlayniteDeleteActionId, PlayniteLibraryWidget.PlayniteDeleteActionId).Disabled(!enabled))
                .Classes("playnite-library-setup-secondary"));
            if (state.Feedback is { } feedback)
                children.Add(UI.Alert(feedback.Title, feedback.Message, state.Kind == PlayniteBridgeConnectionKind.Connected ? AlertTone.Success : AlertTone.Info, prefix + "manual-result"));
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
