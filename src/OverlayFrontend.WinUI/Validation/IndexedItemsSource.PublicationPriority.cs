using Microsoft.UI.Dispatching;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

internal sealed partial class IndexedItemsSource<T> where T : notnull
{
    private static readonly bool lowPriorityBaseline = Environment.GetCommandLineArgs()
        .Contains("--playnite-low-priority-baseline", StringComparer.Ordinal);

    static partial void ConfigureValidationPublicationPriority(ref DispatcherQueuePriority priority)
    {
        if (lowPriorityBaseline) priority = DispatcherQueuePriority.Low;
    }
}
