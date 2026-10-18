using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class AdapterOwnershipTests
{
    internal static void Run(Action<bool,string> check)
    {
        JsonElement Read(string json)=>JsonSerializer.Deserialize<JsonElement>(json);
        const string valid="""{"skillOptionsGroup":1,"skillOptions":[{"id":"mainSkillPart","options":[{"id":1,"title":"First","active":true},{"id":2,"title":"Second","active":false}]},{"id":"stages","kind":"number","title":"Stages","value":"3"}]}""";
        var catalog=PobSkillOptions.Read(Read(valid),1);
        check(catalog.Options.Count==2&&catalog.Options[0].Choices.Count==2&&catalog.Options[1].Numeric,"Typed skill catalog preserves choice identity and numeric kind");
        foreach(var invalid in new[]{"{}","{\"skillOptionsGroup\":1}","{\"skillOptionsGroup\":1,\"skillOptions\":{}}",valid.Replace("mainSkillPart","bad.id"),
            valid.Replace("\"kind\":\"number\"","\"kind\":\"mystery\""),valid.Replace("\"value\":\"3\"","\"value\":3"),valid.Replace("\"id\":2","\"id\":1"),valid.Replace("\"active\":false","\"active\":true")})
        {var rejected=false;try{PobSkillOptions.Read(Read(invalid),1);}catch(IOException){rejected=true;}check(rejected,"Malformed skill catalog is rejected before presentation");}
        check(PobSkillOptions.Read(Read("{\"skillOptionsGroup\":1,\"skillOptions\":[]}"),1).Options.Count==0,"A valid empty skill catalog remains distinct from malformed metadata");
        var owner=new PobSkillOptionsState();var first=new PobSkillContext("doc","xml-a",1);var second=first with{Group=2};
        var old=owner.Begin(first)!;var current=owner.Begin(second)!;
        check(!owner.Complete(old,catalog)&&!owner.Fail(old),"Superseded skill reads cannot publish success or failure");
        check(owner.Fail(current)&&owner.Failed(second)&&owner.Begin(second)is null,"A failed skill read is visible and does not loop automatically");
        owner.Retry(second);check(!owner.Failed(second)&&!owner.UseSnapshot(second),"Explicit retry retires the error and requests fresh adapter data");
        var retry=owner.Begin(second)!;check(owner.Complete(retry,new(2,[]))&&owner.Current(second)is not null,"Retry publishes a valid empty response without treating it as failure");
        var changing=owner.Begin(first)!;owner.Select(first with{Document="new-doc"});check(!owner.Complete(changing,catalog)&&owner.CachedCount==0,"Document change releases the old cache and fences pending replies");
        var resumed=first with{Document="new-doc"};var pending=owner.Begin(resumed)!;owner.Suspend();check(!owner.Complete(pending,catalog),"Lifecycle suspension revokes admitted read publication");
        for(var i=1;i<=40;i++)owner.Publish(resumed with{Group=i},new(i,[]));
        check(owner.CachedCount==32,"Skill option owner bounds its exact-build cache to 32 groups");
        owner.Select(resumed with{Xml="xml-b"});check(owner.CachedCount==0,"Changed build XML clears every cached group");
        var inventory=PobSearchInventory.Read(Read("{\"inventory\":[{\"id\":1,\"title\":\"Item\",\"base\":\"Ring\",\"rarity\":\"RARE\"}]}"));
        check(inventory.Items.Single().Id==1,"Inventory search uses typed validated item identity");
        foreach(var invalid in new[]{"{}","{\"inventory\":null}","{\"inventory\":{}}","{\"inventory\":[{\"title\":\"Item\"}]}",
            "{\"inventory\":[{\"id\":1,\"title\":\"Item\",\"base\":\"Ring\",\"rarity\":\"RARE\"},{\"id\":1,\"title\":\"Other\",\"base\":\"Ring\",\"rarity\":\"RARE\"}]}"})
        {var rejected=false;try{PobSearchInventory.Read(Read(invalid));}catch(IOException){rejected=true;}check(rejected,"Malformed inventory catalog is not presented as an empty successful search");}
        const string calc="""{"sections":[{"id":"1.2","title":"Damage","items":[{"id":"1.2.3.4","title":"DPS","value":"100","outputs":["DPS"]}]}]}""";
        check(PobSearchCalculations.Read(Read(calc)).Cells.Single().Section=="1.2","Calculation search retains exact section/cell identity");
        foreach(var invalid in new[]{"{}","{\"sections\":{}}",calc.Replace("\"id\":\"1.2.3.4\"","\"id\":0"),calc.Replace("\"outputs\":[\"DPS\"]","\"outputs\":[1]"),calc.Replace("\"items\":[","\"other\":[")})
        {var rejected=false;try{PobSearchCalculations.Read(Read(invalid));}catch(IOException){rejected=true;}check(rejected,"Malformed calculation catalog cannot become plausible empty data");}
        var disabled=Read("""{"mainSkillDisabled":true,"mainSkillDisabledReason":"Exact upstream reason","mainSkill":"Fireball","skills":[{"id":7,"main":true}]}""");
        check(PobMainSkillWarning.Read(disabled)is {Group:7,Reason:"Exact upstream reason"},"Disabled main-skill warning uses the supplied main group identity and exact reason");
        check(PobMainSkillWarning.Read(Read("""{"mainSkillDisabled":true,"mainSkillDisabledReason":"Unknown","skills":[]}"""))is null,"Main-skill warning never guesses a missing destination");
        var context=PobInspectionContext.Read(Read("""{"loadouts":[{"title":"Boss","active":true}]}"""),
            "<PathOfBuilding><Items activeItemSet='3'><ItemSet id='3' useSecondWeaponSet='true'/></Items></PathOfBuilding>",
            Read("""{"weaponSets":[{"id":1,"enabled":true},{"id":2,"enabled":false}]}"""));
        check(context.Loadout=="Boss"&&context.WeaponSet=="Weapon set 2"&&context.GroupWeaponSets=="Weapon set 1","Inspection context distinguishes active calculation weapon set from group eligibility");
        var missing=PobInspectionContext.Read(Read("{}"),"<PathOfBuilding/>",Read("{}"));
        check(missing.Summary=="","Missing optional set metadata produces no invented context");
    }
}
