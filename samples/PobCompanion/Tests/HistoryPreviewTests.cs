using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class HistoryPreviewTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string build = "<PathOfBuilding><Build level='80'><PlayerStat stat='DPS' value='10'/></Build><Tree activeSpec='1'><Spec title='Mapping' nodes='1,2'/><Spec title='Bossing' nodes='2,3'/></Tree><Items activeItemSet='1'><Item id='1'>Rarity: RARE\nNew Item\nIron Hat\n+10 to maximum Life</Item><ItemSet id='1' title='Mapping'><Slot name='Helmet' itemId='1'/></ItemSet><ItemSet id='2' title='Bossing'/></Items><Skills activeSkillSet='1'><SkillSet id='1' title='Mapping'><Skill><Gem nameSpec='Spark' level='19' quality='20'/></Skill></SkillSet></Skills><Config activeConfigSet='1'><ConfigSet id='1'><Input name='conditionEnemyShocked' boolean='true'/></ConfigSet></Config><Notes>First note</Notes></PathOfBuilding>";
        var level = PobHistoryPreview.Describe(build.Replace("level='80'", "level='81'"), build);
        check(level.Changes is [{ Before: "81", After: "80" }], "Undo describes the values that will actually be restored");
        var redo = PobHistoryPreview.Describe(build, build.Replace("level='80'", "level='81'"));
        check(redo.Changes is [{ Before: "80", After: "81" }], "Redo reverses the before/after direction");
        var switched = build.Replace("activeSpec='1'", "activeSpec='2'").Replace("activeItemSet='1'", "activeItemSet='2'");
        var loadout = PobHistoryPreview.Describe(switched, build);
        check(loadout.Title == "Switch loadout / sets" && loadout.Changes.Length == 2 && loadout.Changes.All(c => c.Before == "Bossing" && c.After == "Mapping"), "Loadout previews name both the current and target saved sets");
        var gem = PobHistoryPreview.Describe(build, build.Replace("level='19'", "level='20'"));
        check(gem.Changes is [{ Before: "19", After: "20" }] && gem.Changes[0].Label.Contains("Spark"), "Gem changes identify the gem and exact level");
        var item = PobHistoryPreview.Describe(build, build.Replace("+10 to maximum Life", "+25 to maximum Life"));
        check(item.Changes is [{ Before: "+10 to maximum Life", After: "+25 to maximum Life" }], "Item previews show changed lines rather than a repeated raw header");
        var duplicate = PobHistoryPreview.Describe(build.Replace("+10 to maximum Life", "+10 to maximum Life\n+10 to maximum Life"), build);
        check(duplicate.Changes.Single().Before == "+10 to maximum Life", "Repeated modifier lines retain occurrence-sensitive changes");
        using var context = JsonDocument.Parse("{\"conditions\":[{\"id\":\"conditionEnemyShocked\",\"title\":\"Is the enemy Shocked?\"}],\"nodes\":[{\"id\":1,\"name\":\"Intelligence\"},{\"id\":2,\"name\":\"Deep Wisdom\"},{\"id\":3,\"name\":\"Strength\"}]}");
        var config = PobHistoryPreview.Describe(build, build.Replace("boolean='true'", "boolean='false'"), context.RootElement);
        check(config.Changes.Single().Label.Contains("Is the enemy Shocked?") && config.Changes.Single().After == "Off", "Configuration uses upstream labels and readable boolean values");
        var tree = PobHistoryPreview.Describe(build, build.Replace("nodes='1,2'", "nodes='2,3'"), context.RootElement);
        check(tree.Changes.Single().Before.Contains("Intelligence") && tree.Changes.Single().After.Contains("Strength"), "Equal-sized passive edits show changed nodes rather than just counts");
        check(PobHistoryPreview.Describe(build, build.Replace("value='10'", "value='100'")).Changes.Length == 0, "Recomputed stats do not crowd the history description");
        check(PobHistoryPreview.Describe(build, "<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///missing'>]><x>&y;</x>").Changes.Length == 0, "History descriptions reject external entities without breaking rendering");
        var longNote = PobHistoryPreview.Describe(build, build.Replace("First note", new string('a', 4000)));
        check(longNote.Changes.Single().After.Length <= 161, "Long user-authored text stays bounded in the passive preview");
        var many = PobHistoryPreview.Describe(build, build.Replace("level='80'", "level='81' className='Witch' ascendClassName='Infernalist' mainSocketGroup='2' bandit='None'"));
        check(many.Changes.Length == 4 && many.OtherChanges == 1, "Preview truncation announces additional changes");
    }
}
