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
    private sealed record CaptureQuery(string GameId,long Opening,string? Achievement,string? Provider);
    private sealed record RelatedCapturePage(CaptureQuery Query,string? Kind,string? Reference,IReadOnlyList<CaptureLibraryItem> Items,int Total,bool NeedsPermission=false);
    private readonly WidgetQueryResource<CaptureQuery,RelatedCapturePage> relatedCaptures;
    private WidgetLoadedCollection<RelatedCapturePage,CaptureLibraryItem>? relatedCaptureRows;
    private RelatedCapturePage? publishedCaptures;
    private static CaptureQuery RelatedCaptureKey(PlayniteLibraryRenderState state)=>new(state.DetailsItem!.Value.SavedId,state.DetailsOpening,
        state.DetailsExtras.CaptureAchievementId,state.DetailsExtras.CaptureAchievementId is null?null:state.DetailsExtras.Achievements?.ProviderKey);
    private WidgetQueryResource<CaptureQuery,RelatedCapturePage> CreateRelatedCaptures()=>CreateQueryResource<CaptureQuery,RelatedCapturePage>(RelatedCapturePrefix+".data",new()
    {
        Load=async(query,token)=>
        {
            var integration=await _application.GetCaptureIntegrationAsync(token);
            if(integration is null||!Guid.TryParseExact(query.GameId,"D",out var game))return new(query,null,null,[],0);
            var kind=query.Achievement is null?CaptureRelatedIdentity.GameKind:CaptureRelatedIdentity.AchievementKind;
            if(query.Achievement is not null&&string.IsNullOrWhiteSpace(query.Provider))return new(query,null,null,[],0);
            var reference=query.Achievement is null?CaptureRelatedIdentity.Game(integration.ProfileId,game):CaptureRelatedIdentity.Achievement(integration.ProfileId,game,query.Provider!,query.Achievement);
            var permissions=await HostServices.Permissions.CheckAsync([WidgetCaptureLibraryCapabilities.ReadPermission],token);
            if(permissions.Any(item=>item.State!=WidgetPermissionState.Granted))return new(query,kind,reference,[],0,true);
            var page=await HostServices.CaptureLibrary.QueryAsync(new(){OriginPackageId=PlaynitePackage,Reference=new(kind,reference)},count:4,cancellationToken:token);
            return new(query,kind,reference,page.Items,page.TotalCount);
        },
        MapError=_=>new("related_captures_unavailable","Captures could not be loaded. Refresh to try again."),
        CacheDuration=TimeSpan.FromSeconds(10),RetainLastGoodValue=true,
    });
    private void WatchRelatedCaptures()
    {
        if(Operations.IsBusy(RelatedCaptureWatch))return;
        Operations.RunLatest(RelatedCaptureWatch,async context=>
        {
            try
            {
                var permissions=await HostServices.Permissions.CheckAsync([WidgetCaptureLibraryCapabilities.ReadPermission],context.CancellationToken);
                if(permissions.Any(item=>item.State!=WidgetPermissionState.Granted))return;
                await using var changes=await HostServices.CaptureLibrary.OpenChangesAsync(context.CancellationToken);
                Refresh();
                await foreach(var _ in changes.ReadAllAsync(context.CancellationToken))Refresh();
                void Refresh()
                {
                    var state=WithDetailsResources(_model.Value);
                    if(context.IsCurrent&&state.DetailsItem is not null&&state.DetailsExtras.Tab==PlayniteDetailsTab.Captures)
                        relatedCaptures.EnsureLoaded(RelatedCaptureKey(state),_navigation.Value.RouteCancellationToken,true);
                }
            }
            catch(OperationCanceledException){ }
            catch(WidgetCapabilityException){ }
        },WidgetOperationLifetime.Active);
    }
    private bool HandleRelatedCaptures(WidgetActionEvent action)
    {
        if(!action.ActionId.StartsWith(RelatedCapturePrefix+".",StringComparison.Ordinal))return false;
        var state=WithDetailsResources(_model.Value);
        if(state.DetailsItem is null||state.DetailsExtras.Tab!=PlayniteDetailsTab.Captures||LifecycleState!=WidgetLifecycleState.Interactive)return true;
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
                    LoadDetailsSection(PlayniteDetailsTab.Captures,true);
            },WidgetOperationLifetime.Active);
        }
        return true;
    }
    private WidgetElement? RenderRelatedCaptures(PlayniteLibraryRenderState state)
    {
        if(state.DetailsItem is null||state.DetailsExtras.Tab!=PlayniteDetailsTab.Captures)return null;
        var snapshot=relatedCaptures.GetSnapshot(RelatedCaptureKey(state));var page=snapshot.Value;
        var children=new List<WidgetElement>();
        if(state.DetailsExtras.CaptureAchievementName is { } name)
            children.Add(UI.Row(RelatedCapturePrefix+".filter",UI.Text(name,RelatedCapturePrefix+".achievement"),
                UI.Button("All game captures",RelatedCapturePrefix+".all",RelatedCapturePrefix+".all")));
        if(page?.NeedsPermission==true)children.Add(UI.Button("Show captures",RelatedCapturePrefix+".allow",RelatedCapturePrefix+".allow"));
        else if(page is null)children.Add(UI.Text(snapshot.Error?.Message??"Loading captures…",RelatedCapturePrefix+".loading"));
        else if(page.Reference is null)children.Add(UI.Text("Update WidgetRail for Playnite to browse related captures.",RelatedCapturePrefix+".unavailable"));
        else
        {
            if(!ReferenceEquals(publishedCaptures,page))
            {
                var identity=page.Reference;
                if(relatedCaptureRows is null)relatedCaptureRows=CreateLoadedCollection<RelatedCapturePage,CaptureLibraryItem>(RelatedCapturePrefix+".source",identity,page,page.Items,new()
                {
                    PreserveOccurrences=true,ItemKey=item=>new(item.Id),
                    RenderItem=(_,item,ids)=>UI.ReadingSurface(ids.Id("row"),item.Title??item.FileName??"Capture",ActionSurfaceOrientation.Vertical,
                        UI.Artwork(CaptureArtwork(item),ids.Id("thumbnail"),"Capture thumbnail",ImageFit.Contain).Classes("playnite-capture-thumbnail"),
                        UI.Text(item.Title??item.FileName??(item.Media.ContentType.StartsWith("video/",StringComparison.Ordinal)?"Video clip":"Screenshot"),ids.Id("title")).Classes("playnite-hub-game-name")),
                    OnAction=(_,_,_,_)=>ValueTask.CompletedTask,
                    ResolveArtwork=async(_,item,handle,token)=>
                    {
                        if(!item.Available||handle!=CaptureArtwork(item))return null;
                        try{var art=await HostServices.CaptureLibrary.GetThumbnailAsync(item.Id,token);return new(WidgetArtworkContentType.Png,art.Bytes);}
                        catch(WidgetCapabilityException){return null;}
                    },
                });
                else relatedCaptureRows.Publish(identity,page,page.Items);
                publishedCaptures=page;
            }
            if(page.Total==0)children.Add(UI.Text("No captures for this selection yet.",RelatedCapturePrefix+".empty"));
            else children.Add(UI.CollectionGrid(RelatedCapturePrefix+".items",relatedCaptureRows!,260,160,"Related captures",maximumColumns:2).Classes("playnite-captures-grid"));
            children.Add(UI.Button(page.Total>4?$"View all {page.Total:N0} captures":"Open Capture Library",RelatedCapturePrefix+".browse",RelatedCapturePrefix+".browse")
                .OpenIntent(WidgetIntentContracts.CaptureLibraryV2,JsonSerializer.SerializeToElement(new{originPackageId=PlaynitePackage,referenceKind=page.Kind,referenceId=page.Reference,
                    referenceLabel=RelatedCaptureLabel(state.DetailsExtras.CaptureAchievementName??state.DetailsItem.Presentation.DisplayName)})));
        }
        return UI.Stack(RelatedCapturePrefix+".content",children.ToArray()).Classes("playnite-capture-content");
    }
    private static WidgetArtworkHandle CaptureArtwork(CaptureLibraryItem item)=>new("playnite.capture."+item.Id+"."+item.Revision);
    internal static string RelatedCaptureLabel(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return "Related captures";
        var length = Math.Min(name.Length, 256);
        if (length < name.Length && char.IsHighSurrogate(name[length - 1])) --length;
        return name[..length];
    }

    private static bool RelatedRequest(WidgetIntentRequest request)=>request.IsWellFormed()&&request.ContractId==relatedIntent.Id&&request.Version==relatedIntent.Version&&
        request.SchemaDigest==relatedIntent.SchemaDigest&&relatedIntent.Accepts(request.Payload)&&request.Payload.GetProperty("packageId").GetString()==PlaynitePackage;
    public override async ValueTask<bool> IsIntentAvailableAsync(WidgetIntentRequest request,CancellationToken cancellationToken=default)
    {
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

