using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace WidgetRail.Playnite;

internal sealed partial class PlayniteApiDispatcher : ILocalApiDispatcher, IDisposable
{
    private readonly IPlayniteAPI api;
    private readonly ConnectionDescriptor connection;
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private readonly ChangeFeed changes = new ChangeFeed();
    private readonly CommandRegistry<LocalApiResponse> commands = new CommandRegistry<LocalApiResponse>();
    private readonly AchievementAdapter achievements;
    private readonly OptionalPluginData optionalData;
    private readonly SemaphoreSlim optionalReads = new SemaphoreSlim(1, 1);
    private long libraryRevision = 1;
    private bool disposed;
    internal PlayniteApiDispatcher(IPlayniteAPI api, ConnectionDescriptor connection)
    {
        this.api = api; this.connection = connection;
        achievements = new AchievementAdapter(api, changes);
        optionalData = new OptionalPluginData(api.Paths.ExtensionsDataPath);
        api.Database.Games.ItemUpdated += GamesUpdated;
        api.Database.Games.ItemCollectionChanged += GamesChanged;
        StartCaptureCompanions();
    }
    private void GamesUpdated(object sender, ItemUpdatedEventArgs<Game> args) => LibraryChanged();
    private void GamesChanged(object sender, ItemCollectionChangedEventArgs<Game> args) => LibraryChanged();
    internal void LibraryChanged()
    { Interlocked.Increment(ref libraryRevision); changes.Publish(new IntegrationEvent("library-changed")); captureCompanions?.Invalidate(); }
    internal void GameStateChanged(Guid gameId)
    { Interlocked.Increment(ref libraryRevision); changes.Publish(new IntegrationEvent("game-state", gameId.ToString("D"))); }

    public async Task<LocalApiResponse> DispatchAsync(LocalApiRequest request, CancellationToken token)
    {
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (request.Method == "GET" && request.Path == "/v1/connection")
            {
                var provider = await OnUiAsync(() => achievements.Status(), linked.Token).ConfigureAwait(false);
                return WireJson.Response(new
                {
                    pluginId = IntegrationIdentity.PluginId, pluginVersion = IntegrationIdentity.Version,
                    protocolVersion = IntegrationIdentity.ProtocolVersion, profileId = connection.ProfileId,
                    instanceId = connection.InstanceId, port = connection.Port,
                    libraryRevision = Interlocked.Read(ref libraryRevision),
                    paths = new { application = api.Paths.ApplicationPath, configuration = api.Paths.ConfigurationPath },
                    commandRetentionSeconds = CommandRegistry<LocalApiResponse>.RetentionSeconds,
                    providers = new { playniteAchievements = provider },
                    capabilities = IntegrationIdentity.RequiredCapabilities.Concat(new[] { "events.v1", "achievements.v1", "achievements.overview.v1", "achievements.capture-settings.v1", "achievements.capture-integration.v1", "activity.v1", "diagnostics.v1" }).ToArray(),
                });
            }
            if (request.Method == "GET" && request.Path == "/v1/diagnostics")
            {
                // These are Playnite-process totals, not per-plugin attribution.
                // Read on explicit diagnostic demand only; never force a GC.
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    return WireJson.Response(new { scope = "playnite-process", instanceId = connection.InstanceId,
                        privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64,
                        managedBytes = GC.GetTotalMemory(false), loadedAssemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length,
                        cpuMilliseconds = process.TotalProcessorTime.TotalMilliseconds,
                        generation0Collections = GC.CollectionCount(0), generation1Collections = GC.CollectionCount(1), generation2Collections = GC.CollectionCount(2) });
            }
            if (request.Method == "GET" && request.Path == "/v1/capture-settings")
                return WireJson.Response(await OnUiAsync(() => achievements.ReadCaptureSettings(), linked.Token).ConfigureAwait(false));
            if (request.Method == "PATCH" && request.Path == "/v1/capture-settings")
                return await RunMutationAsync(request, () => achievements.UpdateCaptureSettings(WireJson.Body(request)), linked.Token).ConfigureAwait(false);
            if (request.Method == "GET" && request.Path == "/v1/capture-integration")
                return WireJson.Response(await OnUiAsync(CaptureIntegration, linked.Token).ConfigureAwait(false));
            if (request.Method == "PUT" && request.Path == "/v1/capture-integration")
                return await RunMutationAsync(request, () => ConfigureCaptureIntegration(WireJson.Body(request)), linked.Token).ConfigureAwait(false);
            if (request.Method == "GET" && request.Path == "/v1/achievements/overview") return await GetAchievementOverviewAsync(request, linked.Token).ConfigureAwait(false);
            if (request.Method == "GET" && request.Path == "/v1/games") return await GetGamesAsync(request, linked.Token).ConfigureAwait(false);
            if (request.Method == "GET" && request.Path == "/v1/artwork") return await GetArtworkManifestAsync(request, linked.Token).ConfigureAwait(false);
            if (request.Method == "POST" && request.Path == "/v1/artwork/resolve") return await ResolveArtworkAsync(request, linked.Token).ConfigureAwait(false);
            if (request.Method == "GET" && request.Path == "/v1/events")
            {
                var after = -1L;
                if (request.Query["after"] != "now" && (!long.TryParse(request.Query["after"] ?? "0", out after) || after < 0)) throw new ApiException(400, "invalid_cursor");
                var batch = await changes.PollAsync(after, WireJson.Integer(request.Query["wait"], 25000, 25000), linked.Token).ConfigureAwait(false);
                return WireJson.Response(new { instanceId = connection.InstanceId, resetRequired = batch.ResetRequired,
                    nextSequence = batch.NextSequence, events = batch.Events.Select(item => new { sequence = item.Sequence,
                        kind = item.Kind, gameId = item.GameId, provider = item.Provider, achievementId = item.AchievementId, observedAt = item.ObservedAt }).ToArray() });
            }
            if (request.Method == "GET" && request.Path == "/v1/categories")
                return WireJson.Response(await OnUiAsync(() => NamedCatalog(api.Database.Categories), linked.Token).ConfigureAwait(false));
            if (request.Method == "GET" && request.Path == "/v1/completion-statuses")
                return WireJson.Response(await OnUiAsync(() => NamedCatalog(api.Database.CompletionStatuses), linked.Token).ConfigureAwait(false));
            if (request.Method == "GET" && request.Path.StartsWith("/v1/operations/", StringComparison.Ordinal))
            {
                var id = request.Path.Substring(15);
                if (!Guid.TryParseExact(id, "D", out _)) throw new ApiException(400, "invalid_command_id");
                var operation = commands.Find(id) ?? throw new ApiException(404, "operation_unavailable");
                return WireJson.Response(new { id, state = !operation.IsCompleted ? "running" : operation.IsCanceled ? "cancelled" :
                    operation.IsFaulted || operation.Result.Status >= 400 ? "failed" : "completed" });
            }
            if (request.Method == "POST" && request.Path == "/v1/categories")
                return await RunMutationAsync(request, () => CreateCategory(WireJson.Body(request)), linked.Token).ConfigureAwait(false);
            if (request.Path.StartsWith("/v1/games/", StringComparison.Ordinal))
            {
                var parts = request.Path.Substring(10).Split('/');
                if (parts.Length == 0 || !Guid.TryParseExact(parts[0], "D", out var id)) throw new ApiException(400, "invalid_game_id");
                if (parts.Length == 1 && request.Method == "GET")
                    return WireJson.Response(await OnUiAsync(() => ProjectGame(RequireGame(id), true), linked.Token).ConfigureAwait(false));
                if (parts.Length == 3 && parts[1] == "artwork" && request.Method == "GET")
                    return await ReadArtworkAsync(id, parts[2], linked.Token).ConfigureAwait(false);
                if (parts.Length == 2 && request.Method == "GET" && (parts[1] == "achievements" || parts[1] == "activity"))
                    return await ReadOptionalDataAsync(id, parts[1], linked.Token).ConfigureAwait(false);
                if (parts.Length == 3 && request.Method == "POST" && parts[1] == "achievements" && parts[2] == "refresh")
                    return StartAchievementRefresh(request, id, linked.Token);
                if (parts.Length == 1 && (request.Method == "PATCH" || request.Method == "PUT"))
                    return await RunMutationAsync(request, () => UpdateGame(id, WireJson.Body(request)), linked.Token).ConfigureAwait(false);
                if (parts.Length == 2 && request.Method == "POST" && (parts[1] == "launch" || parts[1] == "install" || parts[1] == "uninstall"))
                    return await RunMutationAsync(request, () => RequestGameAction(id, parts[1]), linked.Token).ConfigureAwait(false);
                if (parts.Length == 2 && request.Method == "PUT" && (parts[1] == "categories" || parts[1] == "status"))
                    return await RunMutationAsync(request, () => SetOrganization(id, parts[1], WireJson.Body(request)), linked.Token).ConfigureAwait(false);
            }
            return LocalApiResponse.Error(404, "not_found");
        }
        catch (ApiException error) { return LocalApiResponse.Error(error.Status, error.Code); }
        catch (ArgumentException) { return LocalApiResponse.Error(400, "invalid_request"); }
        catch (EventWaitCapacityException) { return LocalApiResponse.Error(429, "event_wait_capacity"); }
        catch (CommandCapacityException) { return LocalApiResponse.Error(429, "command_capacity"); }
        catch (CommandConflictException) { return LocalApiResponse.Error(409, "command_id_reused"); }
    }

    private Task<T> OnUiAsync<T>(Func<T> action, CancellationToken token, System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Normal)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = token.Register(() => completion.TrySetCanceled());
        try
        {
            api.MainView.UIDispatcher.BeginInvoke(priority, new Action(() =>
            {
                try
                {
                    if (disposed || token.IsCancellationRequested) { completion.TrySetCanceled(); return; }
                    completion.TrySetResult(action());
                }
                catch (Exception error) { completion.TrySetException(error); }
                finally { cancellation.Dispose(); }
            }));
        }
        catch { cancellation.Dispose(); throw; }
        return completion.Task;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; lifetime.Cancel(); captureCompanions?.Dispose(); achievements.Dispose(); changes.Dispose();
        api.Database.Games.ItemUpdated -= GamesUpdated;
        api.Database.Games.ItemCollectionChanged -= GamesChanged;
    }
}
