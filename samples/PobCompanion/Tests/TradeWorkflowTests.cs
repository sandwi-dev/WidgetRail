using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class TradeWorkflowTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await TradeItemTests.RunAsync(game,root,runtime,adapter,xml,check);
        var directory=Path.Combine(Path.GetTempPath(),"widgetrail-trade-workflow-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);var id=Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(directory,id+".xml"),xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        await using var engine=new PobEngine(root,runtime,adapter);
        string clipboard="Rarity: NORMAL\nGold Ring\nImplicits: 1\n15% increased Rarity of Items found"; IOException? clipboardFailure=null;
        var widget=new PobWidget(game,engine,directory,()=>clipboardFailure is {} failure?throw failure:clipboard){TradeLeagueLoader=(_,_)=>Task.FromResult(new[]{"Standard","Hardcore"})};
        var opened=new List<WebSessionDefinition>();var closed=0;var navigated=0;
        var incoming=new System.Collections.Concurrent.ConcurrentQueue<string>();
        WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().WithHandler(WidgetWebSessionCapabilities.Command,async(command,token)=>
        {
            if(command.Operation is WebSessionOperation.Open or WebSessionOperation.OpenAdapted){opened.Add(command.Definition!);return new WebSessionReply{Session=new(new string('a',32))};}
            if(command.Operation==WebSessionOperation.ReadMessages)
            {await Task.Delay(50,token);if(incoming.TryDequeue(out var message))return new(){MessagesBase64=[message]};}
            if(command.Operation==WebSessionOperation.Close)closed++;
            if(command.Operation==WebSessionOperation.Navigate)navigated++;
            return new WebSessionReply();
        }).Build());var sequence=0;
        IEnumerable<ViewNode> Nodes(ViewNode n)=>new[]{n}.Concat((n.Children??[]).SelectMany(Nodes));
        ViewSnapshot View(){try{return widget.Render().CreateSnapshot("trade-workflow",++sequence);}catch(ProtocolValidationException e){throw new InvalidOperationException(JsonSerializer.Serialize(e.Errors),e);}}
        async Task Idle(){var deadline=DateTime.UtcNow.AddSeconds(65);while(Nodes(View().Root).Any(n=>n.Id=="pob.cancel-operation")){if(DateTime.UtcNow>deadline)throw new TimeoutException();await Task.Delay(20);}}
        async Task Action(string action,string? value=null){await widget.OnActionAsync(new(action,"trade-workflow"){CommittedText=value});await Idle();}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
            var baseline=await engine.ExecuteAsync(new{op="snapshot"},default);
            var helmet=baseline.State.GetProperty("items").EnumerateArray().First(i=>i.GetProperty("slot").GetString()=="Helmet").GetProperty("id").GetInt32();
            await Action("section.3");await Action("item."+helmet);
            var itemActions=Nodes(View().Root).ToArray();
            check(itemActions.Single(n=>n.Id=="pob.item-paste").IsDisabled!=true&&itemActions.Single(n=>n.Id=="pob.item-manage").Focus?.Left=="pob.item-paste"&&itemActions.Single(n=>n.Id=="pob.item.tools").Kind==ViewNodeKind.Grid,"Enabled clipboard action is linked from Item settings and actions fit a two-column grid");
            foreach(var sample in new[]{("", "The clipboard has no item text."),("https://pobb.in/example", "PoB could not recognize an item"),(new string('x',32769),"The copied text is too long")})
            {
                clipboard=sample.Item1;await Action("item-paste");
                check(Nodes(View().Root).Any(n=>n.Text?.StartsWith(sample.Item2,StringComparison.Ordinal)==true)&&PobWorkStore.Read(directory,id,game)?.ItemDraft is null,"Clipboard rejection explains the actual cause: "+sample.Item2);
            }
            foreach(var failure in new IOException[]{new ClipboardTextUnavailableException(),new IOException("Clipboard is busy. Try Paste again.")})
            {
                clipboardFailure=failure;await Action("item-paste");
                check(Nodes(View().Root).Any(n=>n.Text?.StartsWith(failure is ClipboardTextUnavailableException?"The clipboard has no item text.":"Clipboard is busy.",StringComparison.Ordinal)==true),"Clipboard access errors retain accurate context: "+failure.GetType().Name);
            }
            clipboardFailure=null;clipboard="Rarity: NORMAL\nGold Ring\nImplicits: 1\n15% increased Rarity of Items found";
            await Action("trade");
            var search=Nodes(View().Root).Single(n=>n.Id=="pob.trade-open");
            var pasteHelp=Nodes(View().Root).Single(n=>n.Id=="pob.trade.paste-help").Text!;
            check(pasteHelp.Contains("Ctrl+C")&&(game==2?pasteHelp.Contains("no built-in Copy item button"):pasteHelp.Contains("On the trade site, use Copy item")),"Item-copy instructions distinguish PoE2 website limitations from PoE1");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.trade-mode").SelectOptions.Any(o=>o.Id=="securable"&&o.IsSelected),"Trade defaults to Instant buyout with explicit alternatives");
            check(search.IsDisabled!=true&&search.Intent?.PreparationKey?.StartsWith("pob.trade-search.",StringComparison.Ordinal)==true&&!Nodes(View().Root).Any(n=>n.Id=="pob.trade-build"),"Trade offers one enabled Search action that prepares its URL on invocation");
            check(search.Intent!.Routing==WidgetIntentRouting.Windows&&search.Intent.Version==2,"Search respects the default browser and declares support for long generated URLs");
            await Action("trade-priority.life");await Action("trade-budget","not a price");
            var invalidRequest=Nodes(View().Root).Single(n=>n.Id=="pob.trade-open").Intent!;
            check(await widget.OnPrepareIntentAsync(search.Intent.PreparationKey!,search.Intent) is null,"Changed search settings invalidate the previous preparation key");
            var invalid=await widget.OnPrepareIntentAsync(invalidRequest.PreparationKey!,invalidRequest);
            check(invalid is null&&Nodes(View().Root).Any(n=>n.Text=="Search not opened"),"Invalid price reports feedback without creating a browser destination");
            await Action("trade-budget","10");
            var saved=File.ReadAllText(Path.Combine(directory,id+".xml"));
            var before=View();var request=Nodes(before.Root).Single(n=>n.Id=="pob.trade-open").Intent!;
            var pending=widget.OnPrepareIntentAsync(request.PreparationKey!,request).AsTask();
            var during=View();
            check(Nodes(during.Root).Any(n=>n.Id=="pob.cancel-operation")&&request.Matches(Nodes(during.Root).Single(n=>n.Id=="pob.trade-open").Intent)&&before.ActiveInputScopeId==during.ActiveInputScopeId,"Search shows cancellable progress while retaining the original intent authority");
            var payload=await pending;
            check(payload is {} value&&value.GetProperty("url").GetString()!.StartsWith($"https://www.pathofexile.com/trade{(game==2?"2":"")}/search/Standard",StringComparison.Ordinal),"One search invocation returns the calculated official-site URL");
            check(File.ReadAllText(Path.Combine(directory,id+".xml"))==saved&&!Nodes(View().Root).Any(n=>n.Id=="pob.cancel-operation"),"Search leaves saved build contents unchanged and clears progress");
            await Action("websites.overlay");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.trade-open").Intent is null,"Overlay trade is a widget action without an intent handoff");
            await Action("trade-open");
            var modal=View();var web=Nodes(modal.Root).Single(n=>n.Id=="pob.website");
            check(modal.Root.Kind==ViewNodeKind.ModalLayer&&web.WebBrowser?.HostedSession?.Id==new string('a',32)&&ViewSnapshotValidator.Validate(modal).Count==0,"Calculated search attaches the existing web session to a valid widget modal");
            check(opened.Count==1&&opened[0].Browser.Storage==BrowserStorage.Persistent&&opened[0].Browser.ProfileKey=="websites"&&opened[0].Browser.Background==BrowserBackgroundMode.Automatic,"Website uses a stable widget-specific persistent profile and automatic background behavior");
            await Action("website-close");
            check(View().Root.Kind!=ViewNodeKind.ModalLayer&&closed==0,"Closing the modal retains the web session and returns to the build");
            await Action("website-resume");
            check(View().Root.Kind==ViewNodeKind.ModalLayer&&opened.Count==1&&navigated==0,"Reopening reuses the same session without navigating away from the selected listing");
            check(opened[0].PageAdapter is { } pageAdapter&&pageAdapter.IsWellFormed()&&pageAdapter.Responses.Single().PathPrefix==(game==2?"/api/trade2/fetch/":"/api/trade/fetch/"),"Trade adapter declares only the current game's response endpoint");
            incoming.Enqueue(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new{kind="page-action",action="compare-item",payload=new{url=$"https://www.pathofexile.com/trade{(game==2?"2":"")}/search/Standard/test",entry=new{}}})));
            var invalidUntil=DateTime.UtcNow.AddSeconds(5);
            while(!Nodes(View().Root).Any(n=>n.Text=="The website returned incomplete item data. Reload the search."))
            {if(DateTime.UtcNow>invalidUntil)throw new TimeoutException("Malformed listing did not report feedback.");await Task.Delay(30);}
            check(File.ReadAllText(Path.Combine(directory,id+".xml"))==saved,"Malformed selected-item response shows feedback without changing the build");
            await Action("website-resume");
            var entry=new {id=new string('b',64),item=new{rarity="Normal",name="",typeLine="Gold Ring",ilvl=80,
                implicitMods=new[]{new{description="15% increased Rarity of Items found"}}},listing=new{price=new{amount=2,currency="chaos"},account=new{name="Test seller"},method="psapi"}};
            var unsupported=JsonSerializer.SerializeToNode(entry)!;
            unsupported["item"]!["veiledMods"]=new System.Text.Json.Nodes.JsonArray("Suffix01");
            var rejected=false;
            try{await engine.ExecuteAsync(new{op="trade-item",entry=unsupported},default,baseline.Xml);}
            catch(PobCommandException failure){rejected=failure.Message.Contains("unrevealed modifiers",StringComparison.Ordinal);}
            check(rejected&&File.ReadAllText(Path.Combine(directory,id+".xml"))==saved,"Unrevealed modifiers produce a clear refusal without changing saved work");
            check(new PobTradeListing(new string('b',64),$"https://www.pathofexile.com/trade{(game==2?"2":"")}/search/Standard/test","2 × Chaos Orb","Test seller","psapi",null,BaseType:"Gold Ring").FindUrl(game).Contains("#widgetrail-listing="),"Saved listing search carries its exact identity without purchase tokens");
            var eventJson=JsonSerializer.SerializeToUtf8Bytes(new{kind="page-action",action="compare-item",payload=new{url=$"https://www.pathofexile.com/trade{(game==2?"2":"")}/search/Standard/test",entry}});
            incoming.Enqueue(Convert.ToBase64String(eventJson));
            async Task WaitComparison(string expectedId)
            {
                var until=DateTime.UtcNow.AddSeconds(30);
                while(PobWorkStore.Read(directory,id,game)?.TradeListing?.Id!=expectedId||!Nodes(View().Root).Any(n=>n.Id=="pob.website.review.title"))
                {if(DateTime.UtcNow>until)throw new TimeoutException("Trade comparison did not appear: "+string.Join(" | ",Nodes(View().Root).Select(n=>n.Text)));await Task.Delay(30);}
                await Idle();
            }
            await WaitComparison(entry.id);
            var work=PobWorkStore.Read(directory,id,game)!;
            check(work.ItemDraft?.Contains("Gold Ring")==true&&work.TradeListing?.Price=="2 × Chaos Orb"&&work.Pending?.Slot=="Ring 1"&&work.TradeBrowse&&File.ReadAllText(Path.Combine(directory,id+".xml"))==saved,"Compare calculates immediately against an explicit slot and persists recovery without equipping");
            check(View().Root.Kind==ViewNodeKind.ModalLayer&&Nodes(View().Root).Any(n=>n.Id=="pob.website")&&ViewSnapshotValidator.Validate(View()).Count==0,"Comparison remains beside the same browser in a valid modal snapshot");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.website").WebBrowser?.InteractionMode==BrowserInteractionMode.ActivateToInteract,"Split website requires A to enter and supports B to leave interaction");
            check(View().ScrollRevealRequest is {ScrollId:"pob.website.review.details",TargetId:"pob.website.review.skill"},"A newly compared item starts at its impact instead of retaining the previous item's scroll position");
            var details=Nodes(View().Root).Single(n=>n.Id=="pob.website.review.details");var ordered=Nodes(details).ToArray();
            check(Array.FindIndex(ordered,n=>n.Id=="pob.website.review.stats.heading")<Array.FindIndex(ordered,n=>n.Id=="pob.website.review.item.heading")&&Array.FindIndex(ordered,n=>n.Id=="pob.website.review.item.heading")<Array.FindIndex(ordered,n=>n.Id=="pob.website.review.purchase.heading"),"Comparison reading order is impact, item description, then purchase details");
            await Action("trade-review-hide");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.website").WebBrowser?.InteractionMode==BrowserInteractionMode.InteractOnFocus&&opened.Count==1,"Hiding the comparison restores automatic browsing on the same session");
            await Action("trade-review-show");
            var originalComparison=work.Pending!.Xml;
            await Action("trade-review-slot.1");
            originalComparison=PobWorkStore.Read(directory,id,game)!.Pending!.Xml;
            check(PobWorkStore.Read(directory,id,game)?.Pending?.Slot=="Ring 2","Slot dropdown recalculates without a separate selection page");
            var second=JsonSerializer.SerializeToNode(entry)!;second["id"]=new string('c',64);second["item"]!["implicitMods"]![0]!["description"]="20% increased Rarity of Items found";second["listing"]!["price"]!["amount"]=3;
            incoming.Enqueue(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new{kind="page-action",action="compare-item",payload=new{url=$"https://www.pathofexile.com/trade{(game==2?"2":"")}/search/Standard/test",entry=second}})));
            await WaitComparison(new string('c',64));
            work=PobWorkStore.Read(directory,id,game)!;
            check(work.TradeCandidates?.Length==2&&work.ItemDraft!.Contains("20% increased")&&WorkStateTests.SameBuild(work.CommittedXml,baseline.Xml),"Second listing replaces only the browsing preview and uses the same committed baseline");
            await Action("trade-review-recent."+entry.id);
            check(PobWorkStore.Read(directory,id,game)?.TradeListing?.Id==entry.id&&WorkStateTests.SameBuild(PobWorkStore.Read(directory,id,game)!.Pending!.Xml,originalComparison),"Recent comparison recalculates the original item against its original slot, not the previous candidate");
            check(Nodes(View().Root).Any(n=>n.Text=="2 × Chaos Orb · Test seller")&&!Nodes(View().Root).Any(n=>n.Text=="3 × Chaos Orb · Test seller"),"Displayed seller and price follow the selected comparison");
            var preserved=File.ReadAllText(Path.Combine(directory,id+".work.json"));
            incoming.Enqueue(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new{kind="page-action",action="compare-item",payload=new{url=$"https://www.pathofexile.com/trade{(game==2?"2":"")}/search/Standard/test",entry=unsupported}})));
            await Task.Delay(300);await Idle();
            check(File.ReadAllText(Path.Combine(directory,id+".work.json"))==preserved,"Rejected next listing preserves the previous comparison and history");
            await Action("trade-review-expand");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.workspace.equipment-comparison")&&Nodes(View().Root).Any(n=>n.Id=="pob.item.name"),"Expanded comparison shows item details beside impact");
            await Action("website-resume");await Action("trade-review-browse");
            check(View().FocusGroupEntryRequest?.GroupId=="pob.website.browser"&&opened.Count==1&&navigated==0,"Browsing restores focus to the retained page without navigation or discard");
            await Action("website-close");
            var recoverySnapshot=PobWorkStore.Read(directory,id,game)!;
            await Action("trade-review-close");await Action("item-paste");
            var manualDraft=PobWorkStore.Read(directory,id,game)!.ItemDraft;
            await Action("website-resume");incoming.Enqueue(Convert.ToBase64String(eventJson));
            var blockedUntil=DateTime.UtcNow.AddSeconds(5);
            while(!Nodes(View().Root).Any(n=>n.Text=="Finish current work"))
            {if(DateTime.UtcNow>blockedUntil)throw new TimeoutException("Existing draft was not protected.");await Task.Delay(30);}
            check(PobWorkStore.Read(directory,id,game)!.ItemDraft==manualDraft&&!PobWorkStore.Read(directory,id,game)!.TradeBrowse,"Website browsing cannot replace a manually pasted or edited draft");
            PobWorkStore.Save(directory,id,game,recoverySnapshot);
            await WidgetTestHost.DestroyAsync(widget);
            check(closed==1,"Widget destruction closes its retained website session");
            await using var reopenedEngine=new PobEngine(root,runtime,adapter);
            var reopened=new PobWidget(game,reopenedEngine,directory);
            WidgetTestHost.Attach(reopened,new WidgetTestHostServicesBuilder().Build());
            try
            {
                await WidgetTestHost.SetLifecycleStateAsync(reopened,WidgetLifecycleState.Interactive);
                var until=DateTime.UtcNow.AddSeconds(20);
                while(Nodes(reopened.Render().CreateSnapshot("reopened",++sequence).Root).Any(n=>n.Id=="pob.cancel-operation"))
                {if(DateTime.UtcNow>until)throw new TimeoutException("Reopening trade draft timed out.");await Task.Delay(30);}
                var restored=Nodes(reopened.Render().CreateSnapshot("reopened",++sequence).Root).ToArray();
                check(restored.Any(n=>n.Text=="2 × Chaos Orb · Test seller")&&restored.Any(n=>n.Text=="Find saved listing"),"Restart restores calculated comparison, item descriptions, recent candidates and listing context");
                check(restored.Any(n=>n.Id=="pob.item.name")&&PobWorkStore.Read(directory,id,game)?.TradeCandidates?.Length==2,"Item identity and recent comparisons survive unloading");
                await reopened.OnActionAsync(new("trade-review-equip","reopened"));
                until=DateTime.UtcNow.AddSeconds(20);
                while(Nodes(reopened.Render().CreateSnapshot("reopened",++sequence).Root).Any(n=>n.Id=="pob.cancel-operation"))
                {if(DateTime.UtcNow>until)throw new TimeoutException();await Task.Delay(30);}
                var applied=PobWorkStore.Read(directory,id,game)!;
                check(applied.Pending is null&&applied.ItemDraft is null&&applied.Undo.Length==1&&WorkStateTests.SameBuild(applied.CommittedXml,originalComparison),"Only explicit Equip commits the comparison, with undo history");
            }
            finally{await WidgetTestHost.DestroyAsync(reopened);}
        }
        finally {await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
}
