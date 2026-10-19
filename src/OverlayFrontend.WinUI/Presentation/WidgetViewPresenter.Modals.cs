using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record PendingModalDismissal(WidgetPresentationBinding Owner, object DialogToken, string Action);
    private PendingModalDismissal? modalKeyboardDismissPending;

    private void ModalKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!args.Handled && args.Key == Windows.System.VirtualKey.Escape && previewOwner is { } owner && !HasTransientControl)
        { args.Handled = true; owner.ExitFocusPreview(); return; }
        if (args.Handled || args.Key != Windows.System.VirtualKey.Escape ||
            Input.GamepadKeyBoundary.Owns(this, args) || HasTransientControl ||
            FocusedBrowser?.Surface?.HasNativeKeyboardFocus == true ||
            presentation is not { } displayed || !IsBindingCurrent(displayed) || !CanDispatchAction ||
            effectiveView?.Root is not { Kind: ViewNodeKind.ModalLayer } modal) return;
        var dialog = modal.Children[1];
        if (dialog.InputScopeId != activeScope || !bindings.TryGetValue(dialog.Id, out var dialogBinding) ||
            dialog.Shortcuts.FirstOrDefault(shortcut =>
                shortcut.Button == ControllerButton.B && shortcut.Phase == ControllerEventPhase.Pressed) is not { } dismiss) return;
        args.Handled = true;
        if (args.KeyStatus.WasKeyDown || modalKeyboardDismissPending is { } pending &&
            pending.Owner.SameInput(displayed) && ReferenceEquals(pending.DialogToken, dialogBinding.Token) &&
            pending.Action == dismiss.ActionId) return;
        var dismissal = new PendingModalDismissal(displayed, dialogBinding.Token, dismiss.ActionId);
        modalKeyboardDismissPending = dismissal;
        _ = DismissModalFromKeyboardAsync(dismissal, dialog.Id);
    }

    private async Task DismissModalFromKeyboardAsync(PendingModalDismissal dismissal, string dialogId)
    {
        var displayed = dismissal.Owner;
        try
        {
            CancelGroupEntry();
            await DispatchCapturedActionAsync(displayed, new(dismissal.Action, dialogId, ControllerButton.B,
                Sequence: ++actionSequence, MonotonicTimestampMicroseconds: Environment.TickCount64 * 1000,
                InputScopeId: displayed.Scope));
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { ReportFailure(error, IsBindingCurrent(displayed)); }
        finally
        {
            // A newer dialog can dispatch while an older action is still returning.
            // Its in-flight guard belongs to that dialog, not the old completion.
            if (ReferenceEquals(modalKeyboardDismissPending, dismissal)) modalKeyboardDismissPending = null;
        }
    }

    private void UpdateModalGeometry()
    {
        foreach (var binding in bindings.Values)
        {
            if (binding.Element is not WidgetModalLayer layer) continue;
            var node = declarations[binding.Identity.Id].Node;
            var dialog = node.Children[1];
            // Dialog motion uses a compositor timeline. External/native surfaces
            // need the page renderer's synchronized placement contract; until a
            // dialog has that contract, keep it stable instead of fading a live
            // swap chain or moving a WebView on a different clock.
            layer.ApplyAppearance(HasDialogSurface(dialog) ? appearance with { AnimateWidgetModals = false } : appearance, systemAnimationsEnabled);
            layer.Configure(bindings[node.Children[0].Id].LayoutElement, bindings[dialog.Id].LayoutElement,
                Length(dialog, "width") ?? 760, Length(dialog, "height") ?? 640,
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

    private bool RejectInactiveFocus(Microsoft.UI.Xaml.Input.GettingFocusEventArgs args)
    {
        if (!presentationInputEnabled && !restoringRetainedFocusPresentation && FindBinding(args.NewFocusedElement) is not null) { args.TryCancel(); return true; }
        if (applying || frame is null || FindBinding(args.NewFocusedElement) is not { } incoming ||
            incoming.Identity.Scope == activeScope) return false;
        args.TryCancel();
        return true;
    }
}
