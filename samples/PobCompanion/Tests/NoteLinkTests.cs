using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;

internal static class NoteLinkTests
{
    internal static void Run(Action<bool,string> check)
    {
        const string id="dQw4w9WgXcQ";
        var links=PobNoteLinks.Read("[Leveling walkthrough](https://www.youtube.com/watch?v="+id+"&t=1m30s)\nhttps://example.org/build-a\nhttps://example.org/build-b");
        check(links[0].Label=="Watch video · Leveling walkthrough","Authored video title replaces generic guide label");
        check(links[1].Label=="Open link · example.org/build-a"&&links[2].Label.EndsWith("/build-b"),"Plain links identify distinct paths on the same host");
        check(links[0].Intent.ContractId==WidgetIntentContracts.OpenVideo&&links[0].Intent.Payload.GetProperty("startTimeSeconds").GetDouble()==90,"YouTube video intent preserves a compound timestamp");
        check(links[0].Intent.Fallback!.Payload.GetProperty("url").GetString()==links[0].Url,"Video fallback preserves the complete original URL");
        foreach(var url in new[]{"https://youtu.be/"+id,"https://m.youtube.com/watch?v="+id,"https://youtube.com/shorts/"+id,"https://youtube.com/live/"+id,"https://youtube.com/embed/"+id})
        {
            var intent=PobNoteLinks.Read(url)[0].Intent;
            check(intent.ContractId==WidgetIntentContracts.OpenVideo&&intent.Payload.GetProperty("videoId").GetString()==id,"Video form routes through YouTube: "+new Uri(url).Host+new Uri(url).AbsolutePath);
            check(intent.IsWellFormed()&&CompiledWidgetIntentContract.Create(WidgetIntentContracts.Video).Accepts(intent.Payload),"Video request satisfies the existing receiver contract");
        }
        foreach(var (suffix,seconds) in new[]{("?t=1h2m3s",3723d),("?start=90.5",90.5),("#t=2m",120d),("?t=0",0d)})
            check(PobNoteLinks.Read("https://youtu.be/"+id+suffix)[0].Intent.Payload.GetProperty("startTimeSeconds").GetDouble()==seconds,"Preserve video timestamp "+suffix);
        foreach(var url in new[]{"https://youtube.com/playlist?list=PLexample","https://youtube.com/@creator","https://youtube.com.evil.example/watch?v="+id,
            "https://youtu.be/invalid","https://youtu.be/"+id+"?t=-1","https://youtu.be/"+id+"?t=90000","https://youtu.be/"+id+"?t=unknown",
            "https://youtube.com/watch?v="+id+"&v=aaaaaaaaaaa","https://youtu.be:8443/"+id})
            check(PobNoteLinks.Read(url)[0].Intent.ContractId==WidgetIntentContracts.OpenWebPage,"Non-video or unsupported seek stays a web link: "+url);
        var punctuation=PobNoteLinks.Read("[Passive tree](https://example.org/build_(scion)). Again: https://example.org/build_(scion)\nhttp://example.org/help");
        check(punctuation.Count==2&&punctuation[0].Url=="https://example.org/build_(scion)"&&punctuation[0].Label=="Open link · Passive tree","Balanced URL parentheses and authored titles survive deduplication");
        check(PobNoteLinks.Read("https://user:password@example.org/private").Count==0,"Credential-bearing links are not turned into launch buttons");
        foreach(var status in Enum.GetValues<WidgetIntentStatus>())
            check(PobNoteLinks.UseFallback(new("test",links[0].Intent,status))==(status is WidgetIntentStatus.Unavailable or WidgetIntentStatus.Rejected),"Fallback policy for "+status);
        check(!PobNoteLinks.UseFallback(new("test",links[1].Intent,WidgetIntentStatus.Unavailable)),"Ordinary web links do not recursively fall back");
        var overlayLinks=PobNoteLinks.Read("https://example.org/build https://youtu.be/"+id,true);
        check(overlayLinks[0].Intent.Routing==WidgetIntentRouting.OverlayBrowser&&overlayLinks[0].Intent.OverlayBrowserStorage==BrowserStorage.Persistent,"Ordinary links request the persistent host browser explicitly");
        check(overlayLinks[1].Intent.ContractId==WidgetIntentContracts.OpenVideo&&overlayLinks[1].Intent.Fallback!.Routing==WidgetIntentRouting.OverlayBrowser,"YouTube retains the video intent and honors website preference on fallback");
        var prefsDirectory=Path.Combine(Path.GetTempPath(),"pob-preferences-"+Guid.NewGuid().ToString("N"));
        try
        {
            var first=new PobPreferences(prefsDirectory);var second=new PobPreferences(prefsDirectory);
            check(!first.OverlayBrowser,"Websites default to Windows");
            first.OverlayBrowser=true;check(second.OverlayBrowser&&new PobPreferences(prefsDirectory).OverlayBrowser,"Both widgets and a restarted reader observe the shared preference");
            second.OverlayBrowser=false;check(!first.OverlayBrowser,"Changing preference in the second widget updates the first");
            File.WriteAllText(Path.Combine(prefsDirectory,"preferences.json"),"invalid");check(!first.OverlayBrowser,"Malformed preference falls back to Windows without failing widget startup");
        }
        finally{if(Directory.Exists(prefsDirectory))Directory.Delete(prefsDirectory,true);}
    }
}
