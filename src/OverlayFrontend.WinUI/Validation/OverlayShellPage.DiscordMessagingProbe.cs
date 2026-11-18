using System.Text.Json.Nodes;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Opt-in bounded diagnostics. No conversation identity, text, URLs, credentials,
    // or raw exception detail is returned by the browser-side probe.
    internal void EnableDiscordMessagingProbe(string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var timer=DispatcherQueue.CreateTimer();timer.Interval=TimeSpan.FromSeconds(1);
        var samples=0;var busy=false;
        timer.Tick+=async(_,_)=>
        {
            if(++samples>60||retired){timer.Stop();return;}
            if(busy||browserOwner is null)return;
            busy=true;
            try
            {
                var result=new JsonObject{["sample"]=samples,["backends"]=await browserOwner.ReadDiscordMessagingProbeAsync()};
                File.AppendAllText(output,result.ToJsonString()+Environment.NewLine);
            }
            catch
            {
                try{File.AppendAllText(output,new JsonObject{["sample"]=samples,["probeError"]="unavailable"}.ToJsonString()+Environment.NewLine);}
                catch{timer.Stop();}
            }
            finally{busy=false;}
        };
        Loaded+=(_,_)=>timer.Start();Unloaded+=(_,_)=>timer.Stop();if(IsLoaded)timer.Start();
    }
}
