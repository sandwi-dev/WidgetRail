using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableBrowserGuideValidation(string path)
    {
        BrowserSurface.FixturePage=url=>"<!doctype html><title>Guide "+new Uri(url).AbsolutePath+"</title><p>Local browser guide fixture</p>";
        Loaded+=async(_,_)=>
        {
            var checks=new List<string>();string? error=null;
            try
            {
                if(startup is not null)await startup;
                await WebAppUntil(()=>FindSlot(surface)?.Surface is {IsReady:true,IsNavigationInProgress:false});
                var slot=FindSlot(surface)!;var page=slot.Surface!;
                page.SetPresentation(true,true);page.Enter();
                var origin=new Uri(page.CurrentUrl!).GetLeftPart(UriPartial.Authority);
                bool Has(ControllerButton button)=>WidgetViewPresenter.CaptureBrowserGuide(slot).Any(h=>h.Button==button);
                void Check(bool passed,string name){if(!passed)throw new InvalidOperationException(name);checks.Add(name);}
                Check(Has(ControllerButton.Y)&&Has(ControllerButton.Menu),"Remote pages retain Address and Page actions");
                Check(Has(ControllerButton.LeftBumper)==page.CanNavigateBack&&Has(ControllerButton.RightBumper)==page.CanNavigateForward,
                    "Initial history hints match the active page's actual history");
                var notified=0;var stopHints=0;var prior=page.InteractionChanged;
                page.InteractionChanged=()=>{notified++;prior?.Invoke();if(page.IsNavigationInProgress&&WidgetViewPresenter.CaptureBrowserGuide(slot).Any(h=>h.Button==ControllerButton.X&&h.Label=="Stop"))stopHints++;};
                try
                {
                    page.NavigateForValidation(origin+"/guide-first");
                    await WebAppUntil(()=>page.CurrentTitle=="Guide /guide-first"&&!page.IsNavigationInProgress);
                    page.NavigateForValidation(origin+"/guide-second");
                    await WebAppUntil(()=>page.CurrentTitle=="Guide /guide-second"&&!page.IsNavigationInProgress);
                    Check(Has(ControllerButton.LeftBumper)&&!Has(ControllerButton.RightBumper),"After forward navigation only Previous is offered");
                    page.HandleButton(ControllerButton.LeftBumper,ControllerEventPhase.Pressed);
                    page.HandleButton(ControllerButton.LeftBumper,ControllerEventPhase.Released);
                    await WebAppUntil(()=>page.CurrentTitle=="Guide /guide-first"&&!page.IsNavigationInProgress);
                    Check(Has(ControllerButton.RightBumper),"Going back makes Forward available immediately");
                    page.HandleButton(ControllerButton.RightBumper,ControllerEventPhase.Pressed);
                    page.HandleButton(ControllerButton.RightBumper,ControllerEventPhase.Released);
                    await WebAppUntil(()=>page.CurrentTitle=="Guide /guide-second"&&!page.IsNavigationInProgress);
                    Check(!Has(ControllerButton.RightBumper),"Returning to the newest history entry removes Forward");
                    Check(notified>0&&stopHints>0&&WidgetViewPresenter.CaptureBrowserGuide(slot).Any(h=>h.Button==ControllerButton.X&&h.Label=="Reload"),
                        "Navigation publishes guide updates and changes Stop back to Reload");
                }
                finally{page.InteractionChanged=prior;}
            }
            catch(Exception failure){error=failure.ToString();}
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=error is null,checks,error}));
        };
    }
}
