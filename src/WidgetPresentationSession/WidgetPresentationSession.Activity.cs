using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetPresentationSession;
public sealed record WidgetPresentationActivity(WidgetHostEffectAuthority Authority, WidgetActivity Activity);
public sealed class WidgetActivityEventArgs(WidgetPresentationActivity activity) : EventArgs
{
    public WidgetPresentationActivity Activity { get; } = activity;
}
public sealed partial class WidgetPresentationSession
{
    private readonly Dictionary<string, BridgeWorkerRun> activityRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BridgeWorkerRun> retiredActivityRuns = new(StringComparer.Ordinal);
    private static int CompareActivityRuns(BridgeWorkerRun left, BridgeWorkerRun right) =>
        left.RegistryGeneration == right.RegistryGeneration ? left.StartOrdinal.CompareTo(right.StartOrdinal) : left.RegistryGeneration.CompareTo(right.RegistryGeneration);
    private readonly Dictionary<string, long> activitySequences = new(StringComparer.Ordinal);
    public event EventHandler<WidgetActivityEventArgs>? ActivityReceived;
    private void HandleWidgetActivity(JsonElement payload)
    {
        var message = BridgeJson.FromElement<BridgeWidgetActivity>(payload);
        _ = ReadHostIdentifier(payload, "widgetId");
        _ = ReadHostIdentifier(payload, "instanceId");
        _ = ReadHostIdentifier(payload, "runtimeGeneration");
        if (message.Sequence <= 0 || message.InitiatedAtMilliseconds < 0 || message.Activity is null ||
            message.WorkerRun is not { RegistryGeneration: > 0, StartOrdinal: > 0 })
            throw new BridgeProtocolException("Invalid widget activity.");
        WidgetHostEffectAuthority? authority = null;
        using (_gate.Enter())
        {
            if (_disposed || _terminalFailure is not null || message.Sequence <= activitySequences.GetValueOrDefault(message.WidgetId) ||
                retiredActivityRuns.TryGetValue(message.WidgetId, out var retiredRun) && CompareActivityRuns(message.WorkerRun, retiredRun) <= 0 ||
                activityRuns.TryGetValue(message.WidgetId, out var currentRun) && CompareActivityRuns(message.WorkerRun, currentRun) < 0) return;
            if (_descriptors.TryGetValue(message.WidgetId, out var descriptor) && !descriptor.IsStopped && descriptor.InstanceId == message.InstanceId &&
                descriptor.RuntimeGeneration == message.RuntimeGeneration &&
                (!_hostEffectAdmissionBoundaries.TryGetValue(descriptor.Id, out var boundary) || message.InitiatedAtMilliseconds > boundary))
            {
                activitySequences[descriptor.Id] = message.Sequence;
                activityRuns[descriptor.Id] = message.WorkerRun;
                authority = new(descriptor.Id, descriptor.InstanceId, descriptor.RuntimeGeneration, _sessionGenerations.GetValueOrDefault(descriptor.Id));
            }
        }
        if (authority is not null) ActivityReceived?.Invoke(this, new(new(authority, message.Activity)));
    }
    private void HandleWidgetActivityRetired(JsonElement payload)
    {
        var message = BridgeJson.FromElement<BridgeWidgetActivityRetired>(payload);
        _ = ReadHostIdentifier(payload, "widgetId");
        if (message.WorkerRun is not { RegistryGeneration: > 0, StartOrdinal: > 0 }) throw new BridgeProtocolException("Invalid activity retirement.");
        WidgetHostEffectAuthority? authority = null;
        using (_gate.Enter())
        {
            if (_disposed || _terminalFailure is not null || !_descriptors.TryGetValue(message.WidgetId, out var descriptor)) return;
            if (!retiredActivityRuns.TryGetValue(message.WidgetId, out var previous) || CompareActivityRuns(message.WorkerRun, previous) > 0)
                retiredActivityRuns[message.WidgetId] = message.WorkerRun;
            if (activityRuns.TryGetValue(message.WidgetId, out var current) && CompareActivityRuns(current, message.WorkerRun) <= 0)
            {
                activityRuns.Remove(message.WidgetId);
                authority = new(descriptor.Id, descriptor.InstanceId, descriptor.RuntimeGeneration, _sessionGenerations.GetValueOrDefault(descriptor.Id));
            }
        }
        if (authority is not null) ActivityReceived?.Invoke(this, new(new(authority, new())));
    }

}
