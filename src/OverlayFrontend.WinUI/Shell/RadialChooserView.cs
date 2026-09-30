using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Eight native controls on an authored Grid; no custom measure/paint or input polling.</summary>
internal sealed class RadialChooserView : ContentControl, IDisposable
{
    private readonly Grid wheel = new() { Width = 400, Height = 400 };
    private readonly Border face = new() { Width = 368, Height = 368, CornerRadius = new(184), Translation = new(0, 0, 16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ThemeShadow elevation = new();
    private readonly Canvas slots = new() { Width = 400, Height = 400 };
    private readonly RadialChooserSurface sectors = new();
    private readonly ContentControl hubSurface = new() { Width = 180, Height = 180, Padding = new(14),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        IsTabStop = true, UseSystemFocusVisuals = false, XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Disabled };
    private readonly Grid paging = new() { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock pageText = new() { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly FontIcon pageGlyph = new() { FontSize = 48, Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button previous = new() { Content = new FontIcon { Glyph = "\uE76B", FontFamily = new("Segoe Fluent Icons"), FontSize = 14 }, Width = 32, Height = 32, Padding = new(0), AllowFocusOnInteraction = false };
    private readonly Button next = new() { Content = new FontIcon { Glyph = "\uE76C", FontFamily = new("Segoe Fluent Icons"), FontSize = 14 }, Width = 32, Height = 32, Padding = new(0), AllowFocusOnInteraction = false };
    private readonly (Button Button, WidgetPackageIconView Icon)[] items;
    private readonly Func<BridgeWidgetDescriptor, string, CancellationToken, Task<WidgetPresentationPackageIcon>> resolveIcon;
    private readonly ShellChromeStyles chrome = new();
    private readonly SolidColorBrush clear = new(Microsoft.UI.Colors.Transparent);
    private IReadOnlyList<BridgeWidgetDescriptor> catalog = [];
    private string? selected;
    private int page;
    private bool updating, disposed, focusPending;
    private WidgetCompositionMotion? motion;
    private WidgetCompositionTarget? motionTarget;
    private WidgetMotionOptions? pendingMotion;
    internal bool HasElevation => face.Shadow is not null;
    internal RadialChooserSurface SectorSurface => sectors;
    internal TimeSpan LastEntranceDuration { get; private set; }
    internal Action<BridgeWidgetDescriptor>? Selected;
    internal Action<BridgeWidgetDescriptor>? Activated;
    internal Action<BridgeWidgetDescriptor>? ContextRequestedFor;
    internal Action<int>? PageRequested;
    internal Action<FocusNavigationDirection>? DirectionRequested;
    internal Func<Task>? BackRequested;

    internal RadialChooserView(Func<BridgeWidgetDescriptor, string, CancellationToken, Task<WidgetPresentationPackageIcon>> icons)
    {
        resolveIcon = icons;
        IsTabStop = false;
        Content = new Viewbox { Child = wheel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        var receiver = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        elevation.Receivers.Add(receiver); face.Shadow = elevation;
        wheel.Children.Add(receiver); wheel.Children.Add(face); wheel.Children.Add(sectors); wheel.Children.Add(slots);
        items = Enumerable.Range(0, 8).Select(index =>
        {
            var icon = new WidgetPackageIconView { Width = 40, Height = 40, FontSize = 40,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { Content = icon, Width = 66, Height = 66, MinWidth = 0, MinHeight = 0,
                Padding = new(8), Background = clear, BorderThickness = new(0), CornerRadius = new(28), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Disabled };
            foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
            { button.Resources["ButtonBackground" + state] = clear; button.Resources["ButtonBorderBrush" + state] = clear; }
            Canvas.SetLeft(button, 200 + Math.Sin(index * Math.PI / 4) * 144 - 33);
            Canvas.SetTop(button, 200 - Math.Cos(index * Math.PI / 4) * 144 - 33);
            slots.Children.Add(button);
            button.GotFocus += (_, _) => { if (!updating && Item(index) is { } item) Selected?.Invoke(item); };
            button.Click += (_, _) => { if (Item(index) is { } item) Activated?.Invoke(item); };
            button.ContextRequested += (_, args) => { if (Item(index) is { } item) { args.Handled = true; ContextRequestedFor?.Invoke(item); } };
            return (button, icon);
        }).ToArray();
        var hub = new StackPanel { Width = 148, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        paging.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); paging.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); paging.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        paging.Children.Add(previous); paging.Children.Add(pageGlyph); paging.Children.Add(next); Grid.SetColumn(pageGlyph, 1); Grid.SetColumn(next, 2);
        hub.Children.Add(paging); hub.Children.Add(pageText);
        hubSurface.Content = hub; wheel.Children.Add(hubSurface);
        previous.Click += (_, _) => PageRequested?.Invoke(-1); next.Click += (_, _) => PageRequested?.Invoke(1);
        AutomationProperties.SetAutomationId(this, "Overlay.Radial"); AutomationProperties.SetName(this, "Choose widget");
        AutomationProperties.SetAutomationId(previous, "Overlay.Radial.Previous"); AutomationProperties.SetName(previous, "Previous widgets");
        AutomationProperties.SetAutomationId(next, "Overlay.Radial.Next"); AutomationProperties.SetName(next, "Next widgets");
        AutomationProperties.SetAutomationId(hubSurface, "Overlay.Radial.Paging");
        AutomationProperties.SetAutomationId(pageGlyph, "Overlay.Radial.PagingHint");
        chrome.Register(pageText, "hint"); chrome.Register(previous, "tray-item"); chrome.Register(next, "tray-item");
        WidgetControllerPrompts.Changed += UpdatePagingGlyph; UpdatePagingGlyph();
        PreviewKeyDown += (_, args) =>
        {
            if (Input.GamepadKeyBoundary.Owns(this, args)) return;
            var direction = args.Key switch { Windows.System.VirtualKey.Left => FocusNavigationDirection.Left, Windows.System.VirtualKey.Up => FocusNavigationDirection.Up,
                Windows.System.VirtualKey.Right => FocusNavigationDirection.Right, Windows.System.VirtualKey.Down => FocusNavigationDirection.Down, _ => FocusNavigationDirection.None };
            if (direction != FocusNavigationDirection.None) { args.Handled = true; DirectionRequested?.Invoke(direction); }
            else if (args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; _ = BackRequested?.Invoke(); }
        };
        Unloaded += (_, _) => { StopMotion(); CancelFocus(); };
        Loaded += (_, _) => TryFocus();
    }

    private BridgeWidgetDescriptor? Item(int slot) => page * 8 + slot < catalog.Count ? catalog[page * 8 + slot] : null;
    internal void Update(IReadOnlyList<BridgeWidgetDescriptor> value, string? selection, int browsedPage,
        IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles, AppearanceSettings appearance, bool animations)
    {
        if (disposed) return;
        catalog = value; selected = selection; page = Math.Clamp(browsedPage, 0, RadialChooserPolicy.PageCount(value.Count) - 1);
        chrome.Update(styles, appearance, animations);
        var palette = ShellChromePalette.Resolve(styles, appearance);
        if (face.Background is SolidColorBrush background) background.Color = palette.Surface;
        else face.Background = new SolidColorBrush(palette.Surface);
        face.Shadow = palette.HighContrast ? null : elevation;
        face.Translation = new(0, 0, palette.HighContrast ? 0 : 16);
        sectors.Translation = slots.Translation = hubSurface.Translation = face.Translation;
        sectors.Update(palette, Math.Min(8, catalog.Count - page * 8),
            Enumerable.Range(0, 8).FirstOrDefault(index => Item(index)?.Id == selected, -1));
        pageText.Foreground = ShellChromePalette.Brush(palette.Muted);
        pageGlyph.Foreground = pageText.Foreground;
        updating = true;
        try
        {
            foreach (var (entry, index) in items.Select((entry, index) => (entry, index)))
            {
                var item = Item(index);
                entry.Button.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
                entry.Button.IsTabStop = item is not null;
                if (item is not null)
                {
                    var asset = item.IconAssets.FirstOrDefault(candidate => candidate.AssetId == item.PackageIcon?.AssetId);
                    var generation = $"{item.Id}|{item.InstanceId}|{item.RuntimeGeneration}|{item.PresentationGeneration}|{item.PackageContentDigest}|{asset?.SourceSha256}|{asset?.NormalizedSha256}";
                    entry.Icon.Update(new() { Id = "radial.icon", Kind = ViewNodeKind.Icon, Glyph = item.Icon,
                        PackageIcon = item.PackageIcon, AccessibilityLabel = item.Name },
                        (assetId, token) => resolveIcon(item, assetId, token), generation, null);
                }

                AutomationProperties.SetAutomationId(entry.Button, "Overlay.Radial.Widget." + (item?.Id ?? index.ToString()));
                AutomationProperties.SetName(entry.Button, item?.Name ?? "");
                NativePopupTheme.SetToolTip(entry.Button, item?.Name);
                var chosen = item?.Id == selected;
                AutomationProperties.SetItemStatus(entry.Button, chosen ? "Selected" : "");
                AutomationProperties.SetPositionInSet(entry.Button, page * 8 + index + 1);
                AutomationProperties.SetSizeOfSet(entry.Button, catalog.Count);
                var ink = ShellChromePalette.Brush(chosen ? palette.SelectedText : palette.Text);
                entry.Button.Foreground = ink; entry.Icon.Foreground = ink;
                foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" }) entry.Button.Resources["ButtonForeground" + state] = ink;
                entry.Button.UseSystemFocusVisuals = palette.HighContrast;
                entry.Button.Width = entry.Button.Height = 66; entry.Button.MinWidth = entry.Button.MinHeight = 0;
            }
            pageText.Text = $"{page + 1}/{RadialChooserPolicy.PageCount(catalog.Count)}";
            pageText.Visibility = catalog.Count > 8 ? Visibility.Visible : Visibility.Collapsed;
            previous.Visibility = next.Visibility = catalog.Count > 8 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(hubSurface, $"Widget page {page + 1} of {RadialChooserPolicy.PageCount(catalog.Count)}");
            AutomationProperties.SetHelpText(this, $"{catalog.FirstOrDefault(item => item.Id == selected)?.Name ?? "Choose widget"}. Page {pageText.Text}");
            UpdatePagingGlyph();
        }
        finally { updating = false; }
    }
    private void UpdatePagingGlyph() => WidgetGlyphs.Apply(pageGlyph, new() { Id = "radial.paging", Kind = ViewNodeKind.ControllerGlyph,
        ControllerPrompt = catalog.Count > 8 ? ControllerPrompt.RightStickMove : ControllerPrompt.LeftStickMove,
        AccessibilityLabel = catalog.Count > 8 ? "Right stick changes widget page" : "Left stick chooses widget" }, WidgetControllerPrompts.PlayStation);
    internal Button? ButtonFor(string id) => items.Select(item => item.Button).FirstOrDefault(button => AutomationProperties.GetAutomationId(button) == "Overlay.Radial.Widget." + id && button.Visibility == Visibility.Visible);
    internal Control ContextAnchor(string id) => (Control?)ButtonFor(id) ?? hubSurface;
    internal bool FocusSelected()
    {
        if (selected is not { } id || ButtonFor(id) is null) return false;
        focusPending = true;
        LayoutUpdated -= FocusReady; LayoutUpdated += FocusReady;
        TryFocus();
        return true;
    }
    private void FocusReady(object? sender, object args) => TryFocus();
    private void TryFocus()
    {
        if (disposed || Visibility != Visibility.Visible) { CancelFocus(); return; }
        if (!IsLoaded) return;
        if (focusPending && selected is { } id && ButtonFor(id) is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } target && target.Focus(FocusState.Keyboard)) CancelFocus();
    }
    private void CancelFocus() { focusPending = false; LayoutUpdated -= FocusReady; }
    internal void FocusPaging() { CancelFocus(); if (!disposed && Visibility == Visibility.Visible && IsLoaded) hubSurface.Focus(FocusState.Programmatic); }
    internal void Close() { CancelFocus(); StopMotion(); }
    internal void Open(WidgetMotionOptions options)
    {
        StopMotion();
        pendingMotion = options;
        LayoutUpdated += MotionReady;
        MotionReady(null, null!);
    }
    private void MotionReady(object? sender, object args)
    {
        if (pendingMotion is not { } options || !IsLoaded || wheel.ActualWidth <= 0) return;
        pendingMotion = null; LayoutUpdated -= MotionReady;
        LastEntranceDuration = WidgetMotionPolicy.Dialog(options, true).Duration;
        if (options.Reduced) return;
        motionTarget = WidgetCompositionTarget.ForClippedDialog(wheel, new Vector2(400));
        motion = new(CompositionTarget.GetCompositorForCurrentThread(), DispatcherQueue);
        _ = motion.PlayAsync((WidgetMotionPlayback[])[new(motionTarget, WidgetMotionPolicy.Dialog(options, true))]);
    }
    private void StopMotion() { pendingMotion = null; LayoutUpdated -= MotionReady; motion?.Dispose(); motion = null; motionTarget?.Dispose(); motionTarget = null; }
    public void Dispose()
    {
        if (disposed) return; disposed = true; CancelFocus(); StopMotion(); chrome.Dispose();
        WidgetControllerPrompts.Changed -= UpdatePagingGlyph;
        face.Shadow = null; elevation.Receivers.Clear();
        foreach (var item in items) item.Icon.Dispose();
    }
}
