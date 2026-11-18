using Microsoft.UI.Xaml.Controls.Primitives;
using WidgetRail.OverlayFrontend.WinUI.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

public sealed partial class WidgetIndexedRowView
{
    private WidgetIndexedRow? laidOutRow;
    private bool awaitingRowLayout;

    internal Microsoft.UI.Xaml.Media.ImageSource? BorrowDecodedArtwork(WidgetIndexedRow row, SelectorItem container, string handle) =>
        row.Lease.IsCurrent && HasCurrentLayout(row, container) ? presenter?.BorrowDecodedArtwork(handle) : null;

    internal bool HasCurrentLayout(WidgetIndexedRow row, SelectorItem container) =>
        !disposed && IsLoaded && ReferenceEquals(Row, row) && ReferenceEquals(appliedRow, row) &&
        ReferenceEquals(laidOutRow, row) && ReferenceEquals(styleContainer, container) &&
        ActualWidth > 0 && ActualHeight > 0 && container.ActualWidth > 0 && container.ActualHeight > 0;

    private void AwaitRowLayout()
    {
        if (awaitingRowLayout) return;
        awaitingRowLayout = true;
        LayoutUpdated += RowLayoutUpdated;
        // An unchanged retained row does not enter this path. Newly applied
        // content/style must finish one native measure/arrange before navigation
        // or saved viewport restoration can use its geometry.
        InvalidateMeasure();
    }

    private void RowLayoutUpdated(object? sender, object args)
    {
        if (disposed || !IsLoaded || Row is not WidgetIndexedRow row || !ReferenceEquals(appliedRow, row) ||
            styleContainer is not { IsLoaded: true } container || selectionCollection is not { } collection ||
            ActualWidth <= 0 || ActualHeight <= 0 || container.ActualWidth <= 0 || container.ActualHeight <= 0) return;
        laidOutRow = row;
        if (!collection.ReportRowLayout(container, this, row)) { laidOutRow = null; return; }
        awaitingRowLayout = false;
        LayoutUpdated -= RowLayoutUpdated;
    }

    private void RetireRowLayout()
    {
        if (styleContainer is { } container) selectionCollection?.ForgetRowLayout(container, this);
        laidOutRow = null;
        awaitingRowLayout = false;
        LayoutUpdated -= RowLayoutUpdated;
    }
}
