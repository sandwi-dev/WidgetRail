using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record NativeEntryRestore(DependencyObject? NativeFallback);
    private NativeEntryRestore? pendingEntryRestore;

    internal WidgetPresentationAuthority? KeyboardFocusAuthority => frame?.Authority;
    internal bool OwnsKeyboardFocusAuthority(WidgetPresentationAuthority authority) => frame is not null &&
        SameOwner(authority, frame.Authority) && authority.WorkerRun == frame.Authority.WorkerRun;

    private Binding? EntryTarget()
    {
        if (remembered.TryGetValue(activeScope, out var identity) && bindings.TryGetValue(identity.Id, out var rememberedBinding) &&
            rememberedBinding.Identity == identity && Navigable(rememberedBinding)) return rememberedBinding;
        if (effectiveView?.InitialFocusId is { } initial && bindings.TryGetValue(initial, out var initialBinding) && Navigable(initialBinding)) return initialBinding;
        return bindings.Values.FirstOrDefault(Navigable);
    }

    private void CaptureEntryOwnership(bool replace = false, DependencyObject? beforePublication = null, bool ownedBeforePublication = false)
    {
        if (!needsEntry || pendingGroupEntry is not null) { pendingEntryRestore = null; return; }
        if (!replace && pendingEntryRestore is { } retained)
        {
            // Loading/ready publications may remove or replace the old target.
            // Rebase its semantic destination, preserving the prior native-focus
            // authority rather than treating a user's outside choice as fallback.
            var currentFocus = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            var fallback = ownedBeforePublication && !ReferenceEquals(beforePublication, currentFocus)
                ? currentFocus : retained.NativeFallback;
            pendingEntryRestore = retained with { NativeFallback = fallback };
            return;
        }
        // An empty/loading scope still has native focus provenance. Do not wait
        // until a target appears and accidentally adopt a later outside choice.
        pendingEntryRestore = new(XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject);
    }

    private bool EntryStillOwnsNativeFocus()
    {
        if (pendingEntryRestore is not { } entry || NativeRestoreStillOwnsFocus(EntryTarget(), entry.NativeFallback)) return true;
        // A user/automation target outside this widget is just as authoritative
        // during scope entry as during an ordinary publication restoration.
        TraceFocus("entry-native-choice-wins");
        pendingEntryRestore = null;
        needsEntry = false;
        ClearEntryLayoutWait();
        return false;
    }

    private bool RedirectCompletedKeyboardRecovery(GettingFocusEventArgs args)
    {
        // Floating keyboard closure can recover focus after its owner has already
        // navigated. That is not a new pointer, directional or UIA focus choice.
        // Use the existing intended entry while native GettingFocus can redirect
        // the recovery, instead of allowing a fallback to overwrite scope memory.
        if (disposed || applying || presentationOnly || !presentationActive || !presentationInputEnabled || !automaticFocusEnabled || HasTransientControl ||
            !needsEntry || pendingGroupEntry is not null || pendingEntryRestore is not { } entry || args.Direction != FocusNavigationDirection.None ||
            args.InputDevice != FocusInputDeviceKind.Keyboard || args.FocusState == FocusState.Pointer ||
            !WidgetTextEntryDialog.SharesCompletedFocusOrigin(args.OldFocusedElement, entry.NativeFallback, this) ||
            EntryTarget() is not { Element: Control { IsEnabled: true } target } binding ||
            target is WidgetIndexedCollectionView || ReferenceEquals(args.NewFocusedElement, target)) return false;
        if (!args.TrySetNewFocusedElement(target))
        {
            if (!args.TryCancel()) return false;
            WaitForEntryLayout(binding);
            TraceFocus("entry-completed-keyboard-recovery-wait");
            return true;
        }
        TraceFocus("entry-completed-keyboard-recovery");
        return true;
    }
}
