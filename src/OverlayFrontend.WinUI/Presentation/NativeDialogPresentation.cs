using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Anchors native dialog chrome without replacing native focus, modality or animation.</summary>
internal sealed class NativeDialogPresentation : IDisposable
{
    private readonly ContentDialog dialog;
    private readonly FrameworkElement origin;
    private readonly Action paint;
    private FrameworkElement? panel;
    private readonly HashSet<Button> actions = [];
    private bool disposed, updating;

    internal NativeDialogPresentation(ContentDialog dialog, FrameworkElement origin, Action paint)
    {
        this.dialog = dialog;
        this.origin = FindAnchor(origin);
        this.paint = paint;
        dialog.Loaded += Loaded;
        dialog.Opened += Opened;
        if (dialog is FrameworkDialog framework) framework.DialogTemplateChanged += TemplateChanged;
        dialog.LayoutUpdated += LayoutUpdated;
        this.origin.LayoutUpdated += LayoutUpdated;
    }

    private static FrameworkElement FindAnchor(FrameworkElement origin)
    {
        for (DependencyObject? node = origin; node is not null; node = PresentationLayers.LogicalParent(node))
            if (node is WidgetViewPresenter presenter) return presenter;
        return origin;
    }

    private void Loaded(object sender, RoutedEventArgs args) { panel = null; RefreshChrome(); Update(); }
    private void Opened(ContentDialog sender, ContentDialogOpenedEventArgs args) { RefreshChrome(); Update(); }
    private void TemplateChanged() { panel = null; RefreshChrome(); }
    private void RefreshChrome()
    {
        // ContentDialog moves its template outside the control's routed-event
        // ancestry. Observe the actual action buttons rather than the dialog.
        foreach (var button in actions) { button.GotFocus -= FocusChanged; button.LostFocus -= FocusChanged; }
        actions.Clear();
        foreach (var name in new[] { "PrimaryButton", "SecondaryButton", "CloseButton" })
            if (FindPart<Button>(dialog, name) is { } button && actions.Add(button))
            { button.GotFocus += FocusChanged; button.LostFocus += FocusChanged; }
        paint();
    }
    private void FocusChanged(object sender, RoutedEventArgs args) => paint();
    private void LayoutUpdated(object? sender, object args) => Update();

    private void Update()
    {
        if (disposed || updating || !dialog.IsLoaded || dialog.XamlRoot is not { } root || !origin.IsLoaded || !ReferenceEquals(origin.XamlRoot, root)) return;
        // Floating keyboards and notification inboxes own their native placement.
        if (dialog is WidgetTextEntryDialog { IsFloating: true }) return;
        panel ??= FindPart<FrameworkElement>(dialog, "BackgroundElement");
        if (panel is null || root.Size.Width <= 0 || root.Size.Height <= 0) return;
        updating = true;
        try
        {
            var availableWidth = Math.Max(1, root.Size.Width - 32);
            var availableHeight = Math.Max(1, root.Size.Height - 32);
            // Keep normal native sizing; only restrict it to the current window.
            var maxWidth = dialog.Resources.TryGetValue("ContentDialogMaxWidth", out var limit) && limit is double width ? width : 548;
            panel.MaxWidth = Math.Min(maxWidth, availableWidth);
            panel.MinWidth = Math.Min(320, panel.MaxWidth);
            panel.MinHeight = 0;
            var bounds = origin.TransformToVisual(root.Content).TransformBounds(new(0, 0, origin.ActualWidth, origin.ActualHeight));
            panel.MaxHeight = availableHeight;
            var w = Math.Min(panel.ActualWidth, availableWidth);
            var h = Math.Min(panel.ActualHeight, panel.MaxHeight);
            var x = Math.Clamp(bounds.X + (bounds.Width - w) / 2, 16, Math.Max(16, root.Size.Width - w - 16));
            var y = Math.Clamp(bounds.Y + (bounds.Height - h) / 2, 16, Math.Max(16, root.Size.Height - h - 16));
            panel.HorizontalAlignment = HorizontalAlignment.Left;
            panel.VerticalAlignment = VerticalAlignment.Top;
            var margin = new Thickness(Math.Round(x), Math.Round(y), 0, 0);
            if (panel.Margin != margin) panel.Margin = margin;
        }
        finally { updating = false; }
    }

    internal static T? FindPart<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is FrameworkDialog dialog && dialog.DialogTemplatePart(name) is T part) return part;
        if (root is T match && match.Name == name) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindPart<T>(VisualTreeHelper.GetChild(root, i), name) is { } child) return child;
        return null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        dialog.Loaded -= Loaded;
        dialog.Opened -= Opened;
        if (dialog is FrameworkDialog framework) framework.DialogTemplateChanged -= TemplateChanged;
        foreach (var button in actions) { button.GotFocus -= FocusChanged; button.LostFocus -= FocusChanged; }
        actions.Clear();
        dialog.LayoutUpdated -= LayoutUpdated;
        origin.LayoutUpdated -= LayoutUpdated;
    }
}
