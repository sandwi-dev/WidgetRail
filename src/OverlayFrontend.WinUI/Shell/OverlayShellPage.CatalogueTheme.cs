using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private CancellationTokenSource? cataloguePreviewCancellation;

    private async Task TryCatalogueThemeAsync(WidgetActionRequest request)
    {
        if (request.Authority.WidgetId != "marketplace" || owner is null || HostChoiceActive ||
            cataloguePreviewCancellation is not null || !visible || !foreground) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var current = ShellPalette;
        var session = owner.Session;
        using var preview = new ThemePreviewContent(Math.Clamp(XamlRoot.Size.Width - 120, 320, 960),
            Math.Clamp(XamlRoot.Size.Height - 250, 180, 500));
        async Task LoadAsync()
        {
            try
            {
                var styles = await session.ReadCatalogueThemePreviewAsync(request.Displayed, request.Action, cancellation.Token);
                if (!cancellation.IsCancellationRequested && !retired && visible && activeWidget == request.Authority.WidgetId)
                    preview.Show(styles, current, Appearance, systemUi.AnimationsEnabled);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                Diagnostics.FrontendFailureLog.Current.Write("catalogue-theme-preview", error);
                if (!cancellation.IsCancellationRequested) preview.Fail();
            }
        }
        cataloguePreviewCancellation = cancellation;
        var loading = Task.CompletedTask;
        try
        {
            var showing = ShowHostChoiceAsync("Try theme", "", [], cancellation.Token,
                configure: dialog =>
                {
                    dialog.CloseButtonText = "Back";
                    dialog.SetInteractiveContent(preview, preview.View.Width);
                });
            if (!showing.IsCompleted) loading = LoadAsync();
            await showing;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            cancellation.Cancel();
            await loading;
            cataloguePreviewCancellation = null;
        }
    }

    private sealed class ThemePreviewContent : IHostDialogContent, IDisposable
    {
        private readonly StackPanel root;
        private readonly CatalogueThemeScene scene;
        private readonly TextBlock status = new()
        {
            Text = "Preparing the theme preview…",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new(16),
        };
        private readonly Button compare = new() { Content = "Compare with current theme", Visibility = Visibility.Collapsed };
        private readonly Button posters = new() { Content = "Poster rail", Visibility = Visibility.Collapsed };
        private readonly Button notification = new() { Content = "Show notification", Visibility = Visibility.Collapsed };
        private bool showingCurrent;
        private bool showingPosters;
        private bool showingNotification;
        private bool ready;
        internal ThemePreviewContent(double width, double height)
        {
            root = new() { Width = width, Spacing = 8 };
            scene = new() { Height = height, Visibility = Visibility.Collapsed };
            AutomationProperties.SetAutomationId(compare, "ThemePreview.Compare");
            AutomationProperties.SetAutomationId(posters, "ThemePreview.Posters");
            AutomationProperties.SetAutomationId(notification, "ThemePreview.Notification");
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            var controls = new StackPanel { Spacing = 8, MinHeight = width >= 560 ? 36 : 106,
                Orientation = width >= 560 ? Orientation.Horizontal : Orientation.Vertical };
            controls.Children.Add(compare);
            controls.Children.Add(posters);
            controls.Children.Add(notification);
            root.Children.Add(controls);
            var stage = new Grid { Height = height };
            stage.Children.Add(scene);
            stage.Children.Add(status);
            root.Children.Add(stage);
            posters.Click += (_, _) =>
            {
                showingPosters = !showingPosters;
                showingNotification = false;
                posters.Content = showingPosters ? "Radial tray" : "Poster rail";
                notification.Content = "Show notification";
                scene.ShowPosters(showingPosters);
            };
            notification.Click += (_, _) =>
            {
                showingNotification = !showingNotification;
                notification.Content = showingNotification ? "Hide notification" : "Show notification";
                scene.ShowNotification(showingNotification);
            };
        }
        public FrameworkElement View => root;
        public event Action? InteractionChanged;
        public IReadOnlyList<ControllerGuideHint>? Guide => null;
        public bool Move(FocusNavigationDirection direction, bool repeated) => false;
        public bool Handle(ControllerButton button, ControllerEventPhase phase) => false;
        public void ResetInput() { }

        internal void Show(IReadOnlyDictionary<string, BridgeNodeRenderStyles> candidate,
            IReadOnlyDictionary<string, BridgeNodeRenderStyles>? current, WidgetRail.PlatformSettings.AppearanceSettings appearance, bool animations)
        {
            if (ready) return;
            ready = true;
            scene.Apply(candidate, appearance, animations);
            scene.Visibility = Visibility.Visible;
            status.Visibility = Visibility.Collapsed;
            posters.Visibility = notification.Visibility = Visibility.Visible;
            if (current is not null)
            {
                compare.Visibility = Visibility.Visible;
                compare.Click += (_, _) =>
                {
                    showingCurrent = !showingCurrent;
                    scene.Apply(showingCurrent ? current : candidate, appearance, animations);
                    compare.Content = showingCurrent ? "Show catalogue theme" : "Compare with current theme";
                };
            }
            InteractionChanged?.Invoke();
        }

        internal void Fail() => status.Text = "Theme preview unavailable. Go back and try again.";
        public void Dispose() => scene.Dispose();
    }
}
