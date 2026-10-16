using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnablePobSkillEditsValidation(string path)
    {
        Loaded+=async (_,_)=>
        {
            var checks=new List<string>();string? error=null;
            try
            {
                if(startup is not null)await startup;
                await WebAppUntil(()=>surface is not null&&FindControl<Button>(surface,"Widget.pob.tab.2") is not null&&interactive&&foreground);
                Invoke("Widget.pob.tab.2");
                await WebAppUntil(()=>FindControl<Button>(surface!,"Widget.pob.skill.3") is not null);
                foreach(var group in new[]{3,4})foreach(var level in new[]{1,2,3,15})
                {
                    Invoke("Widget.pob.skill."+group);
                    await Task.Delay(150);
                    var groupName=FindControl<TextBlock>(surface!,"Widget.pob.skill.name")!.Text;
                    var field=FindControl<Button>(surface!,"Widget.pob.gem.1level")!;
                    field.Focus(FocusState.Keyboard);field.StartBringIntoView();
                    await Task.Delay(150);
                    await Button(ControllerButton.A);
                    await WebAppUntil(()=>ActiveControllerKeyboard?.Content is DependencyObject content&&FindControl<Button>(content,"Widget.TextEntry.Key.0")?.IsLoaded==true);
                    await Task.Delay(350); // Let the popup's initial-focus presentation finish before controller navigation.
                    await Button(ControllerButton.DPadUp);
                    await Button(ControllerButton.Y);
                    foreach(var digit in level.ToString())
                    {
                        var key=FindControl<Button>((DependencyObject)ActiveControllerKeyboard!.Content,"Widget.TextEntry.Key."+(digit=='0'?9:digit-'1'))!;
                        key.Focus(FocusState.Keyboard);
                        await Button(ControllerButton.A);
                    }
                    var typed=FindControl<TextBox>((DependencyObject)ActiveControllerKeyboard!.Content,"Widget.TextEntry.Editor")!.Text;
                    if(typed!=level.ToString())throw new Exception($"Keyboard has '{typed}', expected {level}");
                    var trigger=ControllerFrame.Create();trigger.Connected=1;Receive(trigger);
                    trigger.RightTriggerPressed=1;trigger.State.RightTrigger=255;Receive(trigger);
                    trigger.RightTriggerPressed=0;trigger.State.RightTrigger=0;trigger.RightTriggerReleased=1;Receive(trigger);
                    await WebAppUntil(()=>ActiveControllerKeyboard is null);
                    await Task.Delay(1000);
                    await WebAppUntil(()=>FindControl<Button>(surface!,"Widget.pob.cancel-operation") is null);
                    var current=FindControl<Button>(surface!,"Widget.pob.gem.1level")!;
                    var value=((IValueProvider)FrameworkElementAutomationPeer.CreatePeerForElement(current).GetPattern(PatternInterface.Value)).Value;
                    var name=FindControl<TextBlock>(surface!,"Widget.pob.skill.name")!.Text;
                    if(value!=level.ToString()||name!=groupName)throw new Exception($"After R2: level {value}, group '{name}'; expected {level}, '{groupName}'");
                    checks.Add($"Controller keyboard and R2 save group {group} level {level} and preserve selection");
                }
                Invoke("Widget.pob.group-create");
                await WebAppUntil(()=>FindControl<Button>(surface!,"Widget.pob.gem-add") is not null&&FindControl<Button>(surface!,"Widget.pob.cancel-operation") is null);
                checks.Add("Creating a group opens its label and gem controls");
                Invoke("Widget.pob.gem-add");
                await WebAppUntil(()=>FindControl<WidgetIndexedCollectionView>(surface!,"Widget.pob.gems") is not null&&FindControl<Button>(surface!,"Widget.pob.cancel-operation") is null);
                var collection=FindControl<WidgetIndexedCollectionView>(surface!,"Widget.pob.gems")!;
                collection.Enter(allowFallback:true);
                await WebAppUntil(()=>collection.CaptureFocusedItem() is not null);
                await Button(ControllerButton.LeftStick);
                await WebAppUntil(()=>Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused&&AutomationProperties.GetAutomationId(focused)=="Widget.pob.gem-search");
                await Button(ControllerButton.LeftStick);
                await WebAppUntil(()=>collection.CaptureFocusedItem() is not null);
                await Button(ControllerButton.A);
                await WebAppUntil(()=>FindControl<Button>(surface!,"Widget.pob.gem.1level") is not null&&FindControl<Button>(surface!,"Widget.pob.cancel-operation") is null);
                checks.Add("Controller chooses a virtualized gem; L3 reaches Search and returns to results");
                Invoke("Widget.pob.back");
                await WebAppUntil(()=>FindControl<TextBlock>(surface!,"Widget.pob.skill.title.10") is not null);
                Button? spear=null;
                for(var i=1;i<256;i++)if(FindControl<Button>(surface!,"Widget.pob.skill."+i) is {} row&&FindControl<TextBlock>(surface!,"Widget.pob.skill.title."+i)?.Text=="Spear Throw")spear=row;
                if(spear is null)throw new Exception("Fixture must contain the reported granted Spear Throw");
                ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(spear).GetPattern(PatternInterface.Invoke)).Invoke();
                await WebAppUntil(()=>FindControl<TextBlock>(surface!,"Widget.pob.gem.source.1") is not null);
                if(FindControl<Button>(surface!,"Widget.pob.gem.1level") is not null)throw new Exception("Granted Spear Throw still exposes an editable level");
                checks.Add("Spear Throw shows a source explanation and read-only level");
                async Task Button(ControllerButton button){await RouteButtonAsync(button,ControllerEventPhase.Pressed);await RouteButtonAsync(button,ControllerEventPhase.Released);}
                void Invoke(string id){var button=FindControl<Button>(surface!,id)!;((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Invoke)).Invoke();}
            }
            catch(Exception failure){error=failure.ToString();}
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=error is null,checks,error}));
        };
    }
}
