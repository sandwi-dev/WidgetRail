using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    /// <summary>The presenter borrows media slots; the shell owns document/controller lifetime.</summary>
    internal EmbeddedMediaOwner? MediaOwner { get; set; }
    private bool mediaRefreshQueued;

    private void QueueMediaRefresh()
    {
        if (!presentationActive || MediaOwner is null || disposed || mediaRefreshQueued) return;
        mediaRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            { mediaRefreshQueued = false; if (!disposed) MediaOwner?.Refresh(); })) mediaRefreshQueued = false;
    }

    private void UpdateMediaViewport(WidgetMediaViewport viewport, ViewNode node, string scope)
    {
        var declaration = effectiveView?.EmbeddedMediaSession;
        if (declaration?.Id != node.MediaSessionId) declaration = null;
        viewport.Configure(declaration, !presentationOnly && scope == activeScope);
        // Apply is synchronous on this dispatcher. Its temporary preparation flag
        // is not a revocation of the durable browser's already-admitted command.
        viewport.CanAcceptInput = () => presentationActive && !disposed && !presentationOnly && !HasTransientControl;
        if (presentationActive && !presentationOnly && frame is { } current && declaration is not null)
            MediaOwner?.Bind(current.Authority, node.Id, declaration.Id, viewport);
        else MediaOwner?.Unbind(viewport);
    }

    private void RetireMediaViewport(Binding binding)
    {
        if (binding.Element is not WidgetMediaViewport viewport) return;
        MediaOwner?.Unbind(viewport);
        viewport.Retire();
    }
}
