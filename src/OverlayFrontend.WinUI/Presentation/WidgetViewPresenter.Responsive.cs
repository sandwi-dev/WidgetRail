using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    // Responsive branches use the widget's viewport, never a row's measured size.
    // The protocol's existing breakpoint is shared by both frontends.
    private (double Width, double Height) Viewport()
    {
        WidgetViewPresenter owner = this;
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is WidgetViewPresenter presenter && !presenter.presentationOnly) owner = presenter;
        return (owner.ActualWidth, owner.ActualHeight);
    }

    private void UpdateResponsiveVisibility()
    {
        var (width, height) = Viewport();
        var compact = width < 960 || height < 540;
        foreach (var declaration in declarations.Values)
        {
            var parentVisible = declaration.ParentId is not { } parent || bindings[parent].Element.Visibility == Visibility.Visible;
            var visible = parentVisible && declaration.Node.VisibleWhen switch
            {
                ResponsiveVisibility.CompactOnly => compact,
                ResponsiveVisibility.ExpandedOnly => !compact,
                _ => true,
            };
            bindings[declaration.Node.Id].Element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RefreshResponsiveLayout()
    {
        if (disposed || applying || frame is null) return;
        var focused = FocusedBinding();
        var persistence = focused is not null ? declarations[focused.Identity.Id].Node.FocusPersistenceId : null;
        UpdateResponsiveVisibility();
        foreach (var declaration in declarations.Values)
        {
            var element = bindings[declaration.Node.Id].Element;
            ApplySizeAndTypography(element, declaration.Node);
            if (element is Grid grid && element is not (WidgetModalLayer or WidgetPosterPanel)) UpdateLayout(grid, declaration.Node);
        }
        UpdateModalGeometry();
        UpdateNativeNeighbors();
        ValidateTransientControl();
        if (focused is null || Eligible(focused)) return;
        pendingRestore = persistence is null ? null : bindings.Values.FirstOrDefault(candidate => Eligible(candidate) &&
            declarations[candidate.Identity.Id].Node.FocusPersistenceId == persistence);
        needsEntry = pendingRestore is null;
        QueueEntryFocus();
    }
}
