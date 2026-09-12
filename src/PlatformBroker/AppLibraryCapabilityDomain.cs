using System.Text.Json;
using System.Text;

namespace WidgetRail.PlatformBroker;

internal sealed partial class AppLibraryCapabilityDomain : IDisposable
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
            PlatformCapabilities.TaskWindowsList or PlatformCapabilities.TaskWindowsSwitch or
                PlatformCapabilities.TaskWindowsClose =>
                await TaskWindowsAsync(operation, payload, cancellationToken).ConfigureAwait(false),
            PlatformCapabilities.AppLibraryList => BrokerJson.ToElement(
                await QueryAsync(payload, cancellationToken).ConfigureAwait(false)),
            PlatformCapabilities.AppLibraryResolveSaved => BrokerJson.ToElement(
                await ResolveSavedAsync(payload, cancellationToken).ConfigureAwait(false)),
            PlatformCapabilities.AppRunningList => BrokerJson.ToElement(
                await ObserveRunningAsync(payload, cancellationToken).ConfigureAwait(false)),
            PlatformCapabilities.AppRunningConfirm => BrokerJson.ToElement(
                await ConfirmRunningAsync(payload, cancellationToken).ConfigureAwait(false)),
            PlatformCapabilities.AppRunningRegister => BrokerJson.ToElement(
                await RegisterRunningAsync(payload, cancellationToken).ConfigureAwait(false)),
            PlatformCapabilities.AppRunningForget =>
                await ForgetRunningAsync(payload, cancellationToken).ConfigureAwait(false),
            PlatformCapabilities.AppLibraryLaunch =>
                await LaunchAsync(payload, observed: false, cancellationToken)
                    .ConfigureAwait(false),
            PlatformCapabilities.AppLibraryLaunchObserved =>
                await LaunchAsync(payload, observed: true, cancellationToken)
                    .ConfigureAwait(false),
            _ => throw new BrokerException(
                "unsupported_operation", "App-library operation is unsupported."),
        };

    private async Task<RunningAppObservationSummary> ObserveRunningAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        BrokerCapabilityDomains.DemandEmptyPayload(payload);
        var observed = ValidateRunningPage(await _backend.ObserveRunningAppsAsync(
            cancellationToken).ConfigureAwait(false));
        var artworkHandles = _artwork?.RegisterPage(observed.Items
            .Where(item => item.ArtworkItem is not null).Select(item => item.ArtworkItem!).ToArray()) ??
            new Dictionary<string, string>(StringComparer.Ordinal);
        return new RunningAppObservationSummary(observed.Items.Select(item =>
            new RunningAppCandidateSummary(
                _savedIdIssuer.Issue(_identity, item.StableProviderIdentity),
                item.DisplayName, item.Kind, item.SourceAttribution)
            {
                Artwork = item.ArtworkItem is { } artwork && artworkHandles.TryGetValue(artwork.ProviderAppId, out var handle)
                    ? new([new(AppLibraryArtworkRole.Tile, handle, artwork.ArtworkRevision,
                        item.Kind == AppLibraryKind.Game ? AppLibraryArtworkFallback.Game : AppLibraryArtworkFallback.Application)])
                    : new([]),
            }).ToArray(),
            observed.Revision);
    }

    private async Task<ConfirmRunningAppSummary> ConfirmRunningAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<ConfirmRunningAppRequest>(payload);
        ValidateSavedIds([request.SavedId], 1);
        if (request.Revision is not { Length: > 0 and <= 128 })
            throw new BrokerException("invalid_payload", "Running-app revision is invalid.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var observed = ValidateRunningPage(await _backend.ObserveRunningAppsAsync(
                cancellationToken).ConfigureAwait(false));
            if (!string.Equals(request.Revision, observed.Revision, StringComparison.Ordinal))
                return new ConfirmRunningAppSummary(null);
            var match = observed.Items.SingleOrDefault(item => string.Equals(
                _savedIdIssuer.Issue(_identity, item.StableProviderIdentity),
                request.SavedId, StringComparison.Ordinal));
            if (match is null) return new ConfirmRunningAppSummary(null);
            var page = ValidatePage(await _backend.QueryAppLibraryAsync(
                new AppLibraryBackendCursorRequest(
                    new AppLibraryBackendQuery
                    {
                        StableIdentityFilter = [match.StableProviderIdentity],
                    }, null, null, 1), cancellationToken).ConfigureAwait(false), 1);
            if (page.Items.Count != 1 || !string.Equals(
                    page.Items[0].StableProviderIdentity,
                    match.StableProviderIdentity, StringComparison.Ordinal))
                return new ConfirmRunningAppSummary(null);
            return new ConfirmRunningAppSummary(ProjectPage(page).Items.Single());
        }
        finally
        {
            _gate.Release();
        }
    }

    private static RunningAppBackendObservationPage ValidateRunningPage(
        RunningAppBackendObservationPage? page)
    {
        if (page is null || page.Items is null || page.Items.Count > 64 ||
            page.Revision is not { Length: > 0 and <= 128 })
            throw new BrokerException("invalid_backend_data", "Running-app observation is invalid.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in page.Items)
        {
            if (item is null || !Enum.IsDefined(item.Kind) ||
                item.StableProviderIdentity is not { Length: > 0 and <= 128 } ||
                item.InstanceEvidence is not { Length: > 0 and <= 128 } ||
                !identities.Add(item.StableProviderIdentity))
                throw new BrokerException("invalid_backend_data", "Running-app observation is invalid.");
            ContractValidation.DisplayName(item.DisplayName);
            ContractValidation.DisplayName(item.SourceAttribution);
            if (item.ArtworkItem is { } artwork)
            {
                _ = ValidatePage(new AppLibraryBackendCursorPage([artwork], null, null, "running-artwork"), 1);
                if (!string.Equals(artwork.StableProviderIdentity, item.StableProviderIdentity, StringComparison.Ordinal))
                    throw new BrokerException("invalid_backend_data", "Running artwork identity is invalid.");
            }
        }
        return page with { Items = page.Items.ToArray() };
    }

    private async Task<RegisterRunningAppSummary> RegisterRunningAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<RegisterRunningAppRequest>(payload);
        ValidateSavedIds([request.SavedId], 1);
        if (request.Revision is not { Length: > 0 and <= 128 })
            throw new BrokerException("invalid_payload", "Running-app revision is invalid.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var observed = ValidateRunningPage(await _backend.ObserveRunningAppsAsync(
                cancellationToken).ConfigureAwait(false));
            if (!string.Equals(request.Revision, observed.Revision, StringComparison.Ordinal))
                throw new BrokerException(
                    "stale_observation", "The running-app observation is stale.");
            var match = observed.Items.SingleOrDefault(item => string.Equals(
                _savedIdIssuer.Issue(_identity, item.StableProviderIdentity),
                request.SavedId, StringComparison.Ordinal));
            if (match is null)
                throw new BrokerException(
                    "app_not_found", "The selected running app is no longer available.");
            var registered = await _backend.RegisterRunningAppAsync(
                _identity,
                new RegisterRunningAppBackendRequest(
                    request.SavedId, match.StableProviderIdentity,
                    match.InstanceEvidence, observed.Revision),
                cancellationToken).ConfigureAwait(false);
            if (registered?.Item is null)
                throw InvalidItem();
            var validated = ValidatePage(new AppLibraryBackendCursorPage(
                [registered.Item], null, null, "registered"), 1).Items.Single();
            if (!string.Equals(
                    _savedIdIssuer.Issue(_identity, validated.StableProviderIdentity),
                    request.SavedId, StringComparison.Ordinal))
                throw InvalidItem();
            var projected = ProjectItem(validated, []);
            TrimLaunchWindow();
            return new RegisterRunningAppSummary(
                projected, registered.AlreadyRegistered);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<JsonElement> ForgetRunningAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = BrokerJson.ParsePayload<ForgetRunningAppRequest>(payload);
        ValidateSavedIds([request.SavedId], 1);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _backend.ForgetRunningAppAsync(
                _identity, request.SavedId, cancellationToken).ConfigureAwait(false);
            _launchByPublicId.Clear();
            _publicIdsByBackendId.Clear();
            _launchRecency.Clear();
            _launchNodes.Clear();
            return BrokerCapabilityDomains.Acknowledged();
        }
        finally
        {
            _gate.Release();
        }
    }

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
                        Refresh: false),
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

            var unresolved = request.SavedIds
                .Where(savedId => !matches.ContainsKey(savedId)).ToArray();
            if (unresolved.Length != 0)
            {
                var registered = await _backend.ResolveRegisteredRunningAppsAsync(
                    _identity, unresolved, cancellationToken).ConfigureAwait(false);
                if (registered is null || registered.Count > unresolved.Length)
                    throw InvalidItem();
                var validated = ValidatePage(new AppLibraryBackendCursorPage(
                    registered, null, null, "registered"), unresolved.Length);
                foreach (var item in validated.Items)
                {
                    var savedId = _savedIdIssuer.Issue(
                        _identity, item.StableProviderIdentity);
                    if (!unresolved.Contains(savedId, StringComparer.Ordinal) ||
                        matches.ContainsKey(savedId))
                        throw InvalidItem();
                    matches[savedId] = item;
                }
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
                    _identity, registration.BackendAppId, cancellationToken)
                    .ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            var result = await _backend.LaunchAppLibraryItemObservedAsync(
                _identity, registration.BackendAppId, cancellationToken)
                .ConfigureAwait(false);
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
            projected[index] = ProjectItem(
                page.Items[index], page.Sources,
                artworkHandles.GetValueOrDefault(page.Items[index].ProviderAppId));
        TrimLaunchWindow();
        return new AppLibraryCursorPageSummary(
            projected, page.Before, page.After, page.Revision)
        {
            Sources = page.Sources.ToArray(),
        };
    }

    private AppLibraryItemSummary ProjectItem(
        AppLibraryBackendItemSummary item,
        IReadOnlyList<AppLibrarySourceSummary> sources,
        string? artworkHandle = null)
    {
        if (!_publicIdsByBackendId.TryGetValue(item.ProviderAppId, out var publicId))
        {
            publicId = "app-" + Guid.NewGuid().ToString("N");
            _publicIdsByBackendId[item.ProviderAppId] = publicId;
        }
        if (item.IsLaunchable) TouchLaunch(publicId, item.ProviderAppId);
        return new AppLibraryItemSummary(
            publicId,
            _savedIdIssuer.Issue(_identity, item.StableProviderIdentity),
            CreatePresentation(item, artworkHandle, sources));
    }

    private static AppLibraryItemPresentation CreatePresentation(
        AppLibraryBackendItemSummary item,
        string? artworkHandle,
        IReadOnlyList<AppLibrarySourceSummary> sources)
    {
        var sourceMaterial = string.IsNullOrEmpty(item.SourceIdentity)
            ? item.SourceAttribution
            : item.SourceIdentity;
        var derivedSourceId = "source-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(sourceMaterial)).AsSpan(0, 12))
            .ToLowerInvariant();
        var exactSource = sources.SingleOrDefault(source => string.Equals(
            source.SourceId, derivedSourceId, StringComparison.Ordinal));
        if (exactSource is null)
        {
            var displayMatches = sources.Where(source => string.Equals(
                source.DisplayName, item.SourceAttribution, StringComparison.Ordinal)).ToArray();
            if (displayMatches.Length == 1) exactSource = displayMatches[0];
        }
        var sourceId = exactSource?.SourceId ?? derivedSourceId;
        var fallback = item.Kind == AppLibraryKind.Game
            ? AppLibraryArtworkFallback.Game
            : AppLibraryArtworkFallback.Application;
        AppLibraryArtworkSummary[] artwork = artworkHandle is null ? [] :
        [
            new(AppLibraryArtworkRole.Tile, artworkHandle,
                item.ArtworkRevision, fallback),
        ];
        var actions = item.SupportedActions ?? (item.IsLaunchable
            ? [AppLibraryAction.Launch]
            : []);
        var statusCode = item.AvailabilityStatusCode ??
            (item.IsLaunchable ? "installed" : "play_unavailable");
        return new AppLibraryItemPresentation(
            item.DisplayName,
            item.Kind,
            new AppLibrarySourceReference(
                sourceId, exactSource?.DisplayName ?? item.SourceAttribution),
            new AppLibraryAvailabilitySummary(
                item.AvailabilityState,
                item.IsLaunchable,
                statusCode),
            new AppLibraryArtworkSet(artwork),
            Metadata: null,
            new AppLibraryCapabilitySet(actions),
            ActiveOperation: null);
    }

    private void EnsureRevision(string revision)
    {
        if (string.Equals(_currentRevision, revision, StringComparison.Ordinal)) return;
        _currentRevision = revision;
        _launchByPublicId.Clear();
        _publicIdsByBackendId.Clear();
        _launchRecency.Clear();
        _launchNodes.Clear();
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
            page.Sources is null || page.Sources.Count > 16 ||
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
            if (!string.IsNullOrEmpty(item.ArtworkRevision))
                ContractValidation.OpaqueId(item.ArtworkRevision, "invalid_backend_data");
            var actions = item.SupportedActions ?? (item.IsLaunchable
                ? [AppLibraryAction.Launch]
                : []);
            var statusCode = item.AvailabilityStatusCode ??
                (item.IsLaunchable ? "installed" : "play_unavailable");
            if (!Enum.IsDefined(item.AvailabilityState) ||
                item.AvailabilityState != AppLibraryAvailabilityState.Installed &&
                    item.IsLaunchable ||
                actions.Count > 13 || actions.Distinct().Count() != actions.Count ||
                actions.Any(action => !Enum.IsDefined(action)) ||
                actions.Contains(AppLibraryAction.Launch) != item.IsLaunchable ||
                statusCode is not { Length: > 0 and <= 48 } ||
                statusCode.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                    character is not '_' and not '-' and not '.'))
                throw InvalidItem();
            if (!providerIds.Add(item.ProviderAppId) ||
                !stableIds.Add(item.StableProviderIdentity)) throw InvalidItem();
        }
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in page.Sources)
        {
            if (source is null || !Enum.IsDefined(source.Health) ||
                !Enum.IsDefined(source.AccountState) ||
                source.Revision < 0 ||
                source.LastSuccessfulRefreshAtUnixMilliseconds is < 0 ||
                source.StatusCode is not { Length: > 0 and <= 48 } ||
                source.StatusCode.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.') ||
                !sourceIds.Add(source.SourceId))
                throw new BrokerException(
                    "invalid_backend_data", "App-library source status is invalid.");
            ContractValidation.OpaqueId(source.SourceId, "invalid_backend_data");
            ContractValidation.DisplayName(source.DisplayName);
        }
        return page with
        {
            Items = page.Items.ToArray(),
            Sources = page.Sources.ToArray(),
        };
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

    public void Dispose()
    {
        WindowPreviewRegistry.Retire(this);
        _artwork?.Dispose();
    }

    private sealed record LaunchRegistration(string BackendAppId);
}
