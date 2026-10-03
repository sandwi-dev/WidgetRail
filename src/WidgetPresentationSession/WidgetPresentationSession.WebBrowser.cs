using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    /// <summary>Validates an actual displayed browser node without granting widget access to its page.</summary>
    public WebBrowserDocument ResolveWebBrowserDocument(WidgetPresentationFrame displayed, string elementId,
        WidgetPinnedSelection? selection = null, WidgetPinnedProjection? projection = null)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        using (_gate.Enter())
        {
            var current = DemandPinnedFrameLocked(displayed);
            var originView = displayed.Snapshot;
            var currentView = current.Snapshot;
            if (selection is not null || projection is not null)
            {
                if (selection is null || projection is null || !ReferenceEquals(projection.Frame, displayed))
                    throw BrowserStale();
                currentView = DemandPinnedInputLocked(selection, projection);
                originView = projection.Snapshot;
            }
            var original = FindBrowser(originView.Root, elementId)?.WebBrowser;
            var latest = FindBrowser(currentView.Root, elementId)?.WebBrowser;
            if (displayed.Authority.WorkerRun is null || original is null || original != latest || !original.IsWellFormed())
                throw BrowserStale();
            return original;
        }
    }

    /// <summary>Document lifetime is independent of navigation revision or native placement.</summary>
    public bool IsWebBrowserSessionCurrent(WidgetPresentationAuthority authority, string documentId)
    {
        using (_gate.Enter())
        {
            if (_disposed || _terminalFailure is not null || _states.GetValueOrDefault(authority.WidgetId) is not { Failure: null, LastGood: { } frame } ||
                !SameIndexedOwner(authority, frame.Authority) || authority.WorkerRun is null || authority.WorkerRun != frame.Authority.WorkerRun) return false;
            return HasDocument(frame.Snapshot.Root) || frame.Snapshot.PinnedLayouts.Any(layout => layout.Root is { } root && HasDocument(root));
        }
        bool HasDocument(ViewNode node) => node.WebBrowser?.Id == documentId || node.Children.Any(HasDocument);
    }
    private static ViewNode? FindBrowser(ViewNode node, string id) => node.Id == id && node.Kind == ViewNodeKind.WebBrowser ? node :
        node.Children.Select(child => FindBrowser(child, id)).FirstOrDefault(child => child is not null);
    private static WidgetPresentationSessionException BrowserStale() => new("browser_stale", "The displayed browser document retired or changed.");
}
