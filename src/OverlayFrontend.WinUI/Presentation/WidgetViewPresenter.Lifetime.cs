using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Previews;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private bool presentationActive = true;
    private Task suspension = Task.CompletedTask;
    internal bool IsPresentationActive => presentationActive;

    /// <summary>
    /// Revoke presentation demand before Background; retain native layout and
    /// logical identity. Apply the foreground frame while inactive, then resume.
    /// The shell serializes these transitions and owns visibility/entry focus.
    /// </summary>
    internal Task SetPresentationActiveAsync(bool active)
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Change presentation lifetime on its WinUI dispatcher.");
        ObjectDisposedException.ThrowIf(disposed, this);
        if (presentationActive == active) return active ? Task.CompletedTask : suspension;
        if (active && !suspension.IsCompleted) throw new InvalidOperationException("Await presentation suspension before resuming.");
        if (active && !suspension.IsCompletedSuccessfully) throw new InvalidOperationException("Presentation suspension failed.", suspension.Exception);
        if (!active) RememberFocus();
        presentationActive = active;
        RefreshContextIndicators();
        IsHitTestVisible = active && presentationInputEnabled;
        // Preview controls configure against the renderer's admitted frame.
        // Publish it before resuming bindings, including first cold activation.
        if (active && !presentationOnly && frame is { } current) WindowPreviews?.Apply(current);
        var work = new List<Task>();
        if (!active)
        {
            directionalScroll = null;
            foreach (var reveal in artworkReveals.Values) reveal.Cancel();
            SettleWidgetResize();
            ClearEntryLayoutWait(); CancelGroupEntry(); DismissTransientControl();
            ResetPressedStyles(); SettleTransitions(); SettleModalExit();
            foreach (var (binding, demand) in imageDemands.ToArray())
                if (!demand.Completed)
                {
                    imageDemands.Remove(binding); demand.Lifetime.Cancel(); demand.Lifetime.Dispose();
                }
        }
        foreach (var binding in bindings.Values)
        {
            if (binding.Element is WidgetIndexedCollectionView collection) work.Add(collection.SetPresentationActiveAsync(active));
            if (!active && binding.Element is WidgetPresentationSurface { Fragment: { } fragment }) work.Add(fragment.SetPresentationActiveAsync(false));
            if (!active)
            {
                if (binding.Element is Media.WidgetMediaViewport viewport)
                {
                    MediaOwner?.Unbind(viewport);
                    viewport.ScopeActive = false;
                }
                if (binding.Element is WidgetWindowPreview preview) preview.Dispose();
            }
            else if (declarations.TryGetValue(binding.Identity.Id, out var declaration)) Update(binding, declaration.Node);
        }
        if (active)
        {
            QueueSurfaceUpdate(); QueueMediaRefresh();
        }
        else work.AddRange(retirements.ToArray());
        var completion = Task.WhenAll(work);
        if (!active) suspension = completion;
        NotifyControllerGuideChanged();
        return completion;
    }
}
