using System.Diagnostics.Tracing;
using WidgetRail.Samples.DiscordWebView;

internal static class DiscordReadDiagnosticsTests
{
    internal static void Run(Action<bool,string> check)
    {
        using var listener=new Listener();
        DiscordReadDiagnostics.Log.RecordFailure("messageHistory","rate_limited");
        DiscordReadDiagnostics.Log.RecordFailure("messageChanges","message_watch_changed");
        check(listener.Events.Count==2&&listener.Events[0].SequenceEqual(new[]{"history","rate_limited"})&&
            listener.Events[1].SequenceEqual(new[]{"changes","message_watch_changed"}),
            "Read diagnostics retain only the fixed operation and failure vocabulary");
        DiscordReadDiagnostics.Log.RecordFailure("messageHistory","https://example.invalid/private?token=secret");
        check(listener.Events.Last().SequenceEqual(new[]{"history","provider_error"}),
            "Read diagnostics never emit arbitrary provider text or signed destinations");
        DiscordReadDiagnostics.Log.RecordFailure("messageSend","message_rejected");
        DiscordReadDiagnostics.Log.RecordFailure("unknown-private-operation","account_changed");
        check(listener.Events.Count==3,"Read diagnostics ignore writes and unknown operation names");
    }
    private sealed class Listener:EventListener
    {
        internal readonly List<string[]> Events=[];
        protected override void OnEventSourceCreated(EventSource source)
        {if(source.Name=="WidgetRail-Discord-Reads")EnableEvents(source,EventLevel.Warning);}
        protected override void OnEventWritten(EventWrittenEventArgs args)
        {if(args.EventId==1)Events.Add(args.Payload!.Select(value=>(string)value!).ToArray());}
    }
}
