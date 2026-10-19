using WidgetRail.WidgetProtocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal event Action? ControllerGuideChanged;
    internal object ControllerGuideContext => previewEntered && previewSurface?.Fragment is { } preview ? ("preview", preview.ControllerGuideContext) : (activeScope, HasTransientControl, adjustingSlider?.Identity.Id,
        FocusedBrowser?.IsInteracting, FocusedBrowser?.IsBrowsing, FocusedBrowser?.Surface?.HasDialog, FocusedBrowser?.PresentedInPin, FocusedBrowser?.Surface?.RequiresActivation, FocusedBrowser?.Surface?.IsProviderContent, FocusedBrowser?.Surface?.SessionStopped, FocusedBrowser?.Surface?.NeedsRecovery);
    internal bool IsGuidePresentationReady(bool requireFocus)
    {
        if (previewEntered && previewSurface?.Fragment is { } preview) return preview.IsGuidePresentationReady(requireFocus);
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
    { if (HasTransientControl) ClearFocusPreview(); RefreshContextIndicators(); ControllerGuideChanged?.Invoke(); previewOwner?.ControllerGuideChanged?.Invoke(); }

    internal IReadOnlyList<ControllerGuideHint> CaptureControllerGuide()
    {
        if (disposed || applying || presentationOnly || !presentationActive || presentation is null) return [];
        if (previewEntered && previewSurface?.Fragment is { } preview) return preview.CaptureControllerGuide();
        if (ControllerTextEntry is { } keyboard) return CaptureKeyboardGuide(keyboard);
        if (HasTransientControl) return (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A)];
        if (FocusedMediaPlayer is { IsExpanded: true, CanControl: false })
            return (ControllerGuideHint[])[new(ControllerPrompt.B, "Back", ControllerButton.B)];
        if (FocusedMediaPlayer is { IsImage: true, CanControl: true } picture)
            return (ControllerGuideHint[])[new(ControllerPrompt.A, picture.PrimaryActionLabel, ControllerButton.A),
                new(ControllerPrompt.LeftTrigger, "Zoom out", ControllerButton.LeftTrigger), new(ControllerPrompt.RightTrigger, "Zoom in", ControllerButton.RightTrigger),
                new(ControllerPrompt.LeftStickMove, "Navigate"), new(ControllerPrompt.RightStickMove, "Pan"),
                new(ControllerPrompt.LeftBumper, "Fit image", ControllerButton.LeftBumper),
                new(ControllerPrompt.RightBumper, "Image / controls", ControllerButton.RightBumper),
                .. (picture.CanExpand ? (ControllerGuideHint[])[new(ControllerPrompt.RightStickPress, picture.IsExpanded ? "Collapse" : "Expand", ControllerButton.RightStick)] : []),
                .. (picture.IsExpanded ? (ControllerGuideHint[])[new(ControllerPrompt.B, "Back", ControllerButton.B)] : [])];
        if (FocusedMediaPlayer is { IsVideo: true, CanControl: true } media)
            return (ControllerGuideHint[])[new(ControllerPrompt.A, media.PrimaryActionLabel, ControllerButton.A),
                new(ControllerPrompt.X, "Play / pause", ControllerButton.X),
                new(ControllerPrompt.LeftTrigger, "Seek back", ControllerButton.LeftTrigger), new(ControllerPrompt.RightTrigger, "Seek forward", ControllerButton.RightTrigger),
                new(ControllerPrompt.LeftBumper, "Replay", ControllerButton.LeftBumper),
                new(ControllerPrompt.RightBumper, "Loop", ControllerButton.RightBumper),
                .. (media.CanExpand ? (ControllerGuideHint[])[new(ControllerPrompt.RightStickPress, media.IsExpanded ? "Collapse" : "Expand", ControllerButton.RightStick)] : []),
                .. (media.IsExpanded || media.IsAdjustingVolume ? (ControllerGuideHint[])[new(ControllerPrompt.B, media.IsAdjustingVolume ? "Done" : "Back", ControllerButton.B)] : [])];
        if (FocusedBrowser is { } browser)
        {
            var browserHints=CaptureBrowserGuide(browser);
            if(browser.PresentedInPin||browser.Surface?.ActiveSurface is not {HasDialog:false,NeedsRecovery:false})return browserHints;
            // R3 is the browser's explicitly unowned button; expose only that
            // ancestor shortcut, rather than advertising consumed page controls.
            return (ControllerGuideHint[])[..browserHints,..ControllerGuideModel.Resolve(ControllerFocusPath(),new HashSet<ControllerButton>(),false)
                .Where(hint=>hint.Button==ControllerButton.RightStick)];
        }
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
        if (previewOwner is not null) return (ControllerGuideHint[])[..hints, new(ControllerPrompt.B,"Back to source",ControllerButton.B)];
        if (PreviewEnterButton(CurrentPreviewSource()) is { } enter) return (ControllerGuideHint[])[..hints.Where(hint=>hint.Button!=enter),
            new(enter switch {ControllerButton.X=>ControllerPrompt.X,ControllerButton.Y=>ControllerPrompt.Y,
                ControllerButton.LeftStick=>ControllerPrompt.LeftStickPress,_=>ControllerPrompt.RightStickPress},"Interact",enter)];
        return hints;
    }
    internal static IReadOnlyList<ControllerGuideHint> CaptureKeyboardGuide(WidgetTextEntryDialog keyboard) =>
        (ControllerGuideHint[])[new(ControllerPrompt.LeftStickMove, "Navigate"),
                new(ControllerPrompt.A, keyboard.SuggestionsFocused ? "Use suggestion" : "Type (hold to repeat)", ControllerButton.A),
                .. (keyboard.CanAcceptFirstSuggestion ? (ControllerGuideHint[])[new(ControllerPrompt.LeftStickPress,
                    "Use first suggestion", ControllerButton.LeftStick)] : []),
                .. (keyboard.CanAcceptFirstSuggestion ? (ControllerGuideHint[])[new(ControllerPrompt.RightStickPress,
                    keyboard.SuggestionsFocused ? "Back to keyboard" : "Suggestions", ControllerButton.RightStick)] : []),
                new(ControllerPrompt.X, "Delete (hold to repeat)", ControllerButton.X),
                new(ControllerPrompt.LeftBumper, "Caret left", ControllerButton.LeftBumper),
                new(ControllerPrompt.RightBumper, "Caret right", ControllerButton.RightBumper),
                new(ControllerPrompt.LeftTrigger, "Shift", ControllerButton.LeftTrigger),
                new(ControllerPrompt.Y, "Clear", ControllerButton.Y),
                new(ControllerPrompt.RightTrigger, keyboard.CommitLabel, ControllerButton.RightTrigger),
                new(ControllerPrompt.B, keyboard.DismissLabel, ControllerButton.B),
                .. (keyboard.IsSensitive ? (ControllerGuideHint[])[new(ControllerPrompt.RightStickPress,
                    keyboard.PasswordVisible ? "Hide password" : "Show password", ControllerButton.RightStick)] : [])];

    internal static IReadOnlyList<ControllerGuideHint> CaptureBrowserGuide(Browser.BrowserSlot browser)
    {
        var page = browser.Surface?.ActiveSurface;
        return browser.PresentedInPin
                ? (ControllerGuideHint[])[new(ControllerPrompt.View, "Interact with pin", ControllerButton.View)]
                : page?.HasDialog == true
                ? (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A), new(ControllerPrompt.B, "Cancel", ControllerButton.B)]
                : page?.NeedsRecovery == true
                ? (ControllerGuideHint[])[new(ControllerPrompt.A, "Reopen page", ControllerButton.A), new(ControllerPrompt.B, "Back", ControllerButton.B)]
                : page?.IsToolbarFocused == true
                ? (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A), new(ControllerPrompt.LeftStickMove, "Navigate"),
                    new(ControllerPrompt.LeftStickPress, "Page", ControllerButton.LeftStick),
                    .. (page?.ToolbarBackReturnsToPage == true ? (ControllerGuideHint[])[new(ControllerPrompt.B, "Page", ControllerButton.B)] : [])]
                : browser.IsBrowsing && page?.IsProviderContent == true
                ? (ControllerGuideHint[])[new(ControllerPrompt.DPad, "Jump to control"), new(ControllerPrompt.LeftStickMove, "Pointer"), new(ControllerPrompt.RightStickMove, "Scroll"),
                    new(ControllerPrompt.A, "Open search", ControllerButton.A), new(ControllerPrompt.LeftTrigger, "Zoom out", ControllerButton.LeftTrigger),
                    new(ControllerPrompt.RightTrigger, "Zoom in", ControllerButton.RightTrigger), new(ControllerPrompt.B, "Back", ControllerButton.B)]
                : browser.IsBrowsing && page?.SessionStopped == true
                ? (ControllerGuideHint[])[new(ControllerPrompt.X, "Restart webapp", ControllerButton.X),
                    new(ControllerPrompt.LeftStickPress, "Website controls", ControllerButton.LeftStick), new(ControllerPrompt.B, "Back", ControllerButton.B)]
                : browser.IsBrowsing
                ? (ControllerGuideHint[])[new(ControllerPrompt.DPad, "Jump to control"), new(ControllerPrompt.LeftStickMove, "Pointer"), new(ControllerPrompt.RightStickMove, "Scroll"),
                    new(ControllerPrompt.A, "Select", ControllerButton.A),
                    .. (page?.CanNavigateBack == true ? (ControllerGuideHint[])[new(ControllerPrompt.LeftBumper, "Previous", ControllerButton.LeftBumper)] : []),
                    .. (page?.CanNavigateForward == true ? (ControllerGuideHint[])[new(ControllerPrompt.RightBumper, "Forward", ControllerButton.RightBumper)] : []),
                    .. (page?.CanReloadPage == true ? (ControllerGuideHint[])[new(ControllerPrompt.X, page.IsNavigationInProgress ? "Stop" : "Reload", ControllerButton.X)] : []),
                    .. (page?.CanEditAddress == true ? (ControllerGuideHint[])[new(ControllerPrompt.Y, "Address", ControllerButton.Y)] : []),
                    new(ControllerPrompt.LeftTrigger, "Zoom out", ControllerButton.LeftTrigger), new(ControllerPrompt.RightTrigger, "Zoom in", ControllerButton.RightTrigger),
                    new(ControllerPrompt.LeftStickPress, "Toolbar", ControllerButton.LeftStick),
                    .. (page?.CanOpenPageActions == true ? (ControllerGuideHint[])[new(ControllerPrompt.Menu, "Page actions", ControllerButton.Menu)] : []),
                    .. (browser.Surface?.HasAttachedPopup == true ? (ControllerGuideHint[])[new(ControllerPrompt.B, "Close popup", ControllerButton.B)] : page?.RequiresActivation == true ? (ControllerGuideHint[])[new(ControllerPrompt.B, "Leave interaction", ControllerButton.B)] : [])]
                : (ControllerGuideHint[])[new(ControllerPrompt.A, "Interact", ControllerButton.A),
                    new(ControllerPrompt.B, "Back", ControllerButton.B)];
    }
}
