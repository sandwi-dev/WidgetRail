using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    internal sealed record ChannelAccountPreferences(string Account,string[] Favorites,string[] Recent);
    internal sealed record ChannelPreferenceStore(int SchemaVersion=1,ChannelAccountPreferences[]? Accounts=null);
    private sealed record ChannelPreferenceSnapshot(ChannelPreferenceStore Store,long Revision);
    private WidgetResource<ChannelPreferenceSnapshot> channelPreferences=null!;
    private const string PreferenceWriteOperation="discord.preferences.write";
    private const int MaximumFavoriteChannels=64,MaximumRecentChannels=8,MaximumPreferenceAccounts=8;

    private void InitializeChannelPreferences()=>channelPreferences=CreateResource("discord.preferences",new WidgetResourceOptions<ChannelPreferenceSnapshot>
    {
        Lifetime=WidgetOperationLifetime.Active,CacheDuration=TimeSpan.MaxValue,RetainLastGoodValue=false,
        Load=async token=>
        {
            var saved=await HostServices.PrivateState.ReadAsync<ChannelPreferenceStore>(cancellationToken:token);
            if(saved.Exists&&saved.Value is null)throw new InvalidDataException("Invalid Discord preferences.");
            return new(NormalizeChannelPreferences(saved.Value??new()),saved.Revision);
        },
        MapError=_=>new("discord.preferences.unavailable","Saved channels are unavailable. Refresh to retry.")
    });

    internal static ChannelPreferenceStore NormalizeChannelPreferences(ChannelPreferenceStore store)
    {
        if(store.SchemaVersion!=1)throw new InvalidDataException("Unsupported Discord preference version.");
        static bool ValidId(string? id)=>id is {Length:>0 and <=32}&&id.All(char.IsAsciiDigit);
        static string[] Ids(string[]? ids,int maximum)=>(ids??[]).Where(ValidId).Distinct(StringComparer.Ordinal).Take(maximum).ToArray();
        return new(1,(store.Accounts??[]).Where(account=>account is not null&&ValidId(account.Account))
            .DistinctBy(account=>account.Account,StringComparer.Ordinal).Take(MaximumPreferenceAccounts)
            .Select(account=>new ChannelAccountPreferences(account.Account,Ids(account.Favorites,MaximumFavoriteChannels),
                Ids(account.Recent,MaximumRecentChannels))).ToArray());
    }

    private ChannelAccountPreferences PreferencesFor(string account)=>
        channelPreferences.Snapshot.Value?.Store.Accounts?.FirstOrDefault(value=>value.Account==account)??new(account,[],[]);

    private static ChannelPreferenceStore ReplaceAccount(ChannelPreferenceStore store,ChannelAccountPreferences account)
    {
        var others=(store.Accounts??[]).Where(value=>value.Account!=account.Account).ToArray();
        if(others.Length>=MaximumPreferenceAccounts)throw new InvalidDataException("Preference account limit reached.");
        return new(1,new[]{account}.Concat(others).ToArray());
    }

    private void ObserveChannelPreferences(DiscordState data)
    {
        if(LifecycleState is not (WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive))return;
        if(channelPreferences.Snapshot.Status==WidgetResourceStatus.NotLoaded)channelPreferences.EnsureLoaded();
        // Record observed successful connections, never an attempted Join or a
        // selected-but-unconnected channel. Provider order remains independent.
        if(data is not{Ready:true,ChannelId:not null,VoiceConnection:"connected"}||
            channelPreferences.Snapshot.Value is null||!data.Channels.Any(channel=>channel.Id==data.ChannelId)||
            PreferencesFor(data.Account).Recent.FirstOrDefault()==data.ChannelId||Operations.IsBusy(PreferenceWriteOperation))return;
        var account=data.Account;var channel=data.ChannelId;var session=client?.Reference;
        Operations.RunSerial(PreferenceWriteOperation,async context=>
        {
            // The supplied snapshot is an already-validated provider observation.
            // The main resource may not have published it yet. History records
            // that observation, rather than comparing it with the older UI value.
            if(!context.IsCurrent||client?.Reference!=session)return;
            await MutatePreferencesAsync(account,old=>old with{Recent=new[]{channel}.Concat(old.Recent.Where(id=>id!=channel)).Take(MaximumRecentChannels).ToArray()},context.CancellationToken,false);
        });
    }

    private PickerItem[] PrioritizeChannels(string account,IEnumerable<PickerItem> values)
    {
        var saved=PreferencesFor(account);var favorites=saved.Favorites.ToHashSet(StringComparer.Ordinal);
        var recent=saved.Recent.Select((id,index)=>(id,index)).ToDictionary(pair=>pair.id,pair=>pair.index,StringComparer.Ordinal);
        return values.OrderBy(item=>favorites.Contains(item.Id)?0:recent.ContainsKey(item.Id)?1:2)
            .ThenBy(item=>favorites.Contains(item.Id)?Array.IndexOf(saved.Favorites,item.Id):recent.GetValueOrDefault(item.Id,int.MaxValue)).ToArray();
    }

    private string ChannelBadge(PickerContext context,PickerItem item)
    {
        if(item.IsServer)return "Open";
        var action=item.Id==context.SelectedChannel?"Selected":"Join";
        return context.Favorites.Contains(item.Id)?"Favorite · "+action:context.Recent.Contains(item.Id)?"Recent · "+action:action;
    }

    private ActionSurfaceElement ChannelFavoriteMenu(ActionSurfaceElement row,PickerContext context,PickerItem item)
    {
        if(item.IsServer)return row;
        var favorite=context.Favorites.Contains(item.Id);
        return row.ContextMenuShortcut(ControllerButton.Menu) with{ContextActions=[new WidgetContextAction(favorite?"unfavorite":"favorite",
            favorite?"Remove favorite":"Add favorite",IsDisabled:channelPreferences.Snapshot.Value is null)]};
    }

    private ValueTask ChangeChannelFavoriteAsync(PickerContext context,PickerItem item,bool favorite,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(item.IsServer||context.Catalog is not{} catalog||!IsPickerVisible(CurrentState())||
            !ReferenceEquals(catalog,SuggestionsFor(CurrentState())))return ValueTask.CompletedTask;
        Operations.RunSerial(PreferenceWriteOperation,async work=>
        {
            if(!work.IsCurrent||!IsPickerVisible(CurrentState())||!ReferenceEquals(catalog,SuggestionsFor(CurrentState())))return;
            await MutatePreferencesAsync(catalog.Account,old=>
            {
                if(old.Favorites.Contains(item.Id)==favorite)return old;
                if(favorite&&old.Favorites.Length>=MaximumFavoriteChannels)throw new InvalidOperationException("Favorite limit reached.");
                return old with{Favorites=favorite?old.Favorites.Append(item.Id).ToArray():old.Favorites.Where(id=>id!=item.Id).ToArray()};
            },work.CancellationToken,true);
        });
        return ValueTask.CompletedTask;
    }

    private async ValueTask MutatePreferencesAsync(string account,Func<ChannelAccountPreferences,ChannelAccountPreferences> update,CancellationToken token,bool reportFailure)
    {
        try
        {
            await channelPreferences.EnsureLoaded().Completion.WaitAsync(token);
            if(channelPreferences.Snapshot.Value is not{} snapshot)throw new InvalidDataException("Preferences unavailable.");
            var old=snapshot.Store.Accounts?.FirstOrDefault(value=>value.Account==account)??new(account,[],[]);
            var changed=update(old);if(ReferenceEquals(changed,old))return;
            var store=ReplaceAccount(snapshot.Store,changed);
            var saved=await HostServices.PrivateState.WriteAsync(store,expectedRevision:snapshot.Revision,cancellationToken:token);
            // Publish a confirmed write even if account presentation changed;
            // reads remain keyed by account and cannot expose another account.
            channelPreferences.Publish(new(store,saved.Revision));
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){channelPreferences.Reset();}
        catch(Exception error) when(error is WidgetCapabilityException or WidgetCapabilityUnavailableException or JsonException or InvalidDataException or InvalidOperationException)
        {
            if(error is WidgetCapabilityException{ErrorCode:"state_conflict"})channelPreferences.Refresh();
            if(reportFailure&&CurrentState()?.Account==account)
                view.Update(current=>current with{Notice=error is InvalidOperationException?"Favorite limit reached. Remove one first.":"Could not save channel preferences. Refresh and try again."});
        }
    }

    internal Task WaitForChannelPreferencesAsync(CancellationToken token=default)=>channelPreferences.WhenIdleAsync(token);
    internal Task WaitForPreferenceWriteAsync(CancellationToken token=default)=>Operations.WhenIdleAsync(PreferenceWriteOperation,token);

}
