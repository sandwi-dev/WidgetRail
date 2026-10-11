using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.PlatformBroker;

public sealed record PerformanceTarget(string Id, string ApplicationName, string Title);
public sealed record PerformanceRequest(string? TargetId = null);
public sealed record PerformanceEnableResult(string State);
public sealed record PerformanceSnapshot(DateTimeOffset SampledAtUtc, double? CpuPercent, double? GpuPercent,
    long? MemoryUsedBytes, long? MemoryTotalBytes, long? DedicatedGpuMemoryBytes,
    double? FramesPerSecond, string FpsState, string? SelectedTargetId, IReadOnlyList<PerformanceTarget> Targets);

public interface IPerformancePlatformBackend
{
    Task<PerformanceSnapshot> GetPerformanceAsync(string? targetId, CancellationToken token) =>
        Task.FromException<PerformanceSnapshot>(new BrokerException("performance_unavailable", "Performance monitoring is unavailable."));
    Task<PerformanceEnableResult> EnableFpsAsync(CancellationToken token) =>
        Task.FromResult(new PerformanceEnableResult("unavailable"));
    Task<PerformanceEnableResult> DisableFpsAsync(CancellationToken token) =>
        Task.FromResult(new PerformanceEnableResult("unavailable"));
}

internal sealed class PerformanceCapabilityDomain(IPlatformBrokerBackend backend)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, string> _targets = new(StringComparer.Ordinal);

    internal async Task<JsonElement> ExecuteAsync(string operation, JsonElement payload, CancellationToken token)
    {
        if (operation is PlatformCapabilities.PerformanceEnableFps or PlatformCapabilities.PerformanceDisableFps)
        {
            BrokerCapabilityDomains.DemandEmptyPayload(payload);
            var result = await (operation == PlatformCapabilities.PerformanceEnableFps
                ? backend.EnableFpsAsync(token) : backend.DisableFpsAsync(token)).ConfigureAwait(false);
            if (!PerformanceValidation.ValidFpsState(result.State)) throw new BrokerException("invalid_backend_data", "Invalid collector state.");
            return BrokerJson.ToElement(result);
        }
        if (operation != PlatformCapabilities.PerformanceGet) throw new BrokerException("unsupported_operation", "Unsupported performance operation.");
        var request = BrokerJson.ParsePayload<PerformanceRequest>(payload);
        if (request.TargetId is not null) ContractValidation.OpaqueId(request.TargetId);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string? target = null;
            if (request.TargetId is not null && !_targets.TryGetValue(request.TargetId, out target))
                throw new BrokerException("performance_target_unavailable", "Select a current application.");
            var snapshot = await backend.GetPerformanceAsync(target, token).ConfigureAwait(false);
            PerformanceValidation.Validate(snapshot);
            var old = _targets.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
            var next = new Dictionary<string, string>(StringComparer.Ordinal);
            var targets = snapshot.Targets.Select(item =>
            {
                var id = old.GetValueOrDefault(item.Id) ?? "performance-" + Guid.NewGuid().ToString("N");
                next.Add(id, item.Id);
                return item with { Id = id };
            }).ToArray();
            var selected = next.FirstOrDefault(pair => pair.Value == snapshot.SelectedTargetId).Key;
            _targets = next;
            return BrokerJson.ToElement(snapshot with { SelectedTargetId = selected, Targets = targets });
        }
        finally { _gate.Release(); }
    }
}

public static class PerformanceValidation
{
    public static bool ValidFpsState(string? value) => value is "not-enabled" or "enabling" or "declined" or "missing" or
        "unavailable" or "idle" or "collecting" or "warming" or "permission-required" or "failed" or "no-target" or "no-frames";
    public static void Validate(PerformanceSnapshot value)
    {
        static bool Percent(double? v) => v is null || double.IsFinite(v.Value) && v is >= 0 and <= 100;
        static bool Text(string? v, int max) => !string.IsNullOrWhiteSpace(v) && v.Length <= max && !v.Any(char.IsControl);
        if (value is null || !Percent(value.CpuPercent) || !Percent(value.GpuPercent) ||
            value.MemoryUsedBytes < 0 || value.MemoryTotalBytes < 0 || value.MemoryUsedBytes > value.MemoryTotalBytes || value.DedicatedGpuMemoryBytes < 0 ||
            value.FramesPerSecond is { } fps && (!double.IsFinite(fps) || fps < 0 || fps > 10_000 || value.FpsState != "collecting") ||
            !ValidFpsState(value.FpsState) || value.Targets is null || value.Targets.Count > 64 ||
            value.Targets.Any(t => t is null || !Text(t.Id, 128) || !Text(t.ApplicationName, 120) || !Text(t.Title, 240)) ||
            value.Targets.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != value.Targets.Count ||
            value.SelectedTargetId is not null && !value.Targets.Any(t => t.Id == value.SelectedTargetId))
            throw new BrokerException("invalid_backend_data", "Performance information is unavailable.");
    }
}
