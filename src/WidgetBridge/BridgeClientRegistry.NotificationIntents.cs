using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    private sealed record NotificationIntentGrant(string WidgetId, string Key, string Fingerprint, BridgeWorkerRun Run, WidgetIntentRequest Intent);
    private readonly Dictionary<string, NotificationIntentGrant> notificationIntents = new(StringComparer.Ordinal);

    private bool WebNotificationIntentCurrent(IntentTicket ticket)
    {
        if (ticket.WebNotification is not { } notification) return true;
        return notification.IsWellFormed() && _clients.TryGetValue(ticket.SourceId, out var source) &&
            WidgetRail.PlatformBroker.WebSessionRegistry.Shared.AllowsNativeNotificationIntent(ticket.SourceId,
                new(source.Configured.PackageId, source.Configured.PublisherId, source.Configured.InstanceId), new(notification.SessionId));
    }

    internal Task<WidgetIntentPreparation> PrepareWebNotificationIntentAsync(BridgeWebNotificationIntentPrepare request, CancellationToken token)
    {
        IntentTicket ticket;
        lock (_gate)
        {
            DemandNotDisposed();
            if (request.Notification?.IsWellFormed() != true || !_clients.TryGetValue(request.WidgetId, out var source) ||
                !IsCurrentLocked(source) || !source.Client.IsRunning)
                return Task.FromResult(new WidgetIntentPreparation(request.WidgetId, null, WidgetIntentLaunchKind.Rejected, []));
            var intent = WidgetIntentRequest.Create(WidgetIntentContracts.WebNotification,
                JsonSerializer.SerializeToElement(request.Notification), WidgetIntentPresentation.OpenWidget);
            ticket = new(request.WidgetId, source.Configured.WorkerFingerprint, new(source.Generation, source.Client.Starts), IntentNow(),
                intent, WidgetIntentLaunchKind.Unavailable, new Dictionary<string, string>(), null, intent, WebNotification: request.Notification);
            if (!WebNotificationIntentCurrent(ticket))
                return Task.FromResult(new WidgetIntentPreparation(request.WidgetId, null, WidgetIntentLaunchKind.Rejected, []));
        }
        return ResolveIntentAsync(ticket, token);
    }

    internal bool RegisterNotificationIntent(string widget, string fingerprint, BridgeWorkerRun run,
        WidgetNotificationCommand command, out string? token)
    {
        lock (_gate)
        {
            token = null;
            if (command.ConfigureCategoryId is not null) return true;
            var intent = command.Notification?.OpenIntent;
            if (intent is not null && (!_clients.TryGetValue(widget, out var source) || !IsCurrentLocked(source) || source.Configured.WorkerFingerprint != fingerprint ||
                !Declared(source, intent) || intent.Fallback is { } fallback && !Declared(source, fallback))) return false;
            foreach (var key in notificationIntents.Where(pair => pair.Value.WidgetId == widget && pair.Value.Key == command.Key ||
                !_clients.TryGetValue(pair.Value.WidgetId, out var existing) || !IsCurrentLocked(existing) ||
                existing.Configured.WorkerFingerprint != pair.Value.Fingerprint || !existing.Client.IsRunning ||
                new BridgeWorkerRun(existing.Generation, existing.Client.Starts) != pair.Value.Run).Select(pair => pair.Key).ToArray()) notificationIntents.Remove(key);
            if (intent is null) return true;
            if (notificationIntents.Count >= 256) return false;
            token = Guid.NewGuid().ToString("N");
            notificationIntents.Add(token, new(widget, command.Key, fingerprint, run, intent));
            return true;
        }
    }
    internal void ReleaseNotificationIntent(BridgeNotificationIntentPrepare request)
    {
        lock (_gate)
            if (notificationIntents.TryGetValue(request.IntentToken, out var grant) && grant.WidgetId == request.WidgetId)
                notificationIntents.Remove(request.IntentToken);
    }
    private bool NotificationIntentCurrent(IntentTicket ticket) => ticket.NotificationToken is { } token &&
        notificationIntents.TryGetValue(token, out var grant) && grant.WidgetId == ticket.SourceId &&
        grant.Run == ticket.SourceRun && grant.Intent.Matches(ticket.Original) && Declared(_clients[ticket.SourceId], ticket.Request);

    internal async Task<WidgetIntentPreparation> PrepareNotificationIntentAsync(BridgeNotificationIntentPrepare request, CancellationToken token)
    {
        IntentTicket ticket;
        lock (_gate)
        {
            DemandNotDisposed();
            if (!notificationIntents.TryGetValue(request.IntentToken, out var grant) || grant.WidgetId != request.WidgetId)
                return new(request.WidgetId, null, WidgetIntentLaunchKind.Rejected, []);
            ticket = new(grant.WidgetId, grant.Fingerprint, grant.Run, IntentNow(), grant.Intent, WidgetIntentLaunchKind.Unavailable,
                new Dictionary<string, string>(), null, grant.Intent, request.IntentToken);
        }
        return await ResolveIntentAsync(ticket, token).ConfigureAwait(false);
    }
    internal async Task<WidgetIntentPreparation> PrepareWidgetSettingsIntentAsync(BridgeWidgetManagementRequest request, CancellationToken token)
    {
        ConfiguredWidget managed;
        lock (_gate)
        {
            managed = _catalog.GetConfigured(request.WidgetId);
            var descriptor = managed.PublicDescriptor();
            if (!descriptor.CanManage || descriptor.RuntimeGeneration != request.RuntimeGeneration)
                return new("settings", null, WidgetIntentLaunchKind.Rejected, []);
        }
        // Only Settings is created. Managing a stopped widget must never start it.
        var source = await GetOrCreateAsync("settings", token).ConfigureAwait(false);
        var intent = WidgetIntentRequest.Create(WidgetIntentContracts.WidgetSettings,
            JsonSerializer.SerializeToElement(new { packageId = managed.PackageId }), WidgetIntentPresentation.OpenWidget);
        IntentTicket ticket;
        lock (_gate)
        {
            if (!IsCurrentLocked(source)) return new("settings", null, WidgetIntentLaunchKind.Rejected, []);
            ticket = new("settings", source.Configured.WorkerFingerprint, null, IntentNow(), intent, WidgetIntentLaunchKind.Unavailable,
                new Dictionary<string, string>(), null, intent, HostSettings: true, ManagedWidgetId: managed.Id, ManagedFingerprint: managed.WorkerFingerprint);
            if (!ManagedTargetCurrent(ticket)) return new("settings", null, WidgetIntentLaunchKind.Rejected, []);
        }
        return await ResolveIntentAsync(ticket, token).ConfigureAwait(false);
    }
    private bool ManagedTargetCurrent(IntentTicket ticket)
    {
        if (ticket.ManagedWidgetId is not { } id) return true;
        try { return _catalog.GetConfigured(id).WorkerFingerprint == ticket.ManagedFingerprint; }
        catch (BridgeProtocolException) { return false; }
    }
    // Host-only, closed destination; callers cannot supply an arbitrary payload or source identity.
    internal Task<WidgetIntentPreparation> PrepareAppUpdateIntentAsync(CancellationToken token) =>
        PrepareHostSettingsIntentAsync(WidgetIntentContracts.AppUpdates, token);
    internal Task<WidgetIntentPreparation> PrepareAppearanceIntentAsync(CancellationToken token) =>
        PrepareHostSettingsIntentAsync(WidgetIntentContracts.Appearance, token);
    private async Task<WidgetIntentPreparation> PrepareHostSettingsIntentAsync(WidgetIntentContract contract, CancellationToken token)
    {
        var source = await GetOrCreateAsync("settings", token).ConfigureAwait(false);
        var intent = WidgetIntentRequest.Create(contract, JsonSerializer.SerializeToElement(new { }), WidgetIntentPresentation.OpenWidget);
        IntentTicket ticket;
        lock (_gate)
        {
            if (!IsCurrentLocked(source) || !Declared(source, intent)) return new("settings", null, WidgetIntentLaunchKind.Rejected, []);
            ticket = new("settings", source.Configured.WorkerFingerprint, null, IntentNow(), intent, WidgetIntentLaunchKind.Unavailable,
                new Dictionary<string, string>(), null, intent, HostSettings: true);
        }
        return await ResolveIntentAsync(ticket, token).ConfigureAwait(false);
    }
}
