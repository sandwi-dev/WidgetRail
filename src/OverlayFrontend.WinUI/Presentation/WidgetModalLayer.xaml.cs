using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Windows.Foundation;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using Microsoft.UI.Xaml.Automation.Peers;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// A bounded widget-local overlay. The stable native stage keeps its parent in
/// the same viewport; this Grid owns only the scrim and dialog chrome.
/// The input-blocking scrim is below the dialog and above the unchanged parent.
/// </summary>
internal sealed partial class WidgetModalLayer : Grid, IDisposable
{
    private FrameworkElement? dialog;
    private double preferredWidth;
    private double preferredHeight;
    private AnchoredPresentation? anchoredPresentation;
    private Func<AnchorRectangle?>? resolveAnchor;
    private bool observingAnchor;
    private readonly ContentSizedPresentationState contentHeight = new();
    private double authoredMinWidth;
    private double authoredMinHeight;
    private double authoredMaxWidth;
    private double authoredMaxHeight;
    private readonly RectangleGeometry boundsClip = new();
    internal UIElement Chrome => Scrim;
    private WidgetDialogMotion? motion;
    internal Task<WidgetMotionOutcome>? OpeningMotion => motion?.Opening;
    internal Task<WidgetMotionOutcome> CloseAsync() => motion?.CloseAsync() ?? Task.FromResult(WidgetMotionOutcome.Completed);
    private AppearanceSettings appearance = AppearanceSettings.Default;
    private bool systemAnimationsEnabled = true;
    internal Func<Action?>? CaptureBackdropDismissal { get; set; }
    private (uint PointerId, Point Origin, Action Dismiss)? backdropGesture;
#if ENABLE_WIDGET_VALIDATION
    internal Action<string>? PointerTrace { get; set; }
#endif
    internal void ApplyAppearance(AppearanceSettings value, bool animationsEnabled)
    {
        appearance = value; systemAnimationsEnabled = animationsEnabled;
        motion?.ApplyAppearance(value, animationsEnabled);
    }

    public WidgetModalLayer()
    {
        InitializeComponent();
        Clip = boundsClip;
        SizeChanged += (_, _) => ConstrainDialog();
        Scrim.PointerPressed += BackdropPressed;
        Scrim.PointerMoved += BackdropMoved;
        Scrim.PointerReleased += BackdropReleased;
        Scrim.PointerCanceled += (_, _) => CancelBackdropGesture();
        Scrim.PointerCaptureLost += (_, _) => backdropGesture = null;
#if ENABLE_WIDGET_VALIDATION
        foreach (var (routed, name) in new[] { (PointerPressedEvent, "pressed"), (PointerMovedEvent, "moved"), (PointerReleasedEvent, "released") })
            Scrim.AddHandler(routed, new PointerEventHandler((_, args) =>
            {
                var point = args.GetCurrentPoint(Scrim);
                PointerTrace?.Invoke($"{name}: {point.Position.X:0.0},{point.Position.Y:0.0} handled={args.Handled} left={point.Properties.IsLeftButtonPressed} gesture={backdropGesture is not null}");
            }), true);
#endif
        Unloaded += (_, _) => { if (!IsLoaded) CancelBackdropGesture(); };
    }

    internal void Configure(FrameworkElement parent, FrameworkElement panel,
        double width, double height, double maximumWidth, double maximumHeight, FrameworkElement? semanticDialog = null,
        AnchoredPresentation? anchored = null, Func<AnchorRectangle?>? anchor = null)
    {
        if (!ReferenceEquals(dialog, panel) || anchoredPresentation != anchored) contentHeight.Reset();
        if (!ReferenceEquals(dialog, panel))
        {
            CancelBackdropGesture();
            motion?.Dispose();
            motion = new(panel, Scrim, appearance, systemAnimationsEnabled);
        }
        dialog = panel;
        anchoredPresentation = anchored; resolveAnchor = anchor;
        if (observingAnchor) LayoutUpdated -= AnchorLayoutUpdated;
        observingAnchor = anchored is not null;
        if (observingAnchor) LayoutUpdated += AnchorLayoutUpdated;
        preferredWidth = anchored?.Width ?? width;
        preferredHeight = anchored?.Height ?? height;
        authoredMinWidth = panel.MinWidth;
        authoredMinHeight = panel.MinHeight;
        authoredMaxWidth = maximumWidth;
        authoredMaxHeight = maximumHeight;
        Canvas.SetZIndex(parent, 0);
        Canvas.SetZIndex(panel, 2);
        panel.HorizontalAlignment = anchored is null ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        panel.VerticalAlignment = anchored is null ? VerticalAlignment.Center : VerticalAlignment.Top;
        panel.Margin = new();
        panel.TabFocusNavigation = Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Cycle;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetIsDialog(semanticDialog ?? panel, true);
        ConstrainDialog();
    }

    private void ConstrainDialog()
    {
        boundsClip.Rect = new(0, 0, ActualWidth, ActualHeight);
        if (dialog is null) return;
        // Clamping a native control's requested dimensions is host geometry
        // policy, not a replacement measure/arrange pass. Zero is valid while
        // opening; the first SizeChanged supplies the actual widget viewport.
        var availableWidth = Math.Max(0, ActualWidth - 32);
        var availableHeight = Math.Max(0, ActualHeight - 32);
        dialog.MaxWidth = Math.Min(authoredMaxWidth, availableWidth);
        dialog.MaxHeight = Math.Min(authoredMaxHeight, availableHeight);
        dialog.MinWidth = Math.Min(authoredMinWidth, dialog.MaxWidth);
        dialog.MinHeight = Math.Min(authoredMinHeight, dialog.MaxHeight);
        // Native minimums take precedence over Width/Height. Placement must use
        // the same effective dimensions that WinUI will actually arrange.
        dialog.Width = Math.Clamp(preferredWidth, dialog.MinWidth, dialog.MaxWidth);
        var maximumHeight = Math.Clamp(preferredHeight, dialog.MinHeight, dialog.MaxHeight);
        dialog.Height = anchoredPresentation?.SizeToContentHeight == true
            ? contentHeight.Resolve(dialog.Width, Math.Max(96, dialog.MinHeight), maximumHeight, appearance.TextScale,
                () => ContentSizedPresentationMeasure.Height(dialog, dialog.Width, maximumHeight))
            : maximumHeight;
        if (anchoredPresentation is { } anchored)
        {
            var placed = AnchoredPresentationGeometry.Place(ActualWidth, ActualHeight, resolveAnchor?.Invoke(),
                dialog.Width, dialog.Height, anchored.Placement);
            dialog.Margin = new Thickness(placed.X, placed.Y, 0, 0);
        }
    }

    private void AnchorLayoutUpdated(object? sender, object args) => ConstrainDialog();

    private void BackdropPressed(object sender, PointerRoutedEventArgs args)
    {
        // The scrim remains input blocking even when dismissal is unavailable.
        args.Handled = true;
        CancelBackdropGesture();
        var point = args.GetCurrentPoint(Scrim);
        if (!point.Properties.IsPrimary || args.Pointer.PointerDeviceType == PointerDeviceType.Mouse &&
                (!point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed) ||
            CaptureBackdropDismissal?.Invoke() is not { } dismiss) return;
        if (Scrim.CapturePointer(args.Pointer)) backdropGesture = (args.Pointer.PointerId, point.Position, dismiss);
    }

    private void BackdropMoved(object sender, PointerRoutedEventArgs args)
    {
        if (backdropGesture is not { } gesture || gesture.PointerId != args.Pointer.PointerId) return;
        var point = args.GetCurrentPoint(Scrim).Position;
        var dx = point.X - gesture.Origin.X; var dy = point.Y - gesture.Origin.Y;
        // Moving back onto the original point does not turn a drag into a click.
        if (dx * dx + dy * dy > 25) CancelBackdropGesture();
    }

    private void BackdropReleased(object sender, PointerRoutedEventArgs args)
    {
        args.Handled = true;
        var gesture = backdropGesture;
        backdropGesture = null;
        Scrim.ReleasePointerCapture(args.Pointer);
        if (gesture is not { } captured || captured.PointerId != args.Pointer.PointerId) return;
        var point = args.GetCurrentPoint(Scrim).Position;
        var dx = point.X - captured.Origin.X; var dy = point.Y - captured.Origin.Y;
        if (dx * dx + dy * dy > 25 || point.X < 0 || point.Y < 0 || point.X >= Scrim.ActualWidth || point.Y >= Scrim.ActualHeight) return;
        if (dialog is { } panel)
        {
            var local = args.GetCurrentPoint(panel).Position;
            if (local.X >= 0 && local.Y >= 0 && local.X < panel.ActualWidth && local.Y < panel.ActualHeight) return;
        }
        captured.Dismiss();
    }

    private void CancelBackdropGesture()
    {
        backdropGesture = null;
        Scrim.ReleasePointerCaptures();
    }

    public void Dispose()
    {
        CancelBackdropGesture(); CaptureBackdropDismissal = null;
        if (observingAnchor) LayoutUpdated -= AnchorLayoutUpdated;
        observingAnchor = false; resolveAnchor = null;
        motion?.Dispose(); motion = null;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ModalPeer(this);
    private sealed partial class ModalPeer(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
    }
}
