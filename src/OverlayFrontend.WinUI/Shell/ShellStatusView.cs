using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Passive, width-constrained clock and connectivity beside the widget rail.</summary>
internal sealed class ShellStatusView : ContentControl, IAsyncDisposable
{
    private readonly Border surface = new() { Padding = new Thickness(12, 4, 12, 4) };
    private readonly Grid layout = new() { ColumnSpacing = 8 };
    private readonly StackPanel indicators = new() { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock clock = new() { TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock date = new() { TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly FontIcon internet = new() { FontFamily = new("Segoe Fluent Icons"), Glyph = "\uE774", FontSize = 16 };
    private readonly FontIcon bluetooth = new() { FontFamily = new("Segoe Fluent Icons"), Glyph = "\uE702", FontSize = 16 };
    private readonly TextBlock internetMark = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock bluetoothMark = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly ShellChromeStyles styles = new();
    private readonly DispatcherQueueTimer timer;
    private readonly Func<CancellationToken, Task<ShellStatusSnapshot>> read;
    private CancellationTokenSource? request;
    private Task? pending;
    private Task<ShellStatusSnapshot>? reading;
    private long epoch;
    private long observedAt;
    private bool active;
    private bool disposed;
    private ShellStatusSnapshot snapshot = ShellStatusSnapshot.Unknown;
    internal string Description { get; private set; } = string.Empty;

    protected override AutomationPeer OnCreateAutomationPeer() => new StatusPeer(this);
    private sealed class StatusPeer(ShellStatusView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => nameof(ShellStatusView);
    }

    internal ShellStatusView(Func<CancellationToken, Task<ShellStatusSnapshot>>? read = null)
    {
        this.read = read ?? ShellStatusReader.ReadAsync;
        IsTabStop = false;
        IsHitTestVisible = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        indicators.Children.Add(Indicator(internet, internetMark)); indicators.Children.Add(Indicator(bluetooth, bluetoothMark));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var clockFit = new Viewbox { Child = clock, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAccessibilityView(clockFit, AccessibilityView.Raw);
        text.Children.Add(clockFit); text.Children.Add(date);
        layout.Children.Add(indicators); layout.Children.Add(text); Grid.SetColumn(text, 1);
        surface.Child = layout; Content = surface;
        styles.Register(surface, "tray"); styles.Register(clock, "tray-clock"); styles.Register(date, "tray-date");
        styles.Register(internet, "tray-status-icon"); styles.Register(bluetooth, "tray-status-icon");
        styles.Register(internetMark, "tray-date"); styles.Register(bluetoothMark, "tray-date");
        AutomationProperties.SetAutomationId(this, "Overlay.SystemStatus");
        foreach (var element in new FrameworkElement[] { surface, layout, indicators, text, clock, date, internet, bluetooth, internetMark, bluetoothMark })
            AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(5);
        timer.Tick += Tick;
        Loaded += LoadedStatus;
        Unloaded += UnloadedStatus;
        SizeChanged += (_, _) =>
        {
            // The rail chooses how much spare width to grant. Status never takes
            // icon capacity: first omit symbols, then date, before clipping time.
            indicators.Visibility = ActualWidth >= 160 ? Visibility.Visible : Visibility.Collapsed;
            date.Visibility = ActualWidth >= 100 ? Visibility.Visible : Visibility.Collapsed;
        };
        Paint();
    }

    private static Grid Indicator(FontIcon icon, TextBlock mark)
    {
        var panel = new Grid { MinWidth = 24, MinHeight = 20 };
        panel.Children.Add(icon); panel.Children.Add(mark);
        AutomationProperties.SetAccessibilityView(panel, AccessibilityView.Raw);
        return panel;
    }

    internal void ApplyAppearance(IReadOnlyDictionary<string, BridgeNodeRenderStyles>? palette, AppearanceSettings appearance, bool animationsEnabled) =>
        styles.Update(palette, appearance, animationsEnabled);

    internal Task RefreshNowAsync() { Paint(); Refresh(); return pending ?? Task.CompletedTask; }

    internal void SetActive(bool value)
    {
        if (disposed || active == value) return;
        active = value;
        ++epoch;
        if (!value) { timer.Stop(); request?.Cancel(); }
        else if (IsLoaded) { timer.Start(); Paint(); Refresh(); }
    }

    private void LoadedStatus(object sender, RoutedEventArgs args)
    { if (active && !disposed) { timer.Start(); Paint(); Refresh(); } }
    private void UnloadedStatus(object sender, RoutedEventArgs args)
    { if (!IsLoaded) { ++epoch; timer.Stop(); request?.Cancel(); } }
    private void Tick(DispatcherQueueTimer sender, object args) { Paint(); Refresh(); }

    private void Refresh()
    {
        if (!active || disposed || !IsLoaded || pending is { IsCompleted: false } || reading is { IsCompleted: false }) return;
        request?.Dispose(); request = new(TimeSpan.FromSeconds(4));
        pending = RefreshAsync(epoch, request.Token);
    }
    private async Task RefreshAsync(long version, CancellationToken token)
    {
        ShellStatusSnapshot next;
        try
        {
            reading = read(token);
            _ = reading.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            next = await reading.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { next = ShellStatusSnapshot.Unknown; }
        catch (Exception error) when (error is not OutOfMemoryException) { next = ShellStatusSnapshot.Unknown; }
        if (disposed || !active || version != epoch) return;
        snapshot = next; observedAt = Environment.TickCount64; Paint();
    }
    private void Paint()
    {
        var now = DateTime.Now;
        clock.Text = now.ToString("t", CultureInfo.CurrentCulture);
        date.Text = now.ToString("d", CultureInfo.CurrentCulture);
        var current = observedAt != 0 && Environment.TickCount64 - observedAt < 30000 ? snapshot : ShellStatusSnapshot.Unknown;
        Description = current.Describe(now, CultureInfo.CurrentCulture);
        AutomationProperties.SetName(this, Description);
        AutomationProperties.SetHelpText(this, Description);
        // Off/unavailable and unknown are distinct even without color. Accessible
        // text always names the exact state; icons are decorative.
        internetMark.Text = current.Internet switch { ShellInternetStatus.Online => "", ShellInternetStatus.Offline => "×", ShellInternetStatus.Limited => "!", _ => "?" };
        bluetoothMark.Text = current.Bluetooth switch { ShellBluetoothStatus.On => "", ShellBluetoothStatus.Off => "×", ShellBluetoothStatus.Unavailable => "–", _ => "?" };
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true; ++epoch; timer.Stop(); timer.Tick -= Tick;
        Loaded -= LoadedStatus; Unloaded -= UnloadedStatus;
        request?.Cancel();
        if (pending is not null) await pending;
        request?.Dispose(); request = null;
        styles.Dispose();
    }
}
