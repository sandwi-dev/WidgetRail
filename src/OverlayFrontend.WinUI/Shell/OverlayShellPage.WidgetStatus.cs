using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly Dictionary<string, WidgetPresentationActivity> reportedActivities = new(StringComparer.Ordinal);
    private readonly Dictionary<object, string> nativeAudioSources = [];
    private readonly HashSet<WidgetCatalogItemContent> statusTiles = [];
    private Dictionary<string, int> pendingByWidget = new(StringComparer.Ordinal);
    private bool statusRefreshQueued;
    private readonly HashSet<string> failedStatusWidgets = new(StringComparer.Ordinal);
    private void ObserveWidgetStatusState(WidgetPresentationState state)
    {
        var changed = state.Failure is not null ? failedStatusWidgets.Add(state.WidgetId) : failedStatusWidgets.Remove(state.WidgetId);
        if (changed || reportedActivities.TryGetValue(state.WidgetId, out var reported) && owner?.Session.IsHostEffectAuthorityCurrent(reported.Authority) != true)
            QueueWidgetStatusRefresh();
    }
    private void ReceiveWidgetActivity(object? sender, WidgetActivityEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (retired || owner?.Session.IsHostEffectAuthorityCurrent(args.Activity.Authority) != true) return;
        if (args.Activity.Activity == new WidgetRail.WidgetProtocol.WidgetActivity()) reportedActivities.Remove(args.Activity.Authority.WidgetId);
        else reportedActivities[args.Activity.Authority.WidgetId] = args.Activity;
        QueueWidgetStatusRefresh();
    });
    private void ReportNativeAudio(object source, string widget, bool playing)
    {
        if (playing && !retired) nativeAudioSources[source] = widget;
        else nativeAudioSources.Remove(source);
        QueueWidgetStatusRefresh();
    }
    private WidgetStatus StatusFor(BridgeWidgetDescriptor widget)
    {
        var reported = reportedActivities.GetValueOrDefault(widget.Id);
        var activity = reported is not null && owner?.Session.IsHostEffectAuthorityCurrent(reported.Authority) == true ? reported.Activity : null;
        return new(pendingByWidget.GetValueOrDefault(widget.Id),
            Audio: !widget.IsStopped && (activity?.LocalAudio == true || browserOwner?.PlayingAudio(widget.Id) == true ||
                mediaOwner?.PlayingAudio(widget.Id) == true || nativeAudioSources.Values.Contains(widget.Id)),
            RemotePlayback: !widget.IsStopped && activity?.RemotePlayback == true, Stopped: widget.IsStopped,
            Failed: !widget.IsStopped && (owner?.Session.GetState(widget.Id)?.Failure is not null ||
                browserOwner?.NeedsAttention(widget.Id) == true || mediaOwner?.GetFailure(widget.Id) is not null),
            Recording: !widget.IsStopped && activity?.Recording == true);
    }
    private void QueueWidgetStatusRefresh()
    {
        if (retired || statusRefreshQueued) return;
        statusRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            statusRefreshQueued = false;
            if (retired) return;
            foreach (var entry in reportedActivities.ToArray())
                if (owner?.Session.IsHostEffectAuthorityCurrent(entry.Value.Authority) != true) reportedActivities.Remove(entry.Key);
            pendingByWidget = browserOwner?.Notifications.Entries.GroupBy(entry => entry.Widget, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
            foreach (var tile in statusTiles)
            {
                if (tile.Descriptor is not { } descriptor) continue;
                var status = StatusFor(descriptor);
                tile.SetStatus(status, palette);
                if (Tray.ContainerFromItem(descriptor) is Control target && AutomationProperties.GetName(target) != status.Describe(descriptor.Name))
                {
                    AutomationProperties.SetName(target, status.Describe(descriptor.Name));
                    Presentation.NativePopupTheme.SetToolTip(target, status.Describe(descriptor.Name));
                }
            }
            radialView?.RefreshBadges();
        })) statusRefreshQueued = false;
    }
}
