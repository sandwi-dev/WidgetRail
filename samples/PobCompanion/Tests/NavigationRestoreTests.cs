using System.Reflection;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class NavigationRestoreTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        var directory=Path.Combine(Path.GetTempPath(),"pob-navigation-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);var id=Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(directory,id+".xml"),xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        var section=typeof(PobWidget).GetField("section",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var busy=typeof(PobWidget).GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic)!;
        PobWidget? widget=null;
        async Task Idle()
        {
            var deadline=Environment.TickCount64+65000;
            while((bool)busy.GetValue(widget)!)
            {if(Environment.TickCount64>deadline)throw new TimeoutException("Navigation restore did not settle.");await Task.Delay(15);}
            _=widget!.Render().CreateSnapshot("navigation-restore",1);
        }
        async Task Reload()
        {
            if(widget is not null)await WidgetTestHost.DestroyAsync(widget);
            widget=new PobWidget(game,new PobEngine(root,runtime,adapter),directory);
            WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
        }
        try
        {
            await Reload();check((string)section.GetValue(widget)! =="Calcs","Missing navigation preference defaults to Calcs");
            var names=new[]{"Calcs","Tree","Skills","Items","Configuration","Notes"};
            foreach(var index in new[]{2,3,4,1,5,0})
            {
                await widget!.OnActionAsync(new("section."+index,"test"));await Idle();await Reload();
                check((string)section.GetValue(widget)! ==names[index],names[index]+" restores after widget destruction and recreation");
                check(File.ReadAllText(Path.Combine(directory,id+".xml"))==xml,"Navigation does not edit the build XML");
            }
            await widget!.OnActionAsync(new("next-section","test"));await Idle();await Reload();
            check((string)section.GetValue(widget)! =="Skills","Bumper navigation persists the resulting tab");
            await WidgetTestHost.DestroyAsync(widget!);widget=null;
            File.WriteAllText(Path.Combine(directory,"navigation.txt"),"invalid-tab");await Reload();
            check((string)section.GetValue(widget)! =="Calcs","Unknown saved tabs fall back safely");
            await WidgetTestHost.DestroyAsync(widget!);widget=null;
            await using(var engine=new PobEngine(root,runtime,adapter))
            {var empty=await engine.ExecuteAsync(new{op="new"},default);File.WriteAllText(Path.Combine(directory,id+".xml"),empty.Xml);}
            File.WriteAllText(Path.Combine(directory,"navigation.txt"),"Items");await Reload();
            check((string)section.GetValue(widget)! =="Items","Empty builds restore Items with a valid view");
            check(!Directory.EnumerateFiles(directory,"*.work.json").Any(),"Navigation remains outside undo and recovery history");
        }
        finally{if(widget is not null)await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
}
