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
    private readonly Border face = new() { CornerRadius = new(200), Margin = new(8), Translation = new(0, 0, 16) };
    private readonly ThemeShadow elevation = new();
    private readonly Windows.UI.ViewManagement.AccessibilitySettings accessibility = new();
    private readonly Windows.UI.ViewManagement.UISettings systemUi = new();
    private readonly Grid slots = new() { Margin = new(12) };
    private readonly Border hubSurface = new() { Width = 180, Height = 180, CornerRadius = new(90), Padding = new(14), BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid paging = new() { ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock pageText = new() { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly Button previous = new() { Content = new FontIcon { Glyph = "\uE76B", FontFamily = new("Segoe Fluent Icons"), FontSize = 14 }, Width = 32, Height = 32, Padding = new(0) };
    private readonly Button next = new() { Content = new FontIcon { Glyph = "\uE76C", FontFamily = new("Segoe Fluent Icons"), FontSize = 14 }, Width = 32, Height = 32, Padding = new(0) };
    private readonly (Button Button, WidgetPackageIconView Icon, NativeComputedStyleAdapter Style)[] items;
    private readonly Func<BridgeWidgetDescriptor, string, CancellationToken, Task<WidgetPresentationPackageIcon>> resolveIcon;
    private readonly ShellChromeStyles chrome = new();
    private readonly SolidColorBrush clear = new(Microsoft.UI.Colors.Transparent);
    private readonly SolidColorBrush selectedFill = new();
    private IReadOnlyList<BridgeWidgetDescriptor> catalog = [];
    private string? selected;
    private int page;
    private bool updating, disposed, focusPending;
    private WidgetCompositionMotion? motion;
    private WidgetCompositionTarget? motionTarget;
    private WidgetMotionOptions? pendingMotion;
    internal bool HasElevation => face.Shadow is not null;
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
        elevation.Receivers.Add(receiver); face.Shadow = elevation; face.Child = slots;
        wheel.Children.Add(receiver); wheel.Children.Add(face);
        for (var i = 0; i < 10; ++i) { slots.RowDefinitions.Add(new()); slots.ColumnDefinitions.Add(new()); }
        var positions = new[] { (0, 4), (1, 7), (4, 8), (7, 7), (8, 4), (7, 1), (4, 0), (1, 1) };
        items = Enumerable.Range(0, 8).Select(index =>
        {
            var icon = new WidgetPackageIconView { Width = 40, Height = 40, FontSize = 40,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { Content = icon, Width = 66, Height = 66, MinWidth = 0, MinHeight = 0,
                Padding = new(8), Background = clear, CornerRadius = new(28), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Disabled };
            Grid.SetRow(button, positions[index].Item1); Grid.SetColumn(button, positions[index].Item2);
            Grid.SetRowSpan(button, 2); Grid.SetColumnSpan(button, 2); slots.Children.Add(button);
            button.GotFocus += (_, _) => { if (!updating && Item(index) is { } item) Selected?.Invoke(item); };
            button.Click += (_, _) => { if (Item(index) is { } item) Activated?.Invoke(item); };
            button.ContextRequested += (_, args) => { if (Item(index) is { } item) { args.Handled = true; ContextRequestedFor?.Invoke(item); } };
            return (button, icon, new NativeComputedStyleAdapter(button));
        }).ToArray();
        var hub = new StackPanel { Width = 148, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(hubSurface, 2); Grid.SetColumn(hubSurface, 2); Grid.SetRowSpan(hubSurface, 6); Grid.SetColumnSpan(hubSurface, 6);
        hub.Children.Add(title);
        paging.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); paging.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); paging.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        paging.Children.Add(previous); paging.Children.Add(pageText); paging.Children.Add(next); Grid.SetColumn(pageText, 1); Grid.SetColumn(next, 2);
        hub.Children.Add(paging); hubSurface.Child = hub; slots.Children.Add(hubSurface);
        previous.Click += (_, _) => PageRequested?.Invoke(-1); next.Click += (_, _) => PageRequested?.Invoke(1);
        AutomationProperties.SetAutomationId(this, "Overlay.Radial"); AutomationProperties.SetName(this, "Choose widget");
        AutomationProperties.SetAutomationId(previous, "Overlay.Radial.Previous"); AutomationProperties.SetName(previous, "Previous widgets");
        AutomationProperties.SetAutomationId(next, "Overlay.Radial.Next"); AutomationProperties.SetName(next, "Next widgets");
        chrome.Register(title, "body"); chrome.Register(pageText, "hint"); chrome.Register(previous, "tray-item"); chrome.Register(next, "tray-item");
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
        var contrast = appearance.Contrast == ContrastPreference.High || appearance.Contrast == ContrastPreference.System && accessibility.HighContrast;
        var fallback = Windows.UI.Color.FromArgb(255, 0x1B, 0x1F, 0x29);
        var canvas = OverlaySurfacePaint.Background(styles, "canvas", fallback);
        if (canvas.A == 0) canvas = fallback;
        var surface = OverlaySurfacePaint.Background(styles, "panel", canvas);
        // The chooser is opaque, but a transparent themed panel is not black.
        // Flatten its authored alpha over the theme canvas instead of dropping alpha.
        var color = contrast ? systemUi.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background) : Mix(canvas, surface, surface.A / 255d);
        if (face.Background is SolidColorBrush background) background.Color = color;
        else face.Background = new SolidColorBrush(color);
        face.Shadow = contrast ? null : elevation;
        face.Translation = new(0, 0, contrast ? 0 : 16);
        var foreground = contrast ? systemUi.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground) :
            styles?.GetValueOrDefault("body")?.Base.GetValueOrDefault("color") is { } textColor && NativeComputedStyleAdapter.TryColor(textColor.Text, out var resolved) ? resolved : Microsoft.UI.Colors.White;
        var material = contrast ? color : Mix(color, foreground, .055);
        SetBrush(hubSurface, material, false);
        SetBrush(hubSurface, contrast ? foreground : Mix(color, foreground, .18), true);
        selectedFill.Color = Mix(color, foreground, .16);
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
                ToolTipService.SetToolTip(entry.Button, item?.Name);
                var chosen = item?.Id == selected;
                AutomationProperties.SetItemStatus(entry.Button, chosen ? "Selected" : "");
                AutomationProperties.SetPositionInSet(entry.Button, page * 8 + index + 1);
                AutomationProperties.SetSizeOfSet(entry.Button, catalog.Count);
                var basic = styles?.GetValueOrDefault(chosen ? "tray-item:selected" : "tray-item")?.Base;
                var focus = styles?.GetValueOrDefault(chosen ? "tray-item:selected:focused" : "tray-item:focused")?.Base;
                entry.Style.Update(basic is null ? null : new() { Base = basic, Focused = focus ?? basic, Pressed = focus ?? basic });
                if (basic is null) entry.Button.Background = chosen ? selectedFill : clear;
                entry.Button.Width = entry.Button.Height = 66; entry.Button.MinWidth = entry.Button.MinHeight = 0;
            }
            title.Text = catalog.FirstOrDefault(item => item.Id == selected)?.Name ?? "Choose widget";
            pageText.Text = $"{page + 1}/{RadialChooserPolicy.PageCount(catalog.Count)}";
            paging.Visibility = catalog.Count > 8 ? Visibility.Visible : Visibility.Collapsed;
            previous.Visibility = next.Visibility = catalog.Count > 8 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(this, $"{title.Text}. Page {pageText.Text}");
        }
        finally { updating = false; }
    }
    private static Windows.UI.Color Mix(Windows.UI.Color surface, Windows.UI.Color ink, double amount) =>
        Windows.UI.Color.FromArgb(255, (byte)(surface.R + (ink.R - surface.R) * amount),
            (byte)(surface.G + (ink.G - surface.G) * amount), (byte)(surface.B + (ink.B - surface.B) * amount));
    private static void SetBrush(Border target, Windows.UI.Color color, bool stroke)
    {
        var existing = stroke ? target.BorderBrush : target.Background;
        if (existing is SolidColorBrush brush) brush.Color = color;
        else if (stroke) target.BorderBrush = new SolidColorBrush(color);
        else target.Background = new SolidColorBrush(color);
    }
    internal Button? ButtonFor(string id) => items.Select(item => item.Button).FirstOrDefault(button => AutomationProperties.GetAutomationId(button) == "Overlay.Radial.Widget." + id && button.Visibility == Visibility.Visible);
    internal Control ContextAnchor(string id) => ButtonFor(id) ?? next;
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
        if (focusPending && selected is { } id && ButtonFor(id) is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } target && target.Focus(FocusState.Keyboard)) CancelFocus();
    }
    private void CancelFocus() { focusPending = false; LayoutUpdated -= FocusReady; }
    internal void FocusPaging() { CancelFocus(); next.Focus(FocusState.Keyboard); }
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
        _ = motion.PlayAsync([new(motionTarget, WidgetMotionPolicy.Dialog(options, true))]);
    }
    private void StopMotion() { pendingMotion = null; LayoutUpdated -= MotionReady; motion?.Dispose(); motion = null; motionTarget?.Dispose(); motionTarget = null; }
    public void Dispose()
    {
        if (disposed) return; disposed = true; CancelFocus(); StopMotion(); chrome.Dispose();
        face.Shadow = null; elevation.Receivers.Clear();
        foreach (var item in items) { item.Style.Dispose(); item.Icon.Dispose(); }
    }
}
