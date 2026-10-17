using System.Text.Json;
using System.Xml.Linq;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

internal static class CalculationViewTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var build=await engine.ExecuteAsync(new{op="load",xml,name="Detailed calculations"},default);
        var catalog=await engine.ExecuteAsync(new{op="calculation-catalog"},default,build.Xml);
        var sections=catalog.State.GetProperty("sections").EnumerateArray().ToArray();
        var cells=sections.SelectMany(s=>s.GetProperty("items").EnumerateArray()).ToArray();
        check(sections.Length>10&&cells.Length>50,"Detailed calculation catalog contains upstream sections and values beyond overview totals");
        check(cells.All(c=>!c.GetProperty("value").GetString()!.Contains("{output:")&&!c.GetProperty("value").GetString()!.Contains(":mod:")),"Every calculation value is formatted by upstream rather than exposing format tokens");
        check(WorkStateTests.SameBuild(build.Xml,catalog.Xml),"Reading calculation categories preserves the authored build");
        var totalRows=0;
        foreach(var cell in cells.Where(c=>c.GetProperty("hasBreakdown").GetBoolean()))
        {
            var detail=await engine.ExecuteAsync(new{op="calculation-detail",id=cell.GetProperty("id").GetString()},default,build.Xml);
            var rows=detail.State.GetProperty("sections").EnumerateArray().Sum(s=>s.GetProperty("rows").GetArrayLength());
            if(rows!=detail.State.GetProperty("rowCount").GetInt32())throw new InvalidOperationException("Breakdown count mismatch for "+cell.GetProperty("title").GetString());
            totalRows+=rows;
        }
        check(totalRows>0,"All displayed calculation cells can expose their complete source and formula sections");
        var doc=XDocument.Parse(build.Xml);
        foreach(var config in doc.Descendants("ConfigSet"))
        {
            config.Elements("CustomModifierBlock").Remove();
            for(var i=0;i<130;i++)config.Add(new XElement("CustomModifierBlock",new XAttribute("title","Source "+i),new XAttribute("enabled",true),"+1 to maximum Life"));
        }
        var many=await engine.ExecuteAsync(new{op="load",xml=doc.ToString(),name="Complete source lists"},default);
        var extended=await engine.ExecuteAsync(new{op="calculation-catalog"},default,many.Xml);
        var life=extended.State.GetProperty("sections").EnumerateArray().Single(s=>s.GetProperty("title").GetString()=="Life");
        var totalBase=life.GetProperty("items").EnumerateArray().Single(c=>c.GetProperty("title").GetString()=="Total Base");
        var breakdown=await engine.ExecuteAsync(new{op="calculation-detail",id=totalBase.GetProperty("id").GetString()},default,many.Xml);
        check(breakdown.State.GetProperty("rowCount").GetInt32()>=130,"Source lists retain more than 100 real upstream modifier rows");
        check(breakdown.State.GetProperty("sections").EnumerateArray().SelectMany(s=>s.GetProperty("rows").EnumerateArray()).SelectMany(r=>r.GetProperty("cells").EnumerateArray()).Any(c=>c.GetProperty("text").GetString()!.Contains("Source 129")),"The final source remains available with its upstream attribution");
        var directory=Path.Combine(Path.GetTempPath(),"pob-calculation-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var buildId=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(directory,buildId+".xml"),many.Xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),buildId);
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        IEnumerable<ViewNode> Nodes(ViewNode n)=>new[]{n}.Concat((n.Children??[]).SelectMany(Nodes));
        await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"calculation-ui");
        ViewSnapshot View()=>host.PublishSnapshot();
        async Task Idle(){var end=DateTime.UtcNow.AddSeconds(65);while(Nodes(View().Root).Any(n=>n.Id=="pob.cancel-operation")){if(DateTime.UtcNow>end)throw new TimeoutException();await Task.Delay(20);}}
        async Task Action(string id){await widget.OnActionAsync(new(id,"calculation-ui"));await Idle();}
        try
        {
            await Idle();await Action("stat.Life");await Action("calculations");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.calculation.section")&&Nodes(View().Root).Any(n=>n.Id=="pob.calculation.cells"),"Overview opens categorized detailed calculations with a virtualized list");
            await Action("calculation-section."+life.GetProperty("id").GetString());
            await Action("calculation-cell."+totalBase.GetProperty("id").GetString());
            var source=Nodes(View().Root).Single(n=>n.Id=="pob.calculation.source-list").IndexedCollection!;
            check(source.Count>=130,"Native source collection declares the complete upstream row count");
            var texts=new List<string>();var expandableIndex=-1;
            for(var start=0;start<source.Count;start+=16)
            {
                using var lease=await host.AcquireAsync("pob.calculation.source-list",start,Math.Min(16,source.Count-start));
                foreach(var row in lease.Range.Items)
                {
                    if(WinUiPresentationContract.ValidateSubtree(row.Root).Count!=0)throw new InvalidOperationException("Unsupported lazy source row: "+row.Key);
                    texts.Add(string.Concat(Nodes(row.Root).Where(n=>n.Kind==ViewNodeKind.Text).Select(n=>n.Text)));
                    if(Nodes(row.Root).Any(n=>n.ActionId=="read"))expandableIndex=start+lease.Range.Items.ToList().IndexOf(row);
                }
            }
            check(texts.Any(t=>t.Contains("Source 129",StringComparison.Ordinal)),"Controller-accessible lazy rows include sources beyond the old cutoff");
            using(var lease=await host.AcquireAsync("pob.calculation.source-list",expandableIndex,1))
            {
                check(lease.RouteAction(lease.Range.Items[0].Key,ControllerButton.A)==WidgetOperationAdmission.Enqueued,"A on a source row routes through the collection controller action");
                var deadline=DateTime.UtcNow.AddSeconds(5);
                while(!Nodes(View().Root).Any(n=>n.Id=="pob.feature-reading.body")&&DateTime.UtcNow<deadline)await Task.Delay(20);
                check(Nodes(View().Root).Any(n=>n.Id=="pob.feature-reading.body"&&n.Kind==ViewNodeKind.Scroll),"Full source details open in a scrollable reading panel");
            }
            await Action("feature-close");
            var physicalSection=extended.State.GetProperty("sections").EnumerateArray().First(s=>s.GetProperty("items").EnumerateArray().Any(c=>c.GetProperty("outputs").EnumerateArray().Any(o=>o.GetString()=="PhysicalMaximumHitTaken")));
            var physical=physicalSection.GetProperty("items").EnumerateArray().First(c=>c.GetProperty("outputs").EnumerateArray().Any(o=>o.GetString()=="PhysicalMaximumHitTaken"));
            var physicalDetail=(await engine.ExecuteAsync(new{op="calculation-detail",id=physical.GetProperty("id").GetString()},default,many.Xml)).State;
            var explanations=physicalDetail.GetProperty("sections").EnumerateArray().Where(s=>s.GetProperty("lines").GetArrayLength()>0).ToArray();
            await Action("calculation-section."+physicalSection.GetProperty("id").GetString());
            await Action("calculation-cell."+physical.GetProperty("id").GetString());
            var blocks=Nodes(View().Root).Single(n=>n.Id=="pob.calculation.source-list").IndexedCollection!;
            var narrativeText=new List<string>();var narrativeBlocks=0;
            for(var start=0;start<blocks.Count;start++)
            {
                using var lease=await host.AcquireAsync("pob.calculation.source-list",start,1);
                var row=lease.Range.Items.Single().Root;
                check(WinUiPresentationContract.ValidateSubtree(row).Count==0,"Calculation block satisfies native presentation contract: "+start);
                if(row.StyleClasses?.Contains("pob-calculation-explanation")==true)
                {
                    narrativeBlocks++;narrativeText.Add(string.Concat(Nodes(row).Where(n=>n.Kind==ViewNodeKind.Text).Select(n=>n.Text)));
                    check(Nodes(row).Any(n=>n.Kind==ViewNodeKind.RichText)&&Nodes(row).Count(n=>n.ActionId=="read")==1,"Formula block uses inline rich text with one action for the complete explanation");
                }
            }
            check(explanations.Length>0&&narrativeBlocks>=explanations.Length&&explanations.SelectMany(s=>s.GetProperty("lines").EnumerateArray()).All(l=>narrativeText.Any(t=>t.Contains(l.GetProperty("text").GetString()!,StringComparison.Ordinal))),"Grouped formula retains every upstream explanatory line");
            check(!Nodes(View().Root).Single(n=>n.Id=="pob.calculation.sources-count").Text!.Contains("source rows"),"Formula view does not mislabel table row count as the complete explanation");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }
}
