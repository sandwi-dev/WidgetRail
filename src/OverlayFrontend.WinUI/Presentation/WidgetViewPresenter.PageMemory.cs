using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record PageMemory(string? FocusId, Dictionary<string, (double X, double Y)> Scroll);
    private readonly OrderedDictionary<(string Group, string Key), PageMemory> pageMemory = [];
    private readonly List<PageMemory> pendingPageMemory = [];
    private long pageMemoryVersion;

    private void PreparePageMemory(Dictionary<string, Declaration> plan, bool sameOwner)
    {
        if (!sameOwner) { pageMemory.Clear(); pendingPageMemory.Clear(); ++pageMemoryVersion; return; }
        foreach (var next in plan.Values.Where(d => d.Node.Transition?.Kind == WidgetTransitionKind.Page))
        {
            var navigation = next.Node.Transition!;
            var prior = declarations.Values.FirstOrDefault(d => d.Node.Transition is { Kind: WidgetTransitionKind.Page } t && t.GroupId == navigation.GroupId);
            if (prior?.Node.Transition is not { } previous || previous.Key == navigation.Key) continue;
            ++pageMemoryVersion; pendingPageMemory.Clear();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Collect(prior.Node);
            var focused = FocusedBinding()?.Identity.Id;
            var offsets = bindings.Where(pair => ids.Contains(pair.Key) && pair.Value.Element is ScrollViewer)
                .Take(64).ToDictionary(pair => pair.Key, pair =>
                { var scroll = (ScrollViewer)pair.Value.Element; return (scroll.HorizontalOffset, scroll.VerticalOffset); });
            var key = (previous.GroupId, previous.Key);
            pageMemory.Remove(key);
            pageMemory.Add(key, new(focused is not null && ids.Contains(focused) ? focused : null, offsets));
            while (pageMemory.Count > 32) pageMemory.RemoveAt(0);
            if (navigation.Navigation == WidgetNavigationOperation.Back && pageMemory.TryGetValue((navigation.GroupId, navigation.Key), out var memory))
                pendingPageMemory.Add(memory);

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
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (disposed || !presentationActive || version != pageMemoryVersion) return;
            UpdateLayout();
            foreach (var memory in memories)
                foreach (var (id, offset) in memory.Scroll)
                    if (bindings.TryGetValue(id, out var binding) && binding.Element is ScrollViewer scroll)
                        scroll.ChangeView(offset.X, offset.Y, null, disableAnimation: true);
        });
    }
}
