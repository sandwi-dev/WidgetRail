using System.Text.Json;
using System.Globalization;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private sealed record ConversationAudience(string? SelectedChannel, string? OfferId, WidgetFileReference[]? Incoming,string? CallChannel);
    private sealed record ConversationRowsQuery(ConversationQuery? Query=null, DiscordConversationPage? Page=null, ConversationAudience? Audience=null);
    private sealed record ConversationLocation(string Account,string Version,int TotalCount,string Key,int? Index);
    private WidgetIndexedCollection<ConversationRowsQuery,DiscordConversation> conversationRows=null!;
    private ConversationRowsQuery publishedConversations=new();

    private void InitializeConversationRows()
    {
        conversationRows=CreateIndexedCollection<ConversationRowsQuery,DiscordConversation>("discord.conversations.rows",publishedConversations,0,new()
        {
            ItemKey=item=>new(item.Id),ReadRange=ReadConversationsAsync,FindIndex=LocateConversationAsync,
            RenderItem=(query,item,ids)=>ConversationRow(query,item,ids),
            OnAction=async(query,item,action,token)=>
            {
                if(action.ActionId!="open" || query.Query is not {} selected || query.Audience is not {} audience)return;
                var intake=messages.Value;
                if(intake.Query!=selected || intake.IncomingOfferId!=audience.OfferId || !ReferenceEquals(intake.Incoming,audience.Incoming))return;
                await OpenConversationAsync(selected,item,action,token,expectedIntake:intake);
            }
        });
    }

    private IndexedCollectionElement ConversationCollection(ConversationQuery query,MessageUi current,DiscordConversationPage? page,bool sidebar)
    {
        var next=new ConversationRowsQuery(query,page,new(current.Selection?.Channel,current.IncomingOfferId,current.Incoming,
            CurrentState() is {VoiceConnection:"connected"} call?call.ChannelId:null));
        if(next!=publishedConversations)
        {
            var previous=publishedConversations;publishedConversations=next;
            if(previous.Query!=query || previous.Page is null || page is null)
                conversationRows.PublishQuery(next,page?.Context.TotalCount??0);
            else if(previous.Page.Context.TotalCount!=page.Context.TotalCount || previous.Page.Context.Version!=page.Context.Version &&
                (page.Context.OrderVersion is null || previous.Page.Context.OrderVersion!=page.Context.OrderVersion))
                conversationRows.PublishRefresh(next,page.Context.TotalCount);
            else conversationRows.UpdateContent(next);
        }
        var list=UI.CollectionList("discord.messages.list",conversationRows,72,"Conversations")
            .FocusUp("discord.messages.search").Classes("discord-list");
        if(sidebar)list=list.FocusRight("discord.message.draft");
        return current.Selection?.Channel is {} selected?list.PreferredEntry(new(selected)):list;
    }

    private void ResetConversationRows()
    {
        publishedConversations=new();conversationRows.PublishQuery(publishedConversations,0);
    }

    private async ValueTask<IReadOnlyList<DiscordConversation>> ReadConversationsAsync(ConversationRowsQuery context,int start,int count,CancellationToken token)
    {
        var query=context.Query??throw new OperationCanceledException();
        var first=context.Page??throw new OperationCanceledException();
        var result=new List<DiscordConversation>(count);
        while(result.Count<count)
        {
            token.ThrowIfCancellationRequested();DemandConversationQuery(query);
            var offset=start+result.Count;
            if(offset<first.Items.Length)
            {result.AddRange(first.Items.Skip(offset).Take(count-result.Count));continue;}
            var size=Math.Min(50,count-result.Count);
            var json=await InvokeConversationQueryAsync(query,"messageConversations",new{account=query.Account,query=query.Search,
                offset,limit=size,version=first.Context.Version},token);
            var page=json.Deserialize<DiscordConversationPage>(MessageJson)??throw DiscordMessageValues.Invalid();
            page.Validate(query.Account,offset,size);
            if(page.Context.Version!=first.Context.Version || page.Context.TotalCount!=first.Context.TotalCount || page.Items.Length!=size)
                throw DiscordMessageValues.Invalid();
            DemandConversationQuery(query);result.AddRange(page.Items);
        }
        return result;
    }

    private async ValueTask<int?> LocateConversationAsync(ConversationRowsQuery context,WidgetCollectionItemKey key,CancellationToken token)
    {
        var query=context.Query??throw new OperationCanceledException();
        var first=context.Page??throw new OperationCanceledException();
        DemandConversationQuery(query);
        var cached=Array.FindIndex(first.Items,item=>item.Id==key.Value);
        if(cached>=0)return cached;
        var json=await InvokeConversationQueryAsync(query,"messageLocateConversation",new{account=query.Account,query=query.Search,
            version=first.Context.Version,key=key.Value},token);
        var location=json.Deserialize<ConversationLocation>(MessageJson)??throw DiscordMessageValues.Invalid();
        if(location.Account!=query.Account || location.Version!=first.Context.Version || location.TotalCount!=first.Context.TotalCount ||
            location.Key!=key.Value || location.Index is {} index && (index<0 || index>=first.Context.TotalCount))throw DiscordMessageValues.Invalid();
        DemandConversationQuery(query);return location.Index;
    }

    private void DemandConversationQuery(ConversationQuery query)
    {DemandMessageAccount(query.Account,query.Session);if(messages.Value.Query!=query)throw new OperationCanceledException();}

    private async ValueTask<JsonElement> InvokeConversationQueryAsync(ConversationQuery query,string method,object args,CancellationToken token)
    {
        try{return await client!.InvokeAsync(method,args,token);}
        catch(DiscordCommandException error)when(error.Code=="conversation_catalog_changed")
        {
            if(!token.IsCancellationRequested && messages.Value.Query==query && MessageAccountCurrent(query.Account,query.Session))
                conversations.EnsureLoaded(query,forceRefresh:true);
            throw;
        }
    }

    private static WidgetElement ConversationRow(ConversationRowsQuery query,DiscordConversation item,WidgetIndexedItemContext ids)
    {
        var status=item.Presence switch{"online"=>"Online","idle"=>"Idle","dnd"=>"Do not disturb","offline"=>"Offline",_=>null};
        var inCall=item.ChannelId is not null && query.Audience?.CallChannel==item.ChannelId;
        var summary=item.LastMessagePreview is {} preview?MessageExcerpt(preview,false).Replace('\r',' ').Replace('\n',' ').Replace('\t',' ').Trim():"";
        if(summary.Length>0 && item.LastMessageIsOwn)summary="You: "+summary;
        else if(summary.Length>0 && item.Kind=="group" && item.LastMessageAuthorName is {} author)summary=author+": "+summary;
        if(summary.Length==0)summary=item.Kind=="group"?"Group conversation":item.Kind=="friend"?"Start a conversation":"Direct message";
        var initial=item.Name.Trim() is {Length:>0} name?StringInfo.GetNextTextElement(name).ToUpper(CultureInfo.CurrentCulture):"?";
        WidgetElement portrait=item.AvatarUrl is not null?Avatar(item.AvatarUrl,ids.Id("icon"),item.Name).AddClasses("discord-conversation-avatar-image"):
            UI.Stack(ids.Id("monogram"),UI.Text(initial,ids.Id("icon"),item.Name).Classes("discord-conversation-initial"))
                .Classes("discord-conversation-monogram");
        var avatar=new List<WidgetElement>{portrait.InGrid()};
        if(status is not null)avatar.Add(UI.Grid(ids.Id("presence-badge"),[GridTrack.Star()],[GridTrack.Star()],
            UI.Icon(DiscordIcons.Presence(item.Presence!),ids.Id("presence"),status)
                .Classes("discord-presence","discord-presence-"+item.Presence).InGrid())
            .Classes("discord-presence-badge").InGrid().AlignInGrid(GridCellAlignment.End,GridCellAlignment.End));
        var copy=new List<WidgetElement>{
            UI.Text(item.Name,ids.Id("name")).Classes("discord-row-title",item.Unread?"discord-conversation-unread":"discord-conversation-read").InGrid(),
            UI.Text(summary,ids.Id("detail")).Classes("discord-conversation-snippet",item.Unread?"discord-conversation-unread":"discord-secondary").InGrid(1)};
        string? fullDate=null;
        if(item.LastMessageTimestamp is {} timestamp)
        {
            var date=DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime();
            fullDate=date.ToString("f",CultureInfo.CurrentCulture);
            var today=DateTimeOffset.Now.Date;
            var label=date.Date==today?date.ToString("t",CultureInfo.CurrentCulture):date.Date==today.AddDays(-1)?"Yesterday":
                date.Year==today.Year?date.ToString("MMM d",CultureInfo.CurrentCulture):date.ToString("d",CultureInfo.CurrentCulture);
            copy.Add(UI.Text(label,ids.Id("time"),fullDate).Classes("discord-conversation-time").InGrid(0,1).AlignInGrid(GridCellAlignment.End));
        }
        var badges=new List<WidgetElement>();
        if(inCall)badges.Add(UI.Text("In call",ids.Id("call")).Classes("discord-conversation-call"));
        if(item.Unread)badges.Add(UI.Text("●",ids.Id("unread"),"Unread messages").Classes("discord-conversation-unread-dot"));
        if(badges.Count>0)copy.Add(UI.Row(ids.Id("badges"),badges.ToArray()).Classes("discord-conversation-badges").InGrid(1,1).AlignInGrid(GridCellAlignment.End));
        var accessible=string.Join(", ",new[]{item.Name,status,item.Unread?"Unread messages":null,inCall?"In call":null,summary,fullDate}.Where(value=>value is not null));
        return UI.ActionSurface("open",ids.Id("row"),accessible,ActionSurfaceOrientation.Horizontal,
            UI.Grid(ids.Id("avatar"),(GridTrackDefinition[])[GridTrack.Star()],(GridTrackDefinition[])[GridTrack.Star()],avatar.ToArray()).Classes("discord-conversation-avatar"),
            UI.Grid(ids.Id("copy"),(GridTrackDefinition[])[GridTrack.Auto(),GridTrack.Auto()],(GridTrackDefinition[])[GridTrack.Star(),GridTrack.Auto()],copy.ToArray()).Classes("discord-conversation-copy"))
            .Selected(query.Audience?.SelectedChannel==item.ChannelId && item.ChannelId is not null).Classes("discord-row","discord-conversation-row");
    }
}
