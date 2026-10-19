using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class ItemAuthoringTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var before=await engine.ExecuteAsync(new{op="load",xml,name="Item authoring"},default);
        var bases=await engine.ExecuteAsync(new{op="item-bases"},default,before.Xml);
        var basis=bases.State.GetProperty("choices").EnumerateArray().First(b=>b.GetProperty("kind").GetString()!.Contains("Helmet",StringComparison.OrdinalIgnoreCase));
        var created=await engine.ExecuteAsync(new{op="item-draft",mode="create",kind=basis.GetProperty("kind").GetString(),@base=basis.GetProperty("base").GetString(),rarity="RARE"},default,before.Xml);
        var draft=created.State.GetProperty("authoring");
        string Raw()=>draft.GetProperty("raw").GetString()!;
        check(draft.GetProperty("canEditTitle").GetBoolean(),"Rare draft exposes upstream item title editing");
        var unnamedRaw=Raw();var oldTitle=draft.GetProperty("title").GetString()!;
        var renamed=await engine.ExecuteAsync(new{op="item-draft",mode="title",raw=unnamedRaw,text="Planning Helmet"},default,before.Xml);
        var renamedInfo=renamed.State.GetProperty("authoring");
        check(renamedInfo.GetProperty("title").GetString()=="Planning Helmet"&&
            renamedInfo.GetProperty("raw").GetString()!.Replace("Planning Helmet",oldTitle,StringComparison.Ordinal)==unnamedRaw&&
            WorkStateTests.SameBuild(before.Xml,renamed.Xml),"Draft rename changes only the title and leaves the committed build intact");
        foreach(var invalid in new[]{"", "   ", "Name\n+999 to maximum Life",new string('a',401)})
        {var invalidRejected=false;try{await engine.ExecuteAsync(new{op="item-draft",mode="title",raw=unnamedRaw,text=invalid},default,before.Xml);}catch(IOException){invalidRejected=true;}check(invalidRejected,"Invalid item name is rejected without applying raw item text");}
        draft=renamedInfo;
        check(draft.GetProperty("crafted").GetBoolean()&&draft.GetProperty("affixes").GetArrayLength()>0,"Created draft exposes upstream prefix and suffix slots");
        check(WorkStateTests.SameBuild(before.Xml,created.Xml),"Creating a draft does not equip or save an item into the build");
        foreach(var rarity in new[]{"NORMAL","MAGIC"})
        {
            var other=await engine.ExecuteAsync(new{op="item-draft",mode="create",kind=basis.GetProperty("kind").GetString(),@base=basis.GetProperty("base").GetString(),rarity},default,before.Xml);
            var info=other.State.GetProperty("authoring");
            check(info.GetProperty("rarity").GetString()==rarity&&info.GetProperty("affixes").GetArrayLength()==(rarity=="MAGIC"?2:0),rarity+" uses upstream rarity and affix limits");
            check(!info.GetProperty("canEditTitle").GetBoolean(),rarity+" derived names do not offer rare title editing");
        }
        var quality=await engine.ExecuteAsync(new{op="item-draft",mode="control",raw=Raw(),control="displayItemQualityEdit",value=20},default,before.Xml);
        check(quality.State.GetProperty("authoring").GetProperty("raw").GetString()!.Contains("Quality: 20"),"Draft quality uses the same upstream control as equipped items");
        var affix=await engine.ExecuteAsync(new{op="item-draft",mode="choices",raw=Raw(),slot=1},default,before.Xml);
        var choices=affix.State.GetProperty("authoring").GetProperty("choices").EnumerateArray().ToArray();
        var life=choices.Last(c=>c.GetProperty("title").GetString()!.Contains("maximum Life",StringComparison.Ordinal));
        var changed=await engine.ExecuteAsync(new{op="item-draft",mode="affix",raw=Raw(),slot=1,modId=life.GetProperty("modId").GetString()},default,before.Xml);
        draft=changed.State.GetProperty("authoring");
        check(Raw().Contains("maximum Life",StringComparison.Ordinal),"Choosing an upstream affix writes its numeric modifier into the draft");
        var rolled=await engine.ExecuteAsync(new{op="item-draft",mode="roll",raw=Raw(),slot=1,value=100},default,before.Xml);
        draft=rolled.State.GetProperty("authoring");
        check(draft.GetProperty("affixes")[0].GetProperty("roll").GetInt32()==100,"Affix roll survives raw-item round trip");
        var custom=await engine.ExecuteAsync(new{op="item-draft",mode="custom-add",raw=Raw(),text="+25 to maximum Mana"},default,before.Xml);
        draft=custom.State.GetProperty("authoring");
        check(Raw().Contains("+25 to maximum Mana",StringComparison.Ordinal)&&draft.GetProperty("custom").GetArrayLength()>0,"Custom item modifier is retained separately from rolled affixes");
        var removed=await engine.ExecuteAsync(new{op="item-draft",mode="custom-delete",raw=Raw(),id=1},default,before.Xml);
        draft=removed.State.GetProperty("authoring");
        check(!Raw().Contains("+25 to maximum Mana",StringComparison.Ordinal),"Remove custom modifier leaves the crafted affixes intact");
        var saved=await engine.ExecuteAsync(new{op="inventory-edit",mode="add",raw=Raw()},default,before.Xml);
        var id=saved.State.GetProperty("editedInventoryId").GetInt32();
        var inventory=await engine.ExecuteAsync(new{op="inventory-list"},default,saved.Xml);
        check(inventory.State.GetProperty("inventory").EnumerateArray().Any(i=>i.GetProperty("id").GetInt32()==id),"Crafted draft can be stored without equipping");
        var equipped=await engine.ExecuteAsync(new{op="inventory-edit",mode="equip",id,slot="Helmet"},default,saved.Xml);
        check(equipped.State.GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("slot").GetString()=="Helmet"&&i.GetProperty("itemId").GetInt32()==id),"Equip from inventory preserves item identity");
        var equippedRenamed=await engine.ExecuteAsync(new{op="item-author",mode="title",itemId=id,text="Boss Helmet"},default,equipped.Xml);
        check(equippedRenamed.State.GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("itemId").GetInt32()==id&&i.GetProperty("name").GetString()!.StartsWith("Boss Helmet",StringComparison.Ordinal))&&
            equippedRenamed.State.GetProperty("stats").GetRawText()==equipped.State.GetProperty("stats").GetRawText(),
            "Equipped rename preserves item identity, slot assignment and calculated stats");
        var duplicated=await engine.ExecuteAsync(new{op="inventory-edit",mode="copy",id},default,equipped.Xml);
        var copyId=duplicated.State.GetProperty("editedInventoryId").GetInt32();
        check(copyId!=id,"Duplicate creates an independent item identity");
        var edited=await engine.ExecuteAsync(new{op="item-author",mode="custom-add",itemId=id,text="+25 to maximum Mana"},default,duplicated.Xml);
        var original=await engine.ExecuteAsync(new{op="inventory-inspect",itemId=id},default,edited.Xml);
        var description=string.Join("\n",original.State.GetProperty("authoring").GetProperty("details").EnumerateArray().Select(l=>l.GetProperty("text").GetString()));
        check(!description.Contains("Ctrl+D")&&!description.Contains("Removing this item"),"Inventory descriptions omit desktop-only shortcuts and unrelated slot comparisons");
        var copy=await engine.ExecuteAsync(new{op="inventory-inspect",itemId=copyId},default,edited.Xml);
        check(original.State.GetProperty("authoring").GetProperty("raw").GetString()!.Contains("+25 to maximum Mana")&&!copy.State.GetProperty("authoring").GetProperty("raw").GetString()!.Contains("+25 to maximum Mana"),"Editing original item does not alter its duplicate");
        var deleted=await engine.ExecuteAsync(new{op="inventory-edit",mode="delete",id},default,edited.Xml);
        check(!deleted.State.GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("itemId").GetInt32()==id),"Deleting an equipped item removes its equipment references");
        var restored=await engine.ExecuteAsync(new{op="load",xml=edited.Xml,name="Undo inventory deletion"},default);
        check(restored.State.GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("itemId").GetInt32()==id),"XML history restores deleted item and its equipment references");
        var library=await engine.ExecuteAsync(new{op="item-uniques"},default,before.Xml);
        var uniques=library.State.GetProperty("choices").EnumerateArray().ToArray();
        check(uniques.Length>50&&uniques.Select(u=>u.GetProperty("key").GetString()).Distinct().Count()==uniques.Length,"Unique library exposes stable upstream identities");
        var goldrim=uniques.First(u=>u.GetProperty("title").GetString()!.Contains("Goldrim",StringComparison.Ordinal));
        check(goldrim.GetProperty("preview").GetArrayLength() is >0 and <=3&&uniques.All(item=>item.GetProperty("preview").GetArrayLength()<=3),"Unique catalogue contains bounded upstream-formatted modifier previews");
        check(goldrim.GetProperty("preview").EnumerateArray().All(line=>line.GetProperty("runs").GetArrayLength()>0),"Unique modifier previews preserve upstream formatting and unsupported-effect tones");
        // Compare against the state already loaded for the library: upstream
        // load normalizes whitespace in historical custom-modifier blocks.
        var unique=await engine.ExecuteAsync(new{op="item-unique-draft",key=goldrim.GetProperty("key").GetString()},default,library.Xml);
        var uniqueRaw=unique.State.GetProperty("authoring").GetProperty("raw").GetString();
        check(!unique.State.GetProperty("authoring").GetProperty("canEditTitle").GetBoolean(),"Unique identity names are not exposed as editable rare titles");
        var uniqueRenameRejected=false;try{await engine.ExecuteAsync(new{op="item-draft",mode="title",raw=uniqueRaw,text="Other unique"},default,before.Xml);}catch(IOException){uniqueRenameRejected=true;}
        check(uniqueRenameRejected,"Direct rename cannot bypass unique identity protection");
        check(unique.State.GetProperty("authoring").GetProperty("rarity").GetString()=="UNIQUE"&&WorkStateTests.SameBuild(library.Xml,unique.Xml),"Choosing a library unique creates an unequipped draft only");
        await engine.ExecuteAsync(new{op="item-draft",mode="control",raw=uniqueRaw,control="displayItemQualityEdit",value=7},default,before.Xml);
        var originalUnique=await engine.ExecuteAsync(new{op="item-unique-draft",key=goldrim.GetProperty("key").GetString()},default,before.Xml);
        check(originalUnique.State.GetProperty("authoring").GetProperty("raw").GetString()==uniqueRaw,"Editing a unique draft leaves the shared upstream database unchanged");
        var variant=uniques.First(u=>u.GetProperty("hasVariants").GetBoolean());
        var variantDraft=await engine.ExecuteAsync(new{op="item-unique-draft",key=variant.GetProperty("key").GetString()},default,before.Xml);
        var variantInfo=variantDraft.State.GetProperty("authoring");
        var variantControl=variantInfo.GetProperty("controls").EnumerateArray().First(c=>c.GetProperty("id").GetString()=="displayItemVariant"&&c.GetProperty("options").GetArrayLength()>1);
        var option=variantControl.GetProperty("options").EnumerateArray().First(o=>!o.GetProperty("active").GetBoolean()).GetProperty("id").GetInt32();
        var variantEdited=await engine.ExecuteAsync(new{op="item-draft",mode="control",raw=variantInfo.GetProperty("raw").GetString(),control="displayItemVariant",value=option},default,before.Xml);
        check(variantEdited.State.GetProperty("authoring").GetProperty("raw").GetString()!=variantInfo.GetProperty("raw").GetString(),"Unique variant selection changes the draft through upstream callbacks");
        var rejected=false;try{await engine.ExecuteAsync(new{op="item-unique-draft",key="not an upstream item"},default,before.Xml);}catch(IOException){rejected=true;}
        check(rejected,"Unknown unique identities are rejected");
    }
}
