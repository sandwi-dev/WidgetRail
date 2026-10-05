using WidgetRail.WidgetProtocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal event Action? ControllerGuideChanged;
    internal object ControllerGuideContext => (activeScope, HasTransientControl, adjustingSlider?.Identity.Id,
        FocusedBrowser?.IsInteracting, FocusedBrowser?.IsBrowsing, FocusedBrowser?.Surface?.HasDialog, FocusedBrowser?.PresentedInPin, FocusedBrowser?.Surface?.RequiresActivation, FocusedBrowser?.Surface?.IsProviderContent);
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
        if (ControllerTextEntry is { } keyboard)
            return (ControllerGuideHint[])[new(ControllerPrompt.LeftStickMove, "Navigate"),
                new(ControllerPrompt.A, "Type (hold to repeat)", ControllerButton.A),
                new(ControllerPrompt.X, "Delete (hold to repeat)", ControllerButton.X),
                new(ControllerPrompt.LeftBumper, "Caret left", ControllerButton.LeftBumper),
                new(ControllerPrompt.RightBumper, "Caret right", ControllerButton.RightBumper),
                new(ControllerPrompt.LeftTrigger, "Shift", ControllerButton.LeftTrigger),
                new(ControllerPrompt.Y, "Clear", ControllerButton.Y),
                new(ControllerPrompt.RightTrigger, "Done", ControllerButton.RightTrigger),
                new(ControllerPrompt.B, "Cancel", ControllerButton.B),
                .. (keyboard.IsSensitive ? (ControllerGuideHint[])[new(ControllerPrompt.RightStickPress,
                    keyboard.PasswordVisible ? "Hide password" : "Show password", ControllerButton.RightStick)] : [])];
        if (HasTransientControl) return (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A)];
        if (FocusedMediaPlayer is { HasDialog: true, CanControl: false })
            return (ControllerGuideHint[])[new(ControllerPrompt.B, "Back", ControllerButton.B)];
        if (FocusedMediaPlayer is { IsVideo: true, CanControl: true } media)
            return (ControllerGuideHint[])[new(ControllerPrompt.A, media.PrimaryActionLabel, ControllerButton.A),
                new(ControllerPrompt.LeftTrigger, "Seek back", ControllerButton.LeftTrigger), new(ControllerPrompt.RightTrigger, "Seek forward", ControllerButton.RightTrigger),
                new(ControllerPrompt.LeftBumper, "Replay", ControllerButton.LeftBumper),
                new(ControllerPrompt.RightBumper, "Loop", ControllerButton.RightBumper), new(ControllerPrompt.RightStickPress, media.HasDialog ? "Collapse" : "Expand", ControllerButton.RightStick),
                .. (media.HasDialog || media.IsAdjustingVolume ? (ControllerGuideHint[])[new(ControllerPrompt.B, media.IsAdjustingVolume ? "Done" : "Back", ControllerButton.B)] : [])];
        if (FocusedBrowser is { } browser)
            return browser.PresentedInPin
                ? (ControllerGuideHint[])[new(ControllerPrompt.View, "Interact with pin", ControllerButton.View)]
                : browser.Surface?.HasDialog == true
                ? (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A), new(ControllerPrompt.B, "Cancel", ControllerButton.B)]
                : browser.Surface?.IsToolbarFocused == true
                ? (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A), new(ControllerPrompt.LeftStickMove, "Navigate"),
                    new(ControllerPrompt.LeftStickPress, "Page", ControllerButton.LeftStick)]
                : browser.IsBrowsing && browser.Surface?.IsProviderContent == true
                ? (ControllerGuideHint[])[new(ControllerPrompt.LeftStickMove, "Pointer"), new(ControllerPrompt.RightStickMove, "Scroll"),
                    new(ControllerPrompt.A, "Open search", ControllerButton.A), new(ControllerPrompt.LeftTrigger, "Zoom out", ControllerButton.LeftTrigger),
                    new(ControllerPrompt.RightTrigger, "Zoom in", ControllerButton.RightTrigger), new(ControllerPrompt.B, "Back", ControllerButton.B)]
                : browser.IsBrowsing
                ? (ControllerGuideHint[])[new(ControllerPrompt.LeftStickMove, "Pointer"), new(ControllerPrompt.RightStickMove, "Scroll"),
                    new(ControllerPrompt.A, "Select", ControllerButton.A), new(ControllerPrompt.LeftBumper, "Previous", ControllerButton.LeftBumper),
                    new(ControllerPrompt.RightBumper, "Forward", ControllerButton.RightBumper), new(ControllerPrompt.X, browser.Surface?.IsNavigationInProgress == true ? "Stop" : "Reload", ControllerButton.X),
                    new(ControllerPrompt.Y, "Address", ControllerButton.Y), new(ControllerPrompt.RightStickPress, "Type", ControllerButton.RightStick),
                    new(ControllerPrompt.LeftTrigger, "Zoom out", ControllerButton.LeftTrigger), new(ControllerPrompt.RightTrigger, "Zoom in", ControllerButton.RightTrigger),
                    new(ControllerPrompt.LeftStickPress, "Toolbar", ControllerButton.LeftStick), new(ControllerPrompt.Menu, "Open externally", ControllerButton.Menu),
                    .. (browser.Surface?.RequiresActivation == true ? (ControllerGuideHint[])[new(ControllerPrompt.B, "Leave interaction", ControllerButton.B)] : [])]
                : (ControllerGuideHint[])[new(ControllerPrompt.A, "Interact", ControllerButton.A),
                    new(ControllerPrompt.B, "Back", ControllerButton.B)];
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
