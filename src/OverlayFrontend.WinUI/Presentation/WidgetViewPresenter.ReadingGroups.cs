using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private bool readingGroupBackPressed;

    private bool HandleReadingGroupKey(KeyRoutedEventArgs args)
    {
        if (FindIndexedCollection()?.HandleReadingKey(args) == true) return true;
        if (StaticReadingGroup() is not { } group) return false;
        if (args.Key == Windows.System.VirtualKey.Escape && !ReferenceEquals(FocusedBinding(), group))
        {
            args.Handled = FocusHostTarget(group.Identity.Id);
            return args.Handled;
        }
        if (args.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space) || !CanEnterStaticReadingGroup) return false;
        args.Handled = true;
        if (!args.KeyStatus.WasKeyDown) EnterStaticReadingGroup();
        return true;
    }
    private Binding? StaticReadingGroup()
    {
        if(IndexedActionDispatch is not null || FocusedBinding() is not {} focused)return null;
        for(var current=declarations[focused.Identity.Id];current is not null;
            current=current.ParentId is {} parent?declarations.GetValueOrDefault(parent):null)
            if(current.Node.IsReadingGroup)return bindings[current.Node.Id];
        return null;
    }
    private bool CanEnterStaticReadingGroup=>StaticReadingGroup() is {} group && ReferenceEquals(FocusedBinding(),group) &&
        bindings.Values.Any(child=>IsDescendant(child.Identity.Id,group.Identity.Id) && Eligible(child) && ReadingGroupContentContract.IsInlineControl(declarations[child.Identity.Id].Node));
    private bool EnterStaticReadingGroup()
    {
        if(!CanEnterStaticReadingGroup || StaticReadingGroup() is not {} group)return false;
        foreach(var child in bindings.Values)
            if(IsDescendant(child.Identity.Id,group.Identity.Id) && ReadingGroupContentContract.IsInlineControl(declarations[child.Identity.Id].Node) && FocusHostTarget(child.Identity.Id))return true;
        return false;
    }
    private bool ReturnStaticReadingGroup(ControllerButton button,ControllerEventPhase phase)
    {
        if(button!=ControllerButton.B)return false;
        if(phase==ControllerEventPhase.Released && readingGroupBackPressed){readingGroupBackPressed=false;return true;}
        if(StaticReadingGroup() is not {} group || ReferenceEquals(FocusedBinding(),group))return false;
        if(phase==ControllerEventPhase.Pressed){readingGroupBackPressed=true;FocusHostTarget(group.Identity.Id);}
        return true;
    }
    internal bool IsReadingActionOrigin(DependencyObject? origin)
    {
        // A composed action can be hit on a declared Text/Icon child. Resolve
        // its semantic ancestors instead of mistaking that child for row paint.
        if (FindBinding(origin) is not { } binding) return false;
        for (var declaration = declarations.GetValueOrDefault(binding.Identity.Id); declaration is not null;
            declaration = declaration.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
        {
            if (ReadingGroupContentContract.IsInlineControl(declaration.Node)) return true;
            if (declaration.Node.IsReadingGroup) break;
        }
        return false;
    }
    internal ViewNode? FocusedReadingAction => IndexedActionDispatch is not null && FocusedBinding() is {} binding &&
        Eligible(binding) && declarations[binding.Identity.Id].Node is {} node && ReadingGroupContentContract.IsInlineControl(node) ? node : null;

    internal bool HasReadingActions => IndexedActionDispatch is not null && bindings.Values.Any(binding =>
        Eligible(binding) && binding.Element is Control {IsEnabled:true} && ReadingGroupContentContract.IsInlineControl(declarations[binding.Identity.Id].Node));

    private Binding? ReadingEntry(Binding? group)
    {
        bool Available(Binding child) => child.Element is Control { IsEnabled: true } && Eligible(child) &&
            (group is null || IsDescendant(child.Identity.Id, group.Identity.Id)) &&
            ReadingGroupContentContract.IsInlineControl(declarations[child.Identity.Id].Node);
        if (RememberedBinding() is { } last && Available(last)) return last;
        return bindings.Values.FirstOrDefault(child => declarations[child.Identity.Id].Node.IsSelected == true && Available(child)) ??
            bindings.Values.FirstOrDefault(Available);
    }
    internal FrameworkElement? ReadingEntryControl => ReadingEntry(null)?.Element;
    internal Control? SelectedReadingControl => bindings.Values.FirstOrDefault(child =>
        declarations[child.Identity.Id].Node is { IsSelected: true } node && ReadingGroupContentContract.IsInlineControl(node)
        && child.Element is Control { IsEnabled: true } && Eligible(child))?.Element as Control;

    internal void PreserveReadingFocusBeforePatch(ViewNode nextRoot)
    {
        if (FocusedReadingAction is not { } focused) return;
        ViewNode? selected = null;
        var survives = false;
        Inspect(nextRoot);
        if (!survives && selected is not null) FocusHostTarget(selected.Id);

        void Inspect(ViewNode node)
        {
            if (node.IsDisabled != true && node.IsBusy != true && ReadingGroupContentContract.IsInlineControl(node))
            {
                if (node.Id == focused.Id) survives = true;
                if (node.IsSelected == true) selected ??= node;
            }
            foreach (var child in node.Children) Inspect(child);
        }
    }

    internal bool EnterReadingActions()
    {
        if (!HasReadingActions) return false;
        return ReadingEntry(null) is { } target && FocusHostTarget(target.Identity.Id);
    }

    internal void SetReadingActionPressed(bool pressed)=>UpdateControllerPressedStyle(pressed?ControllerEventPhase.Pressed:ControllerEventPhase.Released);
    internal bool HandleReadingMedia(ControllerButton button, ControllerEventPhase phase) =>
        IndexedActionDispatch is not null && FocusedMediaPlayer?.Handle(button, phase) == true;
    internal IReadOnlyList<ControllerGuideHint>? ReadingMediaGuide =>
        IndexedActionDispatch is not null && FocusedMediaPlayer is not null ? CaptureControllerGuide() : null;
    internal (object Identity, bool Available)? ReadingMediaHeldAction(ControllerButton button) =>
        IndexedActionDispatch is not null && FocusedMediaPlayer is not null ? CaptureHeldAction(button) : null;
}
