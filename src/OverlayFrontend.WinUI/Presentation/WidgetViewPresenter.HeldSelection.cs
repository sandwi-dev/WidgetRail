using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private WidgetIndexedCollectionView? heldSelectionOwner;
    private bool heldSelectionPressed;
    private void CancelHeldCollectionSelection()
    {heldSelectionOwner?.CancelHeldSelection();heldSelectionOwner=null;heldSelectionPressed=false;}

    // The shell supplies controller availability and missing-release evidence;
    // this presenter never reads hardware or reconstructs driver button edges.
    internal void ReconcileHeldSelectionInput(bool available,bool releasedWithoutEdge)
    {
        if(!available)CancelHeldCollectionSelection();
        else if(releasedWithoutEdge&&heldSelectionPressed)
        {heldSelectionPressed=false;heldSelectionOwner?.ReleaseHeldSelection();}
        previewSurface?.Fragment?.ReconcileHeldSelectionInput(available,releasedWithoutEdge);
    }

    private bool TryHeldCollectionSelection(ControllerButton button,ControllerEventPhase phase)
    {
        if(button!=ControllerButton.LeftTrigger)return false;
        if(phase==ControllerEventPhase.Released && heldSelectionPressed && heldSelectionOwner is not null)
        {heldSelectionPressed=false;heldSelectionOwner.ReleaseHeldSelection();return true;}
        if(phase!=ControllerEventPhase.Pressed)return heldSelectionPressed && heldSelectionOwner is not null;
        if(FocusedBinding() is not {Element:WidgetIndexedCollectionView collection} focused || !Eligible(focused) ||
            !collection.CanStartHeldSelection ||
            !collection.SelectionActive && ControllerShortcutResolver.ResolvePath(ControllerFocusPath(true),button,phase).Status!=ControllerShortcutResolutionStatus.NoMatch)
        {CancelHeldCollectionSelection();return false;}
        if(!ReferenceEquals(heldSelectionOwner,collection))CancelHeldCollectionSelection();
        else if(heldSelectionPressed)return true;
        if(!collection.BeginHeldSelection())return false;
        heldSelectionOwner=collection;heldSelectionPressed=true;NotifyControllerGuideChanged();return true;
    }
}
