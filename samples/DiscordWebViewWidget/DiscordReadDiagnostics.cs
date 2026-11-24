using System.Diagnostics.Tracing;
using System.Text.Json;

namespace WidgetRail.Samples.DiscordWebView;

// Standard opt-in .NET diagnostics. Never emit arguments, IDs, message text,
// URLs, exception messages or arbitrary provider strings.
[EventSource(Name="WidgetRail-Discord-Reads")]
internal sealed class DiscordReadDiagnostics : EventSource
{
    internal static readonly DiscordReadDiagnostics Log = new();
    private DiscordReadDiagnostics() { }

    [NonEvent]
    internal void RecordFailure(string method,string code)
    {
        if(!IsEnabled())return;
        var operation=method switch
        {
            "messageHistory"=>"history", "messageChanges"=>"changes", "messageRecover"=>"recovery",
            "messageObserve"=>"watch", "messageMarkRead"=>"read_ack",
            "messageConversations"=>"conversations", "messageLocateConversation"=>"locate", _=>null
        };
        if(operation is null)return;
        var reason=code switch
        {
            "rate_limited" or "message_forbidden" or "conversation_unavailable" or "message_rejected" or
            "message_unconfirmed" or "invalid_message_response" or "message_watch_changed" or
            "read_visit_changed" or "read_ack_busy" or "read_ack_unavailable" or "messaging_unavailable" or
            "account_changed" or "messaging_retired" or "invalid_message_revision" or "invalid_read_target"=>code,
            _=>"provider_error"
        };
        ReadFailed(operation,reason);
    }

    [Event(1,Level=EventLevel.Warning)]
    public void ReadFailed(string operation,string reason)=>WriteEvent(1,operation,reason);

    [NonEvent]
    internal void RecordStreamSample(DiscordWebViewWidget.ShareCadenceSample sample)
    {
        if(IsEnabled(EventLevel.Informational,EventKeywords.None))StreamSample(JsonSerializer.Serialize(sample));
    }

    [Event(2,Level=EventLevel.Informational)]
    public void StreamSample(string metrics)=>WriteEvent(2,metrics);
}
