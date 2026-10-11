using System.Text.Json.Serialization;

namespace WidgetRail.WidgetSdk;

public sealed record WidgetPerformanceTarget(
    [property: JsonRequired] string Id, [property: JsonRequired] string ApplicationName, [property: JsonRequired] string Title);
public sealed record WidgetPerformanceRequest(string? TargetId = null);
public sealed record WidgetPerformanceEnableResult([property: JsonRequired] string State);
/// <summary>Usage metrics are system-wide; FPS refers to SelectedTargetId. Null means unmeasured, never zero.</summary>
public sealed record WidgetPerformanceSnapshot(
    [property: JsonRequired] DateTimeOffset SampledAtUtc,
    double? CpuPercent, double? GpuPercent, long? MemoryUsedBytes, long? MemoryTotalBytes,
    long? DedicatedGpuMemoryBytes, double? FramesPerSecond,
    [property: JsonRequired] string FpsState, string? SelectedTargetId,
    [property: JsonRequired] IReadOnlyList<WidgetPerformanceTarget> Targets);

public static class WidgetPerformanceCapabilities
{
    public static WidgetCapabilityOperation<WidgetPerformanceRequest, WidgetPerformanceSnapshot> Get { get; } =
        new("system.performance.read.v1", "performance.get");
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetPerformanceEnableResult> EnableFps { get; } =
        new("system.performance.fps.control.v1", "performance.fps.enable");
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetPerformanceEnableResult> DisableFps { get; } =
        new("system.performance.fps.control.v1", "performance.fps.disable");
}

/// <summary>
/// Shared host metrics. Request at most once per second while visible or pinned.
/// FPS enablement must follow an explicit user action and may show Windows UAC.
/// </summary>
public sealed class WidgetPerformanceService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetPerformanceService(IWidgetCapabilityClient client) => _client = client;

    /// <param name="targetId">A target ID from this service, or null for the top application excluding WidgetRail.</param>
    public async ValueTask<WidgetPerformanceSnapshot> GetAsync(string? targetId = null, CancellationToken cancellationToken = default)
    {
        if (targetId is not null && !ValidId(targetId)) throw new ArgumentException("Use a current performance target ID.", nameof(targetId));
        var result = await _client.InvokeAsync(WidgetPerformanceCapabilities.Get, new(targetId), cancellationToken).ConfigureAwait(false);
        Validate(result);
        return result with { Targets = result.Targets.ToArray() };
    }

    public async ValueTask<WidgetPerformanceEnableResult> EnableFpsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.InvokeAsync(WidgetPerformanceCapabilities.EnableFps, new(), cancellationToken).ConfigureAwait(false);
        if (result is null || !ValidState(result.State)) throw Invalid();
        return result;
    }

    public async ValueTask<WidgetPerformanceEnableResult> DisableFpsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.InvokeAsync(WidgetPerformanceCapabilities.DisableFps, new(), cancellationToken).ConfigureAwait(false);
        if (result is null || !ValidState(result.State)) throw Invalid();
        return result;
    }

    private static void Validate(WidgetPerformanceSnapshot value)
    {
        static bool Percent(double? v) => v is null || double.IsFinite(v.Value) && v is >= 0 and <= 100;
        static bool Text(string? v, int max) => !string.IsNullOrWhiteSpace(v) && v.Length <= max && !v.Any(char.IsControl);
        if (value is null || !Percent(value.CpuPercent) || !Percent(value.GpuPercent) ||
            value.MemoryUsedBytes < 0 || value.MemoryTotalBytes < 0 || value.MemoryUsedBytes > value.MemoryTotalBytes || value.DedicatedGpuMemoryBytes < 0 ||
            value.FramesPerSecond is { } fps && (!double.IsFinite(fps) || fps < 0 || fps > 10_000 || value.FpsState != "collecting") ||
            !ValidState(value.FpsState) || value.Targets is null || value.Targets.Count > 64 ||
            value.Targets.Any(t => t is null || !ValidId(t.Id) || !Text(t.ApplicationName, 120) || !Text(t.Title, 240)) ||
            value.Targets.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != value.Targets.Count ||
            value.SelectedTargetId is not null && !value.Targets.Any(t => t.Id == value.SelectedTargetId)) throw Invalid();
    }
    private static bool ValidId(string? id) => id is { Length: > 0 and <= 128 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    private static bool ValidState(string? value) => value is "not-enabled" or "enabling" or "declined" or "missing" or "unavailable" or
        "idle" or "collecting" or "warming" or "permission-required" or "failed" or "no-target" or "no-frames";
    private static WidgetCapabilityException Invalid() => new("malformed_response", "Performance information is unavailable.");
}
