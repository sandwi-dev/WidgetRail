using System.Text.Json;
using System.Threading.Channels;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed record LocalWidgetInstallResult(string Status, string WidgetId, string Version, string Message);

public sealed partial class WidgetPresentationSession
{
    public const string InstallLocalWidgetAction = "host.install-local-widget";
    private const string UpdateFilePrefix = "installed.update.file.";
    private Channel<BridgeLocalWidgetPackageInstallCompleted>? localInstallEvents;
    private string? localInstallId;

    // Validate the displayed command before opening any host UI, and again after
    // the picker/review returns. Filesystem access is never delegated to a widget.
    public void ValidateLocalWidgetInstall(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        lock (_gate) _ = LocalInstallOriginLocked(displayed, action);
    }

    private BridgeLocalWidgetPackageOrigin LocalInstallOriginLocked(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
        var update = action.InputScopeId == "installed.update" && action.SourceElementId.StartsWith(UpdateFilePrefix, StringComparison.Ordinal)
            ? action.SourceElementId[UpdateFilePrefix.Length..] : null;
        if (displayed.Authority.WidgetId != "settings" || action.ActionId != InstallLocalWidgetAction ||
            action.Phase != ControllerEventPhase.Pressed || action.RequestedValue is not null || action.CommittedText is not null ||
            !(action.InputScopeId == "installed.widgets" && action.SourceElementId == "installed.install-local" ||
              update is { Length: 64 } && update.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) ||
            ResolveDisplayedAction(displayed.Snapshot, action) != ResolveDisplayedAction(current.Snapshot, action))
            throw OrdinaryInputStale("Local widget installation is only available from the current Settings command.");
        var descriptor = _descriptors[displayed.Authority.WidgetId];
        return new(descriptor.Id, "widgetrail.firstparty.settings", "widgetrail.firstparty", descriptor.InstanceId,
            descriptor.RuntimeGeneration, descriptor.PresentationGeneration, update);
    }

    public async Task<LocalWidgetInstallResult> InstallLocalWidgetAsync(WidgetPresentationFrame displayed, WidgetActionEvent action,
        string path, Func<LocalWidgetInstallResult, CancellationToken, Task<bool>> review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".wrwidget", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select a local .wrwidget file.", nameof(path));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var events = Channel.CreateBounded<BridgeLocalWidgetPackageInstallCompleted>(4);
        var id = Guid.NewGuid().ToString("N");
        BridgeLocalWidgetPackageOrigin origin;
        lock (_gate)
        {
            origin = LocalInstallOriginLocked(displayed, action);
            if (localInstallId is not null) throw new InvalidOperationException("A widget installation is already active.");
            localInstallId = id; localInstallEvents = events;
        }
        var terminal = false;
        try
        {
            await RequestAsync(BridgeMessageTypes.InstallLocalWidgetPackage, new BridgeLocalWidgetPackageInstallRequest(id, path, origin),
                BridgeMessageTypes.Acknowledged, linked.Token);
            var reviewed = false;
            Task<BridgeLocalWidgetPackageInstallCompleted>? nextEvent = null;
            while (true)
            {
                var result = await (nextEvent ?? events.Reader.ReadAsync(linked.Token).AsTask());
                nextEvent = null;
                var value = new LocalWidgetInstallResult(result.Status, result.WidgetId, result.Version, result.Message);
                if (result.Status != "approval-required") { terminal = true; return value; }
                if (reviewed) throw new BridgeProtocolException("Duplicate package approval request.");
                reviewed = true;
                ValidateLocalWidgetInstall(displayed, action);
                using var reviewLifetime = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                var decision = review(value, reviewLifetime.Token);
                nextEvent = events.Reader.ReadAsync(linked.Token).AsTask();
                bool approved;
                try
                {
                    if (await Task.WhenAny(decision, nextEvent) == nextEvent)
                    {
                        var ended = await nextEvent;
                        if (ended.Status == "approval-required") throw new BridgeProtocolException("Duplicate package approval request.");
                        terminal = true;
                        return new(ended.Status, ended.WidgetId, ended.Version, ended.Message);
                    }
                    approved = await decision;
                }
                finally
                {
                    reviewLifetime.Cancel();
                    try { await decision; } catch (OperationCanceledException) when (reviewLifetime.IsCancellationRequested) { }
                }
                ValidateLocalWidgetInstall(displayed, action);
                await RequestAsync(BridgeMessageTypes.ApproveLocalWidgetPackageInstall, new BridgeLocalWidgetPackageInstallApprovalRequest(id, approved),
                    BridgeMessageTypes.Acknowledged, linked.Token);
            }
        }
        finally
        {
            if (!terminal)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await RequestAsync(BridgeMessageTypes.CancelLocalWidgetPackageInstall, new BridgeLocalWidgetPackageInstallCancelRequest(id),
                    BridgeMessageTypes.Acknowledged, cleanup.Token); }
                catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException or WidgetPresentationSessionException or BridgeProtocolException) { }
            }
            lock (_gate) { if (localInstallId == id) { localInstallId = null; localInstallEvents = null; } }
        }
    }

    private void HandleLocalInstallEvent(JsonElement payload)
    {
        var result = BridgeJson.FromElement<BridgeLocalWidgetPackageInstallCompleted>(payload);
        lock (_gate)
        {
            if (result.OperationId != localInstallId) return; // Late completion of a cancelled operation.
            if (result.Status is not ("approval-required" or "installed-disabled" or "cancelled" or "failed") ||
                localInstallEvents?.Writer.TryWrite(result) != true)
                throw new BridgeProtocolException("Invalid local package installation event.");
        }
    }
}
