using System.Text.Json;

namespace WidgetRail.Samples.DiscordWebView;

internal sealed record DiscordReceiverFailure(string? Lease,string Code);

/// <summary>One receiver's domain health, bound once to its negotiated relay lease.</summary>
internal sealed class DiscordReceiverHealth
{
    private readonly object gate=new();
    private readonly TaskCompletionSource<DiscordReceiverFailure> failure=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? lease;
    private bool retired;
    internal Task<DiscordReceiverFailure> Failure=>failure.Task;
    internal void Bind(string value)
    {
        if(value.Length!=32||value.Any(c=>c is not (>= 'a' and <= 'f') and not (>= '0' and <= '9')))
            throw new InvalidDataException("Invalid receiver lease.");
        lock(gate)
        {
            if(retired||lease is not null||failure.Task.IsCompleted)throw new InvalidOperationException("Receiver is no longer available.");
            lease=value;
        }
    }
    internal void Read(JsonElement message)
    {
        if(message.ValueKind!=JsonValueKind.Object||!message.TryGetProperty("receiverHealth",out var version)||
            version.ValueKind!=JsonValueKind.Number||!version.TryGetInt32(out var number)||number!=1||!message.TryGetProperty("lease",out var key)||key.ValueKind!=JsonValueKind.String||
            !message.TryGetProperty("phase",out var phase)||phase.ValueKind!=JsonValueKind.String)return;
        var code=phase.GetString();
        if(code is not("rtc_failed" or "rtc_closed" or "playback_failed" or "track_ended"))return;
        lock(gate)
            if(!retired&&lease is not null&&key.GetString()==lease)failure.TrySetResult(new(lease,code));
    }
    internal void SessionEnded()
    {lock(gate)if(!retired)failure.TrySetResult(new(lease,"session_closed"));}
    internal void Retire()
    {lock(gate){retired=true;failure.TrySetCanceled();}}
}
