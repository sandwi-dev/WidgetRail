using WidgetRail.Samples.DiscordPrototype;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    // The transport retains the channel-array identity while its account-bound
    // catalog is unchanged. Build once per catalog, not once per keystroke/poll.
    private sealed record ChannelSuggestions(string Account,WebSessionReference? Session,DiscordChannel[] Channels,
        string SourceKey,TextSuggestionCatalog Catalog,PickerItem[] Servers,PickerItem[] ChannelItems,
        IReadOnlyDictionary<string,PickerItem> Items);

    private void RefreshChannelSuggestions(DiscordState next)
    {
        ObserveChannelPreferences(next);
        var previous=view.Value.Suggestions;var session=client?.Reference;
        if(!next.Ready)
        {if(previous is not null)view.Update(current=>current with{Suggestions=null});return;}
        if(previous is not null&&previous.Account==next.Account&&previous.Session==session&&ReferenceEquals(previous.Channels,next.Channels))return;
        var servers=next.Channels.DistinctBy(channel=>channel.GuildId,StringComparer.Ordinal)
            .Select(channel=>new PickerItem("server."+channel.GuildId,channel.GuildId,DisplayName(channel.Guild,"Unnamed server"),channel.GuildId,channel.Guild,true))
            .OrderBy(server=>server.Name,StringComparer.OrdinalIgnoreCase).ThenBy(server=>server.Id,StringComparer.Ordinal).ToArray();
        var channelItems=next.Channels.Select(channel=>new PickerItem("channel."+channel.Id,channel.Id,
            DisplayName(channel.Name,"Unnamed channel"),channel.GuildId,channel.Guild,false)).ToArray();
        var items=servers.Concat(channelItems).ToDictionary(item=>item.Key,StringComparer.Ordinal);
        var entries=items.Values.Select(item=>new TextSuggestion(item.Key,SuggestionLabel(item.Name),item.Name,item.IsServer?"Server":item.Guild))
            .Where(entry=>TextSuggestionContract.IsValid(new([entry]),1,120));
        var replacement=new ChannelSuggestions(next.Account,session,next.Channels,"discord.channels."+Guid.NewGuid().ToString("N"),
            new(entries,TextSuggestionMatchField.Value),servers,channelItems,items);
        view.Update(current=>current with{Suggestions=replacement});
    }

    private static string DisplayName(string name,string fallback)=>string.IsNullOrWhiteSpace(name)?fallback:name;
    private static string SuggestionLabel(string name)=>name.Length<=TextSuggestionContract.MaximumLabelLength?name:
        name[..WidgetTextChunks.NextLength(name,0,TextSuggestionContract.MaximumLabelLength-1)]+"…";

    private ChannelSuggestions? SuggestionsFor(DiscordState? data)
    {
        var current=view.Value.Suggestions;
        return data?.Ready==true&&current is not null&&current.Account==data.Account&&current.Session==client?.Reference&&
            ReferenceEquals(current.Channels,data.Channels)?current:null;
    }

    public override ValueTask<TextSuggestionResult> OnSuggestTextAsync(TextSuggestionQuery query,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(!TextSuggestionContract.IsValid(query)||query.FieldId!="discord.channels.search"||query.Text.Length>120||query.MaximumResults>6||
            LifecycleState!=WidgetLifecycleState.Interactive||!IsPickerVisible(CurrentState())||
            SuggestionsFor(CurrentState()) is not { } captured||query.SourceKey!=captured.SourceKey)
            return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
        var result=string.IsNullOrWhiteSpace(query.Text)?TextSuggestionResult.Empty:
            captured.Catalog.Search(query with{MaximumValueLength=Math.Min(120,query.MaximumValueLength)},cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(LifecycleState==WidgetLifecycleState.Interactive&&IsPickerVisible(CurrentState())&&
            ReferenceEquals(captured,SuggestionsFor(CurrentState()))?result:TextSuggestionResult.NotAvailable);
    }
}
