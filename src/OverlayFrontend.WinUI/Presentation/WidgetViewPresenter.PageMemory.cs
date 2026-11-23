using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record PageMemory(string? FocusId, Dictionary<string, (double X, double Y)> Scroll,
        IReadOnlyList<ScrollViewportMemento> Anchors, IReadOnlyList<IndexedViewportMemento> Indexed);
    private readonly OrderedDictionary<(WidgetTransitionKind Kind, string Group, string Key), PageMemory> pageMemory = [];
    private readonly List<PageMemory> pendingPageMemory = [];
    private long pageMemoryVersion;

    private void PreparePageMemory(Dictionary<string, Declaration> plan, bool sameOwner)
    {
        if (!sameOwner) { pageMemory.Clear(); pendingPageMemory.Clear(); ++pageMemoryVersion; return; }
        pendingPageMemory.Clear();
        foreach (var next in plan.Values.Where(d => d.Node.Transition?.Kind is WidgetTransitionKind.Page or WidgetTransitionKind.Content && TransitionVisible(d,plan)))
        {
            var navigation = next.Node.Transition!;
            var prior = declarations.Values.FirstOrDefault(d => d.Node.Transition is { } t && t.Kind == navigation.Kind && t.GroupId == navigation.GroupId && TransitionVisible(d,declarations));
            if (prior?.Node.Transition is not { } previous || previous.Key == navigation.Key) continue;
            ++pageMemoryVersion;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Collect(prior.Node);
            var focused = FocusedBinding()?.Identity.Id;
            var offsets = bindings.Where(pair => ids.Contains(pair.Key) && pair.Value.Element is ScrollViewer)
                .Take(64).ToDictionary(pair => pair.Key, pair =>
                { var scroll = (ScrollViewer)pair.Value.Element; return (scroll.HorizontalOffset, scroll.VerticalOffset); });
            var key = (previous.Kind, previous.GroupId, previous.Key);
            pageMemory.Remove(key);
            var anchors = bindings.Values.Where(binding => ids.Contains(binding.Identity.Id) && binding.Element is ScrollViewer)
                .Select(CaptureScrollViewport).OfType<ScrollViewportMemento>().Take(64).ToArray();
            IReadOnlyList<IndexedViewportMemento> indexed = navigation.Kind == WidgetTransitionKind.Content
                ? bindings.Values.Where(binding => ids.Contains(binding.Identity.Id) && binding.Element is WidgetIndexedCollectionView && IsMemoryVisible(binding.Element))
                .Select(binding => ((WidgetIndexedCollectionView)binding.Element).CaptureViewport(binding.Identity))
                .OfType<IndexedViewportMemento>().Take(64).ToArray() : [];
            pageMemory.Add(key, new(focused is not null && ids.Contains(focused) ? focused : null, offsets, anchors, indexed));
            while (pageMemory.Count > 32) pageMemory.RemoveAt(0);
            if ((navigation.Kind == WidgetTransitionKind.Content || navigation.Navigation == WidgetNavigationOperation.Back) &&
                pageMemory.TryGetValue((navigation.Kind, navigation.GroupId, navigation.Key), out var memory))
                // Switching sections preserves reading position without stealing
                // focus from the persistent navigation control. Page Back keeps
                // its existing remembered-focus policy.
                pendingPageMemory.Add(navigation.Kind == WidgetTransitionKind.Content ? memory with { FocusId = null } : memory);

            void Collect(ViewNode node) { ids.Add(node.Id); foreach (var child in node.Children) Collect(child); }
        }
    }

    private void RestorePageMemory()
    {
        if (pendingPageMemory.Count == 0) return;
        var memories = pendingPageMemory.ToArray(); pendingPageMemory.Clear();
        foreach (var memory in memories)
            if (memory.FocusId is { } id && bindings.TryGetValue(id, out var target) && Navigable(target))
            { remembered[target.Identity.Scope] = target.Identity; needsEntry = true; }
        var version = pageMemoryVersion;
        var intent = entryIntentVersion;
        // Reserve indexed restoration before yielding to native layout. A new
        // list's temporary top offset must not request an older provider page.
        foreach (var viewport in memories.SelectMany(memory => memory.Indexed))
            if (Matches(viewport.Element) && !pendingIndexedViewports.Any(pending => pending.Memory.Element == viewport.Element))
                pendingIndexedViewports.Add(new(viewport));
        if (HasPendingMemoryRestore && frame is not null)
        {
            memoryIntent = intent;
            memoryOwner = PresentationMemoryOwner.From(frame.Authority);
        }
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (disposed || !presentationActive || version != pageMemoryVersion || intent != entryIntentVersion) return;
            UpdateLayout();
            var anchored = new HashSet<string>(StringComparer.Ordinal);
            foreach (var anchor in memories.SelectMany(memory => memory.Anchors))
                if (Matches(anchor.Element) && Matches(anchor.Anchor))
                {
                    anchored.Add(anchor.Element.Id);
                    if (!pendingScrollViewports.Any(pending => pending.Memory.Element == anchor.Element)) pendingScrollViewports.Add(new(anchor));
                }
            if (HasPendingMemoryRestore && frame is not null)
            {
                memoryIntent = entryIntentVersion;
                memoryOwner = PresentationMemoryOwner.From(frame.Authority);
                LayoutUpdated -= MemoryLayoutUpdated; LayoutUpdated += MemoryLayoutUpdated;
                QueueMemoryRestoration();
            }
            foreach (var memory in memories)
                foreach (var (id, offset) in memory.Scroll)
                    if (bindings.TryGetValue(id, out var binding) && binding.Element is ScrollViewer scroll)
                        if (!anchored.Contains(id)) scroll.ChangeView(offset.X, offset.Y, null, disableAnimation: true);
        });
    }
}
