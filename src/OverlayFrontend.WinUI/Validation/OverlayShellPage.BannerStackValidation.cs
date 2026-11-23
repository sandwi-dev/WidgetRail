using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private int validationBannerHeightLimit = int.MaxValue;
    internal void EnableBannerStackValidation(string resultPath)
    {
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            try
            {
                await WebAppUntil(() => notificationCenter is not null && MainFocusEnabled);
                var center = notificationCenter!;
                await center.Ready;
                HideNotificationBanner();
                void Show(string source, string key, string body) => center.Publish(source, this,
                    new(key, new WidgetNotification(key, source) { Body = body, Retention = WidgetNotificationRetention.BannerOnly,
                        Visibility = WidgetNotificationVisibility.Always }), () => !retired);
                Show("capture-library", "capture.1", "Screenshot saved.");
                await WebAppUntil(() => notificationBanner?.IsVisible == true && notificationBannerState.Visible.Count == 1);
                var first = notificationBannerState.Visible[0];
                var deadline = first.ExpiresAt;
                validationBannerHeightLimit = 0;
                PumpNotificationBanners();
                if (notificationBanner!.IsVisible || !notificationBannerTimer!.IsRunning)
                    throw new InvalidOperationException("Obstruction stopped placement retries for a lone active card.");
                validationBannerHeightLimit = int.MaxValue;
                await WebAppUntil(() => notificationBanner.IsVisible);
                await Task.Delay(180);
                Show("capture-library", "capture.2", "Screenshot saved.");
                await WebAppUntil(() => notificationBannerState.Visible.Count == 2);
                var top = notificationBannerState.Visible[0];
                if (top.Entry.Id == first.Entry.Id || first.ExpiresAt != deadline || top.ExpiresAt <= deadline ||
                    Canvas.GetTop(bannerViews[first].Chrome) <= Canvas.GetTop(bannerViews[top].Chrome))
                    throw new InvalidOperationException("A second capture did not move the first below it with its original deadline.");
                Show("widgetrail.samples.discord-controls", "message.1", "Message received.");
                await WebAppUntil(() => notificationBannerState.Visible.Count == 3);
                await CapturePopupDecisionAsync(notificationBanner!.AutomationRoot, "stack", resultPath);
                Show("capture-library", "capture.3", "Another screenshot saved.");
                var waiting = notificationBannerState.Waiting.Single();
                var oneCardHeight = (int)Math.Ceiling(bannerViews[first].Height * XamlRoot.RasterizationScale * Appearance.InterfaceScale);
                validationBannerHeightLimit = oneCardHeight;
                PumpNotificationBanners();
                if (!notificationBanner.IsVisible || bannerStack.Children.Count != 1 || !notificationBannerState.Waiting.Contains(waiting))
                    throw new InvalidOperationException("Constrained placement lost the queue or failed to show the newest card.");
                validationBannerHeightLimit = 0;
                PumpNotificationBanners();
                if (notificationBanner.IsVisible || waiting.Entry.WasShown || !notificationBannerState.Waiting.Contains(waiting))
                    throw new InvalidOperationException("An obstructed work area consumed the waiting notification.");
                validationBannerHeightLimit = int.MaxValue;
                PumpNotificationBanners();
                if (!notificationBanner.IsVisible || bannerStack.Children.Count != 3 || first.ExpiresAt != deadline)
                    throw new InvalidOperationException("Restoring placement lost cards or renewed an existing deadline.");
                center.Inbox.WithdrawWidgetKey("capture-library", "capture.2");
                await WebAppUntil(() => waiting.Entry.WasShown);
                if (!notificationBannerState.Visible.Contains(first) || notificationBanner.Interactive || waiting.ExpiresAt <= deadline)
                    throw new InvalidOperationException("Withdrawal disturbed a neighboring card or activated the peer.");
                await WebAppUntil(() => !notificationBanner.IsVisible);
                Show("capture-library", "capture.close", "Screenshot saved before closing the overlay.");
                await WebAppUntil(() => notificationBanner.IsVisible && notificationBannerState.Visible.Count == 1);
                var crossing = notificationBannerState.Visible.Single();
                var originalDeadline = crossing.ExpiresAt;
                void VerifyBanner()
                {
                    if (!notificationBanner.IsVisible || notificationBanner.Interactive ||
                        !ReferenceEquals(notificationBannerState.Visible.SingleOrDefault(), crossing) ||
                        crossing.ExpiresAt != originalDeadline || !crossing.Entry.WasShown)
                        throw new InvalidOperationException("Overlay visibility changed the active banner or its deadline.");
                }
                var main = (nint)hostWindow;
                if (!OverlayWindowFrame.IsAbove(notificationBanner.Handle, main))
                    throw new InvalidOperationException("The notification is behind the open widget.");
                HideRequested?.Invoke();
                await WebAppUntil(() => OverlayWindowFrame.IsWindowVisible(main) == 0);
                VerifyBanner();
                ((MainWindow)App.Window).ShowOverlay();
                await WebAppUntil(() => visible && OverlayWindowFrame.IsWindowVisible(main) != 0 &&
                    OverlayWindowFrame.IsAbove(notificationBanner.Handle, main));
                VerifyBanner();
                await WebAppUntil(() => !notificationBanner.IsVisible);
                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = true, distinctCaptures = 3, concurrentCards = 3,
                    interruptedPlacement = true, survivesCloseAndReopen = true, widget = activeWidget,
                    reducedMotion = Motion.WidgetMotionOptions.From(Appearance, systemUi.AnimationsEnabled).Reduced }));
            }
            catch (Exception error)
            {
                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = false, error = error.ToString() }));
            }
        };
    }
}
