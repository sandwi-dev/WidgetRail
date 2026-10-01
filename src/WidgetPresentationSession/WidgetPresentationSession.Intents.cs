using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public bool HasIntentAction(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        lock (_gate)
        {
            if (IntentActionAuthority.Revalidate(displayed.Snapshot, displayed.Snapshot, action) is null) return false;
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            if (IntentActionAuthority.Revalidate(displayed.Snapshot, current.Snapshot, action) is null)
                throw OrdinaryInputStale("The displayed intent changed.");
            return true;
        }
    }

    public async Task<WidgetIntentPreparation> PrepareIntentAsync(WidgetPresentationFrame displayed,
        WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (!HasIntentAction(displayed, action) || displayed.Authority.WorkerRun is null)
            throw OrdinaryInputStale("No current intent action is available.");
        var response = await RequestAsync(BridgeMessageTypes.PrepareIntent,
            new BridgeIntentPrepareRequest(displayed.Authority.WidgetId, displayed.Authority.SnapshotSequence,
                displayed.Authority.WorkerRun, action), BridgeMessageTypes.IntentPrepared, cancellationToken).ConfigureAwait(false);
        var result = BridgeJson.FromElement<WidgetIntentPreparation>(response.Payload);
        if (result.SourceWidgetId != displayed.Authority.WidgetId || !Enum.IsDefined(result.Kind) || result.Destinations is null ||
            result.Destinations.Count > 256 || result.Destinations.Any(item => item is null || !Safe(item.WidgetId) ||
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 256) ||
            result.Destinations.Select(item => item.WidgetId).Distinct(StringComparer.Ordinal).Count() != result.Destinations.Count ||
            (result.Kind is WidgetIntentLaunchKind.Rejected or WidgetIntentLaunchKind.Unavailable
                ? result.TicketId is not null || result.Destinations.Count != 0
                : !Guid.TryParseExact(result.TicketId, "N", out _)) ||
            result.Kind == WidgetIntentLaunchKind.Widget && result.Destinations.Count != 1 ||
            result.Kind == WidgetIntentLaunchKind.ChooseHandler && result.Destinations.Count < 2 ||
            result.Kind == WidgetIntentLaunchKind.ExternalBrowser && result.Destinations.Count != 0)
            throw new BridgeProtocolException("Invalid intent preparation response.");
        return result;
    }

    public async Task<WidgetIntentCompletion> CommitIntentAsync(WidgetIntentPreparation prepared,
        WidgetPresentationFrame? target = null, CancellationToken cancellationToken = default)
    {
        if (!Safe(prepared.SourceWidgetId) || !Guid.TryParseExact(prepared.TicketId, "N", out _))
            throw new ArgumentException("No intent ticket is available.", nameof(prepared));
        if (target is not null)
        {
            lock (_gate)
            {
                _ = ValidateDisplayedOrdinaryFrameLocked(target, target.Snapshot.ActiveInputScopeId);
                if (target.Authority.WorkerRun is null || !prepared.Destinations.Any(item => item.WidgetId == target.Authority.WidgetId))
                    throw OrdinaryInputStale("Intent destination changed.");
            }
        }
        try
        {
            var response = await RequestAsync(BridgeMessageTypes.CommitIntent,
                new BridgeIntentCommitRequest(prepared.SourceWidgetId, prepared.TicketId!, target?.Authority.WidgetId,
                    target?.Authority.WorkerRun), BridgeMessageTypes.IntentCompleted, cancellationToken).ConfigureAwait(false);
            var result = BridgeJson.FromElement<WidgetIntentCompletion>(response.Payload);
            if (result.ExternalUrl is { } url && (!result.Accepted || prepared.Kind != WidgetIntentLaunchKind.ExternalBrowser ||
                url.Length > 2048 || url.Any(char.IsControl) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))
                throw new BridgeProtocolException("Invalid external intent result.");
            if (result.Accepted && prepared.Kind == WidgetIntentLaunchKind.ExternalBrowser && result.ExternalUrl is null)
                throw new BridgeProtocolException("External intent result is missing its URL.");
            if (result.BrowserFallbackUrl is { } fallback && (result.Accepted || result.ExternalUrl is not null ||
                prepared.Kind is not (WidgetIntentLaunchKind.Widget or WidgetIntentLaunchKind.ChooseHandler) ||
                fallback.Length > 2048 || fallback.Any(char.IsControl) || !Uri.TryCreate(fallback, UriKind.Absolute, out var fallbackUri) ||
                fallbackUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(fallbackUri.UserInfo)))
                throw new BridgeProtocolException("Invalid browser fallback offer.");
            return result;
        }
        catch (OperationCanceledException)
        {
            await CancelIntentAsync(prepared).ConfigureAwait(false);
            throw;
        }
    }

    public async Task CancelIntentAsync(WidgetIntentPreparation prepared)
    {
        if (!Safe(prepared.SourceWidgetId) || !Guid.TryParseExact(prepared.TicketId, "N", out _)) return;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await RequestAsync(BridgeMessageTypes.CancelIntent, new BridgeIntentCancelRequest(prepared.SourceWidgetId, prepared.TicketId!),
                BridgeMessageTypes.Acknowledged, cleanup.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException or
            WidgetPresentationSessionException or BridgeProtocolException) { }
    }
}
