using WidgetRail.Samples.DiscordPrototype;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private sealed record PickerItem(string Key,string Id,string Name,string GuildId,string Guild,bool IsServer);
    private sealed record PickerSelection(string? Account=null,WebSessionReference? Session=null,string? GuildId=null,bool ShowServers=false);
    private sealed record PickerContext(ChannelSuggestions? Catalog,string? SelectedChannel,bool CanJoin,string[] Favorites,string[] Recent);
    private WidgetModel<PickerSelection> pickerSelection=null!;
    private WidgetLoadedCollection<PickerContext,PickerItem> pickerItems=null!;

    private void InitializePicker()
    {
        InitializeChannelPreferences();
        pickerSelection=CreateModel(new PickerSelection());
        pickerItems=CreateLoadedCollection<PickerContext,PickerItem>("discord.channels.source","",new(null,null,false,[],[]),[],new()
        {
            ItemKey=item=>new(item.Key),PreserveOccurrences=true,
            RenderItem=(context,item,ids)=>ChannelFavoriteMenu(UI.ActionSurface("choose",ids.Id("choose"),
                item.Name+", "+(item.IsServer?"server, show voice channels":item.Guild+", "+(item.Id==context.SelectedChannel?"connected":"join voice")),
                ActionSurfaceOrientation.Horizontal,
                UI.Icon(item.IsServer?DiscordIcons.Channels:DiscordIcons.Voice,ids.Id("icon"),item.IsServer?"Server":"Voice channel").Classes("discord-row-icon"),
                UI.Stack(ids.Id("copy"),UI.Text(item.Name,ids.Id("name")).Classes("discord-row-title"),
                    UI.Text(item.IsServer?"Server":item.Guild,ids.Id("detail")).Classes("discord-secondary")).Classes("discord-copy"),
                UI.Text(ChannelBadge(context,item),ids.Id("state")).Classes("discord-row-state"))
                .Selected(!item.IsServer&&item.Id==context.SelectedChannel).Disabled(!item.IsServer&&!context.CanJoin).Classes("discord-row"),context,item),
            OnAction=(context,item,action,token)=>action.ActionId switch
            {"choose"=>ChoosePickerItemAsync(context,item,token),"favorite"=>ChangeChannelFavoriteAsync(context,item,true,token),
             "unfavorite"=>ChangeChannelFavoriteAsync(context,item,false,token),_=>ValueTask.CompletedTask}
        });
    }

    private bool IsPickerVisible(DiscordState? data)=>data?.Ready==true&&
        (navigation.Value.Route==Route.Channels||navigation.Value.Route==Route.Home&&data.ChannelId is null);

    private string PickerInitialFocusId(DiscordState data)=>SuggestionsFor(data) is null?"discord.refresh":"discord.channels.search";

    private WidgetElement PickerPage(DiscordState data)
    {
        var catalog=SuggestionsFor(data);
        if(catalog is null)return Empty("discord.channels.loading","Loading channels","",DiscordIcons.Channels);
        var selected=CurrentPickerSelection(catalog);
        var query=view.Value.Query.Trim();
        var values=query.Length>0?catalog.Catalog.FindMatches(query,120).Select(match=>catalog.Items[match.Id]).ToArray():
            selected.ShowServers?catalog.Servers:
            selected.GuildId is null?catalog.ChannelItems:catalog.ChannelItems.Where(item=>item.GuildId==selected.GuildId).ToArray();
        // Identity carries the domain filter and exact backend/catalog authority.
        // The indexed source owns realization, leases and viewport continuity.
        if(query.Length==0&&!selected.ShowServers)values=PrioritizeChannels(data.Account,values);
        var preferences=PreferencesFor(data.Account);
        pickerItems.Publish(catalog.SourceKey+"\u001f"+selected.GuildId+"\u001f"+selected.ShowServers+"\u001f"+query,
            new(catalog,data.ChannelId,data.Capabilities.Voice,preferences.Favorites,preferences.Recent),values);
        var search=UI.TextEntry(view.Value.Query,"Search servers and voice channels","search","discord.channels.search",120)
            .Suggest(catalog.SourceKey).Classes("discord-search");
        var children=new List<WidgetElement>
        {
            UI.Row("discord.channels.search.row",search,
                UI.Button("Clear","clear-search","discord.channels.clear").Disabled(query.Length==0).Classes("discord-control"))
                .Classes("discord-actions")
        };
        if(query.Length==0)
        {
            if(catalog.Servers.Length<ProtocolConstants.MaximumSelectOptionCount)
            {
                var options=new List<SelectOption>{new("all","All servers",ServerAction(catalog,null),selected.GuildId is null)};
                options.AddRange(catalog.Servers.Select(server=>new SelectOption(server.Key,server.Name,ServerAction(catalog,server.Id),selected.GuildId==server.Id)));
                children.Add(UI.Select("Server",options,"discord.channels.server","Choose a server").Classes("discord-server"));
            }
            else
            {
                // Select is deliberately bounded. A native indexed server list
                // keeps every server reachable when a catalog exceeds that bound.
                var name=catalog.Servers.FirstOrDefault(server=>server.Id==selected.GuildId)?.Name??"All servers";
                children.Add(UI.Row("discord.channels.server.row",
                    UI.Button(selected.ShowServers?"Show all channels":name,PickerBrowseAction(catalog,!selected.ShowServers),"discord.channels.server")
                        .Icon(DiscordIcons.Channels).Classes("discord-control","discord-server")));
            }
        }
        if(channelPreferences.Snapshot.Error is { } preferenceError)
            children.Add(UI.Text(preferenceError.Message,"discord.channels.preferences-error").Classes("discord-secondary"));
        children.Add(SectionTitle("discord.channels.heading",query.Length>0?"Search results":selected.ShowServers?"Servers":"Voice channels",values.Length.ToString()));
        if(values.Length>0)children.Add(UI.CollectionList("discord.channels",pickerItems,72,"Servers and voice channels").Classes("discord-list","discord-picker-list"));
        else children.Add(Empty("discord.channels.empty",query.Length>0?"No matches":"No voice channels",
            query.Length>0?"Try another server or channel name.":"Check your channel access in Discord.",DiscordIcons.Channels));
        return UI.Stack("discord.channels.page",children.ToArray()).Classes("discord-page");
    }

    private PickerSelection CurrentPickerSelection(ChannelSuggestions catalog)
    {
        var selected=pickerSelection.Value;
        if(selected.Account!=catalog.Account||selected.Session!=catalog.Session)return new(catalog.Account,catalog.Session);
        return selected.GuildId is not null&&!catalog.Servers.Any(item=>item.Id==selected.GuildId)?selected with{GuildId=null}:selected;
    }

    private static string ServerAction(ChannelSuggestions catalog,string? guildId)=>
        DomainAction("server",catalog.Session?.Id,catalog.Account,catalog.SourceKey,guildId);
    private static string PickerBrowseAction(ChannelSuggestions catalog,bool servers)=>
        DomainAction("serverBrowse",catalog.Session?.Id,catalog.Account,catalog.SourceKey,servers.ToString());

    private bool TryPickerAction(WidgetActionEvent action,CancellationToken token,out ValueTask task)
    {
        task=ValueTask.CompletedTask;
        if(action.ActionId=="refresh"){channelPreferences.Refresh();return false;}
        if(action.ActionId is not("search" or "clear-search")&&!action.ActionId.StartsWith("server.",StringComparison.Ordinal)&&
            !action.ActionId.StartsWith("serverBrowse.",StringComparison.Ordinal))return false;
        token.ThrowIfCancellationRequested();
        var data=CurrentState();
        if(!IsPickerVisible(data)||SuggestionsFor(data) is not { } catalog)return true;
        if(action.ActionId=="search")
        {
            var text=action.CommittedText??"";
            if(text.Length<=120&&!text.Any(char.IsControl))view.Update(current=>current with{Query=text});
            return true;
        }
        if(action.ActionId=="clear-search"){view.Update(current=>current with{Query=""});return true;}
        if(action.ActionId==ServerAction(catalog,null))SelectServer(catalog,null);
        else if(catalog.Servers.FirstOrDefault(server=>ServerAction(catalog,server.Id)==action.ActionId) is { } server)SelectServer(catalog,server.Id);
        else if(action.ActionId==PickerBrowseAction(catalog,true))
        {pickerSelection.Update(_=>new(catalog.Account,catalog.Session,ShowServers:true));view.Update(current=>current with{Query=""});}
        else if(action.ActionId==PickerBrowseAction(catalog,false))SelectServer(catalog,null);
        return true;
    }

    private ValueTask ChoosePickerItemAsync(PickerContext context,PickerItem item,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var data=CurrentState();
        if(!IsPickerVisible(data)||context.Catalog is not { } captured||!ReferenceEquals(captured,SuggestionsFor(data)))return ValueTask.CompletedTask;
        if(item.IsServer){SelectServer(captured,item.Id);return ValueTask.CompletedTask;}
        return JoinAsync(captured.Account,item.Id,token);
    }

    private void SelectServer(ChannelSuggestions catalog,string? guildId)
    {
        pickerSelection.Update(_=>new(catalog.Account,catalog.Session,guildId));
        view.Update(current=>current with{Query=""});
    }
}
