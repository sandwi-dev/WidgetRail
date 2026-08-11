using System.Text.Json;
using System.Text;

namespace GameBarAlternative.PlatformBroker;

internal sealed class AppLibraryCapabilityDomain : IDisposable
{
    private const int MaximumRetainedLaunchIds = 256;
    private const int MaximumTraversalPages = 160;
    private readonly IPlatformBrokerBackend _backend;
    private readonly BrokerWidgetIdentity _identity;
    private readonly IAppLibrarySavedIdIssuer _savedIdIssuer;
    private readonly AppLibraryArtworkRegistry.AppLibraryArtworkSession? _artwork;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, LaunchRegistration> _launchByPublicId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _publicIdsByBackendId =
        new(StringComparer.Ordinal);
    private readonly LinkedList<string> _launchRecency = [];
    private readonly Dictionary<string, LinkedListNode<string>> _launchNodes =
        new(StringComparer.Ordinal);
    private string? _currentRevision;

    internal AppLibraryCapabilityDomain(
        IPlatformBrokerBackend backend,
        BrokerWidgetIdentity identity,
        IAppLibrarySavedIdIssuer savedIdIssuer,
        AppLibraryArtworkRegistry.AppLibraryArtworkSession? artwork = null)
    {
        _backend = backend;
        _identity = identity;
        _savedIdIssuer = savedIdIssuer;
        _artwork = artwork;
    }

    internal async Task<JsonElement> ExecuteAsync(
        string operation,
        JsonElement payload,
        CancellationToken cancellationToken) => operation switch
        {
            PlatformCapabilities.AppLibraryList => BrokerJson.ToElement(
                await QueryAsync(payload, cancellationToken).ConfigureAwait(false)),
            PlatformCapabilities.AppLibraryResolveSaved => BrokerJson.ToElement(
                await ResolveSavedAsync(payload, cancellationToken).ConfigureAwait(false)),
            PlatformCapabilities.AppLibraryLaunch =>
                await LaunchAsync(payload, observed: false, cancellationToken)
                    .ConfigureAwait(false),
            PlatformCapabilities.AppLibraryLaunchObserved =>
                await LaunchAsync(payload, observed: true, cancellationToken)
                    .ConfigureAwait(false),
            _ => throw new BrokerException(
                "unsupported_operation", "App-library operation is unsupported."),
        };

    private async Task<AppLibraryCursorPageSummary> QueryAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<AppLibraryCursorRequest>(payload);
        var backendRequest = ValidateRequest(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (request.Query.FavoriteSavedIds.Count != 0)
            {
                var stableIdentities = await ResolveFavoriteStableIdentitiesAsync(
                    backendRequest.Query, request.Query.FavoriteSavedIds,
                    request.Refresh, cancellationToken).ConfigureAwait(false);
                backendRequest = backendRequest with
                {
                    Query = backendRequest.Query with
                        { StableIdentityFilter = stableIdentities },
                    Refresh = false,
                };
            }
            var page = ValidatePage(await _backend.QueryAppLibraryAsync(
                    backendRequest, cancellationToken).ConfigureAwait(false), request.Limit);
            cancellationToken.ThrowIfCancellationRequested();
            return ProjectPage(page);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<string>> ResolveFavoriteStableIdentitiesAsync(
        AppLibraryBackendQuery query,
        IReadOnlyList<string> favoriteSavedIds,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var requested = favoriteSavedIds.ToHashSet(StringComparer.Ordinal);
        var matches = new List<string>(requested.Count);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        string? revision = null;
        for (var pageIndex = 0; pageIndex < MaximumTraversalPages; pageIndex++)
        {
            var page = ValidatePage(await _backend.QueryAppLibraryAsync(
                new AppLibraryBackendCursorRequest(
                    query with { StableIdentityFilter = null }, cursor,
                    cursor is null ? null : AppLibraryCursorDirection.After,
                    PlatformCapabilityBroker.MaximumAppLibraryPageSize,
                    Refresh: cursor is null && refresh), cancellationToken)
                .ConfigureAwait(false), PlatformCapabilityBroker.MaximumAppLibraryPageSize);
            revision ??= page.Revision;
            if (!string.Equals(revision, page.Revision, StringComparison.Ordinal))
                throw new BrokerException(
                    "invalid_backend_data", "App-library revision changed during query filtering.");
            foreach (var item in page.Items)
                if (requested.Contains(_savedIdIssuer.Issue(
                        _identity, item.StableProviderIdentity)))
                    matches.Add(item.StableProviderIdentity);
            if (matches.Count == requested.Count || page.After is null) break;
            if (!seenCursors.Add(page.After))
                throw new BrokerException(
                    "invalid_backend_data", "App-library cursor loop is invalid.");
            cursor = page.After;
        }
        return matches;
    }

    private async Task<ResolveSavedAppLibraryItemsSummary> ResolveSavedAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<ResolveSavedAppLibraryItemsRequest>(payload);
        ValidateSavedIds(
            request.SavedIds, PlatformCapabilityBroker.MaximumResolvedAppLibraryItems);
        if (request.SavedIds.Count == 0)
            return new ResolveSavedAppLibraryItemsSummary([]);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requested = request.SavedIds.ToHashSet(StringComparer.Ordinal);
            var matches = new Dictionary<string, AppLibraryBackendItemSummary>(StringComparer.Ordinal);
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            string? revision = null;
            for (var pageIndex = 0;
                 pageIndex < MaximumTraversalPages && matches.Count < requested.Count;
                 pageIndex++)
            {
                var page = ValidatePage(await _backend.QueryAppLibraryAsync(
                    new AppLibraryBackendCursorRequest(
                        new AppLibraryBackendQuery(), cursor,
                        cursor is null ? null : AppLibraryCursorDirection.After,
                        PlatformCapabilityBroker.MaximumAppLibraryPageSize,
                        Refresh: cursor is null),
                    cancellationToken).ConfigureAwait(false),
                    PlatformCapabilityBroker.MaximumAppLibraryPageSize);
                cancellationToken.ThrowIfCancellationRequested();
                revision ??= page.Revision;
                if (!string.Equals(revision, page.Revision, StringComparison.Ordinal))
                    throw new BrokerException(
                        "invalid_backend_data", "App-library revision changed during traversal.");
                foreach (var item in page.Items)
                {
                    var savedId = _savedIdIssuer.Issue(
                        _identity, item.StableProviderIdentity);
                    if (requested.Contains(savedId)) matches[savedId] = item;
                }
                if (page.After is null) break;
                if (!seenCursors.Add(page.After))
                    throw new BrokerException(
                        "invalid_backend_data", "App-library cursor loop is invalid.");
                cursor = page.After;
            }

            var matchedItems = request.SavedIds.Where(matches.ContainsKey)
                .Select(savedId => matches[savedId]).ToArray();
            var projected = ProjectPage(new AppLibraryBackendCursorPage(
                matchedItems, null, null, revision!));
            return new ResolveSavedAppLibraryItemsSummary(projected.Items);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<JsonElement> LaunchAsync(
        JsonElement payload,
        bool observed,
        CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<LaunchAppLibraryItemRequest>(payload);
        ContractValidation.OpaqueId(request.AppId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_launchByPublicId.TryGetValue(request.AppId, out var registration))
                throw new BrokerException(
                    "app_not_found", "The selected app is no longer available.");
            if (!observed)
            {
                await _backend.LaunchAppLibraryItemAsync(
                    registration.BackendAppId, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            var result = await _backend.LaunchAppLibraryItemObservedAsync(
                registration.BackendAppId, cancellationToken).ConfigureAwait(false);
            if (!Enum.IsDefined(result.State) ||
                result.State == AppLibraryLaunchObservationState.Running &&
                    !result.SupportsRunning ||
                result.State == AppLibraryLaunchObservationState.Ended &&
                    !result.SupportsEnded)
                throw new BrokerException(
                    "invalid_backend_data", "App launch evidence is invalid.");
            return BrokerJson.ToElement(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    private AppLibraryCursorPageSummary ProjectPage(AppLibraryBackendCursorPage page)
    {
        EnsureRevision(page.Revision);
        var artworkHandles = _artwork?.RegisterPage(page.Items) ??
            new Dictionary<string, string>(StringComparer.Ordinal);
        var projected = new AppLibraryItemSummary[page.Items.Count];
        for (var index = 0; index < page.Items.Count; index++)
        {
            var item = page.Items[index];
            if (!_publicIdsByBackendId.TryGetValue(item.ProviderAppId, out var publicId))
            {
                publicId = "app-" + Guid.NewGuid().ToString("N");
                _publicIdsByBackendId[item.ProviderAppId] = publicId;
            }
            TouchLaunch(publicId, item.ProviderAppId);
            projected[index] = new AppLibraryItemSummary(
                publicId, item.DisplayName, item.Kind)
            {
                SavedId = _savedIdIssuer.Issue(_identity, item.StableProviderIdentity),
                ArtworkHandle = artworkHandles.GetValueOrDefault(item.ProviderAppId),
                SourceAttribution = item.SourceAttribution,
            };
        }
        TrimLaunchWindow();
        return new AppLibraryCursorPageSummary(
            projected, page.Before, page.After, page.Revision);
    }

    private void EnsureRevision(string revision)
    {
        if (string.Equals(_currentRevision, revision, StringComparison.Ordinal)) return;
        _currentRevision = revision;
        _launchByPublicId.Clear();
        _publicIdsByBackendId.Clear();
        _launchRecency.Clear();
        _launchNodes.Clear();
        _artwork?.Reset();
    }

    private void TouchLaunch(string publicId, string backendAppId)
    {
        if (_launchNodes.Remove(publicId, out var existing))
            _launchRecency.Remove(existing);
        _launchNodes[publicId] = _launchRecency.AddLast(publicId);
        _launchByPublicId[publicId] = new LaunchRegistration(backendAppId);
    }

    private void TrimLaunchWindow()
    {
        while (_launchByPublicId.Count > MaximumRetainedLaunchIds)
        {
            var publicId = _launchRecency.First!.Value;
            _launchRecency.RemoveFirst();
            _launchNodes.Remove(publicId);
            if (_launchByPublicId.Remove(publicId, out var registration))
                _publicIdsByBackendId.Remove(registration.BackendAppId);
        }
    }

    private static AppLibraryBackendCursorRequest ValidateRequest(
        AppLibraryCursorRequest request)
    {
        if (request.Query is null || !Enum.IsDefined(request.Query.Sort) ||
            request.Query.Kind is { } kind && !Enum.IsDefined(kind) ||
            request.Limit is < 1 or > PlatformCapabilityBroker.MaximumAppLibraryPageSize ||
            (request.Cursor is null) != (request.Direction is null) ||
            request.Cursor is { Length: > 128 } ||
            request.Query.SourceAttribution is { Length: > 64 } ||
            request.Query.SearchText is { Length: > 96 } ||
            request.Query.SearchText is { } search &&
                (string.IsNullOrWhiteSpace(search) || search.Any(char.IsControl) ||
                 search != NormalizeSearchText(search)) ||
            request.Query.FavoriteSavedIds is null ||
            request.Query.FavoriteSavedIds.Count > 128 ||
            request.Query.FavoriteSavedIds.Distinct(StringComparer.Ordinal).Count() !=
                request.Query.FavoriteSavedIds.Count)
            throw new BrokerException(
                "invalid_payload", "App-library cursor query is invalid.");
        if (request.Query.SourceAttribution is { } source)
            ContractValidation.DisplayName(source);
        ValidateSavedIds(request.Query.FavoriteSavedIds, 128);
        return new AppLibraryBackendCursorRequest(
            new AppLibraryBackendQuery(
                request.Query.InstalledOnly,
                request.Query.Kind,
                request.Query.SourceAttribution,
                request.Query.Sort)
            {
                SearchText = request.Query.SearchText,
            },
            request.Cursor, request.Direction, request.Limit, request.Refresh);
    }

    private static string? NormalizeSearchText(string? value) => value is null
        ? null
        : string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static AppLibraryBackendCursorPage ValidatePage(
        AppLibraryBackendCursorPage? page,
        int limit)
    {
        if (page is null || page.Items is null || page.Items.Count > limit ||
            page.Revision is not { Length: > 0 and <= 128 } ||
            page.Before is { Length: > 128 } || page.After is { Length: > 128 })
            throw new BrokerException(
                "invalid_backend_data", "App-library cursor page is invalid.");
        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in page.Items)
        {
            if (item is null || !Enum.IsDefined(item.Kind))
                throw InvalidItem();
            ContractValidation.OpaqueId(item.ProviderAppId, "invalid_backend_data");
            AppLibrarySavedIdIssuer.ValidateStableProviderIdentity(item.StableProviderIdentity);
            ContractValidation.DisplayName(item.DisplayName);
            ContractValidation.DisplayName(item.SourceAttribution);
            if (!providerIds.Add(item.ProviderAppId) ||
                !stableIds.Add(item.StableProviderIdentity)) throw InvalidItem();
        }
        return page with { Items = page.Items.ToArray() };
    }

    private static BrokerException InvalidItem() =>
        new("invalid_backend_data", "App-library entry is invalid.");

    private static void ValidateSavedIds(
        IReadOnlyList<string>? savedIds,
        int maximumCount)
    {
        if (savedIds is null ||
            savedIds.Count > maximumCount)
            throw new BrokerException(
                "invalid_payload", "Saved app-library identifier bounds are invalid.");
        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in savedIds)
        {
            ContractValidation.OpaqueId(savedId);
            if (!savedId.StartsWith("saved-", StringComparison.Ordinal) ||
                !requested.Add(savedId))
                throw new BrokerException(
                    "invalid_payload", "Saved app-library identifiers are invalid.");
        }
    }

    public void Dispose() => _artwork?.Dispose();

    private sealed record LaunchRegistration(string BackendAppId);
}
