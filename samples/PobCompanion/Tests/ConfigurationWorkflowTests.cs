using System.Reflection;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ConfigurationWorkflowTests
{
    internal static async Task RunAsync(int game, string root, string runtime, string adapter, string xml, Action<bool,string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pob-configuration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(directory, id + ".xml"), xml);
        File.WriteAllText(Path.Combine(directory, "selected.txt"), id);
        await using var engine = new PobEngine(root, runtime, adapter);
        var widget = new PobWidget(game, engine, directory);
        WidgetTestHost.Attach(widget, new WidgetTestHostServicesBuilder().Build());
        PobResult Result() => (PobResult)typeof(PobWidget).GetField("result", BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(widget)!;
        IEnumerable<ViewNode> Walk(ViewNode n) => new[]{n}.Concat(n.Children.SelectMany(Walk));
        ViewNode View() => widget.Render().CreateSnapshot("configuration", 1).Root;
        async Task Idle()
        {
            var deadline = Environment.TickCount64 + 65000;
            while (Walk(View()).Any(n => n.Id == "pob.cancel-operation"))
            { if (Environment.TickCount64 > deadline) throw new TimeoutException("Configuration edit did not settle."); await Task.Delay(20); }
        }
        async Task Action(string action, string? text = null)
        { await widget.OnActionAsync(new(action, "configuration") { CommittedText=text }); await Idle(); }
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive); await Idle();
            await Action("section.4");
            var original = Result();
            check(original.Configuration.SupportsChangedFilter && original.Configuration.Options.Count>0,
                "Every upstream configuration option supplies typed default comparison");
            var option = original.Configuration.Options.First(c => c.Changed==false && c.Raw.GetProperty("value").ValueKind==JsonValueKind.False);
            var sections = original.Configuration.Options.Select(c=>c.Section).Distinct().ToArray();
            await Action("config-section."+Array.IndexOf(sections,option.Section));
            check(Walk(View()).Any(n=>n.Id=="pob.condition."+option.Index),"Default setting is initially reachable");
            await Action("config-changed");
            check(!Walk(View()).Any(n=>n.Id=="pob.condition."+option.Index),"Changed-only filter removes upstream defaults");
            check(Result().Xml==original.Xml,"Filtering configuration does not mutate the build");
            await Action("config-changed");
            await Action("condition."+option.Index);
            var edited=Result().Configuration.Options.Single(c=>c.Id==option.Id);
            check(edited.Changed==true,"Editing a default toggle is classified by upstream default comparison");
            await Action("config-changed");
            check(Walk(View()).Any(n=>n.Id=="pob.condition."+edited.Index) &&
                Walk(View()).Any(n=>n.Id=="pob.config.category."+Array.IndexOf(sections,option.Section)&&n.Text!.Contains("changed")),
                "Changed setting remains reachable with its section count");
            var source=Walk(View()).Single(n=>n.Id=="pob.search").TextEntrySuggestions!;
            var query=new TextSuggestionQuery("pob.search",source.Key,Guid.NewGuid().ToString("N"),1,edited.Title,source.MaximumResults,256);
            check(!(await widget.OnSuggestTextAsync(query,default)).Unavailable,"Changed-filter suggestions use the current filter source");
            await Action("config-changed");
            check((await widget.OnSuggestTextAsync(query,default)).Unavailable,"Filter changes reject a late suggestion response source");
            await Action("undo");
            check(Result().Configuration.Options.Single(c=>c.Id==option.Id).Changed==false,"Undo restores the upstream default classification");
            await Action("config-changed");
            await Action("search","no-such-configuration-setting-2197");
            check(Walk(View()).Any(n=>n.Id=="pob.config.empty"),"Changed-filter empty state stays explicit and navigable");
            await Action("config-clear-search");
            await Action("config-changed");
            check(Walk(View()).Any(n=>n.Id=="pob.condition."+option.Index),"Clearing filter returns to the original configuration section");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); Directory.Delete(directory,true); }
    }
}
