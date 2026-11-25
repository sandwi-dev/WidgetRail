using System.Text.Json;
using WidgetRail.Playnite;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string RelatedCapturePrefix="playnite-library.related-captures";
    private const string RelatedCaptureWatch=RelatedCapturePrefix+".watch";
    private const string PlaynitePackage="widgetrail.samples.playnite-library";
    private static readonly CompiledWidgetIntentContract relatedIntent=CompiledWidgetIntentContract.Create(WidgetIntentContracts.RelatedItem);
    private sealed record CaptureQuery(string GameId,long Opening,string? Achievement,string? Provider,string Name,int? PickerOffset,bool BrowseLinked)
    {
        internal CaptureQuery Timeline => this with { PickerOffset = null, BrowseLinked = false };
    }
    private sealed record RelatedCapturePage(CaptureQuery Query,string? Kind,string? Reference,IReadOnlyList<CaptureLibraryItem> Items,int Total,bool NeedsPermission=false)
    {
        internal bool CanLink { get; init; }
        internal string? Connection { get; init; }
        internal string? Revision { get; init; }
        internal CaptureLibraryRunsPage? Runs { get; init; }
        internal IReadOnlyList<CaptureLibraryRun>? LinkedRuns { get; init; }
        internal bool HasMore => Runs?.HasMore ?? (Query.PickerOffset is { } start && start + 12 < Total);
    }
    private readonly WidgetQueryResource<CaptureQuery,RelatedCapturePage> relatedCaptures;
    private readonly WidgetQueryResource<CaptureQuery,RelatedCapturePage> capturePicker;
    private WidgetQueryResource<CaptureQuery,RelatedCapturePage> ResourceFor(CaptureQuery query) =>
        query.PickerOffset is null ? relatedCaptures : capturePicker;
    private static CaptureQuery RelatedCaptureKey(PlayniteLibraryRenderState state)=>new(state.DetailsItem!.Value.SavedId,state.DetailsOpening,
        state.DetailsExtras.CaptureAchievementId,state.DetailsExtras.CaptureAchievementId is null?null:state.DetailsExtras.Achievements?.ProviderKey,
        RelatedCaptureLabel(state.DetailsItem.Presentation.DisplayName, 160),state.DetailsExtras.CapturePickerOffset,state.DetailsExtras.BrowsingLinkedCaptures);
    private WidgetQueryResource<CaptureQuery,RelatedCapturePage> CreateRelatedCaptures(string key)=>CreateQueryResource<CaptureQuery,RelatedCapturePage>(RelatedCapturePrefix + key,new()
    {
        Load=async(query,token)=>
        {
            var integration=await _application.GetCaptureIntegrationAsync(token,query.GameId);
            if(integration is null||!Guid.TryParseExact(query.GameId,"D",out var game))return new(query,null,null,[],0);
            var kind=query.Achievement is null?CaptureRelatedIdentity.GameKind:CaptureRelatedIdentity.AchievementKind;
            if(query.Achievement is not null&&string.IsNullOrWhiteSpace(query.Provider))return new(query,null,null,[],0);
            var reference=query.Achievement is null?CaptureRelatedIdentity.Game(integration.ProfileId,game):CaptureRelatedIdentity.Achievement(integration.ProfileId,game,query.Provider!,query.Achievement);
            var permissions=await HostServices.Permissions.CheckAsync([WidgetCaptureLibraryCapabilities.ReadPermission, WidgetCaptureLibraryCapabilities.ManagePermission],token);
            bool Granted(string id) => permissions.Any(p => p.CapabilityId == id && p.State == WidgetPermissionState.Granted);
            if(!Granted(WidgetCaptureLibraryCapabilities.ReadPermission))return new(query,kind,reference,[],0,true);
            var filter = new CaptureLibraryQuery { AvailableOnly = true, OriginPackageId = PlaynitePackage, Reference = new(kind,reference) };
            var canLink = Granted(WidgetCaptureLibraryCapabilities.ManagePermission);
            var picker = !query.BrowseLinked && query.Achievement is null && canLink ? query.PickerOffset : null;
            if (picker is not null) filter = filter with { ExcludeReference = true };
            if (picker is { } offset)
            {
                var process = Organization.CaptureProcesses.FirstOrDefault(entry => entry.Game == reference)?.Process;
                if (await ReadRunsAsync(new(filter, offset, 12, TitleTerms(query.Name), process), token) is { } runs)
                    return new(query, kind, reference, runs.Items.Select(run => run.Latest).ToArray(), runs.Items.Count)
                        { CanLink = true, Connection = integration.ConnectionId, Runs = runs };
            }
            var page=await HostServices.CaptureLibrary.QueryAsync(filter,query.PickerOffset ?? 0,query.PickerOffset is null ? 4 : 12,cancellationToken:token);
            var items = page.Items;
            IReadOnlyList<CaptureLibraryRun>? linkedRuns = null;
            if (query.PickerOffset is null && query.Achievement is null && page.TotalCount > 0)
            {
                // Keep the ordinary gallery usable if a new capture arrives between the two reads.
                var runs = await ReadRunsAsync(new(filter, Count: 4), token);
                if (runs?.Revision == page.Revision)
                {
                    linkedRuns = runs.Items;
                    items = runs.Items.SelectMany(run => page.Items.Prepend(run.Latest)
                        .Where(item => item.Available && SameRun(run, item)).DistinctBy(item => item.Id)).ToArray();
                }
            }
            token.ThrowIfCancellationRequested();
            if (query.Achievement is null && canLink && query.PickerOffset is null && integration.Sessions.Count > 0)
                ReconcileGameCaptures(query, filter, integration.Sessions);
            return new(query,kind,reference,items,page.TotalCount) { CanLink = canLink, Connection = integration.ConnectionId, Revision = page.Revision, LinkedRuns = linkedRuns };
        },
        MapError=_=>new("related_captures_unavailable","Captures could not be loaded."),
        CacheDuration=TimeSpan.FromSeconds(10),RetainLastGoodValue=true,
    });
    private async ValueTask<CaptureLibraryRunsPage?> ReadRunsAsync(CaptureLibraryRunsRequest request, CancellationToken token)
    {
        try { return await HostServices.CaptureLibrary.RunsAsync(request, token); }
        catch (WidgetCapabilityUnavailableException) { return null; }
        catch (WidgetCapabilityException error) when (error.ErrorCode is "unsupported_capability" or "unsupported_operation") { return null; }
    }
    private void WatchRelatedCaptures()
    {
        var navigation = _navigation.Value;
        var route = navigation.RouteCancellationToken;
        Operations.RunSingleFlight(RelatedCaptureWatch + "." + navigation.Revision, async context=>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, route);
            var token = lifetime.Token;
            try
            {
                var permissions=await HostServices.Permissions.CheckAsync([WidgetCaptureLibraryCapabilities.ReadPermission],token);
                if(permissions.Any(item=>item.State!=WidgetPermissionState.Granted))return;
                await using var changes=await HostServices.CaptureLibrary.OpenChangesAsync(token);
                await Refresh();
                await foreach(var _ in changes.ReadAllAsync(token)) await Refresh();
                async Task Refresh()
                {
                    if (!context.IsCurrent || token.IsCancellationRequested) return;
                    var state=WithDetailsResources(_model.Value);
                    if (_navigation.Value.Route == PlayniteLibraryRoute.GameDetails && state.DetailsItem is not null &&
                        LifecycleState == WidgetLifecycleState.Interactive)
                    {
                        if (state.DetailsExtras.Tab == PlayniteDetailsTab.Captures)
                            RefreshCaptureResources(state, true);
                        else if (state.DetailsExtras.Tab is PlayniteDetailsTab.Activity or PlayniteDetailsTab.Description)
                        {
                            var key = DetailsKey(state);
                            await timelineCaptures.WhenIdleAsync(token);
                            await timelineCaptures.EnsureLoaded(key, route, forceRefresh: true).Completion.WaitAsync(token);
                        }
                        return;
                    }
                    if (_navigation.Value.Route != PlayniteLibraryRoute.Library) return;
                    await RefreshHomeCaptureViewsAsync(token);
                }
            }
            catch(OperationCanceledException){ }
            catch(WidgetCapabilityException){ }
            catch(WidgetCapabilityUnavailableException){ }
        },WidgetOperationLifetime.Active);
    }
    private void ResumeHomeCaptureLinks(CancellationToken stateLifetime)
    {
        if (PlayingId(_model.Value) is not { } game) return;
        var captures = playingCaptures.GetSnapshot(game);
        if (captures.Value?.Pending.Count is not > 0 && captures.Status is not (WidgetResourceStatus.Loading or WidgetResourceStatus.Refreshing)) return;
        var route = _navigation.Value.RouteCancellationToken;
        Operations.RunLatest(RelatedCapturePrefix + ".resume-home", async context =>
        {
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, stateLifetime, route);
            await RefreshHomeCaptureViewsAsync(scope.Token);
        });
    }

    private async Task RefreshHomeCaptureViewsAsync(CancellationToken token)
    {
        if (PlayingId(_model.Value) is { } game)
        {
            await playingCaptures.WhenIdleAsync(token);
            if (PlayingId(_model.Value) == game)
                await playingCaptures.EnsureLoaded(game, token, forceRefresh: true).Completion.WaitAsync(token);
        }
        // Read the feed after the session matches and any older feed read have settled.
        await homeCaptures.WhenIdleAsync(token);
        token.ThrowIfCancellationRequested();
        await homeCaptures.Refresh().Completion.WaitAsync(token);
    }
    private bool HandleRelatedCaptures(WidgetActionEvent action)
    {
        if(!action.ActionId.StartsWith(RelatedCapturePrefix+".",StringComparison.Ordinal))return false;
        var state=WithDetailsResources(_model.Value);
        if(state.DetailsItem is null||state.DetailsExtras.Tab!=PlayniteDetailsTab.Captures||LifecycleState!=WidgetLifecycleState.Interactive)return true;
        if (HandleSessionReview(action.ActionId,state) || HandleCaptureViewer(action.ActionId,state) || HandleCaptureLinkChange(action.ActionId, state) || HandleCaptureLink(action.ActionId, state)) return true;
        if (action.ActionId == RelatedCapturePrefix + ".retry")
        {
            LoadDetailsSection(PlayniteDetailsTab.Captures, refresh: true);
            return true;
        }
        if(action.ActionId==RelatedCapturePrefix+".all")
        {
            UpdateDetails(state.DetailsGeneration,value=>value with{CaptureAchievementId=null,CaptureAchievementName=null});
            LoadDetailsSection(PlayniteDetailsTab.Captures,true);return true;
        }
        if(action.ActionId==RelatedCapturePrefix+".allow")
        {
            Operations.RunLatest(RelatedCapturePrefix+".permission",async context=>
            {
                var granted=await HostServices.Permissions.RequestAsync([WidgetCaptureLibraryCapabilities.ReadPermission],context.CancellationToken);
                if(granted==WidgetPermissionOutcome.Granted&&context.IsCurrent&&_model.Value.DetailsOpening==state.DetailsOpening)
                {
                    timelineCaptures.Reset();
                    homeCaptures.Reset();
                    playingCaptures.Reset();
                    LoadDetailsSection(_model.Value.DetailsExtras.Tab,true);
                }
            },WidgetOperationLifetime.Active);
        }
        return true;
    }
    internal static string RelatedCaptureLabel(string name, int maximumLength = 256)
    {
        name = name.Trim();
        if (name.Length == 0) return "Related captures";
        var length = Math.Min(name.Length, maximumLength);
        if (length < name.Length && char.IsHighSurrogate(name[length - 1])) --length;
        return name[..length];
    }

    private static bool RelatedRequest(WidgetIntentRequest request)=>request.IsWellFormed()&&request.ContractId==relatedIntent.Id&&request.Version==relatedIntent.Version&&
        request.SchemaDigest==relatedIntent.SchemaDigest&&relatedIntent.Accepts(request.Payload)&&request.Payload.GetProperty("packageId").GetString()==PlaynitePackage;
    public override async ValueTask<bool> IsIntentAvailableAsync(WidgetIntentRequest request,CancellationToken cancellationToken=default)
    {
        if (request.ContractId == WidgetIntentContracts.AssociateCaptures)
            return ReadCaptureOffer(request) is not null && CanOpenGameChooser(_model.Value) &&
                await CaptureChooserConnectionAsync(cancellationToken) is not null;
        if(!RelatedRequest(request))return false;
        try
        {
            var integration=await _application.GetCaptureIntegrationAsync(cancellationToken);
            var kind=request.Payload.GetProperty("kind").GetString()!;var id=request.Payload.GetProperty("id").GetString()!;
            if(integration is null||!CaptureRelatedIdentity.TryGame(integration.ProfileId,kind,id,out var game)||
                await _application.GetGameDetailsAsync(game.ToString("D"),cancellationToken) is null)return false;
            if(kind==CaptureRelatedIdentity.GameKind)return true;
            var achievements=await _application.GetAchievementsAsync(game.ToString("D"),cancellationToken);
            return achievements.ConnectionId==integration.ConnectionId&&achievements.ProviderKey is {Length:>0} provider&&
                achievements.Items.Any(item=>item.Id is {Length:>0} api&&CaptureRelatedIdentity.Achievement(integration.ProfileId,game,provider,api)==id);
        }
        catch(Exception error)when(error is not(OperationCanceledException or OutOfMemoryException or StackOverflowException or AccessViolationException)){return false;}
    }
    public override async ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request,CancellationToken cancellationToken=default)
    {
        if (request.ContractId == WidgetIntentContracts.AssociateCaptures)
            return await OpenCaptureGamesAsync(request, cancellationToken);
        if (_model.Value.GameChooser is not null) return WidgetIntentResult.Rejected;
        if(!IsActive||!RelatedRequest(request)||_model.Value.CaptureSettings.Open||Operations.IsBusy(CaptureWork))return WidgetIntentResult.Rejected;
        var beforeOpening=_model.Value.DetailsOpening;var beforeScope=_navigation.Value.InputScopeId;
        var integration=await _application.GetCaptureIntegrationAsync(cancellationToken);
        var kind=request.Payload.GetProperty("kind").GetString()!;var reference=request.Payload.GetProperty("id").GetString()!;
        if(integration is null||!CaptureRelatedIdentity.TryGame(integration.ProfileId,kind,reference,out var game))return WidgetIntentResult.Rejected;
        var id=game.ToString("D");var full=await _application.GetGameDetailsAsync(id,cancellationToken);
        if(full is null)return WidgetIntentResult.Rejected;
        PlayniteAchievements? achievements=null;PlayniteAchievement? selected=null;
        if(kind==CaptureRelatedIdentity.AchievementKind)
        {
            achievements=await _application.GetAchievementsAsync(id,cancellationToken);
            if(achievements.ConnectionId!=integration.ConnectionId||achievements.ProviderKey is not {Length:>0} provider)return WidgetIntentResult.Rejected;
            selected=achievements.Items.SingleOrDefault(item=>item.Id is {Length:>0} api&&CaptureRelatedIdentity.Achievement(integration.ProfileId,game,provider,api)==reference);
            if(selected is null)return WidgetIntentResult.Rejected;
        }
        var current=await _application.GetCaptureIntegrationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if(!IsActive||current?.ConnectionId!=integration.ConnectionId||current.ProfileId!=integration.ProfileId||
            _model.Value.DetailsOpening!=beforeOpening||_navigation.Value.InputScopeId!=beforeScope)return WidgetIntentResult.Rejected;
        _navigation.Navigate(PlayniteLibraryRoute.Library);
        var item=PlayniteLibraryItem.From(full.Item);var opening=_model.Value.DetailsOpening+1;
        OpenDetails(item,capturedTarget:new(item,()=>IsActive&&_model.Value.DetailsOpening==opening&&_model.Value.DetailsItem?.Value.SavedId==id));
        if(selected is not null&&achievements is not null)
        {
            _model.Update(state=>state with{DetailsExtras=state.DetailsExtras with{Tab=PlayniteDetailsTab.Achievements,AchievementSearch=selected.Name}});
            LoadDetailsSection(PlayniteDetailsTab.Achievements);
        }
        return WidgetIntentResult.Accepted;
    }
}

