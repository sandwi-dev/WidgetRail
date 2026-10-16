using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Run against a disposable profile containing long Notes and at least one link.
    internal void EnablePobNotesValidation(string path)
    {
        Loaded+=async (_,_)=>
        {
            var checks=new List<string>();string? error=null;
            try
            {
                if(startup is not null)await startup;
                await WebAppUntil(()=>surface is not null&&FindControl<Button>(surface,"Widget.pob.tab.5") is not null&&interactive&&foreground);
                var tab=FindControl<Button>(surface!,"Widget.pob.tab.5")!;
                ((IInvokeProvider)new ButtonAutomationPeer(tab).GetPattern(PatternInterface.Invoke)).Invoke();
                await WebAppUntil(()=>FindControl<ScrollViewer>(surface!,"Widget.pob.notes.scroll") is not null&&FindControl<Button>(surface!,"Widget.pob.cancel-operation") is null);
                await Task.Delay(250);
                var scroll=FindControl<ScrollViewer>(surface!,"Widget.pob.notes.scroll")!;
                var link=FindControl<Button>(surface!,"Widget.pob.note-link.0")??throw new Exception("Fixture needs a Notes link");
                link.Focus(FocusState.Keyboard);
                await WebAppUntil(()=>Focused("Widget.pob.note-link.0"));
                await Jump(scroll);
                checks.Add("L3 from a Notes link focuses the Notes navigation button without scrolling");
                scroll.ChangeView(null,scroll.ScrollableHeight,null,true);
                await WebAppUntil(()=>scroll.VerticalOffset>scroll.ViewportHeight);
                scroll.Focus(FocusState.Keyboard);
                await WebAppUntil(()=>Focused("Widget.pob.notes.scroll"));
                await Task.Delay(250);
                await Jump(scroll);
                checks.Add("L3 from deep reading content focuses the Notes navigation button and preserves its viewport");
                tab=FindControl<Button>(surface!,"Widget.pob.tab.5")!;
                var peer=FindControl<Button>(surface!,"Widget.pob.tab.3")!;
                if(Math.Abs(tab.ActualWidth-peer.ActualWidth)>1||Math.Abs(tab.ActualHeight-peer.ActualHeight)>1)
                    throw new Exception($"Notes tab size {tab.ActualWidth}x{tab.ActualHeight} differs from peer {peer.ActualWidth}x{peer.ActualHeight}");
                checks.Add("Notes navigation button retains the same dimensions as its peer tabs");
                async Task Jump(ScrollViewer area)
                {
                    var offset=area.VerticalOffset;
                    await RouteButtonAsync(ControllerButton.LeftStick,ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.LeftStick,ControllerEventPhase.Released);
                    await WebAppUntil(()=>Focused("Widget.pob.tab.5"));
                    await Task.Delay(200);
                    if(Math.Abs(area.VerticalOffset-offset)>.5)throw new Exception("L3 moved the Notes viewport");
                }
                bool Focused(string id)
                {
                    for(var item=FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;item is not null;item=VisualTreeHelper.GetParent(item))
                        if(AutomationProperties.GetAutomationId(item)==id)return true;
                    return false;
                }
            }
            catch(Exception failure){error=failure.ToString();}
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=error is null,checks,error}));
        };
    }
}
