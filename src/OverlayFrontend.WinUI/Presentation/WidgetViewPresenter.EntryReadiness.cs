using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private Binding? waitingEntry;

    private void WaitForEntryLayout(Binding target)
    {
        // Low dispatcher priority is not a guarantee that the target's native
        // template/layout is ready. Retry from native readiness events only;
        // never force layout or spin another dispatcher callback on failure.
        if (ReferenceEquals(waitingEntry, target)) return;
        ClearEntryLayoutWait();
        waitingEntry = target;
        target.Element.Loaded += EntryLoaded;
        target.Element.LayoutUpdated += EntryLayoutUpdated;
    }

    private void EntryLoaded(object sender, RoutedEventArgs args) => EntryLayoutUpdated(sender, args);
    private void EntryLayoutUpdated(object? sender, object args)
    {
        if (waitingEntry is not { } target) return;
        if (disposed || !needsEntry || !bindings.TryGetValue(target.Identity.Id, out var current) ||
            !ReferenceEquals(target, current) || !Eligible(target))
        { ClearEntryLayoutWait(); return; }
        if (!target.Element.IsLoaded || target.Element.ActualWidth <= 0 || target.Element.ActualHeight <= 0) return;
        ClearEntryLayoutWait();
        QueueEntryFocus();
    }

    private void ClearEntryLayoutWait()
    {
        if (waitingEntry is not { } target) return;
        waitingEntry = null;
        target.Element.Loaded -= EntryLoaded;
        target.Element.LayoutUpdated -= EntryLayoutUpdated;
    }
}
