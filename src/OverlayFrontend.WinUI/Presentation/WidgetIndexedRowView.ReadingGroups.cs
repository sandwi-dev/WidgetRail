using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

public sealed partial class WidgetIndexedRowView
{
    private void ReadingGuideChanged()=>selectionCollection?.PresentationChanged?.Invoke();
    internal ViewNode? FocusedReadingAction => readingGroup?presenter?.FocusedReadingAction:null;
    internal bool HasReadingActions => readingGroup && presenter?.HasReadingActions==true;
    internal FrameworkElement? ReadingEntryControl => readingGroup ? presenter?.ReadingEntryControl : null;
    internal Control? SelectedReadingControl => readingGroup ? presenter?.SelectedReadingControl : null;
    internal Control? FocusedReadingControl => FocusedReadingAction is not null && XamlRoot is not null
        ? FocusManager.GetFocusedElement(XamlRoot) as Control : null;
    internal bool IsActionRail => readingGroup &&
        appliedRow?.Item.Root.Children.Any(ReadingGroupContentContract.IsHorizontalViewport) == true;
    internal bool EnterReadingActions()=>readingGroup && presenter?.EnterReadingActions()==true;
    internal bool MoveReadingFocus(FocusNavigationDirection direction)=>FocusedReadingAction is not null && presenter?.MoveFocus(direction)==true;
    internal bool ReturnToReadingRow()=>FocusedReadingAction is not null && styleContainer?.Focus(FocusState.Keyboard)==true;
    internal void ActivateReadingAction(){if(FocusedReadingAction is not null)presenter!.ActivateFocused();}
    internal bool HandleReadingMedia(ControllerButton button, ControllerEventPhase phase) => readingGroup && presenter?.HandleReadingMedia(button,phase)==true;
    internal IReadOnlyList<ControllerGuideHint>? ReadingMediaGuide => readingGroup ? presenter?.ReadingMediaGuide : null;
    internal (object Identity, bool Available)? ReadingMediaHeldAction(ControllerButton button) => readingGroup ? presenter?.ReadingMediaHeldAction(button) : null;
    internal void SetReadingActionPressed(bool pressed)
    {
        if (readingGroup) presenter?.SetReadingActionPressed(pressed);
    }
    internal void RefreshReadingInteraction()
    {
        if(!readingGroup || presenter is null)return;
        var enabled=selectionCollection?.ReadingActionsEnabled==true;
        if(!enabled && selectionCollection?.SelectionActive==true)ReturnToReadingRow();
        presenter.IsEnabled=enabled;
        presenter.IsHitTestVisible=enabled;
    }
}
