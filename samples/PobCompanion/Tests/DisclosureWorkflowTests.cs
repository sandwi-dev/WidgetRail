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
        async Task ExportCalculationRows(string name)
        {
            if(Environment.GetEnvironmentVariable("POB_DISCLOSURE_FIXTURES") is not {Length:>0} fixtures)return;
            Directory.CreateDirectory(fixtures);
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"pob-disclosures",500);
            var materialized=new Dictionary<string,ViewNode[]>();
            foreach(var collectionId in new[]{"pob.calculation.cells","pob.calculation.source-list"})
            {
                var source=Nodes(host.CurrentSnapshot.Root).FirstOrDefault(node=>node.Id==collectionId)?.IndexedCollection;
                if(source is null||source.Count==0)continue;
                using var lease=await host.AcquireAsync(collectionId,0,Math.Min(3,source.Count));
                materialized[collectionId]=lease.Range.Items.Select(item=>item.Root).ToArray();
            }
            File.WriteAllText(Path.Combine(fixtures,$"poe{game}-{name}-rows.json"),JsonSerializer.Serialize(materialized));
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
            check(!Has("pob.calculation.details-toggle")&&Has("pob.calculation.source-list"),"Detailed calculations always show available sources without a redundant toggle");
            await ExportCalculationRows("calculation-disclosure");
            Export("calculation-details");
            await Action("summary-calculation.1",focused:"pob.calculation.source-list");
            check(Has("pob.calculation.source-list")&&Has("pob.calculation.mode"),"Retired breakdown actions cannot hide sources or mode controls");
            check(Node("pob.calculation.title").Text==title&&Node("pob.calculation.value").Text==value&&Node("pob.calculation.context").Text==context,
                "Retired breakdown actions leave the selected calculation value and context unchanged");
            check(ReferenceEquals(Get("result"),baseline),"Reading calculation details does not change the build");Valid();
            await Action("calculation-cell."+(string)Get("calculationCell")!);
            check(Has("pob.calculation.source-list"),"Same-cell refresh keeps sources visible");
            await Action("calculation-mode.BUFFED");
            check(Has("pob.calculation.source-list"),"Changed calculation context keeps its sources visible");
            var maximumHitSection=catalog.GetProperty("sections").EnumerateArray().First(section=>
                section.GetProperty("items").EnumerateArray().Any(cell=>cell.GetProperty("outputs").EnumerateArray().Any(output=>output.GetString()=="LightningMaximumHitTaken")));
            var maximumHit=maximumHitSection.GetProperty("items").EnumerateArray().First(cell=>
                cell.GetProperty("outputs").EnumerateArray().Any(output=>output.GetString()=="LightningMaximumHitTaken"));
            await Action("calculation-section."+maximumHitSection.GetProperty("id").GetString());
            await Action("calculation-cell."+maximumHit.GetProperty("id").GetString());
            check(Node("pob.calculation.source-list").IndexedCollection?.Count==1&&Has("pob.calculation.explanation"),
                "Maximum lightning hit exposes its explanation and single source row");
            Export("calculation-short");await ExportCalculationRows("calculation-short");
            // PublishCalculations owns the matching source list; use its current empty typed payload through a no-source cell context.
            var sourcesType=Get("calculationSources")!.GetType();
            var items=sourcesType.GetProperty("Items")!.PropertyType.GetElementType()!;
            Set("calculationSources",Activator.CreateInstance(sourcesType,new object[]{Array.CreateInstance(items,0)}));
            Set("calculationDetail",JsonSerializer.SerializeToElement(new{sections=new[]{new{title="Damage taken",lines=new[]{
                new{text="Final damage taken:"},new{text="348.9 incoming damage"},new{text="x 0.350 damage multiplier"},new{text="= 122.1"}}}}}));
            check(Has("pob.calculation.explanation")&&!Has("pob.calculation.source-list"),"Explanation-only breakdown remains available without source rows");
            Export("calculation-explanation");
            Set("calculationDetail",JsonSerializer.SerializeToElement(new{sections=Array.Empty<object>()}));
            check(!Has("pob.calculation.details-toggle")&&Has("pob.calculation.empty"),"A calculation with no optional breakdown has no empty disclosure");
            await Action("back");await Action("section.4");
            Set("result",baseline);Set("filter","");Set("configChangedOnly",false);
            Set("configSection",baseline.Configuration.Options.Select(option=>option.Section).FirstOrDefault(section=>section.Contains("Combat",StringComparison.OrdinalIgnoreCase))??baseline.Configuration.Options.GroupBy(option=>option.Section).OrderByDescending(group=>group.Count()).First().Key);
            Set("configHelp",false);Export("configuration-density");
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
