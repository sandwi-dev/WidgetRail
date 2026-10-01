using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Passive feedback for the shell's existing prepare/commit transaction.</summary>
internal sealed partial class WidgetOpeningIndicator : ContentControl, IDisposable
{
    private readonly Border plate = new() { Padding = new(14, 10, 18, 10), BorderThickness = new(1) };
    private readonly Ellipse halo = new() { Width = 46, Height = 46, StrokeThickness = 1.5,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock name = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock status = new() { Text = "Opening…" };
    private readonly WidgetCatalogItemContent icon;
    private readonly Image applicationIcon = new() { Width = 30, Height = 30,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed };
    private readonly ShellChromeStyles styles = new();
    private readonly DispatcherQueueTimer delay;
    private WidgetCompositionMotion? fade;
    private WidgetCompositionTarget? fadeTarget;
    private ScalarKeyFrameAnimation? pulse;
    private WidgetMotionOptions motion = WidgetMotionOptions.From(AppearanceSettings.Default, true);
    private bool highContrast, pending, disposed;
    private long selection, fadeVersion;
    internal bool IsShowing => Visibility == Visibility.Visible;
    internal bool IsPulsing => pulse?.IterationBehavior == AnimationIterationBehavior.Forever;
    internal bool IsCompleting => !pending && IsShowing && fade is not null;
    internal string StatusText => status.Text;
    internal string WidgetName => name.Text;
    internal int ShowCount { get; private set; }

    internal WidgetOpeningIndicator(Func<BridgeWidgetDescriptor, string, CancellationToken, Task<WidgetPresentationPackageIcon>> resolve)
    {
        IsTabStop = false;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        MaxWidth = 360;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        icon = new(resolve) { ShowLabel = false, IconSize = 26, TileSize = 30,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mark = new Grid { Width = 50, Height = 50 };
        mark.Children.Add(halo); mark.Children.Add(icon); mark.Children.Add(applicationIcon);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name); text.Children.Add(status);
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.Children.Add(mark); row.Children.Add(text); Grid.SetColumn(text, 1);
        plate.Child = row; Content = plate;
        plate.SizeChanged += (_, _) => TryStartArrival();
        plate.Loaded += (_, _) => TryStartArrival();
        styles.Register(name, "body"); styles.Register(status, "hint");
        AutomationProperties.SetAutomationId(this, "Overlay.WidgetOpening");
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        foreach (var child in new FrameworkElement[] { plate, row, mark, halo, icon, applicationIcon, text, name, status })
            AutomationProperties.SetAccessibilityView(child, AccessibilityView.Raw);
        delay = DispatcherQueue.CreateTimer();
        delay.Interval = TimeSpan.FromMilliseconds(200); delay.IsRepeating = false;
        delay.Tick += Show;
        Unloaded += (_, _) => { if (!IsLoaded) Clear(); };
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OpeningPeer(this);
    private sealed partial class OpeningPeer(WidgetOpeningIndicator owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => nameof(WidgetOpeningIndicator);
    }

    internal void Begin(long version, BridgeWidgetDescriptor? descriptor)
    {
        if (disposed) return;
        Clear(); selection = version; pending = true;
        name.Text = descriptor?.Name ?? "Widget";
        status.MinWidth = 0;
        status.Text = "Opening…";
        icon.SetItem(descriptor);
        AutomationProperties.SetName(this, "Opening " + name.Text);
        delay.Start();
    }

    // Cold-start feedback shares the widget badge's theme, pulse and interruptible
    // completion instead of maintaining another animation implementation.
    internal void BeginApplication(long version)
    {
        if (disposed) return;
        Clear(); selection = version; pending = true;
        name.Text = "WidgetRail";
        status.MinWidth = 0;
        status.Text = "Starting…";
        applicationIcon.Source ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(
            new Uri("ms-appx:///Assets/WidgetRail.svg")) { RasterizePixelWidth = 120, RasterizePixelHeight = 120 };
        icon.Visibility = Visibility.Collapsed;
        applicationIcon.Visibility = Visibility.Visible;
        AutomationProperties.SetName(this, "Starting WidgetRail");
        Show(delay, EventArgs.Empty);
    }

    private void Show(DispatcherQueueTimer sender, object args)
    {
        if (disposed || !pending) return;
        Visibility = Visibility.Visible; ++ShowCount;
        UpdatePulse();
        TryStartArrival();
        FrameworkElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    internal void Complete(long version)
    {
        if (disposed || version != selection || !pending) return;
        pending = false; delay.Stop();
        if (!IsShowing || !IsLoaded || plate.ActualWidth <= 0 || plate.ActualHeight <= 0 || motion.Reduced || highContrast) { Clear(); return; }
        status.MinWidth = status.ActualWidth;
        status.Text = "Ready";
        AutomationProperties.SetName(this, name.Text + " ready");
        FrameworkElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        SettleHalo();
        StartFade(new(WidgetMotionPose.Identity, WidgetMotionPose.Identity with { Opacity = 0 }, motion.Duration(500)), retire: true);
    }

    private void StartFade(WidgetMotionRecipe recipe, bool retire)
    {
        var epoch = ++fadeVersion;
        fadeTarget ??= WidgetCompositionTarget.ForClippedDialog(plate, new((float)plate.ActualWidth, (float)plate.ActualHeight));
        fade ??= new(ElementCompositionPreview.GetElementVisual(plate).Compositor, DispatcherQueue);
        // Reuse the same target so completion during arrival retargets from the
        // compositor's current opacity rather than flashing back to opaque.
        _ = FinishFadeAsync(fade.PlayAsync((WidgetMotionPlayback[])[new(fadeTarget, recipe)]), epoch, retire);
    }

    private void TryStartArrival()
    {
        if (disposed || !pending || !IsShowing || fade is not null || motion.Reduced || highContrast ||
            !plate.IsLoaded || plate.ActualWidth <= 0 || plate.ActualHeight <= 0) return;
        StartFade(new(WidgetMotionPose.Identity with { Opacity = 0 }, WidgetMotionPose.Identity, motion.Duration(100)), retire: false);
    }

    private async Task FinishFadeAsync(Task<WidgetMotionOutcome> playback, long epoch, bool retire)
    {
        try { await playback; }
        catch (Exception error) { Diagnostics.FrontendFailureLog.Current.Write("widget-opening-motion", error); }
        finally { if (!disposed && epoch == fadeVersion && retire) Clear(); }
    }

    internal void ApplyAppearance(IReadOnlyDictionary<string, BridgeNodeRenderStyles>? palette, AppearanceSettings appearance, bool animationsEnabled)
    {
        if (disposed) return;
        styles.Update(palette, appearance, animationsEnabled);
        var paint = ShellChromePalette.Resolve(palette, appearance);
        plate.Background = ShellChromePalette.Brush(paint.Surface);
        plate.BorderBrush = ShellChromePalette.Brush(paint.HighContrast ? paint.Text : ShellChromePalette.Blend(paint.Surface, paint.Text, .18));
        plate.CornerRadius = new(Math.Min(16, OverlaySurfacePaint.CornerRadius(palette)));
        icon.Foreground = ShellChromePalette.Brush(paint.Text);
        halo.Stroke = ShellChromePalette.Brush(paint.Focus);
        var next = WidgetMotionOptions.From(appearance, animationsEnabled);
        if (motion != next || highContrast != paint.HighContrast)
        {
            motion = next; highContrast = paint.HighContrast;
            if (!pending && IsShowing) Clear();
            else
            {
                if (motion.Reduced || highContrast) StopFade();
                UpdatePulse();
            }
        }
    }

    private void UpdatePulse()
    {
        StopPulse();
        if (disposed || !pending || !IsShowing || motion.Reduced || highContrast) return;
        var visual = ElementCompositionPreview.GetElementVisual(halo);
        pulse = visual.Compositor.CreateScalarKeyFrameAnimation();
        pulse.InsertKeyFrame(0, .25f); pulse.InsertKeyFrame(.5f, .85f); pulse.InsertKeyFrame(1, .25f);
        pulse.Duration = motion.Duration(1600); pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(Visual.Opacity), pulse);
    }

    private void StopPulse()
    {
        if (pulse is null) return;
        ElementCompositionPreview.GetElementVisual(halo).StopAnimation(nameof(Visual.Opacity));
        pulse.Dispose(); pulse = null;
    }

    private void SettleHalo()
    {
        // Replace the loop from its sampled compositor value; stopping it first
        // would briefly restore the fully opaque XAML base value.
        var visual = ElementCompositionPreview.GetElementVisual(halo);
        var previous = pulse;
        pulse = visual.Compositor.CreateScalarKeyFrameAnimation();
        pulse.InsertExpressionKeyFrame(0, "this.StartingValue");
        pulse.InsertKeyFrame(1, .45f);
        pulse.Duration = motion.Duration(140);
        visual.StartAnimation(nameof(Visual.Opacity), pulse);
        previous?.Dispose();
    }

    internal void Clear()
    {
        pending = false; delay.Stop();
        Visibility = Visibility.Collapsed;
        StopPulse(); StopFade();
        icon.SetItem(null);
        icon.Visibility = Visibility.Visible;
        applicationIcon.Visibility = Visibility.Collapsed;
    }

    private void StopFade()
    {
        ++fadeVersion;
        fade?.Dispose(); fade = null; fadeTarget?.Dispose(); fadeTarget = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        Clear(); disposed = true; delay.Tick -= Show; icon.Dispose(); styles.Dispose();
    }
}
