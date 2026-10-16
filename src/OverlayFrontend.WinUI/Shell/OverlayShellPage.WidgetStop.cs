using Microsoft.UI.Xaml;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private bool showingStoppedWidget;

    private async Task StopWidgetAsync(BridgeWidgetDescriptor widget)
    {
        if (owner is null || !widget.CanStop || widget.IsStopped) return;
        if (await ShowHostChoiceAsync("Stop widget?",
            $"{widget.Name}\n\nThis ends its activity, playback, calls and pinned content. It stays stopped until you reload it, including after restarting WidgetRail. Saved data and sign-ins are kept.",
            (HostDialogChoice[])[new("stop", "Stop widget")], lifetime.Token) != "stop") return;
        await ChangeWidgetStoppedAsync(widget.Id, stopped: true);
    }

    private async Task ChangeWidgetStoppedAsync(string id, bool stopped)
    {
        if (owner is null || retired) return;
        var requestOwner = owner;
        var selected = requestedWidget == id;
        var enter = interactive;
        var failureCurrent = CapturePresentationFailureGuard();
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (retired || !ReferenceEquals(owner, requestOwner)) return;
                if (activeWidget == id) interactionAdmission.Invalidate();
                var target = requestOwner.Session.GetTarget(id);
                if (stopped) await requestOwner.Session.StopWidgetAsync(target, lifetime.Token);
                else await requestOwner.Session.StartWidgetAsync(target, lifetime.Token);
                SetCatalog(await requestOwner.Session.ListWidgetsAsync(lifetime.Token));
                if (stopped)
                {
                    if (pinned is { } pin && pin.WidgetId == id) await RemovePinnedCoreAsync();
                    await RetireWidgetSurfaceAsync(id);
                    if (browserOwner is not null) await browserOwner.RetireWidgetAsync(id);
                    if (mediaOwner is not null) await mediaOwner.RetireWidgetAsync(id);
                }
            }
            finally { transitions.Release(); }
            if (selected && requestedWidget == id && visible && !retired) await SelectAsync(id, enter);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
    }

    // This is host chrome: no worker, SDK snapshot or WebView is instantiated.
    private async Task PresentStoppedWidgetAsync(BridgeWidgetDescriptor descriptor)
    {
        previewRenderer?.SetVisible(false);
        if (surface is not null)
        {
            surface.Visibility = Visibility.Collapsed;
            await surface.SetPresentationActiveAsync(false);
        }
        await RetireWidgetSurfaceAsync(descriptor.Id);
        surface = null;
        previewRenderer = null;
        activeWidget = descriptor.Id;
        publication = 0;
        Tray.SelectedItem = catalogItems.FirstOrDefault(item => item.Id == descriptor.Id);
        preferences = preferences with { LastWidget = descriptor.Id, ReopenWidget = true };
        UpdateSurfaceHints(new() { PreferredWidth = 520, PreferredHeight = 180 });
        ShowRecovery($"{descriptor.Name}\nWidget stopped. Reload to start it again.", canRetry: true, stopped: true);
    }
}
