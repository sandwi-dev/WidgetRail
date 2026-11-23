using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Focus snaps between measured sections. The trailing body keeps its
/// own viewport while the header or intermediate sections scroll out of view.</summary>
internal sealed partial class WidgetFocusRevealGrid : Grid
{
    private readonly RevealContent content = new();
    private readonly ScrollViewer viewport = new()
    {
        IsTabStop = false, BringIntoViewOnFocusChange = false,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
        HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        ZoomMode = ZoomMode.Disabled,
    };
    private int? focusedRow;
    private double?[]? snapEnds;
    private FrameworkElement? peekTarget;
    private double peekExtent;

    internal void ConfigureStops(double?[]? ends, FrameworkElement? peek, double extent)
    {
        if ((snapEnds == ends || snapEnds is not null && ends is not null && snapEnds.SequenceEqual(ends)) &&
            ReferenceEquals(peekTarget, peek) && peekExtent == extent) return;
        snapEnds = ends;
        peekTarget = peek;
        peekExtent = extent;
        content.SetHeaderCount(ends is null ? 1 : ends.Length - 1);
        QueueReveal();
    }
    private bool queued;
    private bool initialSectionPosition = true;
    internal Grid Tracks => content;
    private bool reducedMotion;
    internal bool ReducedMotion
    {
        get => reducedMotion;
        set
        {
            if (reducedMotion == value) return;
            reducedMotion = value;
            if (value && focusedRow.HasValue) QueueReveal();
        }
    }

    internal WidgetFocusRevealGrid()
    {
        Children.Add(viewport);
        viewport.Content = content;
        content.GotFocus += (_, _) => ObserveFocus();
        content.SizeChanged += (_, _) => { if (focusedRow.HasValue) QueueReveal(); };
        // Nested controls have already processed their own reveal. The outer
        // viewport moves to a section boundary, never to an individual child.
        content.BringIntoViewRequested += (_, args) => args.Handled = true;
        Loaded += (_, _) => ObserveFocus();
    }

    private void ObserveFocus()
    {
        if (XamlRoot is null) return;
        for (var child = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; child is not null;)
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (ReferenceEquals(parent, content) && child is FrameworkElement element)
            {
                var row = GetRow(element);
                if (focusedRow != row) { focusedRow = row; QueueReveal(); }
                return;
            }
            child = parent;
        }
    }

    private void QueueReveal()
    {
        if (queued) return;
        queued = DispatcherQueue.TryEnqueue(() =>
        {
            queued = false;
            if (!IsLoaded || focusedRow is not { } row || content.RowDefinitions.Count < 2) return;
            var offset = row > 0 ? content.RowDefinitions[0].ActualHeight + content.RowSpacing : 0;
            if (snapEnds is { } ends && row < ends.Length && row < content.RowDefinitions.Count)
            {
                offset = 0;
                for (var i = 0; i < row; ++i) offset += content.RowDefinitions[i].ActualHeight + content.RowSpacing;
                if (row == 0 && peekTarget is { IsLoaded: true } peek && ReferenceEquals(peek.XamlRoot, XamlRoot))
                    offset = Math.Clamp(peek.TransformToVisual(content).TransformPoint(new Point()).Y +
                        peekExtent - viewport.ViewportHeight, 0, content.RowDefinitions[0].ActualHeight);
                else if (ends[row] is { } end)
                    offset = Math.Max(0, offset - Math.Max(0, end - content.RowDefinitions[row].ActualHeight));
            }
            // On entry, place the first focused section before animating any
            // user navigation. Restoring it should not play a long scroll.
            viewport.ChangeView(null, offset, null, disableAnimation: ReducedMotion || snapEnds is not null && initialSectionPosition);
            initialSectionPosition = false;
        });
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        content.SetViewport(availableSize.Height);
        return base.MeasureOverride(availableSize);
    }

    private sealed partial class RevealContent : Grid
    {
        private int headerCount = 1;
        internal void SetHeaderCount(int count)
        {
            if (headerCount == count) return;
            headerCount = count;
            InvalidateMeasure();
        }
        private double viewportHeight = double.PositiveInfinity;
        internal void SetViewport(double height)
        {
            if (viewportHeight == height) return;
            viewportHeight = height;
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (!double.IsFinite(viewportHeight) || RowDefinitions.Count < 2)
                return base.MeasureOverride(availableSize);
            // Let Grid measure Auto using the authored column widths and margins.
            // A second measure is only needed when the intrinsic header size changes.
            var count = Math.Min(headerCount, RowDefinitions.Count - 1);
            var previous = 0d;
            for (var i = 0; i < count; i++) previous += RowDefinitions[i].ActualHeight;
            var size = new Size(availableSize.Width, viewportHeight + previous + count * RowSpacing);
            var desired = base.MeasureOverride(size);
            double measured = 0;
            for (var i = 0; i < count; ++i)
            {
                var height = 0d;
                foreach (var child in Children)
                    if (child is FrameworkElement element && GetRow(element) == i)
                        height = Math.Max(height, element.DesiredSize.Height);
                measured += Math.Clamp(height, RowDefinitions[i].MinHeight, RowDefinitions[i].MaxHeight);
            }
            if (Math.Abs(measured - previous) > .01)
            {
                size.Height = viewportHeight + measured + count * RowSpacing;
                desired = base.MeasureOverride(size);
            }
            return new Size(desired.Width, size.Height);
        }
    }
}
