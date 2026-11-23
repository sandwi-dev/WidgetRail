using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private const string SettingsWidgetId = PlatformSettingsDocument.DefaultHomeWidgetId;
    private string homeWidgetId = SettingsWidgetId;
    private long homePreferenceRevision;

    private BridgeWidgetDescriptor? ResolveHomeWidget(string id) =>
        catalogItems.FirstOrDefault(widget => widget.Id == id && !widget.IsStopped)
        ?? catalogItems.FirstOrDefault(widget => widget.Id == SettingsWidgetId && !widget.IsStopped);

    private async Task SelectStartupHomeAsync(string id)
    {
        var fallback = id != SettingsWidgetId && catalogItems.Any(widget => widget.Id == SettingsWidgetId);
        var expected = selectionVersion + 1;
        var opening = visibleSince;
        var opened = await SelectAsync(id, enterWidget: id != SettingsWidgetId, homeFallbackAvailable: fallback);
        if (!opened && fallback && !retired && visible && visibleSince == opening && selectionVersion == expected &&
            catalogItems.Any(widget => widget.Id == SettingsWidgetId))
            await SelectAsync(SettingsWidgetId, enterWidget: false);
    }

    private async Task ChooseHomeWidgetAsync(WidgetActionRequest request)
    {
        if (owner is null || !visible || !foreground || retired) return;
        var session = owner.Session;
        session.ValidateHomeWidgetCommand(request.Displayed, request.Action);
        var choices = catalogItems.Where(widget => !widget.IsStopped)
            .OrderBy(widget => widget.Id == SettingsWidgetId ? 0 : 1)
            .ThenBy(widget => widget.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(widget => new HostDialogChoice(widget.Id, widget.Name)).ToArray();
        var current = choices.Any(choice => choice.Id == homeWidgetId);
        var selected = await ShowHostPreferenceAsync("Home widget",
            current ? "Choose which widget opens when WidgetRail starts." : "Your saved widget is unavailable. WidgetRail will start in Settings.",
            choices, lifetime.Token, currentChoiceId: current ? homeWidgetId : SettingsWidgetId, closeLabel: "Cancel");
        if (selected is null || selected == homeWidgetId || retired || !visible || !foreground) return;
        session.ValidateHomeWidgetCommand(request.Displayed, request.Action);
        if (!catalogItems.Any(widget => widget.Id == selected && !widget.IsStopped)) return;
        var saved = await new PlatformSettingsStore(new(options.SettingsRoot))
            .UpdateAsync(settings => settings with { HomeWidgetId = selected }, lifetime.Token);
        if (!retired)
        {
            homePreferenceRevision++;
            homeWidgetId = saved.HomeWidgetId;
        }
    }
}
