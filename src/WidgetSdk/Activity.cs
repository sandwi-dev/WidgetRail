using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetSdk;

public static class WidgetActivityCapabilities
{
    public const string Service = "widget.activity.v1";
    public static WidgetCapabilityOperation<WidgetActivity, WidgetActivityReceipt> Report { get; } = new(Service, "activity.report");
}

/// <summary>Package-scoped current activity, available while visible, interactive or background.
/// Each source owns an independent contribution; clearing it preserves other sources. Host retirement clears it automatically.
/// Identical reports share delivery and rapid changes coalesce to the latest snapshot. A replaced pending aggregate
/// completes with Superseded=true. Cancellation cancels the caller's wait, not an already submitted state change.
/// Prefer automatic host browser/media detection when using framework playback surfaces.</summary>
public sealed class WidgetActivityService
{
    private sealed record Pending(WidgetActivity Value, TaskCompletionSource<WidgetActivityReceipt> Completion);
    private readonly IWidgetCapabilityClient client;
    private readonly object gate = new();
    private Pending? pending, sending;
    private WidgetActivity? accepted;
    private bool pumping;
    private long nextSend;
    internal WidgetActivityService(IWidgetCapabilityClient client) => this.client = client;
    private readonly Dictionary<string, WidgetActivity> sources = new(StringComparer.Ordinal);
    /// <summary>Replace one component's contribution. Up to 16 stable source IDs per widget.</summary>
    public ValueTask<WidgetActivityReceipt> SetAsync(string source, WidgetActivity activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return ChangeAsync(source, activity, cancellationToken);
    }
    /// <summary>Remove only this component's contribution; other activity remains active.</summary>
    public ValueTask<WidgetActivityReceipt> ClearAsync(string source, CancellationToken cancellationToken = default) =>
        ChangeAsync(source, null, cancellationToken);
    private ValueTask<WidgetActivityReceipt> ChangeAsync(string source, WidgetActivity? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(source) || source.Length > 64 || source.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
            throw new ArgumentException("Activity source must be 1–64 ASCII letters, digits, dots, hyphens or underscores.", nameof(source));
        cancellationToken.ThrowIfCancellationRequested();
        Task<WidgetActivityReceipt> result;
        var start = false;
        lock (gate)
        {
            if (value is null || value == new WidgetActivity()) sources.Remove(source);
            else
            {
                if (!sources.ContainsKey(source) && sources.Count >= 16) throw new InvalidOperationException("A widget can report at most 16 activity sources.");
                sources[source] = value;
            }
            var activity = new WidgetActivity(sources.Values.Any(s => s.LocalAudio), sources.Values.Any(s => s.RemotePlayback), sources.Values.Any(s => s.Recording));
            if (pending?.Value == activity) result = pending.Completion.Task;
            else if (pending is null && sending?.Value == activity) result = sending.Completion.Task;
            else
            {
                pending?.Completion.TrySetResult(new(false, Superseded: true));
                pending = null;
                if (sending is null && accepted == activity) result = Task.FromResult(new WidgetActivityReceipt(true));
                else
                {
                    pending = new(activity, new(TaskCreationOptions.RunContinuationsAsynchronously));
                    result = pending.Completion.Task;
                    if (!pumping) { pumping = true; start = true; }
                }
            }
        }
        if (start) _ = PumpAsync();
        return new(cancellationToken.CanBeCanceled ? result.WaitAsync(cancellationToken) : result);
    }
    private async Task PumpAsync()
    {
        while (true)
        {
            // At most one delivery and one replaceable snapshot; never queue every playback tick.
            var delay = nextSend - Environment.TickCount64;
            if (delay > 0) await Task.Delay((int)Math.Min(delay, 100)).ConfigureAwait(false);
            Pending current;
            lock (gate)
            {
                if (pending is null) { pumping = false; return; }
                current = pending; pending = null;
                if (accepted == current.Value) { current.Completion.TrySetResult(new(true)); continue; }
                sending = current;
            }
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var receipt = await client.InvokeAsync(WidgetActivityCapabilities.Report, current.Value, deadline.Token).ConfigureAwait(false)
                    ?? throw new WidgetCapabilityException("malformed_response", "Activity response is invalid.");
                lock (gate) { if (receipt.Accepted) accepted = current.Value; }
                current.Completion.TrySetResult(receipt);
            }
            catch (Exception error) { current.Completion.TrySetException(error); }
            finally { lock (gate) sending = null; nextSend = Environment.TickCount64 + 100; }
        }
    }
}
