using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class DisclosureWorkflowTests
{
    internal static void Models(Action<bool,string> check)
    {
        var state=new PobDisclosureState();
        var first=state.Select("build-a/cell-a",true);
        check(first.Expanded&&state.Toggle(first.Token)&&state.Current?.Expanded==false,"Disclosure toggles retain their selected context");
        var refresh=state.Select("build-a/cell-a",true);
        check(refresh.Token==first.Token&&!refresh.Expanded,"Same-context refresh preserves collapse and action identity");
        var next=state.Select("build-a/cell-b",true);
        check(next.Expanded&&!state.Toggle(first.Token),"Switching cells resets default presentation and rejects stale toggles");
        check(!state.Select("build-a/cell-a",true).Expanded,"Returning to a recent cell restores its disclosure preference");
        check(state.Select("build-b/cell-a",true).Expanded,"Another build does not inherit the old build's collapsed cell");
        for(var index=0;index<100;index++)state.Select("cell-"+index,true);
        check(state.RetainedCount==PobDisclosureState.MaximumContexts,"Disclosure preference memory is bounded");
    }
    internal static async Task RunAsync(int game,string engineRoot,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        Models(check);
        var directory=Path.Combine(Path.GetTempPath(),"pob-disclosures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var id=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(directory,id+".xml"),xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        await using var engine=new PobEngine(engineRoot,runtime,adapter);
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
        ViewSnapshot View()=>widget.Render().CreateSnapshot("pob-disclosures",1);
        bool Has(string id)=>Nodes(View().Root).Any(node=>node.Id==id);
        ViewNode Node(string id)=>Nodes(View().Root).Single(node=>node.Id==id);
        object? Get(string field)=>typeof(PobWidget).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget);
        void Set(string field,object? value)=>typeof(PobWidget).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,value);
        async Task Idle()
        {
            var deadline=Environment.TickCount64+65000;
            while(Has("pob.cancel-operation")){if(Environment.TickCount64>deadline)throw new TimeoutException();await Task.Delay(20);}
        }
        async Task Action(string action,string? text=null,string? focused=null)
        {await widget.OnActionAsync(new(action,focused??"disclosure-test"){CommittedText=text});await Idle();}
        void Valid()=>check(ViewSnapshotValidator.Validate(View()).Count==0,"Disclosure state emits a valid bounded snapshot");
        void Export(string name)
        {
            if(Environment.GetEnvironmentVariable("POB_DISCLOSURE_FIXTURES") is not {Length:>0} fixtures)return;
            Directory.CreateDirectory(fixtures);File.WriteAllBytes(Path.Combine(fixtures,$"poe{game}-"+name+".json"),SnapshotJson.Serialize(View()));
        }
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
            await Action("stat.Life");await Action("calculations");
            var catalog=(JsonElement)Get("calculationCatalog")!;
            var life=catalog.GetProperty("sections").EnumerateArray().Single(s=>s.GetProperty("title").GetString()=="Life");
            check((string)Get("calculationSection")! == life.GetProperty("id").GetString(),"Overview stat navigation selects its containing calculation section");
            var totalBase=life.GetProperty("items").EnumerateArray().Single(c=>c.GetProperty("title").GetString()=="Total Base");
            await Action("calculation-cell."+totalBase.GetProperty("id").GetString());
            var baseline=(PobResult)Get("result")!;
            var title=Node("pob.calculation.title").Text;var value=Node("pob.calculation.value").Text;var context=Node("pob.calculation.context").Text;
            check(Has("pob.calculation.details-toggle"),"PoB Life calculation exposes supporting detail disclosure");
            if(Environment.GetEnvironmentVariable("POB_DISCLOSURE_FIXTURES") is {Length:>0} calculationFixtures)
            {
                Directory.CreateDirectory(calculationFixtures);
                using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"pob-disclosures",500);
                var materialized=new Dictionary<string,ViewNode[]>();
                foreach(var collectionId in new[]{"pob.calculation.cells","pob.calculation.source-list"})
                {
                    var source=Nodes(host.CurrentSnapshot.Root).FirstOrDefault(node=>node.Id==collectionId)?.IndexedCollection;
                    if(source is null||source.Count==0)continue;
                    using var lease=await host.AcquireAsync(collectionId,0,Math.Min(3,source.Count));
                    materialized[collectionId]=lease.Range.Items.Select(item=>item.Root).ToArray();
                }
                File.WriteAllText(Path.Combine(calculationFixtures,$"poe{game}-calculation-disclosure-rows.json"),JsonSerializer.Serialize(materialized));
            }
            Export("calculation-expanded");
            var toggle=Node("pob.calculation.details-toggle");
            check(toggle.Text!.Contains("source rows",StringComparison.Ordinal)&&!toggle.Text.Contains("0 lines",StringComparison.Ordinal),"Breakdown toggle reports available supporting detail without empty counts");
            await Action(toggle.ActionId!,focused:"pob.calculation.source-list");
            check(!Has("pob.calculation.explanation")&&!Has("pob.calculation.source-list")&&Has("pob.calculation.mode"),"Collapsed breakdown removes only explanations/sources, keeping mode controls");
            check(Node("pob.calculation.title").Text==title&&Node("pob.calculation.value").Text==value&&Node("pob.calculation.context").Text==context,
                "Selected calculation value and context remain visible when collapsed");
            check(View().FocusGroupEntryRequest?.GroupId=="pob.calculation.disclosure","Collapse from child focus explicitly returns to the stable toggle");
            check(ReferenceEquals(Get("result"),baseline),"Collapsing calculation details does not calculate or change the build");Valid();Export("calculation-collapsed");
            var token=Node("pob.calculation.details-toggle").ActionId;
            await Action("calculation-cell."+(string)Get("calculationCell")!);
            check(Node("pob.calculation.details-toggle").ActionId==token&&Node("pob.calculation.details-toggle").Text!.StartsWith("Show",StringComparison.Ordinal),"Same-cell source refresh preserves collapsed state and toggle identity");
            await Action("calculation-mode.BUFFED");
            check(Node("pob.calculation.details-toggle").Text!.StartsWith("Hide",StringComparison.Ordinal),"Changed calculation context starts with its own disclosure preference");
            await Action(token!);
            check(Node("pob.calculation.details-toggle").Text!.StartsWith("Hide",StringComparison.Ordinal),"Late prior-context toggle cannot collapse the newly selected context");
            Set("calculationDetail",JsonSerializer.SerializeToElement(new{sections=Array.Empty<object>()}));
            // PublishCalculations owns the matching source list; use its current empty typed payload through a no-source cell context.
            var sourcesType=Get("calculationSources")!.GetType();
            var items=sourcesType.GetProperty("Items")!.PropertyType.GetElementType()!;
            Set("calculationSources",Activator.CreateInstance(sourcesType,new object[]{Array.CreateInstance(items,0)}));
            check(!Has("pob.calculation.details-toggle")&&Has("pob.calculation.empty"),"A calculation with no optional breakdown has no empty disclosure");
            await Action("back");await Action("section.4");
            var state=JsonNode.Parse(baseline.State.GetRawText())!.AsObject();
            var condition=state["conditions"]!.AsArray().OfType<JsonObject>().First(candidate=>candidate["value"]!.GetValueKind() is JsonValueKind.True or JsonValueKind.False);
            var conditionId=condition["id"]!.GetValue<string>();
            condition["section"]="Retained settings fixture";condition["applicable"]=false;condition["changed"]=true;condition["value"]=true;
            var fixture=new PobResult(JsonSerializer.SerializeToElement(state),baseline.Xml);Set("result",fixture);Set("configSection","Retained settings fixture");Set("filter","");Set("configChangedOnly",false);
            var option=fixture.Configuration.Options.Single(item=>item.Id==conditionId);
            var settingId="pob.condition."+option.Index;
            check(Has("pob.config.inactive.toggle")&&!Has(settingId)&&Node("pob.config.inactive.summary."+option.Index).Text!.Contains("On",StringComparison.Ordinal),
                "Collapsed inactive settings retain each current choice while hiding secondary editing controls");
            check(Node("pob.config.inactive.toggle").Text!.Contains("1 changed",StringComparison.Ordinal),"Inactive section summary keeps changed-setting count");
            Export("configuration-collapsed");
            var inactiveToggle=Node("pob.config.inactive.toggle").ActionId!;
            await Action(inactiveToggle);
            check(Has(settingId),"Expanding inactive settings exposes their original controls");Valid();Export("configuration-expanded");
            await Action(inactiveToggle,focused:settingId);
            check(!Has(settingId)&&View().FocusGroupEntryRequest?.GroupId=="pob.config.inactive.disclosure","Collapsing secondary settings returns child focus to the stable toggle");
            await Action("search",option.Title);
            check(Has(settingId)&&!Has("pob.config.inactive.toggle"),"Search reveals matching inactive settings directly instead of hiding the result");
            check(ReferenceEquals(Get("result"),fixture)&&File.ReadAllText(Path.Combine(directory,id+".xml"))==xml,"Disclosure and search changes leave build and saved XML untouched");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
}
