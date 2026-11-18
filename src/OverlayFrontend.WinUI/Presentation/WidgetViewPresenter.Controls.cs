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
    private sealed record SelectPopup(Binding Owner, WidgetPresentationBinding Presentation,
        IReadOnlyList<WidgetSelectOption> Options, bool IsCommandMenu, MenuFlyout Flyout, IReadOnlyList<MenuFlyoutItemBase> Items, IReadOnlyList<WidgetNativePackageIcon> Icons)
    { internal IDisposable? Theme; }
    private SelectPopup? selectPopup;
    private SelectPopup? returningSelectPopup;
    private int selectFocusIndex;

    internal bool HasTransientControl => selectPopup is not null || textEntryPopup is not null || contextPopup is not null;

    /// <summary>Route B here before widget shortcuts/modal dismissal. Also call when hiding the host.</summary>
    internal bool DismissTransientControl()
    {
        if (DismissTextEntry() || DismissContextMenu()) return true;
        if (selectPopup is not { } popup) return false;
        // A native flyout may use another XamlRoot. Keep its exact preview opener
        // as the focus owner while the popup window closes, without retaining
        // command authority or a widget-authored return flag.
        if (previewOwner?.PreviewInputCurrent(this) == true && SelectIsCurrent(popup)) returningSelectPopup = popup;
        selectPopup = null;
        ((WidgetValueButton)popup.Owner.Element).SetExpanded(false);
        NotifyControllerGuideChanged(); // revoke immediately; an exiting popup has no action authority
        popup.Flyout.Hide();
        popup.Theme?.Dispose();
        foreach (var icon in popup.Icons) icon.Dispose();
        return true;
    }

    private Button CreateSelect(WidgetElementIdentity identity, object token)
    {
        var button = new WidgetValueButton { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        button.ExpandRequested = () => OpenSelect(identity, token);
        button.CollapseRequested = () => { if (selectPopup?.Owner.Element == button) DismissTransientControl(); };
        button.Click += (_, _) => OpenSelect(identity, token);
        button.Unloaded += (_, _) =>
        {
            if (selectPopup?.Owner.Element == button) DismissTransientControl();
        };
        return button;
    }

    private void OpenSelect(WidgetElementIdentity identity, object token)
    {
        if (applying || disposed || presentationOnly || !presentationInputEnabled || frame is null ||
            !bindings.TryGetValue(identity.Id, out var binding) || binding.Identity != identity ||
            !ReferenceEquals(binding.Token, token) || !Eligible(binding) || !binding.Element.IsLoaded) return;
        if (previewOwner is { } owner && !owner.EnterFocusPreview(focusFirst: false)) return;
        DismissTransientControl();
        returningSelectPopup = null; // A newer native control owns any subsequent focus.
        var declaration = declarations[identity.Id].Node;
        var options = declaration.SelectOptions.ToArray();
        var commandMenu = declaration.IsCommandMenu == true;
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        Input.GamepadKeyBoundary.ObserveFlyout(flyout, binding.Element);
        var items = options.Select(option =>
        {
            MenuFlyoutItemBase item = commandMenu
                ? new MenuFlyoutItem { Text = option.Label }
                : new ToggleMenuFlyoutItem { Text = option.Label, IsChecked = option.IsSelected };
            item.IsEnabled = !option.IsDisabled && !option.IsBusy;
            AutomationProperties.SetAutomationId(item, $"Widget.{identity.Id}.Option.{option.Id}");
            AutomationProperties.SetName(item, option.AccessibilityLabel ?? option.Label);
            flyout.Items.Add(item);
            return item;
        }).ToArray();
        var icons = options.Select((option, index) => CreateSelectIcon(items[index], option)).OfType<WidgetNativePackageIcon>().ToArray();
        var popup = new SelectPopup(binding, presentation!, options, commandMenu, flyout, items, icons);
        popup.Theme = NativePopupTheme.Menu(flyout, this);
        selectPopup = popup;
        ((WidgetValueButton)binding.Element).SetExpanded(true);
        NotifyControllerGuideChanged();
        for (var index = 0; index < items.Length; ++index)
        {
            var option = options[index];
            var focusIndex = index;
            items[index].GotFocus += (_, _) => { if (ReferenceEquals(selectPopup, popup)) selectFocusIndex = focusIndex; };
            if (items[index] is MenuFlyoutItem command)
                command.Click += async (_, _) => await InvokeSelectAsync(popup, option);
            else if (items[index] is ToggleMenuFlyoutItem choice)
                choice.Click += async (_, _) => await InvokeSelectAsync(popup, option);
        }
        flyout.Opened += (_, _) =>
        {
            if (!ReferenceEquals(selectPopup, popup)) return;
            if (!SelectIsCurrent(popup)) { DismissTransientControl(); return; }
            var selected = Array.FindIndex(options, option => option.IsSelected && !option.IsDisabled && !option.IsBusy);
            if (selected < 0) selected = Array.FindIndex(items, item => item.IsEnabled);
            if (selected >= 0) items[selected].Focus(FocusState.Keyboard);
        };
        flyout.Closed += (_, _) =>
        {
            popup.Theme?.Dispose();
            if (ReferenceEquals(selectPopup, popup))
            { selectPopup = null; ((WidgetValueButton)popup.Owner.Element).SetExpanded(false); }
            NotifyControllerGuideChanged();
            foreach (var icon in popup.Icons) icon.Dispose();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => CompleteSelectFocusReturn(popup));
        };
        try { flyout.ShowAt(binding.Element); }
        catch (Exception error)
        {
            popup.Theme?.Dispose();
            if (ReferenceEquals(selectPopup, popup))
            { selectPopup = null; ((WidgetValueButton)popup.Owner.Element).SetExpanded(false); }
            NotifyControllerGuideChanged();
            foreach (var icon in popup.Icons) icon.Dispose();
            ReportFailure(error);
        }
    }

    // WinUI owns focus for the native flyout's complete open lifetime,
    // including the intermediate popup presenter before an item receives focus.
    // Its separate root is not part of the fragment's widget bindings.
    private bool OwnsSelectPopupFocus => selectPopup is { } popup && SelectIsCurrent(popup);

    private bool SelectFocusReturnCurrent(SelectPopup popup) => !disposed && !applying && presentationActive &&
        presentationInputEnabled && popup.Presentation.SameInput(presentation) &&
        bindings.TryGetValue(popup.Owner.Identity.Id, out var binding) && ReferenceEquals(binding, popup.Owner) &&
        Eligible(binding) && binding.Element is Control { IsLoaded: true, IsEnabled: true } &&
        (declarations[binding.Identity.Id].Node.IsCommandMenu == true) == popup.IsCommandMenu &&
        previewOwner?.PreviewInputCurrent(this) == true;

    private bool OwnsSelectFocusReturn
    {
        get
        {
            if (returningSelectPopup is not { } popup || !SelectFocusReturnCurrent(popup)) return false;
            var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            return focused is null || ReferenceEquals(FindBinding(focused), popup.Owner) ||
                ReferenceEquals(focused, previewOwner?.previewSource?.Anchor) ||
                ReferenceEquals(focused, previewOwner?.previewSource?.Source.Element);
        }
    }

    private void CompleteSelectFocusReturn(SelectPopup popup)
    {
        if (ReferenceEquals(returningSelectPopup, popup))
        {
            TraceFocus($"select-focus-return owner={popup.Owner.Identity.Id} current={OwnsSelectFocusReturn}");
            if (OwnsSelectFocusReturn) FocusBinding(popup.Owner);
            returningSelectPopup = null;
        }
        // Light-dismiss may already have delivered GotFocus to an outside
        // control while the popup still owned focus. Reconcile after Closed,
        // without taking focus back from that explicit outside choice.
        if (!HasTransientControl && previewOwner is { } owner && owner.PreviewInputCurrent(this) &&
            FocusedBinding() is null) owner.ExitFocusPreview(restoreFocus: false);
        NotifyControllerGuideChanged();
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

    private bool SelectIsCurrent(SelectPopup popup) => !disposed && presentationInputEnabled && frame is not null &&
        popup.Presentation.SameInput(presentation) &&
        bindings.TryGetValue(popup.Owner.Identity.Id, out var binding) && ReferenceEquals(binding, popup.Owner) &&
        Eligible(binding) && (declarations[binding.Identity.Id].Node.IsCommandMenu == true) == popup.IsCommandMenu &&
        declarations[binding.Identity.Id].Node.SelectOptions.SequenceEqual(popup.Options);

    private bool MoveSelectFocus(FocusNavigationDirection direction)
    {
        if (selectPopup is not { } popup) return false;
        if (!SelectIsCurrent(popup)) { DismissTransientControl(); return true; }
        // Consume every direction while open: a boundary cannot escape to the
        // underlying widget. Cycle through enabled options at either boundary.
        NativeMenuFocus.Move(popup.Items, direction, ref selectFocusIndex);
        return true;
    }

    private bool ActivateSelect()
    {
        if (selectPopup is { } popup)
        {
            var popupRoot = popup.Items.FirstOrDefault()?.XamlRoot ?? XamlRoot;
            var focused = popupRoot is null ? null : FocusManager.GetFocusedElement(popupRoot);
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
            option.IsDisabled || option.IsBusy || !CanDispatchAction) return;
        // Read current authority after validating the opening's semantic binding.
        // Harmless snapshots may advance the sequence while this menu stays open.
        var displayed = presentation!;
        var action = new WidgetActionEvent(option.ActionId, popup.Owner.Identity.Id, ControllerButton.A,
            ControllerEventPhase.Pressed, ++actionSequence, Environment.TickCount64 * 1000,
            InputScopeId: displayed.Scope) { FocusedElementId = popup.Owner.Identity.Id };
        DismissTransientControl();
        try { await DispatchCapturedActionAsync(displayed, action); }
        catch (WidgetPresentationSessionException error) when (IsRetiredInput(error)) { Session?.RequestInputRefresh(displayed.Frame); }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { ReportFailure(error, IsBindingCurrent(displayed)); }
    }
}
