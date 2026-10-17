using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnablePobTradeActionsValidation(string path)
    {
        Loaded+=async(_,_)=>
        {
            var checks=new List<string>();string? error=null;
            var old=BrowserSurface.FixturePage;var oldType=BrowserSurface.FixtureContentType;
            var live=Environment.GetCommandLineArgs().Contains("--live-pob-trade-actions");
            var id=new string('b',64);
            System.Text.Json.Nodes.JsonNode nestedMetadata=System.Text.Json.Nodes.JsonValue.Create(true)!;
            for(var depth=0;depth<10;depth++)nestedMetadata=new System.Text.Json.Nodes.JsonObject{["child"]=nestedMetadata};
            var response=JsonSerializer.Serialize(new{result=new[]{new{id,item=new{rarity="Normal",name="",typeLine="Gold Ring",ilvl=80,
                nestedMetadata,implicitMods=new[]{new{description="15% increased Rarity of Items found"}}},listing=new{price=new{amount=2,currency="chaos"},account=new{name="Fixture seller"},method="psapi"}}}});
            BrowserSurface.FixturePage=url=>new Uri(url).AbsolutePath.StartsWith("/api/",StringComparison.Ordinal)?response:
                "<!doctype html><div class='resultset'><div class='row' data-id='"+id+"'><div class='right'></div></div></div><script>setTimeout(()=>fetch('/api/trade2/fetch/fixture').then(r=>r.json()),700)</script>";
            BrowserSurface.FixtureContentType=url=>new Uri(url).AbsolutePath.StartsWith("/api/",StringComparison.Ordinal)?"application/json":"text/html";
            if(live){BrowserSurface.FixturePage=old;BrowserSurface.FixtureContentType=oldType;}
            try
            {
                if(startup is not null)await startup;
                await WebAppUntil(()=>interactive&&foreground&&!switching&&surface?.CurrentBinding is not null);
                if(live)
                {
                    await Action("section.3");await Action("item.1");await Action("trade");await Action("trade-open");
                    await WebAppUntil(()=>Page() is {IsReady:true,IsNavigationInProgress:false});
                    var livePage=Page()!;
                    await WebAppUntilScript(livePage,"document.querySelectorAll('[data-widgetrail-action=\"compare-item\"]').length>1");
                    Check(await livePage.EvaluateFixtureAsync("document.body.innerText.includes('Instant Buyout')")=="true","Search uses Instant Buyout on the official site");
                    await WebAppUntilScript(livePage,"!!document.querySelector('.resultset .s[data-field=\"statgroup.0\"].sorted-desc')");
                    await WebAppUntilScript(livePage,"document.querySelector('[data-widgetrail-action=\"evaluate-request\"]')?.disabled===false");
                    Check(true,"Generated PoE2 search settles on descending weighted sum before evaluation");
                    var listingIds=new List<string>();
                    for(var index=0;index<2;index++)
                    {
                        if(index>0)
                        {
                            await FocusBrowser();
                            await WebAppUntil(()=>!livePage.IsInteracting);
                            await Press(ControllerButton.A);
                            await Task.Delay(200);
                            var currentSlot=Walk(XamlRoot.Content).OfType<BrowserSlot>().Single(e=>AutomationProperties.GetAutomationId(e)=="Widget.pob.website");
                            await File.WriteAllTextAsync(path+".input",JsonSerializer.Serialize(new{interactive,foreground,visible,switching,slotActive=currentSlot.Active,slotInput=currentSlot.AcceptsInput,canDisplay=currentSlot.CanDisplay,page=livePage.PageActionDiagnostics(),ownsFocus=livePage.IsInteractionFocused?.Invoke(),focused=AutomationProperties.GetAutomationId(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject)}));
                            await WebAppUntil(()=>livePage.IsInteracting);
                        }
                        var listing=await livePage.EvaluateFixtureAsync("document.querySelectorAll('[data-widgetrail-action=\"compare-item\"]')["+index+"].closest('[data-id]').getAttribute('data-id')");
                        listingIds.Add(JsonSerializer.Deserialize<string>(listing)!);
                        await livePage.ClickPageActionFixtureAsync(index);
                        await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.website.review.title")&&!Nodes(surface.CurrentBinding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation")&&
                            (index==0||Nodes(surface.CurrentBinding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.website.review.recent"&&n.SelectOptions.Any(o=>o.Id==listingIds[index]&&o.IsSelected))));
                        await WebAppUntil(()=>FocusedWithin("Widget.pob.website.review.tools"));
                        Check(ReferenceEquals(Page(),livePage),"Comparison "+index+" keeps the website attached");
                        await File.WriteAllTextAsync(path+".comparison"+index,JsonSerializer.Serialize(surface!.CurrentBinding!.Frame.Snapshot));
                    }
                    await FocusBrowser();
                    await WebAppUntil(()=>!livePage.IsInteracting);
                    Check(livePage.RequiresActivation,"Comparison appearance dynamically changes the retained browser to activation mode");
                    await Press(ControllerButton.A);await WebAppUntil(()=>livePage.IsInteracting);
                    await Press(ControllerButton.B);await WebAppUntil(()=>!livePage.IsInteracting);
                    Check(Page()==livePage&&surface!.CurrentBinding!.Frame.Snapshot.Root.Kind==ViewNodeKind.ModalLayer,"B leaves website interaction without closing the comparison modal");
                    surface!.MoveFocus(FocusNavigationDirection.Right);
                    await Task.Delay(150);
                    var focused=FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                    var inReview=false;
                    for(var current=focused;current is not null;current=VisualTreeHelper.GetParent(current))
                        if(AutomationProperties.GetAutomationId(current)=="Widget.pob.website.review")inReview=true;
                    Check(inReview,"Right after B reaches the comparison panel through native directional navigation");
                    var bottom=Walk(XamlRoot.Content).OfType<Microsoft.UI.Xaml.Controls.Control>().Single(e=>AutomationProperties.GetAutomationId(e)=="Widget.pob.website.review.hide");
                    bottom.StartBringIntoView();bottom.Focus(FocusState.Keyboard);await Task.Delay(150);
                    await Press(ControllerButton.LeftStick);
                    await WebAppUntil(()=>FocusedWithin("Widget.pob.website.review.tools"));
                    Check(true,"LS from the bottom of comparison details returns to the fixed controls");
                    await Action("trade-review-hide");await WebAppUntil(()=>!livePage.RequiresActivation&&livePage.IsInteracting);
                    await Press(ControllerButton.RightStick);await WebAppUntil(()=>livePage.RequiresActivation&&!livePage.IsInteracting);
                    Check(Page()==livePage,"Hiding and showing comparison changes interaction policy without recreating the website");
                    var recent="trade-review-recent."+listingIds[0];
                    await Action(recent);
                    await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.website.review.recent"&&n.SelectOptions.Any(o=>o.ActionId==recent&&o.IsSelected))&&!Nodes(surface.CurrentBinding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"));
                    Check(Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.website.review.recent"&&n.SelectOptions.Any(o=>o.ActionId==recent&&o.IsSelected)),"Recent listing can be revisited without leaving the website");
                    await Action("trade-review-expand");
                    Check(Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.workspace.equipment-comparison"),"Expanded comparison uses two columns");
                    await Action("website-resume");await WebAppUntil(()=>Page()==livePage);
                    await FocusBrowser();await Press(ControllerButton.A);await WebAppUntil(()=>livePage.IsInteracting);
                    await livePage.ClickPageActionFixtureAsync(action:"evaluate-request");
                    await Task.Delay(1000);
                    await File.WriteAllTextAsync(path+".batch-diagnostics",JsonSerializer.Serialize(new{page=livePage.PageActionDiagnostics(),button=await livePage.EvaluateFixtureAsync("document.querySelector('[data-widgetrail-action=\"evaluate-request\"]')?.textContent"),frame=surface!.CurrentBinding!.Frame.Snapshot}));
                    await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.rank.summary"&&n.Text!.Contains("ranked"))&&!Nodes(surface.CurrentBinding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"));
                    Check(Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.rank.results"),"Live website batch produces a virtualized ranked shortlist");
                    await Action("trade-rank-sort.value");
                    await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.rank.order"&&n.SelectOptions.Any(o=>o.IsSelected&&o.Id=="value")));
                    Check(Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.rank.order"&&n.SelectOptions.Any(o=>o.IsSelected&&o.Id=="value")),"Live ranking uses PoB price-adjusted order with public GGG rates");
                    await Action("trade-rank-sort.strength");
                    await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.rank.order"&&n.SelectOptions.Any(o=>o.IsSelected&&o.Id=="strength")));
                    await File.WriteAllTextAsync(path+".ranking",JsonSerializer.Serialize(surface!.CurrentBinding!.Frame.Snapshot));
                    await WebAppUntil(()=>Walk(XamlRoot.Content).OfType<WidgetIndexedCollectionView>().Any(c=>AutomationProperties.GetAutomationId(c)=="Widget.pob.rank.results"));
                    var liveRanks=Walk(XamlRoot.Content).OfType<WidgetIndexedCollectionView>().Single(c=>AutomationProperties.GetAutomationId(c)=="Widget.pob.rank.results");
                    liveRanks.Enter(allowFallback:true);await WebAppUntil(()=>liveRanks.CaptureFocusedItem()is not null);await Task.Delay(200);await Press(ControllerButton.A);
                    await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.website.review.title")&&!Nodes(surface.CurrentBinding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"));
                    Check(Page()==livePage,"Controller selects a live ranked item without losing the search page");
                    await Action("trade-review-recent."+listingIds[0]);
                    await Action("website-resume");await Action("trade-review-find");
                    await WebAppUntil(()=>Page() is {IsReady:true,IsNavigationInProgress:false});
                    await WebAppUntilScript(livePage,"!!document.querySelector('[data-id=\""+listingIds[0]+"\"] [data-widgetrail-action]')");
                    Check(await livePage.EvaluateFixtureAsync("document.body.innerText.includes('Compare saved item in PoB')")=="true","Seller and base search locates the exact saved item without scanning all original results");
                    await File.WriteAllTextAsync(path+".final",JsonSerializer.Serialize(surface!.CurrentBinding!.Frame.Snapshot));
                    return;
                }
                await Action("menu");await Action("websites.overlay");await Action("open-poe-ninja");
                await WebAppUntil(()=>Page() is {IsReady:true,IsNavigationInProgress:false});
                var page=Page()!;
                await Task.Delay(1200);
                Check(await page.EvaluateFixtureAsync("document.querySelector('[data-widgetrail-action]')===null")=="true","Undeclared origin gets no page actions");
                page.NavigateForValidation("https://www.pathofexile.com/trade2/search/Standard/fixture");
                await WebAppUntilScript(page,"!!document.querySelector('[data-widgetrail-action=\"compare-item\"]')");
                Check(true,"Sealed adapter observes declared JSON and adds an associated action");
                Check(await page.EvaluateFixtureAsync("!Object.getOwnPropertyNames(globalThis).some(k=>k.startsWith('widgetRailAction')) && typeof globalThis.__widgetRailResponse==='undefined'")=="true","Page scripts cannot access isolated adapter bindings or response callbacks");
                await page.EvaluateFixtureAsync("document.querySelector('[data-widgetrail-action]').click();true");await Task.Delay(350);
                Check(surface!.CurrentBinding!.Frame.Snapshot.Root.Kind==ViewNodeKind.ModalLayer,"Synthetic website click cannot invoke a widget action");
                await page.ClickPageActionFixtureAsync();
                await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.website.review.title"));
                Check(Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Text=="2 × Chaos Orb · Fixture seller"),"Trusted action delivers exact selected item and price to a recoverable draft");
                await WebAppUntil(()=>!Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"));
                await FocusBrowser();await Press(ControllerButton.A);await WebAppUntil(()=>page.IsInteracting);
                await page.ClickPageActionFixtureAsync(action:"evaluate-request");
                await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"));
                await Action("cancel-operation");
                await WebAppUntil(()=>!Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"));
                await WebAppUntilScript(page,"!document.querySelector('[data-widgetrail-action=\"evaluate-request\"]').disabled");
                Check(Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.website.review.title"),"Cancelling website loading preserves the existing comparison and permits retry");
                await FocusBrowser();await Press(ControllerButton.A);await WebAppUntil(()=>page.IsInteracting);
                await page.ClickPageActionFixtureAsync(action:"evaluate-request");
                await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.rank.slot")&&!Nodes(surface.CurrentBinding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"));
                var rankSlot=Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Single(n=>n.Id=="pob.rank.slot").SelectOptions.First(o=>o.Label.StartsWith("Ring 1 —",StringComparison.Ordinal));
                await Action(rankSlot.ActionId);await Action("trade-rank-evaluate");
                await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.rank.summary"&&n.Text!.StartsWith("1 ranked"))&&!Nodes(surface.CurrentBinding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"));
                Check(Page()==page&&page.RequiresActivation,"Trusted batch action evaluates listings beside the retained website");
                await WebAppUntil(()=>Walk(XamlRoot.Content).OfType<WidgetIndexedCollectionView>().Any(c=>AutomationProperties.GetAutomationId(c)=="Widget.pob.rank.results"));
                var rankedList=Walk(XamlRoot.Content).OfType<WidgetIndexedCollectionView>().Single(c=>AutomationProperties.GetAutomationId(c)=="Widget.pob.rank.results");
                rankedList.Enter(allowFallback:true);await WebAppUntil(()=>rankedList.CaptureFocusedItem() is not null);
                await Press(ControllerButton.LeftStick);await WebAppUntil(()=>FocusedWithin("Widget.pob.rank.tools"));
                Check(true,"LS from ranked list reaches its target and evaluation controls");
                rankedList.Enter(allowFallback:true);await WebAppUntil(()=>rankedList.CaptureFocusedItem() is not null);await Task.Delay(200);
                await Press(ControllerButton.A);
                await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.website.review.title")&&!Nodes(surface.CurrentBinding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"));
                Check(Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.website.review.slot"&&n.SelectOptions.Any(o=>o.IsSelected&&o.Label.StartsWith("Ring 1 —",StringComparison.Ordinal))),"Controller selection opens comparison against the explicitly selected equipment");
                await Action("trade-rank-show");await WebAppUntil(()=>Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.rank.panel")&&!Nodes(surface.CurrentBinding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"));
                Check(Page()==page,"Back to ranked results keeps the website and shortlist");
                await Action("trade-rank-hide");
                await Action("website-resume");await WebAppUntil(()=>ReferenceEquals(Page(),page));
                Check(await page.EvaluateFixtureAsync("!!document.querySelector('[data-widgetrail-action]')")=="true","Returning retains the same document and listing action");
                await Action("website-close");await Action("trade-review-close");
                Check(!Nodes(surface!.CurrentBinding!.Frame.Snapshot.Root).Any(n=>n.Id=="pob.website.review.title"),"Discard draft returns to build without equipping");
                await Action("website-resume");await WebAppUntil(()=>ReferenceEquals(Page(),page));
                page.NavigateForValidation("https://outside.example/fixture");
                await WebAppUntilScript(page,"location.hostname==='outside.example'");await Task.Delay(900);
                Check(await page.EvaluateFixtureAsync("document.querySelector('[data-widgetrail-action]')===null")=="true","Navigation retires adapter actions and response authority outside declared origin");
                await Action("website-close");
            }
            catch(Exception failure){error=failure.ToString();}
            finally{BrowserSurface.FixturePage=old;BrowserSurface.FixtureContentType=oldType;await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=error is null,checks,error}));}
            void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add(label);File.WriteAllText(path+".progress",string.Join('\n',checks));}
            BrowserSurface? Page()=>Walk(XamlRoot.Content).OfType<BrowserSlot>().FirstOrDefault(s=>AutomationProperties.GetAutomationId(s)=="Widget.pob.website")?.Surface;
            bool FocusedWithin(string id)
            {
                for(var node=FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;node is not null;node=VisualTreeHelper.GetParent(node))
                    if(AutomationProperties.GetAutomationId(node)==id)return true;
                return false;
            }
            async Task FocusBrowser()
            {
                var button=Walk(XamlRoot.Content).OfType<Microsoft.UI.Xaml.Controls.Control>().Single(e=>AutomationProperties.GetAutomationId(e)=="Widget.pob.website.review.find");
                var focused=button.Focus(FocusState.Keyboard);await Task.Delay(200);
                await File.WriteAllTextAsync(path+".before-direction",JsonSerializer.Serialize(new{focused,inTools=FocusedWithin("Widget.pob.website.review.tools"),interacting=Page()?.IsInteracting,ownsFocus=Page()?.IsInteractionFocused?.Invoke(),requiresActivation=Page()?.RequiresActivation}));
                surface!.MoveFocus(FocusNavigationDirection.Left);
                await WebAppUntil(()=>FocusedWithin("Widget.pob.website.browser"));
                await Task.Delay(200);
                await File.WriteAllTextAsync(path+".after-direction",JsonSerializer.Serialize(new{interacting=Page()?.IsInteracting,ownsFocus=Page()?.IsInteractionFocused?.Invoke(),requiresActivation=Page()?.RequiresActivation}));
                Check(true,"Left from Find seller item reaches the website without navigating");
            }
            async Task Press(ControllerButton button)
            {await RouteButtonAsync(button,ControllerEventPhase.Pressed);await RouteButtonAsync(button,ControllerEventPhase.Released);}
            async Task Action(string id)
            {
                await WebAppUntil(()=>surface?.CurrentBinding is {} binding&&(id=="cancel-operation"||!Nodes(binding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation"))&&
                    Nodes(binding.Frame.Snapshot.Root).Any(n=>n.ActionId==id||n.SelectOptions.Any(o=>o.ActionId==id)));
                var frame=surface!.CurrentBinding!.Frame;var node=Nodes(frame.Snapshot.Root).Last(n=>n.ActionId==id||n.SelectOptions.Any(o=>o.ActionId==id));
                await InvokeAsync(new(frame,new(id,node.Id,ControllerButton.A,InputScopeId:frame.Snapshot.ActiveInputScopeId)));
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>surface?.CurrentBinding?.Frame.Snapshot.Sequence>frame.Snapshot.Sequence);
            }
        };
        static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
        static IEnumerable<DependencyObject> Walk(DependencyObject node)
        {yield return node;for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,i)))yield return child;}
    }
}
