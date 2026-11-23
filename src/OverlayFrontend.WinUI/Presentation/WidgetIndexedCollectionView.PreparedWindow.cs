using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private sealed class PendingWindow(WidgetIndexedRows owner, WidgetPresentationBinding binding, ViewNode node, string scope)
    {
        internal readonly WidgetIndexedRows Owner = owner;
        internal WidgetPresentationBinding Binding = binding;
        internal ViewNode Node = node;
        internal string Scope = scope;
    }
    private PendingWindow? pendingWindow;
    private CancellationTokenSource? windowPreparationCancellation;
    private bool windowPreparationRunning;
    private Task? windowPreparationTask;
    private bool applyingPreparedWindow;
    private PreparedIndexedWindow<WidgetIndexedRow>? preparedWindow;
    internal Action<WidgetIndexedCollectionView>? DeferredQueryCommitted { get; set; }

    private bool TryPrepareWindow(WidgetPresentationBinding binding, ViewNode node, string scope)
    {
        if (applyingPreparedWindow) return false;
        var eligible = !disposed && presentationActive && inputActive && binding.Scope == scope &&
            source is not null && view is not null && layoutKind == node.CollectionLayout?.Kind && axis == node.ScrollAxis &&
            node.IsDisabled != true && node.IsBusy != true &&
            node.IndexedCollection is { ObservesBoundaries: true } && source.CanRebaseWindow(binding,node) &&
            pendingIndex is null && !IsEntryPending &&
            (binding.View.FocusGroupEntryRequest is null || binding.View.FocusGroupEntryRequest.RequestId == source.Presentation.View.FocusGroupEntryRequest?.RequestId) &&
            (binding.View.ScrollRevealRequest is null || binding.View.ScrollRevealRequest.RequestId == source.Presentation.View.ScrollRevealRequest?.RequestId);
        // Explicit focus/reveal requests keep the established synchronous query
        // handoff. Preparation is only an incremental paging transaction; it
        // must not make those existing intents target the preceding query.
        // Clearing a consumed entry/reveal is not a new navigation intent. A
        // cached adjacent read can coalesce that clearing with its first prepend;
        // it still needs prepared rows, not the synchronous empty-range fallback.
        if (!eligible) { CancelPreparedWindow(); return false; }
        if (pendingWindow is { } pending && ReferenceEquals(pending.Owner,source) &&
            pending.Binding.SameInput(binding) && pending.Node.IndexedCollection == node.IndexedCollection)
        {
            pending.Binding=binding;pending.Node=node;pending.Scope=scope;
            StartWindowPreparation();
            return true;
        }
        pendingWindow = new(source!,binding,node,scope);
        windowPreparationCancellation?.Cancel();
        StartWindowPreparation();
        return true;
    }

    private void StartWindowPreparation()
    {
        if(windowPreparationRunning||pendingWindow is null||disposed)return;
        windowPreparationRunning=true;
        windowPreparationTask=PrepareWindowsAsync();
        TrackRetirement(windowPreparationTask);
    }

    private void CancelPreparedWindow()
    {
        pendingWindow=null;
        windowPreparationCancellation?.Cancel();
    }

    private async Task PrepareWindowsAsync()
    {
        // WinUI's dispatcher synchronization context owns the pending successor.
        // Cancellation drains the old range before the newest successor starts.
        try
        {
            while (pendingWindow is { } pending && !disposed)
            {
                using var cancellation = new CancellationTokenSource();
                windowPreparationCancellation=cancellation;
                PreparedIndexedWindow<WidgetIndexedRow>? ready=null;
                var committing=false;
                try
                {
                    ready=await pending.Owner.PrepareWindowAsync(pending.Binding,pending.Node,cancellation.Token);
                    if (!Current())
                    {
                        if(ReferenceEquals(pendingWindow,pending))pendingWindow=null;
                        continue;
                    }
                    pendingWindow=null;
                    committing=true;
                    applyingPreparedWindow=true;
                    preparedWindow=ready;
                    // Capture continuity now, after any input during preparation;
                    // never restore an anchor saved when the request began.
                    Apply(pending.Binding,pending.Node,pending.Scope);
                    if(source?.Declaration.IndexedCollection==pending.Node.IndexedCollection)
                        DeferredQueryCommitted?.Invoke(this);
                    PresentationChanged?.Invoke();
                }
                catch (OperationCanceledException)
                {
                    // New query/surface authority is delivered through Apply.
                    // Its pending successor, if any, is handled by this pump.
                    if (ReferenceEquals(pendingWindow,pending)) pendingWindow=null;
                }
                catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
                {
                    if (committing) failed(error);
                    else if (Current())
                    {
                        pendingWindow=null;
                        applyingPreparedWindow=true;
                        preparedWindow=null;
                        // Preserve the ordinary range failure/retry behavior.
                        // A preparation failure must not strand an old window.
                        failed(error);
                        Apply(pending.Binding,pending.Node,pending.Scope);
                        DeferredQueryCommitted?.Invoke(this);
                    }
                    else if(ReferenceEquals(pendingWindow,pending))pendingWindow=null;
                }
                finally
                {
                    applyingPreparedWindow=false;preparedWindow=null;
                    if (ReferenceEquals(windowPreparationCancellation,cancellation)) windowPreparationCancellation=null;
                    if (ready is not null) await ready.DisposeAsync();
                }
                bool Current() => !disposed && !cancellation.IsCancellationRequested && presentationActive && inputActive &&
                    ReferenceEquals(pendingWindow,pending) && ReferenceEquals(source,pending.Owner) &&
                    pending.Binding.IsCurrent && source.CanRebaseWindow(pending.Binding,pending.Node) &&
                    source.IsPreparedQueryCurrent(pending.Binding,pending.Node);
            }
        }
        finally { windowPreparationRunning=false;StartWindowPreparation(); }
    }
}
