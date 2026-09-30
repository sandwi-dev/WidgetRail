using WidgetRail.WidgetProtocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal event Action? ControllerGuideChanged;
    internal object ControllerGuideContext => (activeScope, HasTransientControl, adjustingSlider?.Identity.Id);
    internal bool IsGuidePresentationReady(bool requireFocus)
    {
        if (disposed || applying || !presentationActive || frame is null || !IsLoaded) return false;
        if (HasTransientControl) return true;
        if (requireFocus && (restoreNativeFocus || pendingRestore is not null || pendingGroupEntry is not null ||
            needsEntry && bindings.Values.Any(Navigable))) return false;
        foreach (var binding in bindings.Values)
        {
            if (binding.Identity.Scope != activeScope || !IsMemoryVisible(binding.Element) ||
                binding.Element is not WidgetIndexedCollectionView collection) continue;
            if (!collection.IsLoaded) return false;
            var clip = new Rect(0, 0, ActualWidth, ActualHeight);
            for (var parent = VisualTreeHelper.GetParent(collection); parent is not null && !ReferenceEquals(parent, this);
                parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer scroll)
                    clip = WidgetIndexedCollectionView.GuideIntersection(clip, scroll.TransformToVisual(this)
                        .TransformBounds(new Rect(0, 0, scroll.ViewportWidth, scroll.ViewportHeight)));
            var bounds = collection.TransformToVisual(this).TransformBounds(new Rect(0, 0, collection.ActualWidth, collection.ActualHeight));
            if (WidgetIndexedCollectionView.GuideIntersection(clip, bounds) is { Width: 0 } or { Height: 0 }) continue;
            if (!collection.HasCurrentGuideRows(this, clip) || requireFocus && collection.IsEntryPending) return false;
        }
        return true;
    }
    private void NotifyControllerGuideChanged()
    { RefreshContextIndicators(); ControllerGuideChanged?.Invoke(); }

    internal IReadOnlyList<ControllerGuideHint> CaptureControllerGuide()
    {
        if (disposed || applying || presentationOnly || !presentationActive || presentation is null) return [];
        if (HasTransientControl) return (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A)];
        var focused = FocusedBinding();
        var path = ControllerFocusPath();
        if (focused?.Element is WidgetIndexedCollectionView && path.Count == 0) return [];
        var leaf = focused is null || path.Count == 0 ? null : path[^1];
        var activation = leaf is { IsDisabled: not true, IsBusy: not true } &&
            (leaf.Kind is ViewNodeKind.Button or ViewNodeKind.ActionSurface or ViewNodeKind.Slider && leaf.ActionId is not null ||
             leaf.Kind == ViewNodeKind.Select && leaf.SelectOptions.Any(option => !option.IsDisabled && !option.IsBusy) ||
             leaf.Kind == ViewNodeKind.TextEntry && leaf.ActionId is not null ||
             leaf.Kind == ViewNodeKind.Slider && leaf.SliderInteractionMode == SliderInteractionMode.ActivateToAdjust);
        var context = new HashSet<ControllerButton>();
        var unavailableContext = new HashSet<ControllerButton>();
        foreach (var button in new[] { ControllerButton.Menu, ControllerButton.X, ControllerButton.Y })
            if (FindContextTarget(button) is { } target)
            {
                if (HasAvailableActions(target.Node)) context.Add(button);
                else unavailableContext.Add(button);
            }
        var hints = ControllerGuideModel.Resolve(path, context, activation, unavailableContext);
        if (leaf is { Kind: ViewNodeKind.Slider, SliderInteractionMode: SliderInteractionMode.ActivateToAdjust } && focused is not null)
        {
            var adjusting = ReferenceEquals(adjustingSlider, focused);
            return (ControllerGuideHint[])[.. hints.Where(hint => hint.Button != ControllerButton.A),
                .. (adjusting ? (ControllerGuideHint[])[new(ControllerPrompt.DPadHorizontal, "Adjust")] : []),
                .. (adjusting || activation ? (ControllerGuideHint[])[new(ControllerPrompt.A, adjusting ? "Done" : "Adjust", ControllerButton.A)] : [])];
        }
        return hints;
    }
}
