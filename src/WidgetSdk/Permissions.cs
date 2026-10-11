using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetPermissionCapabilities
{
    public const string Service = "widget.permissions.v1";
    public static WidgetCapabilityOperation<WidgetPermissionQuery, WidgetPermissionStatuses> Check { get; } = new(Service, "permissions.check");
    public static WidgetCapabilityOperation<WidgetPermissionQuery, WidgetPermissionRequestState> Request { get; } = new(Service, "permissions.request");
    public static WidgetCapabilityOperation<WidgetPermissionTicket, WidgetPermissionRequestState> Status { get; } = new(Service, "permissions.status");
    public static WidgetCapabilityOperation<WidgetPermissionTicket, WidgetCapabilityAcknowledgement> Cancel { get; } = new(Service, "permissions.cancel");
}

/// <summary>Check access silently or explicitly ask the host to show permission consent for this widget's declarations.</summary>
public sealed class WidgetPermissionService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetPermissionService(IWidgetCapabilityClient client) => this.client = client;
    public async ValueTask<IReadOnlyList<WidgetPermissionStatus>> CheckAsync(IReadOnlyList<string> capabilityIds, CancellationToken cancellationToken = default)
    {
        var query = Query(capabilityIds);
        var result = await client.InvokeAsync(WidgetPermissionCapabilities.Check, query, cancellationToken).ConfigureAwait(false);
        if (result?.Permissions is not { } statuses || statuses.Count != query.CapabilityIds.Count ||
            statuses.Where((s, i) => s is null || s.CapabilityId != query.CapabilityIds[i] || !Enum.IsDefined(s.State)).Any()) throw Malformed();
        return statuses.ToArray();
    }

    /// <summary>Call from an explicit user action, using Widget.Operations so input is not blocked while the dialog is open.
    /// Granted allows the caller to continue its action; it is not a permanent execution grant. Background requests never prompt.</summary>
    public async ValueTask<WidgetPermissionOutcome> RequestAsync(IReadOnlyList<string> capabilityIds, CancellationToken cancellationToken = default)
    {
        var query = Query(capabilityIds); string? pending = null; var complete = false;
        try
        {
            var state = await client.InvokeAsync(WidgetPermissionCapabilities.Request, query, cancellationToken).ConfigureAwait(false);
            var deadline = Environment.TickCount64 + 125_000;
            while (true)
            {
                if (state is null || !Enum.IsDefined(state.Outcome) ||
                    state.RequestId is not null && !CaptureLimits.ValidToken(state.RequestId) ||
                    state.Outcome == WidgetPermissionOutcome.Pending && state.RequestId is null ||
                    pending is not null && state.RequestId != pending) throw Malformed();
                if (state.Outcome != WidgetPermissionOutcome.Pending) { complete = true; return state.Outcome; }
                pending = state.RequestId;
                if (Environment.TickCount64 >= deadline) return WidgetPermissionOutcome.Unavailable;
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                state = await client.InvokeAsync(WidgetPermissionCapabilities.Status, new(pending!), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (WidgetCapabilityException error) when (error.ErrorCode is "unsupported_capability" or "capability_not_declared" or "lifecycle_denied")
        { return WidgetPermissionOutcome.Unavailable; }
        finally
        {
            if (!complete && pending is not null)
                try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await client.InvokeAsync(WidgetPermissionCapabilities.Cancel, new(pending), cleanup.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is WidgetCapabilityException or OperationCanceledException) { }
        }
    }
    private static WidgetPermissionQuery Query(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var query = new WidgetPermissionQuery(ids.ToArray());
        if (!query.IsWellFormed()) throw new ArgumentException("Request one to eight distinct permission IDs.", nameof(ids));
        return query;
    }
    private static WidgetCapabilityException Malformed() => new("malformed_response", "Permission response is invalid.");
}
