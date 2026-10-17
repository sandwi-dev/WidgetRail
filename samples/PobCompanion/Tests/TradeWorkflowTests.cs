using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class TradeWorkflowTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        var directory=Path.Combine(Path.GetTempPath(),"widgetrail-trade-workflow-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);var id=Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(directory,id+".xml"),xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        await using var engine=new PobEngine(root,runtime,adapter);
        var widget=new PobWidget(game,engine,directory){TradeLeagueLoader=(_,_)=>Task.FromResult(new[]{"Standard","Hardcore"})};
        var opened=new List<WebSessionDefinition>();var closed=0;var navigated=0;
        WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().WithHandler(WidgetWebSessionCapabilities.Command,async(command,token)=>
        {
            if(command.Operation==WebSessionOperation.Open){opened.Add(command.Definition!);return new WebSessionReply{Session=new(new string('a',32))};}
            if(command.Operation==WebSessionOperation.ReadMessages)await Task.Delay(50,token);
            if(command.Operation==WebSessionOperation.Close)closed++;
            if(command.Operation==WebSessionOperation.Navigate)navigated++;
            return new WebSessionReply();
        }).Build());var sequence=0;
        IEnumerable<ViewNode> Nodes(ViewNode n)=>new[]{n}.Concat((n.Children??[]).SelectMany(Nodes));
        ViewSnapshot View()=>widget.Render().CreateSnapshot("trade-workflow",++sequence);
        async Task Idle(){var deadline=DateTime.UtcNow.AddSeconds(65);while(Nodes(View().Root).Any(n=>n.Id=="pob.cancel-operation")){if(DateTime.UtcNow>deadline)throw new TimeoutException();await Task.Delay(20);}}
        async Task Action(string action,string? value=null){await widget.OnActionAsync(new(action,"trade-workflow"){CommittedText=value});await Idle();}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
            var baseline=await engine.ExecuteAsync(new{op="snapshot"},default);
            var helmet=baseline.State.GetProperty("items").EnumerateArray().First(i=>i.GetProperty("slot").GetString()=="Helmet").GetProperty("id").GetInt32();
            await Action("section.3");await Action("item."+helmet);await Action("trade");
            var search=Nodes(View().Root).Single(n=>n.Id=="pob.trade-open");
            var pasteHelp=Nodes(View().Root).Single(n=>n.Id=="pob.trade.paste-help").Text!;
            check(pasteHelp.Contains("Ctrl+C")&&(game==2?pasteHelp.Contains("no built-in Copy item button"):pasteHelp.Contains("On the trade site, use Copy item")),"Item-copy instructions distinguish PoE2 website limitations from PoE1");
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
            await Action("website-close");
            await WidgetTestHost.DestroyAsync(widget);
            check(closed==1,"Widget destruction closes its retained website session");
        }
        finally {await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
}
