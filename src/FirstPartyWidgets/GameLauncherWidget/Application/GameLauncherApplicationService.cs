using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WindowsAppLibraryProvider;
using PackageAppLibraryProvider =
    GameBarAlternative.WindowsAppLibraryProvider.WindowsAppLibraryProvider;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal sealed class GameLauncherApplicationService(
    PackageAppLibraryProvider provider,
    GameLauncherSavedIdIssuer savedIds,
    GameLauncherStateFileStore state) : IGameLauncherApplicationService
{
    private const int MaximumTraversalPages = 160;
    private const int MaximumArtworkEntries = 128;
    private readonly PackageAppLibraryProvider _provider = provider ??
        throw new ArgumentNullException(nameof(provider));
    private readonly GameLauncherSavedIdIssuer _savedIds = savedIds ??
        throw new ArgumentNullException(nameof(savedIds));
    private readonly GameLauncherStateFileStore _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ArtworkRegistration> _artwork =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _artworkContent =
        new(StringComparer.Ordinal);
    private readonly Queue<string> _artworkOrder = new();
    public bool OwnsArtworkContent => true;

    public async ValueTask<WidgetAppLibraryPage> QueryAsync(
        WidgetAppLibraryQuery query,
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var backendQuery = ToBackend(query);
            if (query.FavoriteSavedIds.Count != 0)
            {
                backendQuery = backendQuery with
                {
                    StableIdentityFilter = await ResolveStableIdentitiesAsync(
                        query.FavoriteSavedIds,
                        backendQuery with { StableIdentityFilter = null },
                        refresh,
                        cancellationToken).ConfigureAwait(false),
                };
                refresh = false;
            }
            var page = await _provider.QueryAppLibraryAsync(
                new AppLibraryBackendCursorRequest(
                    backendQuery,
                    cursor?.Value,
                    direction is null ? null : ToBackend(direction.Value),
                    limit,
                    refresh),
                cancellationToken).ConfigureAwait(false);
            return Project(page);
        }
        catch (BrokerException exception)
        {
            throw Safe(exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<WidgetAppLibraryItem>> ResolveSavedAsync(
        IReadOnlyList<string> savedIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(savedIds);
        if (savedIds.Count == 0) return [];
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requested = savedIds.ToHashSet(StringComparer.Ordinal);
            var matches = new Dictionary<string, AppLibraryBackendItemSummary>(
                StringComparer.Ordinal);
            await TraverseAsync(
                new AppLibraryBackendQuery(),
                refresh: true,
                cancellationToken,
                item =>
                {
                    var savedId = _savedIds.Issue(item.StableProviderIdentity);
                    if (requested.Contains(savedId)) matches[savedId] = item;
                    return matches.Count == requested.Count;
                }).ConfigureAwait(false);
            return savedIds.Where(matches.ContainsKey)
                .Select(savedId => Project(matches[savedId], []))
                .ToArray();
        }
        catch (BrokerException exception)
        {
            throw Safe(exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<WidgetRunningAppObservation> ObserveRunningAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var observed = await _provider.ObserveRunningAppsAsync(cancellationToken)
                .ConfigureAwait(false);
            return new(
                observed.Items.Select(item => new WidgetRunningAppCandidate(
                    _savedIds.Issue(item.StableProviderIdentity),
                    item.DisplayName,
                    ToWidget(item.Kind),
                    item.SourceAttribution)).ToArray(),
                observed.Revision);
        }
        catch (BrokerException exception)
        {
            throw Safe(exception);
        }
    }

    public async ValueTask<WidgetAppLibraryItem?> ConfirmRunningAsync(
        string savedId,
        string revision,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var observed = await _provider.ObserveRunningAppsAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(observed.Revision, revision, StringComparison.Ordinal))
                return null;
            var match = observed.Items.SingleOrDefault(item => string.Equals(
                _savedIds.Issue(item.StableProviderIdentity),
                savedId,
                StringComparison.Ordinal));
            if (match is null) return null;
            var page = await _provider.QueryAppLibraryAsync(
                new AppLibraryBackendCursorRequest(
                    new AppLibraryBackendQuery
                    {
                        StableIdentityFilter = [match.StableProviderIdentity],
                    },
                    null,
                    null,
                    1),
                cancellationToken).ConfigureAwait(false);
            return page.Items.Count == 1 && string.Equals(
                page.Items[0].StableProviderIdentity,
                match.StableProviderIdentity,
                StringComparison.Ordinal)
                    ? Project(page.Items[0], page.Sources)
                    : null;
        }
        catch (BrokerException exception)
        {
            throw Safe(exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<WidgetAppLaunchObservation> LaunchObservedAsync(
        string appId,
        WidgetAppLaunchOverlayBehavior overlayBehavior,
        CancellationToken cancellationToken)
    {
        _ = overlayBehavior;
        try
        {
            var result = await _provider.LaunchAppLibraryItemObservedAsync(
                appId, cancellationToken).ConfigureAwait(false);
            return new(
                result.State switch
                {
                    AppLibraryLaunchObservationState.LauncherStarted =>
                        WidgetAppLaunchObservationState.LauncherStarted,
                    AppLibraryLaunchObservationState.Running =>
                        WidgetAppLaunchObservationState.Running,
                    AppLibraryLaunchObservationState.Ended =>
                        WidgetAppLaunchObservationState.Ended,
                    _ => WidgetAppLaunchObservationState.RequestAccepted,
                },
                result.SupportsRunning,
                result.SupportsEnded);
        }
        catch (BrokerException exception)
        {
            throw Safe(exception);
        }
    }

    public async ValueTask<string?> ResolveArtworkAsync(
        WidgetAppLibraryArtwork artwork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artwork);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_artwork.TryGetValue(artwork.Handle, out var registration) ||
                !string.Equals(registration.Revision, artwork.Revision,
                    StringComparison.Ordinal))
                return null;
            if (_artworkContent.TryGetValue(artwork.Handle, out var cached))
                return cached;
            var result = await _provider.GetAppLibraryIconAsync(
                registration.AppId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _artworkContent[artwork.Handle] = result.PngBase64;
            return result.PngBase64;
        }
        catch (BrokerException exception)
        {
            if (exception.Code is "app_not_found" or "platform_unavailable")
                return null;
            throw Safe(exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask<WidgetPrivateStateValue<GameLauncherPrivateState>> ReadStateAsync(
        CancellationToken cancellationToken) => _state.ReadAsync(cancellationToken);

    public ValueTask<WidgetPrivateStateMutation> WriteStateAsync(
        GameLauncherPrivateState state,
        long? expectedRevision,
        CancellationToken cancellationToken) =>
        _state.WriteAsync(state, expectedRevision, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        _artwork.Clear();
        _artworkContent.Clear();
        _artworkOrder.Clear();
        _gate.Dispose();
    }

    private async Task<IReadOnlyList<string>> ResolveStableIdentitiesAsync(
        IReadOnlyList<string> requestedSavedIds,
        AppLibraryBackendQuery query,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var requested = requestedSavedIds.ToHashSet(StringComparer.Ordinal);
        var matches = new List<string>(requested.Count);
        await TraverseAsync(query, refresh, cancellationToken, item =>
        {
            if (requested.Contains(_savedIds.Issue(item.StableProviderIdentity)))
                matches.Add(item.StableProviderIdentity);
            return matches.Count == requested.Count;
        }).ConfigureAwait(false);
        return matches;
    }

    private async Task TraverseAsync(
        AppLibraryBackendQuery query,
        bool refresh,
        CancellationToken cancellationToken,
        Func<AppLibraryBackendItemSummary, bool> visit)
    {
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        string? revision = null;
        for (var pageIndex = 0; pageIndex < MaximumTraversalPages; pageIndex++)
        {
            var page = await _provider.QueryAppLibraryAsync(
                new AppLibraryBackendCursorRequest(
                    query,
                    cursor,
                    cursor is null ? null : AppLibraryCursorDirection.After,
                    WidgetAppLibraryService.MaximumPageSize,
                    Refresh: cursor is null && refresh),
                cancellationToken).ConfigureAwait(false);
            revision ??= page.Revision;
            if (!string.Equals(revision, page.Revision, StringComparison.Ordinal))
                throw new BrokerException(
                    "invalid_backend_data", "The app library changed during traversal.");
            foreach (var item in page.Items)
                if (visit(item)) return;
            if (page.After is null) return;
            if (!cursors.Add(page.After)) throw new BrokerException(
                "invalid_backend_data", "The app-library cursor loop is invalid.");
            cursor = page.After;
        }
        throw new BrokerException(
            "invalid_backend_data", "The app library exceeded its traversal bound.");
    }

    private WidgetAppLibraryPage Project(AppLibraryBackendCursorPage page) => new(
        page.Items.Select(item => Project(item, page.Sources)).ToArray(),
        page.Before,
        page.After,
        page.Revision)
    {
        Sources = page.Sources.Select(Project).ToArray(),
    };

    private WidgetAppLibraryItem Project(
        AppLibraryBackendItemSummary item,
        IReadOnlyList<AppLibrarySourceSummary> sources)
    {
        var sourceId = SourceId(string.IsNullOrWhiteSpace(item.SourceIdentity)
            ? item.SourceAttribution
            : item.SourceIdentity);
        var source = sources.FirstOrDefault(candidate => string.Equals(
                         candidate.SourceId, sourceId, StringComparison.Ordinal)) ??
                     sources.FirstOrDefault(candidate => string.Equals(
                         candidate.DisplayName, item.SourceAttribution,
                         StringComparison.Ordinal));
        sourceId = source?.SourceId ?? sourceId;
        var actions = item.SupportedActions ?? (item.IsLaunchable
            ? [AppLibraryAction.Launch]
            : []);
        var artwork = RegisterArtwork(item);
        var metadataRevision = ContentId(
            "metadata", item.StableProviderIdentity, item.ArtworkRevision);
        var retrievedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new(
            item.ProviderAppId,
            _savedIds.Issue(item.StableProviderIdentity),
            new WidgetAppLibraryPresentation(
                item.DisplayName,
                ToWidget(item.Kind),
                new WidgetAppLibrarySourceReference(
                    sourceId, source?.DisplayName ?? item.SourceAttribution),
                new WidgetAppLibraryAvailability(
                    item.AvailabilityState switch
                    {
                        AppLibraryAvailabilityState.StaleSource =>
                            WidgetAppLibraryAvailabilityState.StaleSource,
                        AppLibraryAvailabilityState.Unavailable =>
                            WidgetAppLibraryAvailabilityState.Unavailable,
                        _ => WidgetAppLibraryAvailabilityState.Installed,
                    },
                    item.IsLaunchable,
                    item.AvailabilityStatusCode ??
                        (item.IsLaunchable ? "installed" : "play_unavailable")),
                artwork,
                new WidgetAppLibraryMetadata(
                    metadataRevision,
                    new WidgetAppLibraryMetadataAttribution(
                        "Game Launcher Community",
                        metadataRevision,
                        source?.DisplayName ?? item.SourceAttribution,
                        retrievedAt)),
                new WidgetAppLibraryCapabilitySet(actions.Select(ToWidget).ToArray()),
                ActiveOperation: null));
    }

    private WidgetAppLibraryArtworkSet RegisterArtwork(
        AppLibraryBackendItemSummary item)
    {
        if (string.IsNullOrWhiteSpace(item.ArtworkRevision))
            return new([]);
        var revision = ContentId(
            "artwork-revision", item.StableProviderIdentity, item.ArtworkRevision);
        var handle = ContentId(
            "artwork-handle", item.StableProviderIdentity, item.ArtworkRevision);
        if (!_artwork.ContainsKey(handle))
        {
            _artwork.Add(handle, new(item.ProviderAppId, revision));
            _artworkOrder.Enqueue(handle);
            while (_artworkOrder.Count > MaximumArtworkEntries)
            {
                var retired = _artworkOrder.Dequeue();
                _artwork.Remove(retired);
                _artworkContent.Remove(retired);
            }
        }
        var fallback = item.Kind == AppLibraryKind.Game
            ? WidgetAppLibraryArtworkFallback.Game
            : WidgetAppLibraryArtworkFallback.Application;
        return new([
            new(WidgetAppLibraryArtworkRole.Tile, handle, revision, fallback),
            new(WidgetAppLibraryArtworkRole.Cover, handle, revision, fallback),
            new(WidgetAppLibraryArtworkRole.Hero, handle, revision, fallback),
        ]);
    }

    private static WidgetAppLibrarySource Project(AppLibrarySourceSummary source) => new(
        source.SourceId,
        source.DisplayName,
        source.Health switch
        {
            AppLibrarySourceHealth.Degraded => WidgetAppLibrarySourceHealth.Degraded,
            AppLibrarySourceHealth.Unavailable => WidgetAppLibrarySourceHealth.Unavailable,
            AppLibrarySourceHealth.Refreshing => WidgetAppLibrarySourceHealth.Refreshing,
            _ => WidgetAppLibrarySourceHealth.Healthy,
        },
        source.Revision,
        source.StatusCode)
    {
        AccountState = source.AccountState switch
        {
            AppLibrarySourceAccountState.SignedOut =>
                WidgetAppLibrarySourceAccountState.SignedOut,
            AppLibrarySourceAccountState.SigningIn =>
                WidgetAppLibrarySourceAccountState.SigningIn,
            AppLibrarySourceAccountState.Ready => WidgetAppLibrarySourceAccountState.Ready,
            AppLibrarySourceAccountState.Expired => WidgetAppLibrarySourceAccountState.Expired,
            AppLibrarySourceAccountState.Denied => WidgetAppLibrarySourceAccountState.Denied,
            AppLibrarySourceAccountState.Unavailable =>
                WidgetAppLibrarySourceAccountState.Unavailable,
            _ => WidgetAppLibrarySourceAccountState.NotApplicable,
        },
        LastSuccessfulRefreshAtUnixMilliseconds =
            source.LastSuccessfulRefreshAtUnixMilliseconds,
    };

    private static AppLibraryBackendQuery ToBackend(WidgetAppLibraryQuery query) => new(
        query.InstalledOnly,
        query.Kind switch
        {
            WidgetAppLibraryKind.Application => AppLibraryKind.Application,
            WidgetAppLibraryKind.Game => AppLibraryKind.Game,
            WidgetAppLibraryKind.Unknown => AppLibraryKind.Unknown,
            _ => null,
        },
        query.SourceAttribution,
        query.Sort switch
        {
            WidgetAppLibrarySortOrder.DisplayNameDescending =>
                AppLibrarySortOrder.DisplayNameDescending,
            WidgetAppLibrarySortOrder.SourceThenDisplayName =>
                AppLibrarySortOrder.SourceThenDisplayName,
            _ => AppLibrarySortOrder.DisplayName,
        })
    {
        SearchText = query.SearchText,
    };

    private static AppLibraryCursorDirection ToBackend(WidgetCursorDirection direction) =>
        direction == WidgetCursorDirection.Before
            ? AppLibraryCursorDirection.Before
            : AppLibraryCursorDirection.After;

    private static WidgetAppLibraryKind ToWidget(AppLibraryKind kind) => kind switch
    {
        AppLibraryKind.Application => WidgetAppLibraryKind.Application,
        AppLibraryKind.Game => WidgetAppLibraryKind.Game,
        _ => WidgetAppLibraryKind.Unknown,
    };

    private static WidgetAppLibraryAction ToWidget(AppLibraryAction action) => action switch
    {
        AppLibraryAction.Install => WidgetAppLibraryAction.Install,
        AppLibraryAction.Pause => WidgetAppLibraryAction.Pause,
        AppLibraryAction.Resume => WidgetAppLibraryAction.Resume,
        AppLibraryAction.Cancel => WidgetAppLibraryAction.Cancel,
        AppLibraryAction.Update => WidgetAppLibraryAction.Update,
        AppLibraryAction.Repair => WidgetAppLibraryAction.Repair,
        AppLibraryAction.Move => WidgetAppLibraryAction.Move,
        AppLibraryAction.Import => WidgetAppLibraryAction.Import,
        AppLibraryAction.Uninstall => WidgetAppLibraryAction.Uninstall,
        AppLibraryAction.CloudSync => WidgetAppLibraryAction.CloudSync,
        AppLibraryAction.OpenSourceClient => WidgetAppLibraryAction.OpenSourceClient,
        AppLibraryAction.ManageAddOns => WidgetAppLibraryAction.ManageAddOns,
        _ => WidgetAppLibraryAction.Launch,
    };

    private static string SourceId(string value) => "source-" + Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(value)).AsSpan(0, 12)).ToLowerInvariant();

    private static string ContentId(string kind, params string[] values) =>
        "gl-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                kind + "\0" + string.Join("\0", values))).AsSpan(0, 16))
            .ToLowerInvariant();

    private static WidgetCapabilityException Safe(BrokerException exception) => new(
        exception.Code is "invalid_payload" or "invalid_cursor" or "app_not_found" or
            "stale_observation" or "platform_unavailable"
            ? exception.Code
            : "app_library_unavailable",
        exception.Code switch
        {
            "app_not_found" => "The selected game is no longer available.",
            "stale_observation" => "The running application list changed.",
            _ => "The installed game library is unavailable.",
        });

    private sealed record ArtworkRegistration(string AppId, string Revision);
}
