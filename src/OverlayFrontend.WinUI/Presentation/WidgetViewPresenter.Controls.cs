using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    // A popup holds declaration authority, never a worker callback or a second
    // command queue. WinUI owns popup placement, scrolling and pointer/keyboard UI.
    private sealed record SelectPopup(Binding Owner, WidgetPresentationAuthority Authority,
        IReadOnlyList<WidgetSelectOption> Options, MenuFlyout Flyout, IReadOnlyList<ToggleMenuFlyoutItem> Items, IReadOnlyList<WidgetNativePackageIcon> Icons);
    private SelectPopup? selectPopup;
    private int selectFocusIndex;

    internal bool HasTransientControl => selectPopup is not null || textEntryPopup is not null || contextPopup is not null;

    /// <summary>Route B here before widget shortcuts/modal dismissal. Also call when hiding the host.</summary>
    internal bool DismissTransientControl()
    {
        if (DismissTextEntry() || DismissContextMenu()) return true;
        if (selectPopup is not { } popup) return false;
        selectPopup = null; // revoke immediately; an exiting popup has no action authority
        popup.Flyout.Hide();
        foreach (var icon in popup.Icons) icon.Dispose();
        return true;
    }

    private Button CreateSelect(WidgetElementIdentity identity, object token)
    {
        var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => OpenSelect(identity, token);
        button.Unloaded += (_, _) =>
        {
            if (selectPopup?.Owner.Element == button) DismissTransientControl();
        };
        return button;
    }

    private void OpenSelect(WidgetElementIdentity identity, object token)
    {
        if (applying || disposed || presentationOnly || frame is null ||
            !bindings.TryGetValue(identity.Id, out var binding) || binding.Identity != identity ||
            !ReferenceEquals(binding.Token, token) || !Eligible(binding) || !binding.Element.IsLoaded) return;
        DismissTransientControl();
        var options = declarations[identity.Id].Node.SelectOptions.ToArray();
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        var items = options.Select(option =>
        {
            var item = new ToggleMenuFlyoutItem { Text = option.Label, IsChecked = option.IsSelected,
                IsEnabled = !option.IsDisabled && !option.IsBusy };
            AutomationProperties.SetAutomationId(item, $"Widget.{identity.Id}.Option.{option.Id}");
            AutomationProperties.SetName(item, option.AccessibilityLabel ?? option.Label);
            flyout.Items.Add(item);
            return item;
        }).ToArray();
        var icons = options.Select((option, index) => CreateSelectIcon(items[index], option)).OfType<WidgetNativePackageIcon>().ToArray();
        var popup = new SelectPopup(binding, frame.Authority, options, flyout, items, icons);
        selectPopup = popup;
        for (var index = 0; index < items.Length; ++index)
        {
            var option = options[index];
            var focusIndex = index;
            items[index].GotFocus += (_, _) => { if (ReferenceEquals(selectPopup, popup)) selectFocusIndex = focusIndex; };
            items[index].Click += async (_, _) => await InvokeSelectAsync(popup, option);
        }
        flyout.Opened += (_, _) =>
        {
            if (!ReferenceEquals(selectPopup, popup)) return;
            var selected = Array.FindIndex(options, option => option.IsSelected && !option.IsDisabled && !option.IsBusy);
            if (selected < 0) selected = Array.FindIndex(items, item => item.IsEnabled);
            if (selected >= 0) items[selected].Focus(FocusState.Keyboard);
        };
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(selectPopup, popup)) selectPopup = null;
            foreach (var icon in popup.Icons) icon.Dispose();
        };
        try { flyout.ShowAt(binding.Element); }
        catch (Exception error)
        {
            if (ReferenceEquals(selectPopup, popup)) selectPopup = null;
            foreach (var icon in popup.Icons) icon.Dispose();
            ReportFailure(error);
        }
    }

    private bool TryRestoreTransientFocus()
    {
        if (textEntryPopup is { } edit) { edit.Dialog.RestoreFocus(); return true; }
        if (contextPopup is { } menu)
        {
            if (!ContextIsCurrent(menu)) { DismissContextMenu(); return false; }
            menu.Items[contextFocusIndex].Focus(FocusState.Keyboard); return true;
        }
        if (selectPopup is not { } popup) return false;
        if (!SelectIsCurrent(popup)) { DismissTransientControl(); return false; }
        if (selectFocusIndex >= 0 && selectFocusIndex < popup.Items.Count)
            popup.Items[selectFocusIndex].Focus(FocusState.Keyboard);
        return true;
    }

    private void ValidateTransientControl()
    {
        if (contextPopup is { } menu && !ContextIsCurrent(menu)) DismissContextMenu();
        if (textEntryPopup is { } edit && !TextEntryIsCurrent(edit)) DismissTextEntry();
        if (selectPopup is { } popup && !SelectIsCurrent(popup)) DismissTransientControl();
    }

    private bool SelectIsCurrent(SelectPopup popup) => !disposed && frame is not null &&
        SameOwner(popup.Authority, frame.Authority) && popup.Authority.ActiveInputScopeId == frame.Authority.ActiveInputScopeId &&
        bindings.TryGetValue(popup.Owner.Identity.Id, out var binding) && ReferenceEquals(binding, popup.Owner) &&
        Eligible(binding) && declarations[binding.Identity.Id].Node.SelectOptions.SequenceEqual(popup.Options);

    private bool MoveSelectFocus(FocusNavigationDirection direction)
    {
        if (selectPopup is not { } popup) return false;
        if (!SelectIsCurrent(popup)) { DismissTransientControl(); return true; }
        // Consume every direction while open: a boundary cannot escape to the
        // underlying widget. Native keyboard/pointer navigation remains native.
        if (direction is not (FocusNavigationDirection.Up or FocusNavigationDirection.Down)) return true;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        var index = popup.Items.ToList().FindIndex(item => ReferenceEquals(item, focused));
        var step = direction == FocusNavigationDirection.Up ? -1 : 1;
        if (index < 0) index = step > 0 ? -1 : popup.Items.Count;
        for (index += step; index >= 0 && index < popup.Items.Count; index += step)
            if (popup.Items[index].IsEnabled) { popup.Items[index].Focus(FocusState.Keyboard); break; }
        return true;
    }

    private bool ActivateSelect()
    {
        if (selectPopup is { } popup)
        {
            var focused = FocusManager.GetFocusedElement(XamlRoot);
            for (var index = 0; index < popup.Items.Count; ++index)
                if (ReferenceEquals(popup.Items[index], focused))
                { _ = InvokeSelectAsync(popup, popup.Options[index]); break; }
            return true;
        }
        if (FocusedBinding() is { Identity.Kind: ViewNodeKind.Select } binding && Eligible(binding))
        { OpenSelect(binding.Identity, binding.Token); return true; }
        return false;
    }

    private async Task InvokeSelectAsync(SelectPopup popup, WidgetSelectOption option)
    {
        if (applying || !ReferenceEquals(selectPopup, popup) || !SelectIsCurrent(popup) ||
            option.IsDisabled || option.IsBusy || DispatchActionAsync is null) return;
        // Read current authority after validating the opening's semantic binding.
        // Harmless snapshots may advance the sequence while this menu stays open.
        var displayed = frame!;
        var authority = displayed.Authority;
        var action = new WidgetActionEvent(option.ActionId, popup.Owner.Identity.Id, ControllerButton.A,
            ControllerEventPhase.Pressed, ++actionSequence, Environment.TickCount64 * 1000,
            InputScopeId: authority.ActiveInputScopeId) { FocusedElementId = popup.Owner.Identity.Id };
        DismissTransientControl();
        try { await DispatchActionAsync(new(displayed, action)); }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { ReportFailure(error); }
    }
}
