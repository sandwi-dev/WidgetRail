using System.Text.Json;

namespace GameBarAlternative.PlatformBroker;

internal sealed class AppLibraryCapabilityDomain
{
    private readonly IPlatformBrokerBackend _backend;
    private readonly BrokerWidgetIdentity _identity;
    private readonly IAppLibrarySavedIdIssuer _savedIdIssuer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _publicIdsByBackendId =
        new(StringComparer.Ordinal);
    private IReadOnlyList<AppLibraryItemSummary>? _snapshot;
    private Dictionary<string, string> _backendIdsByPublicId =
        new(StringComparer.Ordinal);

    internal AppLibraryCapabilityDomain(
        IPlatformBrokerBackend backend,
        BrokerWidgetIdentity identity,
        IAppLibrarySavedIdIssuer savedIdIssuer)
    {
        _backend = backend;
        _identity = identity;
        _savedIdIssuer = savedIdIssuer;
    }

    internal async Task<JsonElement> ExecuteAsync(
        string operation,
        JsonElement payload,
        CancellationToken cancellationToken) => operation switch
        {
            PlatformCapabilities.AppLibraryList => BrokerJson.ToElement(
                await GetPageAsync(payload, cancellationToken).ConfigureAwait(false)),
            PlatformCapabilities.AppLibraryResolveSaved => BrokerJson.ToElement(
                await ResolveSavedAsync(payload, cancellationToken).ConfigureAwait(false)),
            PlatformCapabilities.AppLibraryLaunch =>
                await LaunchAsync(payload, cancellationToken).ConfigureAwait(false),
            _ => throw new BrokerException(
                "unsupported_operation", "App-library operation is unsupported."),
        };

    internal static IReadOnlyList<AppLibraryBackendItemSummary> ValidateItems(
        IReadOnlyList<AppLibraryBackendItemSummary>? items)
    {
        if (items is null || items.Count > PlatformCapabilityBroker.MaximumAppLibraryItems)
            throw new BrokerException(
                "invalid_backend_data", "App library result is invalid.");
        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        var stableIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || !Enum.IsDefined(item.Kind))
                throw new BrokerException(
                    "invalid_backend_data", "App library entry is invalid.");
            ContractValidation.OpaqueId(item.ProviderAppId, "invalid_backend_data");
            AppLibrarySavedIdIssuer.ValidateStableProviderIdentity(
                item.StableProviderIdentity);
            ContractValidation.DisplayName(item.DisplayName);
            if (!providerIds.Add(item.ProviderAppId) ||
                !stableIdentities.Add(item.StableProviderIdentity))
                throw new BrokerException(
                    "invalid_backend_data", "App library identities are duplicated.");
        }
        return items.ToArray();
    }

    private async Task<AppLibraryPageSummary> GetPageAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<AppLibraryPageRequest>(payload);
        if (request.Offset < 0 ||
            request.Offset > PlatformCapabilityBroker.MaximumAppLibraryItems ||
            request.Limit is < 1 or
                > PlatformCapabilityBroker.MaximumAppLibraryPageSize)
            throw new BrokerException(
                "invalid_payload", "App library page bounds are invalid.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (request.Offset == 0 || _snapshot is null)
            {
                var backendItems = ValidateItems(
                    await _backend.RefreshAppLibraryAsync(cancellationToken)
                        .ConfigureAwait(false));
                cancellationToken.ThrowIfCancellationRequested();
                _snapshot = ProjectSnapshot(backendItems);
            }
            var items = _snapshot;
            var page = items.Skip(request.Offset).Take(request.Limit).ToArray();
            var consumed = request.Offset + page.Length;
            return new AppLibraryPageSummary(
                page, consumed < items.Count ? consumed : null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ResolveSavedAppLibraryItemsSummary> ResolveSavedAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<ResolveSavedAppLibraryItemsRequest>(payload);
        if (request.SavedIds is null ||
            request.SavedIds.Count >
                PlatformCapabilityBroker.MaximumResolvedAppLibraryItems)
            throw new BrokerException(
                "invalid_payload", "Saved app-library identifier bounds are invalid.");
        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in request.SavedIds)
        {
            ContractValidation.OpaqueId(savedId);
            if (!savedId.StartsWith("saved-", StringComparison.Ordinal) ||
                !requested.Add(savedId))
                throw new BrokerException(
                    "invalid_payload", "Saved app-library identifiers are invalid.");
        }
        if (request.SavedIds.Count == 0)
            return new ResolveSavedAppLibraryItemsSummary([]);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var backendItems = ValidateItems(
                await _backend.RefreshAppLibraryAsync(cancellationToken)
                    .ConfigureAwait(false));
            cancellationToken.ThrowIfCancellationRequested();
            _snapshot = ProjectSnapshot(backendItems);
            var currentBySavedId = _snapshot.ToDictionary(
                item => item.SavedId, StringComparer.Ordinal);
            var resolved = request.SavedIds
                .Where(currentBySavedId.ContainsKey)
                .Select(savedId => currentBySavedId[savedId])
                .ToArray();
            var iconBytes = 0;
            for (var index = 0;
                 index < resolved.Length &&
                 index < AppLibraryImageLimits.MaximumResolvedIconCount;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_backendIdsByPublicId.TryGetValue(
                        resolved[index].AppId, out var backendAppId))
                    continue;
                AppLibraryIconSummary? icon;
                try
                {
                    icon = await _backend.GetAppLibraryIconAsync(
                        backendAppId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is BrokerException or IOException or
                        UnauthorizedAccessException or InvalidOperationException or
                        ArgumentException or NotSupportedException)
                {
                    icon = null;
                }
                var validated = BrokerInlinePngPolicy.Validate(
                    icon?.PngBase64,
                    AppLibraryImageLimits.MaximumPngBytes,
                    AppLibraryImageLimits.MaximumPixelDimension);
                if (validated is null ||
                    iconBytes + validated.Value.Bytes >
                        AppLibraryImageLimits.MaximumAggregatePngBytes)
                    continue;
                iconBytes += validated.Value.Bytes;
                resolved[index] = resolved[index] with
                {
                    IconPngBase64 = validated.Value.Base64,
                };
            }
            return new ResolveSavedAppLibraryItemsSummary(resolved);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<JsonElement> LaunchAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<LaunchAppLibraryItemRequest>(payload);
        ContractValidation.OpaqueId(request.AppId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_backendIdsByPublicId.TryGetValue(request.AppId, out var backendAppId))
                throw new BrokerException(
                    "app_not_found", "The selected app is no longer available.");
            await _backend.LaunchAppLibraryItemAsync(backendAppId, cancellationToken)
                .ConfigureAwait(false);
            return BrokerCapabilityDomains.Acknowledged();
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyList<AppLibraryItemSummary> ProjectSnapshot(
        IReadOnlyList<AppLibraryBackendItemSummary> backendItems)
    {
        var liveBackendIds = backendItems.Select(item => item.ProviderAppId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _publicIdsByBackendId.Keys
                     .Where(id => !liveBackendIds.Contains(id)).ToArray())
            _publicIdsByBackendId.Remove(stale);

        var byPublicId = new Dictionary<string, string>(StringComparer.Ordinal);
        var projected = new AppLibraryItemSummary[backendItems.Count];
        for (var index = 0; index < backendItems.Count; index++)
        {
            var item = backendItems[index];
            if (!_publicIdsByBackendId.TryGetValue(item.ProviderAppId, out var publicId))
            {
                publicId = "app-" + Guid.NewGuid().ToString("N");
                _publicIdsByBackendId.Add(item.ProviderAppId, publicId);
            }
            byPublicId.Add(publicId, item.ProviderAppId);
            projected[index] = new AppLibraryItemSummary(
                publicId, item.DisplayName, item.Kind)
            {
                SavedId = _savedIdIssuer.Issue(
                    _identity, item.StableProviderIdentity),
            };
        }
        _backendIdsByPublicId = byPublicId;
        return Array.AsReadOnly(projected);
    }
}
