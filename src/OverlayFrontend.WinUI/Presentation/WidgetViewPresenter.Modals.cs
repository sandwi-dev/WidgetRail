using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record PendingModalDismissal(WidgetPresentationBinding Owner, object LayerToken,
        object DialogToken, string DialogId, string Action);
    private PendingModalDismissal? modalDismissPending;
    // The shell may return from pin interaction only after owned Escape contexts.
    internal bool OwnsKeyboardEscape => previewEntered || pendingPrimaryPreview is not null || HasTransientControl || effectiveView?.Root.Kind==ViewNodeKind.ModalLayer ||
        HasClosingModal || previewOwner is not null || bindings.Values.Any(binding=>
            binding.Element is Browser.BrowserSlot slot&&slot.Surface?.HasNativeKeyboardFocus==true);

    // A modal is painted beside the shell, outside this presenter's subtree.
    // Native focus can still be on shell chrome (for example after a pointer
    // invocation). Claim Escape before sibling chrome's preview handlers, but
    // retain normal bubbling inside the modal so its controls get first refusal.
    internal IDisposable AttachModalDismissalInput(UIElement root, Func<bool> isVisible)
    {
        KeyEventHandler handler = (sender, args) => { if (isVisible()) ModalKeyDown(sender, args); };
        KeyEventHandler preview = (sender, args) =>
        {
            if (!isVisible() || XamlRoot is null ||
                FindBinding(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject) is { } focused && focused.Identity.Scope == activeScope) return;
            ModalKeyDown(sender, args);
        };
        root.PreviewKeyDown += preview;
        root.KeyDown += handler;
        return new InputRootLease(() => { root.PreviewKeyDown -= preview; root.KeyDown -= handler; });
    }

    private void ModalKeyDown(object sender, KeyRoutedEventArgs args)
    {
        // Preview portals sit beside their owner, including owners with a modal.
        // Give their nested controls Escape before the outer modal or pin return.
        if (!args.Handled && args.Key == Windows.System.VirtualKey.Escape && previewEntered && previewSurface?.Fragment is { } preview)
        {
            if (!preview.HasTransientControl) preview.ModalKeyDown(sender, args);
            return;
        }
        if (!args.Handled && args.Key == Windows.System.VirtualKey.Escape && previewOwner is { } owner && !HasTransientControl)
        {
            args.Handled = true;
            if (!args.KeyStatus.WasKeyDown)
            {
                if (FocusedMediaPlayer is { } media && media.Handle(ControllerButton.B, ControllerEventPhase.Pressed))
                    media.Handle(ControllerButton.B, ControllerEventPhase.Released);
                else if (!HandleSliderButton(ControllerButton.B, ControllerEventPhase.Pressed)) owner.ExitFocusPreview();
            }
            return;
        }
        if (args.Handled || args.Key != Windows.System.VirtualKey.Escape ||
            Input.GamepadKeyBoundary.Owns(this, args) || HasTransientControl ||
            FocusedBrowser?.Surface?.HasNativeKeyboardFocus == true ||
            !TryCaptureModalDismissal(backdrop: false, out var dismissal)) return;
        args.Handled = true;
        if (!args.KeyStatus.WasKeyDown) BeginModalDismissal(dismissal);
    }

    private bool TryCaptureModalDismissal(bool backdrop, out PendingModalDismissal dismissal)
    {
        dismissal = null!;
        if (disposed || applying || presentationOnly || fragmentRootId is not null || !presentationActive ||
            HasTransientControl || XamlRoot is null || VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Any(popup => popup.IsOpen && popup.Child is not ToolTip) ||
            presentation is not { } displayed || !IsBindingCurrent(displayed) || !CanDispatchAction ||
            effectiveView?.Root is not { Kind: ViewNodeKind.ModalLayer, Children.Count: 2 } modal ||
            !bindings.TryGetValue(modal.Id, out var layer)) return false;
        var dialog = modal.Children[1];
        if (dialog.InputScopeId != activeScope || dialog.IsDisabled == true || dialog.IsBusy == true ||
            !bindings.TryGetValue(dialog.Id, out var dialogBinding)) return false;
        // Older snapshots declare keyboard/controller cancellation through B. Only
        // the explicit new contract authorizes pointer dismissal of their backdrop.
        if (backdrop && modal.ModalDismissal?.DismissOnBackdrop != true) return false;
        var action = modal.ModalDismissal?.ActionId ?? dialog.Shortcuts.FirstOrDefault(shortcut =>
            shortcut.Button == ControllerButton.B && shortcut.Phase == ControllerEventPhase.Pressed)?.ActionId;
        if (action is null) return false;
        dismissal = new(displayed, layer.Token, dialogBinding.Token, dialog.Id, action);
        return true;
    }

    private Action? CaptureModalBackdropDismissal(object layerToken)
    {
        if (!TryCaptureModalDismissal(backdrop: true, out var captured) ||
            !ReferenceEquals(captured.LayerToken, layerToken)) return null;
        return () =>
        {
            if (TryCaptureModalDismissal(backdrop: true, out var current) && SameModalDismissal(captured, current))
                BeginModalDismissal(captured);
        };
    }

    private static bool SameModalDismissal(PendingModalDismissal first, PendingModalDismissal second) =>
        first.Owner.SameInput(second.Owner) && ReferenceEquals(first.LayerToken, second.LayerToken) &&
        ReferenceEquals(first.DialogToken, second.DialogToken) && first.Action == second.Action;

    private void BeginModalDismissal(PendingModalDismissal dismissal)
    {
        if (modalDismissPending is { } pending && SameModalDismissal(pending, dismissal)) return;
        modalDismissPending = dismissal;
        _ = DismissModalAsync(dismissal);
    }

    private async Task DismissModalAsync(PendingModalDismissal dismissal)
    {
        var displayed = dismissal.Owner;
        try
        {
            CancelGroupEntry();
            await DispatchCapturedActionAsync(displayed, new(dismissal.Action, dismissal.DialogId, ControllerButton.B,
                Sequence: ++actionSequence, MonotonicTimestampMicroseconds: Environment.TickCount64 * 1000,
                InputScopeId: displayed.Scope));
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { ReportFailure(error, IsBindingCurrent(displayed)); }
        finally
        {
            // A newer dialog can dispatch while an older action is still returning.
            // Its in-flight guard belongs to that dialog, not the old completion.
            if (ReferenceEquals(modalDismissPending, dismissal)) modalDismissPending = null;
        }
    }

    private void UpdateModalGeometry()
    {
        foreach (var binding in bindings.Values)
        {
            if (binding.Element is not WidgetModalLayer layer) continue;
            var node = declarations[binding.Identity.Id].Node;
            var dialog = node.Children[1];
            layer.CaptureBackdropDismissal = () => CaptureModalBackdropDismissal(binding.Token);
            // Dialog motion uses a compositor timeline. External/native surfaces
            // need the page renderer's synchronized placement contract; until a
            // dialog has that contract, keep it stable instead of fading a live
            // swap chain or moving a WebView on a different clock.
            layer.ApplyAppearance(HasDialogSurface(dialog) ? appearance with { AnimateWidgetModals = false } : appearance, systemAnimationsEnabled);
            layer.OverlayPresentation = node.OverlayModal;
            layer.Configure(bindings[node.Children[0].Id].LayoutElement, bindings[dialog.Id].LayoutElement,
                Length(dialog, "width") ?? 760,
                ComputedStyle(dialog, "height")?.Text == "auto" ? double.NaN : Length(dialog, "height") ?? 640,
                Length(dialog, "max-width") ?? double.PositiveInfinity,
                Length(dialog, "max-height") ?? double.PositiveInfinity, bindings[dialog.Id].Element,
                node.AnchoredPresentation, () => ResolvePresentationAnchor(node.AnchoredPresentation?.Anchor, this));
        }
    }
    private static bool HasDialogSurface(ViewNode node) => node.Kind is ViewNodeKind.MediaPlayer or ViewNodeKind.CapturedMedia or
        ViewNodeKind.MediaViewport or ViewNodeKind.WebBrowser or ViewNodeKind.WindowPreview || node.Children.Any(HasDialogSurface);

    private bool IsModalDialog(Declaration declaration, IReadOnlyDictionary<string, Declaration> plan) =>
        declaration.ParentId is { } parent && plan.TryGetValue(parent, out var owner) &&
        owner.Node.Kind == ViewNodeKind.ModalLayer && owner.Node.Children[1].Id == declaration.Node.Id;

    private bool IsModalBackdrop(string id)
    {
        // A background can own a nested navigation scope and remain visible on every tab.
        // Only the background branch of an actual modal loses its artwork timeline.
        for (var child = declarations.GetValueOrDefault(id); child?.ParentId is { } parentId;)
        {
            var parent = declarations[parentId];
            if (parent.Node.Kind == ViewNodeKind.ModalLayer && parent.Node.Children[0].Id == child.Node.Id) return true;
            child = parent;
        }
        return false;
    }

    private bool RejectInactiveFocus(Microsoft.UI.Xaml.Input.GettingFocusEventArgs args)
    {
        if (!presentationInputEnabled && !restoringRetainedFocusPresentation && FindBinding(args.NewFocusedElement) is not null) { args.TryCancel(); return true; }
        if (applying || frame is null || FindBinding(args.NewFocusedElement) is not { } incoming ||
            incoming.Identity.Scope == activeScope) return false;
        args.TryCancel();
        return true;
    }
}
