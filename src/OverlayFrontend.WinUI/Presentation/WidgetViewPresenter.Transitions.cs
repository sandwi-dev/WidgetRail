using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed class TransitionCommit
    {
        internal HashSet<string> ReplacedIds { get; } = new(StringComparer.Ordinal);
        internal HashSet<Binding> Retained { get; } = [];
        internal List<SectionExit> Sections { get; } = [];
        internal Dictionary<string, (Rect Bounds, bool Selection)> Layout { get; } = new(StringComparer.Ordinal);
        internal List<WidgetCompositionTarget> Targets { get; } = [];
        internal WidgetCompositionMotion? Motion;
        internal EventHandler<object>? LayoutReady;
    }
    private sealed record SectionExit(string IncomingId, int Direction, WidgetMotionHost Outgoing)
    { internal WidgetMotionHost? Owner { get; set; } }
    private readonly WidgetMotionGroups motionGroups = new();
    private WidgetMotionStage? motionStage;
    private TransitionCommit? activeTransition;
    internal int OutgoingTransitionCount => activeTransition?.Sections.Count ?? 0;
    internal Task<WidgetMotionOutcome>? TransitionPlayback { get; private set; }
    internal int LastTransitionTargetCount { get; private set; }
    private long transitionStarts;
    private WidgetMotionOutcome? lastTransitionOutcome;

    private TransitionCommit PrepareTransitions(Dictionary<string, Declaration> plan, bool sameOwner)
    {
        var visible = plan.Values.Where(declaration => declaration.Node.Transition is not null && TransitionVisible(declaration, plan)).ToArray();
        if (activeTransition is { } active && active.Sections.Any(section =>
            !plan.TryGetValue(section.IncomingId, out var next) || !TransitionVisible(next, plan) ||
            !bindings.TryGetValue(section.IncomingId, out var current) || current.Identity != next.Identity)) SettleTransitions();
        if (!sameOwner) { SettleTransitions(); motionGroups.Reset(); }
        var changes = motionGroups.Update(visible.Select(d => d.Node.Transition).OfType<WidgetTransition>());
        var commit = new TransitionCommit();
        // Establish the stable stage on first declaration, before native layout.
        // Reparenting an already loaded root during capture unloads its sources.
        if (motionStage is null && visible.Any(d => d.Node.Transition is not null))
        {
            var oldRoot = Content as FrameworkElement;
            Content = null;
            motionStage = new();
            motionStage.SetCurrent(oldRoot);
            Content = motionStage;
        }
        if (changes.Count == 0) return commit;
        SettleTransitions();
        var options = WidgetMotionOptions.From(appearance, systemAnimationsEnabled);
        if (!presentationActive || !sameOwner || !IsLoaded || options.Reduced || options.Section == WidgetRail.PlatformSettings.WidgetSectionAnimation.None) return commit;
        foreach (var change in changes)
        {
            foreach (var nextLayout in visible.Where(d => d.Node.Transition is { Kind: WidgetTransitionKind.Layout } t && t.GroupId == change.GroupId))
                if (bindings.TryGetValue(nextLayout.Node.Id, out var priorLayout) && Bounds(priorLayout.LayoutElement) is { } bounds)
                    commit.Layout[nextLayout.Node.Id] = (bounds, false);
            var nextSelected = visible.FirstOrDefault(d => d.Node.Transition is { Kind: WidgetTransitionKind.Selection } t &&
                t.GroupId == change.GroupId && d.Node.IsSelected is true);
            var priorSelected = declarations.Values.FirstOrDefault(d => d.Node.Transition is { Kind: WidgetTransitionKind.Selection } t &&
                t.GroupId == change.GroupId && d.Node.IsSelected is true && TransitionVisible(d, declarations));
            if (nextSelected is not null && priorSelected is not null && Bounds(bindings[priorSelected.Node.Id].LayoutElement) is { } selectionBounds)
                commit.Layout[nextSelected.Node.Id] = (selectionBounds, true);
            var incoming = visible.FirstOrDefault(d => d.Node.Transition is { Kind: WidgetTransitionKind.Content } t && t.GroupId == change.GroupId);
            var outgoing = declarations.Values.FirstOrDefault(d => d.Node.Transition is { Kind: WidgetTransitionKind.Content } t &&
                t.GroupId == change.GroupId && TransitionVisible(d, declarations));
            if (incoming is null || outgoing is null || bindings[outgoing.Node.Id].MotionHost is not { } host || Bounds(host) is not { } oldBounds) continue;
            // A separately owned live viewport cannot be duplicated with a
            // retained XAML subtree. Its owner keeps placement/input authority;
            // selection/header motion can still run independently.
            if (HasLiveSurface(incoming.Node) || HasLiveSurface(outgoing.Node)) continue;
            Collect(incoming.Node, id => commit.ReplacedIds.Add(id));
            Collect(outgoing.Node, id =>
            {
                var binding = bindings[id]; commit.Retained.Add(binding);
                binding.Element.IsHitTestVisible = false;
                if (binding.Element is Control control) control.IsTabStop = false;
                if (binding.Element is WidgetIndexedCollectionView collection) collection.SuspendForTransition();
            });
            if (VisualTreeHelper.GetParent(host) is Panel parent) parent.Children.Remove(host);
            else if (ReferenceEquals(motionStage!.Current, host)) motionStage.SetCurrent(null);
            host.Margin = new(); host.Width = oldBounds.Width; host.Height = oldBounds.Height;
            host.MinWidth = host.MinHeight = 0; host.MaxWidth = host.MaxHeight = double.PositiveInfinity;
            Canvas.SetLeft(host, 0); Canvas.SetTop(host, 0);
            commit.Sections.Add(new(incoming.Node.Id, change.Direction, host));
        }
        if (commit.Sections.Count > 0 || commit.Layout.Count > 0) activeTransition = commit;
        return commit;

        static void Collect(ViewNode node, Action<string> visit)
        { visit(node.Id); foreach (var child in node.Children) Collect(child, visit); }
        static bool HasLiveSurface(ViewNode node) => node.Kind is ViewNodeKind.MediaViewport or ViewNodeKind.WindowPreview || node.Children.Any(HasLiveSurface);
    }

    private bool TransitionVisible(Declaration declaration, IReadOnlyDictionary<string, Declaration> plan)
    {
        var viewport = Viewport();
        var compact = viewport.Width < 960 || viewport.Height < 540;
        for (Declaration? current = declaration; current is not null; current = current.ParentId is { } id ? plan[id] : null)
            if (current.Node.VisibleWhen == (compact ? ResponsiveVisibility.ExpandedOnly : ResponsiveVisibility.CompactOnly)) return false;
        return true;
    }
    private Rect? Bounds(FrameworkElement element)
    {
        if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
        return element.TransformToVisual(this).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
    }
    private static void ApplyMotionGeometry(Binding binding)
    {
        if (binding.MotionHost is not { } host) return;
        var element = binding.Element;
        host.Width = element.Width; host.Height = element.Height;
        host.MinWidth = element.MinWidth; host.MinHeight = element.MinHeight;
        host.MaxWidth = element.MaxWidth; host.MaxHeight = element.MaxHeight;
        host.Margin = element.Margin;
        element.Width = element.Height = double.NaN;
        element.MinWidth = element.MinHeight = 0;
        element.MaxWidth = element.MaxHeight = double.PositiveInfinity;
        element.Margin = new();
    }
    private void StartTransitions(TransitionCommit commit)
    {
        if (!ReferenceEquals(activeTransition, commit)) return;
        // Retained pixels stay within their incoming declaration's native
        // ancestors. Hoisting them to the widget root loses modal/scroll clips
        // and paints old dialog text over the scrim and unrelated controls.
        foreach (var section in commit.Sections)
        {
            var incoming = bindings[section.IncomingId].MotionHost!;
            section.Owner = incoming;
            incoming.EnsureOutgoing().Children.Add(section.Outgoing);
        }
        commit.LayoutReady = (_, _) =>
        {
            if (!ReferenceEquals(activeTransition, commit)) return;
            foreach (var section in commit.Sections)
                if (!bindings.TryGetValue(section.IncomingId, out var binding) || Bounds(binding.LayoutElement) is null)
                {
                    // This publication's native layout has completed. An empty
                    // target is valid and must not retain the old page waiting
                    // indefinitely for a later publication to give it a size.
                    SettleTransitions(); return;
                }
            LayoutUpdated -= commit.LayoutReady; commit.LayoutReady = null;
            try
            {
                var options = WidgetMotionOptions.From(appearance, systemAnimationsEnabled);
                var playbacks = new List<WidgetMotionPlayback>();
                foreach (var section in commit.Sections)
                {
                    var incoming = bindings[section.IncomingId].MotionHost!;
                    var size = new Vector2((float)incoming.ActualWidth, (float)incoming.ActualHeight);
                    var recipe = WidgetMotionPolicy.Section(options, size, section.Direction);
                    playbacks.Add(new(Target(incoming), recipe.Incoming));
                    playbacks.Add(new(Target(section.Outgoing), recipe.Outgoing));
                }
                foreach (var (id, priorLayout) in commit.Layout)
                    if (bindings.TryGetValue(id, out var binding) && binding.MotionHost is { } host && Bounds(host) is { } current)
                    {
                        var prior = priorLayout.Bounds;
                        var recipe = WidgetMotionPolicy.Layout(options, new((float)(prior.X - current.X), (float)(prior.Y - current.Y)),
                            new((float)prior.Width, (float)prior.Height), new((float)current.Width, (float)current.Height), priorLayout.Selection);
                        playbacks.Add(new(Target(host, false, priorLayout.Selection), recipe));
                    }
                if (playbacks.Count == 0) { SettleTransitions(); return; }
                LastTransitionTargetCount = playbacks.Count;
                ++transitionStarts;
                commit.Motion = new(CompositionTarget.GetCompositorForCurrentThread(), DispatcherQueue);
                TransitionPlayback = commit.Motion.PlayAsync(playbacks);
                _ = CompleteAsync();
            }
            catch (Exception error) { SettleTransitions(); ReportFailure(error); }

            WidgetCompositionTarget Target(WidgetMotionHost host, bool clipped = true, bool selection = false)
            {
                var size = new Vector2((float)host.ActualWidth, (float)host.ActualHeight);
                var target = clipped ? WidgetCompositionTarget.ForElement(host.Layer, host.Viewport, size)
                    : WidgetCompositionTarget.ForClippedDialog(selection ? host.SurfaceLayer : host.Layer, size);
                commit.Targets.Add(target); return target;
            }
            async Task CompleteAsync()
            {
                lastTransitionOutcome = await TransitionPlayback!;
                if (ReferenceEquals(activeTransition, commit)) SettleTransitions();
            }
        };
        LayoutUpdated += commit.LayoutReady;
        InvalidateArrange();
    }
    private void SettleTransitions()
    {
        if (activeTransition is not { } commit) return;
        activeTransition = null;
        if (commit.LayoutReady is not null) LayoutUpdated -= commit.LayoutReady;
        commit.Motion?.Dispose();
        foreach (var target in commit.Targets) target.Dispose();
        foreach (var section in commit.Sections) section.Owner?.ClearOutgoing();
        foreach (var binding in commit.Retained) Retire(binding);
    }
}
