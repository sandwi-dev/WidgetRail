using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnablePobQuickControlsValidation(string path)
    {
        Loaded+=async (_,_)=>
        {
            var checks=new List<string>();string? error=null;
            try
            {
                if(startup is not null)await startup;
                await WebAppUntil(()=>surface is not null&&FindControl<Button>(surface,"Widget.pob.tab.1") is not null&&interactive&&foreground);
                Invoke("Widget.pob.tab.1");
                await WebAppUntil(()=>FindControl<WidgetIndexedCollectionView>(surface!,"Widget.pob.passives") is not null);
                await WebAppUntil(()=>FindSlot(surface)?.Surface is {IsReady:true,IsNavigationInProgress:false} &&
                    FindControl<Button>(surface!,"Widget.pob.cancel-operation") is null);
                await Task.Delay(250); // Finish the opening operation's final presentation before shortcut input.
                var collection=FindControl<WidgetIndexedCollectionView>(surface!,"Widget.pob.passives")!;
                collection.Enter(allowFallback:true);
                await WebAppUntil(()=>collection.CaptureFocusedItem() is not null);
                for(var i=0;i<18;i++)
                {
                    var old=collection.CaptureFocusedItem()!.Index;
                    surface!.MoveFocus(FocusNavigationDirection.Down);
                    await WebAppUntil(()=>collection.CaptureFocusedItem()?.Index==old+1);
                }
                var remembered=collection.CaptureFocusedItem()!;
                var scroll=Child<ScrollViewer>(collection)!;
                await Task.Delay(150);var offset=scroll.VerticalOffset;
                if(offset<=0)throw new Exception("Fixture did not scroll its long list");
                await L3();await WebAppUntil(()=>Focused("Widget.pob.search"));
                if(Math.Abs(scroll.VerticalOffset-offset)>.5)throw new Exception("Jump to search moved the list viewport");
                checks.Add("L3 reaches Search from a deep passive row without moving the list viewport");
                await L3();await WebAppUntil(()=>collection.CaptureFocusedItem()?.Index==remembered.Index);
                if(Math.Abs(scroll.VerticalOffset-offset)>.5)throw new Exception("Returning to the row moved its viewport");
                checks.Add("L3 returns to the remembered virtualized row and exact scroll position");
                await R3();await WebAppUntil(()=>Focused("Widget.pob.loadout")||Focused("Widget.pob.menu"));
                if(Math.Abs(scroll.VerticalOffset-offset)>.5)throw new Exception("R3 to build controls moved the passive viewport");
                await R3();await WebAppUntil(()=>collection.CaptureFocusedItem()?.Index==remembered.Index&&Focused("Widget.pob.passives"));
                if(Math.Abs(scroll.VerticalOffset-offset)>.5)throw new Exception("R3 return moved the passive viewport");
                checks.Add("R3 reaches build controls and restores the deep virtualized passive row without scrolling");
                var map=FindSlot(surface)!;map.Enter();
                await WebAppUntil(()=>map.IsBrowsing);
                if(!surface!.CaptureControllerGuide().Any(h=>h.Button==ControllerButton.RightStick&&h.Label=="Build controls"))throw new Exception("Tree focus omitted the R3 build-controls hint");
                await R3();await WebAppUntil(()=>Focused("Widget.pob.loadout")||Focused("Widget.pob.menu"));
                await R3();await WebAppUntil(()=>Focused("Widget.pob.map"));
                checks.Add("Tree WebView passes R3 to its ancestor and shows the matching hint");
                Invoke("Widget.pob.tab.4");
                await WebAppUntil(()=>FindControl<Button>(surface!,"Widget.pob.config.category.0") is not null);
                var count=0;while(FindControl<Button>(surface!,"Widget.pob.config.category."+count)is not null)count++;
                var category="Widget.pob.config.category."+Math.Min(10,count-1);
                FindControl<Button>(surface!,category)!.Focus(FocusState.Keyboard);
                await Task.Delay(150);
                var categories=FindControl<ScrollViewer>(surface!,"Widget.pob.config.categories")!;
                var categoryOffset=categories.VerticalOffset;
                if(count>8)
                {
                await L3();await WebAppUntil(()=>Focused("Widget.pob.search"));
                await L3();await WebAppUntil(()=>Focused("Widget.pob.config.help"));
                await L3();await WebAppUntil(()=>Focused(category));
                if(Math.Abs(categories.VerticalOffset-categoryOffset)>.5)throw new Exception("Configuration cycle moved the section list");
                checks.Add("L3 cycles Search/set, explanations and the remembered section without scrolling its list");
                }
                else
                {
                    await L3();await Task.Delay(100);
                    if(!Focused(category))throw new Exception("A short configuration list acquired an unnecessary shortcut");
                    checks.Add("Short configuration lists retain ordinary navigation without an L3 jump");
                }
                double? compactTabsY=null;
                foreach(var (tab,target,large) in new[]{("Widget.pob.tab.2","Widget.pob.skill.1",false),("Widget.pob.tab.3","Widget.pob.item.1",false),("Widget.pob.section","Widget.pob.headline.2",true)})
                {
                    Invoke(tab);
                    await WebAppUntil(()=>FindControl<Button>(surface!,target) is not null&&FindControl<Button>(surface!,"Widget.pob.cancel-operation") is null&&
                        (FindControl<TextBlock>(surface!,"Widget.pob.headline.value.0") is not null)==large);
                    await Task.Delay(350);
                    var control=FindControl<Button>(surface!,target)!;control.Focus(FocusState.Keyboard);control.StartBringIntoView();
                    await Task.Delay(150);
                    if(!large)
                    {
                        var y=FindControl<Button>(surface!,tab)!.TransformToVisual(surface).TransformPoint(default).Y;
                        if(compactTabsY is {} oldY&&Math.Abs(y-oldY)>1)throw new Exception("Skills and Items tabs shifted despite identical compact summaries");
                        compactTabsY=y;
                    }
                    await R3();await WebAppUntil(()=>Focused("Widget.pob.loadout")||Focused("Widget.pob.menu"));
                    await R3();await WebAppUntil(()=>Focused(target));
                    checks.Add(tab+" has the correct summary and R3 restores "+target);
                }
                Invoke("Widget.pob.tab.2");
                await WebAppUntil(()=>FindControl<Button>(surface!,"Widget.pob.gem.1level") is not null);
                FindControl<Button>(surface!,"Widget.pob.gem.1level")!.Focus(FocusState.Keyboard);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>ActiveControllerKeyboard is not null);
                var keyboard=ActiveControllerKeyboard;await R3();
                if(!ReferenceEquals(keyboard,ActiveControllerKeyboard))throw new Exception("R3 escaped the controller keyboard");
                await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Pressed);await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Released);
                checks.Add("R3 remains owned by the open keyboard");
                async Task R3(){await RouteButtonAsync(ControllerButton.RightStick,ControllerEventPhase.Pressed);await RouteButtonAsync(ControllerButton.RightStick,ControllerEventPhase.Released);}
                async Task L3(){await RouteButtonAsync(ControllerButton.LeftStick,ControllerEventPhase.Pressed);await RouteButtonAsync(ControllerButton.LeftStick,ControllerEventPhase.Released);}
                bool Focused(string id){for(var item=FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;item is not null;item=VisualTreeHelper.GetParent(item))if(AutomationProperties.GetAutomationId(item)==id)return true;return false;}
                void Invoke(string id){var button=FindControl<Button>(surface!,id)!;((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();}
            }
            catch(Exception failure){error=failure.ToString();}
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=error is null,checks,error}));
        };
        static T? Child<T>(DependencyObject root)where T:DependencyObject
        {if(root is T found)return found;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(Child<T>(VisualTreeHelper.GetChild(root,i))is {} child)return child;return null;}
    }
}
