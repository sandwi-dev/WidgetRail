using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    // UI-thread owned: two previous views per presenter, three across the host.
    // Page memory survives eviction. This cache owns native paint, never old input authority.
    private static readonly LinkedList<CachedPage> cachedPages = new();
    private const int MaximumCachedPages = 3;
    private const int MaximumCachedPagesPerWidget = 2;
    private const int MaximumCachedBindings = 512;
    private sealed class CachedPage(WidgetViewPresenter owner, WidgetTransition transition,
        Dictionary<string, Declaration> plan, Dictionary<string, Binding> content, Binding root)
    {
        internal WidgetViewPresenter Owner { get; } = owner;
        internal WidgetTransition Transition { get; } = transition;
        internal Dictionary<string, Declaration> Plan { get; } = plan;
        internal Dictionary<string, Binding> Bindings { get; } = content;
        internal Binding Root { get; } = root;
        internal double RootOpacity { get; } = root.LayoutElement.Opacity;
        internal Dictionary<Binding, bool> HitTesting { get; } = content.Values.ToDictionary(binding => binding, binding => binding.Element.IsHitTestVisible);
        internal Task Suspension { get; set; } = Task.CompletedTask;
    }
    private readonly List<CachedPage> retainedPages = [];
    internal int CachedPageCount => retainedPages.Count;
    internal long PageCacheHits { get; private set; }
    internal string? PageCacheStatus { get; private set; }

    private CachedPage? PreparePageCache(Dictionary<string, Declaration> plan, bool sameOwner)
    {
        if (!sameOwner) { ClearPageCache(); return null; }
        if (presentationOnly || fragmentRootId is not null || presentation?.Selection is not null) return null;
        var incoming = plan.Values.FirstOrDefault(value => value.Node.Transition is { Kind: WidgetTransitionKind.Page or WidgetTransitionKind.Content } next &&
            TransitionVisible(value, plan) && declarations.Values.Any(old => old.Node.Transition is { } prior &&
                prior.Kind == next.Kind && prior.GroupId == next.GroupId && prior.Key != next.Key && TransitionVisible(old, declarations)));
        if (incoming?.Node.Transition is not { } next) return null;
        var outgoing = declarations.Values.First(value => value.Node.Transition is { } transition && transition.Kind == next.Kind &&
            transition.GroupId == next.GroupId && transition.Key != next.Key && TransitionVisible(value, declarations));
        var prior = outgoing.Node.Transition!;
        SettleTransitions();
        ClearFocusPreview(); DismissTransientControl();

        CachedPage? restored = null;
        var candidate = retainedPages.FirstOrDefault(page => page.Transition.GroupId == next.GroupId && page.Transition.Kind == next.Kind && page.Transition.Key == next.Key);
        if (candidate is not null &&
            candidate.Root.Identity.Id == incoming.Node.Id && candidate.Suspension.IsCompletedSuccessfully && CompatibleCachedPage(candidate, incoming, plan))
        {
            restored = candidate;
            retainedPages.Remove(candidate); cachedPages.Remove(candidate);
            DetachCachedRoot(candidate, restoring: true);
            foreach (var (binding, enabled) in candidate.HitTesting) binding.Element.IsHitTestVisible = enabled;
            ++PageCacheHits;
            PageCacheStatus = "hit:" + next.Key;
        }
        else if (candidate is not null)
        {
            var subtree = Subtree(incoming, plan);
            var mismatch = subtree.FirstOrDefault(pair => !candidate.Plan.TryGetValue(pair.Key, out var old) || old.Identity != pair.Value.Identity ||
                old.ParentId != pair.Value.ParentId || !old.Node.Children.Select(child => child.Id).SequenceEqual(pair.Value.Node.Children.Select(child => child.Id)));
            PageCacheStatus = $"miss:{next.Key} ready={candidate.Suspension.IsCompletedSuccessfully} old={candidate.Bindings.Count} new={subtree.Count} mismatch={mismatch.Key}";
            EvictCachedPage(candidate);
        }
        else PageCacheStatus = "cold:" + next.Key;

        var outgoingPlan = Subtree(outgoing, declarations);
        if (CacheablePage(outgoingPlan) && !declarations.Values.Any(value => value.Node.Kind == ViewNodeKind.ModalLayer) && outgoingPlan.Count <= MaximumCachedBindings &&
            bindings[outgoing.Node.Id].LayoutElement is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 })
        {
            var content = outgoingPlan.Keys.ToDictionary(id => id, id => bindings[id], StringComparer.Ordinal);
            var page = new CachedPage(this, prior, declarations, content, bindings[outgoing.Node.Id]);
            // A top-level page can stay under the same native parent throughout
            // its lifetime. Reparenting a GridView preserves the control object
            // but lets WinUI recycle its row templates, defeating retained paint.
            motionStage?.RetainRoot(page.Root.LayoutElement);
            Input.PointerCaptureLifetime.ReleaseWithin(page.Root.LayoutElement);
            foreach (var duplicate in retainedPages.Where(value => value.Transition.GroupId == prior.GroupId && value.Transition.Kind == prior.Kind && value.Transition.Key == prior.Key).ToArray()) EvictCachedPage(duplicate);
            retainedPages.Add(page); cachedPages.AddLast(page);
            var drains = new List<Task>();
            foreach (var binding in page.Bindings.Values)
            {
                // Retain paint, not an unsent input intent from the hidden view.
                RetireSlider(binding);
                binding.Element.IsHitTestVisible = false;
                if (binding.Element is Control control) control.IsTabStop = false;
                if (binding.Element is WidgetIndexedCollectionView collection) drains.Add(collection.SetPresentationActiveAsync(false));
                if (binding.Element is WidgetPresentationSurface { Fragment: { } fragment }) drains.Add(fragment.SetPresentationActiveAsync(false));
                if (artworkReveals.TryGetValue(binding, out var reveal)) reveal.Cancel();
                if (imageDemands.TryGetValue(binding, out var demand) && !demand.Completed)
                { imageDemands.Remove(binding); demand.Lifetime.Cancel(); demand.Lifetime.Dispose(); }
            }
            page.Suspension = Task.WhenAll(drains);
            TrackRetirement(page.Suspension);
            while (retainedPages.Count > MaximumCachedPagesPerWidget) EvictCachedPage(retainedPages[0]);
            while (cachedPages.Count > MaximumCachedPages)
            { var oldest = cachedPages.First!.Value; oldest.Owner.EvictCachedPage(oldest); }
        }
        return restored;
    }

    private static Dictionary<string, Declaration> Subtree(Declaration root, Dictionary<string, Declaration> plan)
    {
        var result = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        Visit(root.Node);
        return result;
        void Visit(ViewNode node) { result.Add(node.Id, plan[node.Id]); foreach (var child in node.Children) Visit(child); }
    }

    private static bool CacheablePage(Dictionary<string, Declaration> plan) =>
        !plan.Values.Any(value => value.Node.Kind is ViewNodeKind.WebBrowser or ViewNodeKind.WindowPreview or
            ViewNodeKind.MediaViewport or ViewNodeKind.MediaPlayer or ViewNodeKind.CapturedMedia or
            ViewNodeKind.ModalLayer);

    private bool CompatibleCachedPage(CachedPage page, Declaration incoming, Dictionary<string, Declaration> plan)
    {
        var subtree = Subtree(incoming, plan);
        return page.Bindings.Count == subtree.Count && subtree.All(pair => page.Bindings.ContainsKey(pair.Key) && page.Plan.TryGetValue(pair.Key, out var old) &&
            old.ParentId == pair.Value.ParentId && old.Identity == pair.Value.Identity &&
            old.Node.Children.Select(child => child.Id).SequenceEqual(pair.Value.Node.Children.Select(child => child.Id)) &&
            (old.Node.Disclosure is null) == (pair.Value.Node.Disclosure is null) &&
            (old.Node.FocusPreview?.EnterButton is null) == (pair.Value.Node.FocusPreview?.EnterButton is null) &&
            SameGridLayoutMode(old.Node, pair.Value.Node) &&
            IsInlineLink(old, page.Plan) == IsInlineLink(pair.Value, plan) &&
            old.Node.Transition?.Kind == pair.Value.Node.Transition?.Kind &&
            old.Node.ActionSurfacePresentation == pair.Value.Node.ActionSurfacePresentation &&
            (old.Node.ActionId is null) == (pair.Value.Node.ActionId is null) &&
            SurfaceOwners(old, page.Plan).SequenceEqual(SurfaceOwners(pair.Value, plan)));
    }

    private static IEnumerable<WidgetElementIdentity> SurfaceOwners(Declaration declaration, Dictionary<string, Declaration> plan)
    {
        for (var parent = declaration.ParentId; parent is not null; parent = plan[parent].ParentId)
            if (plan[parent].Node.Kind is ViewNodeKind.BackgroundSurface or ViewNodeKind.FocusPresentationSurface or ViewNodeKind.ModalLayer)
                yield return plan[parent].Identity;
    }

    private bool IsCachedBinding(Binding binding) => retainedPages.Any(page => ReferenceEquals(page.Bindings.GetValueOrDefault(binding.Identity.Id), binding));

    private void ParkCachedPage()
    {
        // Teardown settles animations too, but must never remount their visual
        // trees into a presenter whose native lifetime is already retiring.
        if (disposed || motionStage is null) return;
        foreach (var page in retainedPages)
        {
            if (activeTransition?.Retained.Contains(page.Root) == true) continue;
            var root = page.Root.LayoutElement;
            if (motionStage.IsRetainedRoot(root)) { root.Opacity = 0; continue; }
            if (ReferenceEquals(VisualTreeHelper.GetParent(root), motionStage.Cached)) continue;
            // Keep the virtualized native tree mounted: unloading would discard its
            // row presenters and decoded artwork. Only the current page is measured.
            root.Width = root.ActualWidth; root.Height = root.ActualHeight;
            DetachCachedRoot(page);
            motionStage.Cached.Children.Add(root);
        }
    }

    private void DetachCachedRoot(CachedPage page, bool restoring = false)
    {
        var root = page.Root.LayoutElement;
        if (motionStage?.IsRetainedRoot(root) == true)
        {
            motionStage.ReleaseRoot(root, remove: !restoring);
            if (restoring) root.Opacity = page.RootOpacity;
            return;
        }
        if (ReferenceEquals(motionStage?.Current, root)) motionStage.SetCurrent(null);
        else if (VisualTreeHelper.GetParent(root) is Panel parent) parent.Children.Remove(root);
    }

    private void ResumeCachedPage(CachedPage? page)
    {
        if (page is null || !presentationActive) return;
        foreach (var binding in page.Bindings.Values)
            if (binding.Element is WidgetIndexedCollectionView collection)
                TrackRetirement(collection.SetPresentationActiveAsync(true));
    }

    private void ClearPageCache()
    {
        foreach (var page in retainedPages.ToArray()) EvictCachedPage(page);
    }

    private void EvictCachedPage(CachedPage page)
    {
        retainedPages.Remove(page); cachedPages.Remove(page);
        DetachCachedRoot(page);
        foreach (var binding in page.Bindings.Values)
            if (activeTransition?.Retained.Contains(binding) != true && !bindings.Values.Contains(binding)) Retire(binding);
    }
}
