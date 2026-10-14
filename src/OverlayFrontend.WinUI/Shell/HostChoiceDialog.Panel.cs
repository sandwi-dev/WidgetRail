using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class HostChoiceDialog
{
    private FrameworkElement? panelAnchor;
    private readonly TextBlock panelStatus = new() { TextWrapping = TextWrapping.Wrap, Opacity = .8 };
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? statusTimer;
    private double panelScale = 1;
    private string emptyMessage = "";

    internal void ConfigurePendingPanel(FrameworkElement anchor, double scale)
    {
        panelAnchor = anchor; panelScale = scale;
        DefaultButton = ContentDialogButton.None;
        Resources["ContentDialogMinHeight"] = 0d;
        Resources["ContentDialogMinWidth"] = 0d;
        Resources["ContentDialogPadding"] = new Thickness(16 * scale);
        choices.Resources["ListViewItemSelectionIndicatorVisualEnabled"] = false;
        // Arrivals must not animate the content away from its stable reading position.
        choices.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
        CloseButtonText = "Close";
        UseActionList();
        if (Content is StackPanel content)
        {
            content.MaxWidth = double.PositiveInfinity;
            content.Children.Insert(0, panelStatus);
        }
        AutomationProperties.SetAutomationId(panelStatus, "Overlay.NotificationStatus");
        AutomationProperties.SetLiveSetting(panelStatus, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Resources["ContentDialogSmokeFill"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Opened += (_, _) =>
        {
            PositionPendingPanel();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!IsLoaded || !opened) return;
                PositionPendingPanel(); FocusFirstChoice();
            });
            XamlRoot.Changed += PanelRootChanged;
        };
        Closed += (_, _) => { XamlRoot.Changed -= PanelRootChanged; statusTimer?.Stop(); };
        SizeChanged += (_, _) => { if (IsLoaded) PositionPendingPanel(); };
    }
    internal Windows.Foundation.Rect PendingPanelBounds => GetTemplateChild("BackgroundElement") is FrameworkElement panel
        ? panel.TransformToVisual(null).TransformBounds(new(0, 0, panel.ActualWidth, panel.ActualHeight)) : default;
    private void PanelRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => PositionPendingPanel();
    private void PositionPendingPanel()
    {
        if (panelAnchor is null || XamlRoot is null || GetTemplateChild("BackgroundElement") is not FrameworkElement panel) return;
        var root = XamlRoot.Size;
        var anchor = panelAnchor.IsLoaded ? panelAnchor.TransformToVisual(null).TransformBounds(new(0, 0, panelAnchor.ActualWidth, panelAnchor.ActualHeight))
            : new Windows.Foundation.Rect(root.Width - 48, root.Height - 76, 48, 48);
        var left = anchor.X + anchor.Width / 2 < root.Width / 2;
        var bottom = Math.Clamp(root.Height - anchor.Y + 12, 12, Math.Max(12, root.Height - 240));
        var width = Math.Min(460 * panelScale, Math.Max(160, root.Width - 32));
        var x = Math.Clamp(left ? anchor.X : anchor.Right - width, 16, Math.Max(16, root.Width - width - 16));
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        panel.VerticalAlignment = VerticalAlignment.Bottom;
        panel.MinHeight = panel.MinWidth = 0;
        panel.Width = width;
        panel.MaxWidth = width;
        panel.Margin = new(x, 16, 16, bottom);
        SetListHeight(Math.Min(440 * panelScale, Math.Max(48, root.Height - bottom - 150 * panelScale)));
    }
    internal void SetPendingStatus(bool empty, string? overflow)
    {
        emptyMessage = empty ? "You're all caught up" : overflow ?? "";
        if (statusTimer?.IsRunning != true) ShowPanelStatus(emptyMessage);

    }
    internal void ShowBriefStatus(string text)
    {
        ShowPanelStatus(text);
        statusTimer ??= DispatcherQueue.CreateTimer();
        statusTimer.Stop(); statusTimer.Interval = TimeSpan.FromSeconds(4); statusTimer.IsRepeating = false;
        statusTimer.Tick -= ClearPanelStatus; statusTimer.Tick += ClearPanelStatus; statusTimer.Start();
    }
    private void ClearPanelStatus(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => ShowPanelStatus(emptyMessage);
    private void ShowPanelStatus(string text)
    { panelStatus.Text = text; panelStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }
}
