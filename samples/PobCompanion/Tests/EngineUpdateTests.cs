using WidgetRail.Samples.PobCompanion;

internal static class EngineUpdateTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string evidence,Action<bool,string> check)
    {
        await BundleTests.RunAsync(check);
        Directory.CreateDirectory(evidence);string xml;
        await using(var engine=new PobEngine(root,runtime,adapter))
        {
            var state=await engine.ExecuteAsync(new{op="new"},default);
            state=await engine.ExecuteAsync(new{op="level",value=80},default,state.Xml);
            state=await engine.ExecuteAsync(new{op="skill-group",mode="create"},default,state.Xml);
            var group=state.State.GetProperty("editedSkillGroup").GetInt32();
            var gems=await engine.ExecuteAsync(new{op="gem-catalog",group,gem=1},default,state.Xml);
            var fireball=gems.State.GetProperty("gemCatalog").EnumerateArray().First(g=>g.GetProperty("name").GetString()=="Fireball");
            state=await engine.ExecuteAsync(new{op="gem-manage",mode="add",group,gemId=fireball.GetProperty("id").GetString()},default,state.Xml);
            state=await engine.ExecuteAsync(new{op="gem",group,id=1,field="level",value=20},default,state.Xml);
            state=await engine.ExecuteAsync(new{op="skill",id=group,field="main"},default,state.Xml);
            var bases=await engine.ExecuteAsync(new{op="item-bases"},default,state.Xml);
            var basis=bases.State.GetProperty("choices").EnumerateArray().First(b=>b.GetProperty("kind").GetString()!.Contains("Helmet",StringComparison.OrdinalIgnoreCase));
            var item=await engine.ExecuteAsync(new{op="item-draft",mode="create",kind=basis.GetProperty("kind").GetString(),@base=basis.GetProperty("base").GetString(),rarity="RARE"},default,state.Xml);
            item=await engine.ExecuteAsync(new{op="item-draft",mode="custom-add",raw=item.State.GetProperty("authoring").GetProperty("raw").GetString(),text="+100 to maximum Life"},default,state.Xml);
            state=await engine.ExecuteAsync(new{op="item",slot="Helmet",raw=item.State.GetProperty("authoring").GetProperty("raw").GetString()},default,state.Xml);
            var node=state.State.GetProperty("nodes").EnumerateArray().First(n=>n.GetProperty("canAllocate").GetBoolean()&&n.GetProperty("pathCount").GetInt32() is >=2 and <=8);
            state=await engine.ExecuteAsync(new{op="node",id=node.GetProperty("id").GetInt32(),mode="allocate"},default,state.Xml);
            state=await engine.ExecuteAsync(new{op="load",xml=state.Xml,name="Engine update fixture"},default);
            xml=state.Xml;File.WriteAllText(Path.Combine(evidence,"engine-update.xml"),xml);
            check(state.State.GetProperty("stats").EnumerateArray().Any(s=>s.GetProperty("name").GetString()=="Life"&&s.GetProperty("value").GetDouble()>100),"Candidate engine builds a real equipped character from upstream data");
        }
        await SetManagementTests.RunAsync(game,root,runtime,adapter,xml,check);
        await ItemAuthoringTests.RunAsync(game,root,runtime,adapter,xml,check);
        await ItemWorkflowTests.RunAsync(game,root,runtime,adapter,xml,check);
        await WorkStateTests.RunAsync(game,root,runtime,adapter,xml,check);
        await ManagementTests.RunAsync(game,root,runtime,adapter,xml,check);
        await SkillManagementTests.RunAsync(game,root,runtime,adapter,xml,check);
        await SkillEditTests.RunAsync(game,root,runtime,adapter,xml,check);
        await SkillOptionTests.RunAsync(game,root,runtime,adapter,check);
        await PassiveChoiceTests.RunAsync(game,root,runtime,adapter,xml,check);
        await CalculationContextTests.RunAsync(game,root,runtime,adapter,xml,check);
        await CalculationViewTests.RunAsync(game,root,runtime,adapter,xml,check);
        await TradeSearchTests.RunAsync(game,root,runtime,adapter,xml,check);
        await TradeMenuTests.RunAsync(game,root,runtime,adapter,xml,check);
        await TradeWorkflowTests.RunAsync(game,root,runtime,adapter,xml,check);
        await TreeEditingTests.RunAsync(game,root,runtime,adapter,xml,check);
        await CurrencyRankingTests.RunAsync(game,root,runtime,adapter,xml,check);
        await TradeImprovementTests.RunAsync(game,root,runtime,adapter,xml,check);
        await ImportExportTests.RunAsync(game,root,runtime,adapter,xml,check);
        await ComparisonTests.RunAsync(root,runtime,adapter,xml,check);
        await EngineFailureTests.RunAsync(root,runtime,adapter,xml,check);
    }
}
