namespace WidgetRail.Samples.DiscordWebView;

internal sealed record DiscordReceiverFailure(string? Lease,string Code);

/// <summary>One receiver's domain health, bound once to its selected video lease.</summary>
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
    internal void SessionEnded()
    {lock(gate)if(!retired)failure.TrySetResult(new(lease,"session_closed"));}
    internal void Retire()
    {lock(gate){retired=true;failure.TrySetCanceled();}}
}
