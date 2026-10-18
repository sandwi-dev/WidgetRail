using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private Task? startupUpdateCheck;
    private readonly object appUpdateNotificationOwner = new();
    private string? notifiedAppVersion;
    private long appUpdateRevision = -1;
    private async Task CheckAppUpdatesAfterStartupAsync()
    {
        try
        {
            if (startup is { } initial) await initial.WaitAsync(lifetime.Token);
            await Task.Delay(TimeSpan.FromSeconds(10), lifetime.Token);
            if (retired || owner is null) return;
            ReceiveAppUpdate(owner.Session, await owner.Session.StartAppUpdateCheckAsync(lifetime.Token));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("app-update-check", null, error.GetType().Name); }
    }
    private void ReceiveAppUpdate(object? sender, AppUpdateSnapshot snapshot)
    {
        if (!ReferenceEquals(sender, owner?.Session)) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (notificationCenter is not { } center) return;
            await center.Ready;
            if (retired || !ReferenceEquals(sender, owner?.Session) || snapshot.Revision <= appUpdateRevision) return;
            appUpdateRevision = snapshot.Revision;
            if (snapshot.State == "failed" && snapshot.InstallerOutcomePending)
            {
                center.Publish("settings", appUpdateNotificationOwner, new("app-update", new WidgetNotification("app-update", "WidgetRail update needs attention")
                {
                    Body = snapshot.Message,
                    Visibility = WidgetNotificationVisibility.Always,
                }), () => !retired && ReferenceEquals(sender, owner?.Session));
                return;
            }
            if (snapshot.State == "current") { center.Inbox.WithdrawWidgetKey("settings", "app-update"); return; }
            if (snapshot.State is not ("available" or "ready") || snapshot.AvailableVersion is not { } version || version == notifiedAppVersion) return;
            notifiedAppVersion = version;
            center.Publish("settings", appUpdateNotificationOwner, new("app-update", new WidgetNotification("app-update", "WidgetRail update available")
            {
                Body = "Version " + version + " is available. Open to review the update.",
                Visibility = WidgetNotificationVisibility.Always,
            }), () => !retired && ReferenceEquals(sender, owner?.Session));
        });
    }
}
