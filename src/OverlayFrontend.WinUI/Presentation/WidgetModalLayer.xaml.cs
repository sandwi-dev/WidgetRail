using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    internal Action? DismissPreview { get; set; }
    private bool dismissalQueued;
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
        PreviewDismissSurface.PointerPressed += (_, args) => { args.Handled = true; DismissPreview?.Invoke(); };
    }

    internal void Configure(FrameworkElement parent, FrameworkElement panel,
        double width, double height, double maximumWidth, double maximumHeight, FrameworkElement? semanticDialog = null,
        AnchoredPresentation? anchored = null, Func<AnchorRectangle?>? anchor = null)
    {
        if (!ReferenceEquals(dialog, panel) || anchoredPresentation != anchored) contentHeight.Reset();
        if (!ReferenceEquals(dialog, panel))
        {
            motion?.Dispose();
            motion = new(panel, Scrim, appearance, systemAnimationsEnabled);
        }
        dialog = panel;
        anchoredPresentation = anchored; resolveAnchor = anchor;
        Scrim.Visibility = anchored?.IsInteractivePreview == true ? Visibility.Collapsed : Visibility.Visible;
        PreviewDismissSurface.Visibility = anchored?.IsInteractivePreview == true ? Visibility.Visible : Visibility.Collapsed;
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
            var source = resolveAnchor?.Invoke();
            if (anchored.IsInteractivePreview && source is not { Valid: true })
            {
                dialog.IsHitTestVisible = false;
                if (!dismissalQueued && IsLoaded && ActualWidth > 0)
                {
                    dismissalQueued = true;
                    DispatcherQueue.TryEnqueue(() => { dismissalQueued = false;
                        if (anchoredPresentation?.IsInteractivePreview == true && resolveAnchor?.Invoke() is not { Valid: true }) DismissPreview?.Invoke(); });
                }
                return;
            }
            dialog.IsHitTestVisible = true;
            var placed = AnchoredPresentationGeometry.Place(ActualWidth, ActualHeight, source,
                dialog.Width, dialog.Height, anchored.Placement);
            dialog.Margin = new Thickness(placed.X, placed.Y, 0, 0);
        }
    }

    private void AnchorLayoutUpdated(object? sender, object args) => ConstrainDialog();
    public void Dispose()
    {
        if (observingAnchor) LayoutUpdated -= AnchorLayoutUpdated;
        observingAnchor = false; resolveAnchor = null;
        DismissPreview = null;
        motion?.Dispose(); motion = null;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ModalPeer(this);
    private sealed partial class ModalPeer(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
    }
}
