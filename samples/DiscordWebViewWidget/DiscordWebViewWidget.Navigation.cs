using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private static bool IsMessageRoute(Route route)=>route is Route.Messages or Route.Conversation or Route.MessageMedia;
    private const string ServersSectionLabel="Servers";

    private void OpenCallSection()
    {
        if(navigation.Value.Route==Route.Home)return;
        if(messages.Value.IncomingFiles)CancelIncomingShare(messages.Value);
        navigation.NavigateRoot(Route.Home);
        ObserveMessages();
    }

    private WidgetElement SectionNavigation(DiscordState? data,Route route)
    {
        var unread=data?.Messaging?.HasUnreadConversations ??
            (messages.Value.Query is {} query && conversations.GetSnapshot(query).Value?.Items.Any(item=>item.Unread)==true);
        return UI.SegmentedTabs("discord.sections",IsMessageRoute(route)?"discord.messages.open":"discord.call.open",
            new("discord.call.open",ServersSectionLabel,"discord.section.call"){Focus=new(Left:"discord.account.open")},
            new("discord.messages.open",unread?"Messages •":"Messages","messages",unread?"Messages, unread conversations":"Messages",
                data?.Messaging?.CanRead!=true){Focus=new(Right:"discord.refresh")}).Classes("discord-section-tabs");
    }

    private WidgetElement ConversationWorkspace(DiscordState data,out string first,out ScrollRevealRequest? reveal)
    {
        var chat=MessagePage(data,Route.Conversation,out first,out reveal);
        var current=messages.Value;
        if(current.Query is not {} query || !MessageAccountCurrent(query.Account,query.Session))return chat;
        return UI.Row("discord.message.workspace",
            ConversationList(query,current,sidebar:true).AddClasses("discord-conversation-sidebar").VisibleWhen(ResponsiveVisibility.ExpandedOnly),
            chat.AddClasses("discord-conversation-content")).Classes("discord-body","discord-message-workspace");
    }

    private WidgetElement MessageCallDock(DiscordState data)
    {
        var guild=data.Channels.FirstOrDefault(channel=>channel.Id==data.ChannelId)?.Guild;
        var title=data.ChannelName+(guild is null?"":" · "+guild);
        var summary=VoiceConnectionLabel(data)+" · "+title+", "+data.Participants.Length+" participants";
        if(data.VoiceConnection!="connected")title=VoiceConnectionLabel(data)+" · "+title;
        var share=ShareButton(data,"discord.call.dock.share","Share");
        var people=data.Participants.Take(4).Select(person=>(WidgetElement)UI.Stack("discord.call.dock.person."+person.Id,
            Avatar(person.AvatarUrl,"discord.call.dock.avatar."+person.Id,person.Name+", "+ParticipantStatus(person)).AddClasses("discord-dock-avatar-image"))
            .Classes("discord-dock-avatar",person.Speaking&&!person.Muted&&!person.Deafened?"discord-voice-speaking":"discord-voice-listening")).ToList();
        if(data.Participants.Length>4)people.Add(UI.Text("+"+(data.Participants.Length-4),"discord.call.dock.more",$"{data.Participants.Length-4} more participants").Classes("discord-secondary"));
        return UI.Row("discord.call.dock",
            UI.ActionSurface("discord.section.call","discord.call.dock.open",summary,ActionSurfaceOrientation.Vertical,
                UI.Text(title,"discord.call.dock.name").Classes("discord-row-title","discord-dock-title"),
                UI.Row("discord.call.dock.people",people.ToArray()).Classes("discord-dock-people"))
                .Classes("discord-dock-summary"),
            UI.Row("discord.call.dock.actions",
                Action(data.Muted?"Unmute":"Mute",CallAction("mute",data),"discord.call.dock.mute",data.Muted?DiscordIcons.Muted:DiscordIcons.Microphone)
                    .Selected(data.Muted).Disabled(!data.Capabilities.Mute),
                Action(data.Deafened?"Undeafen":"Deafen",CallAction("deafen",data),"discord.call.dock.deafen",DiscordIcons.Voice)
                    .Selected(data.Deafened).Disabled(!data.Capabilities.Deafen),
                Action(share.Label,share.ActionId,share.Id,DiscordIcons.Share).Disabled(share.IsDisabled==true).AddClasses("discord-primary"),
                Action("Leave",CallAction("leave",data),"discord.call.dock.leave",DiscordIcons.Leave).AddClasses("discord-leave"))
                .Classes("discord-dock-actions"))
            .Classes("discord-call-dock");
        static ActionSurfaceElement Action(string label,string action,string id,WidgetIcon icon)=>
            UI.ActionSurface(action,id,label,ActionSurfaceOrientation.Horizontal,
                UI.Icon(icon,id+".icon",label),UI.Text(label,id+".label").VisibleWhen(ResponsiveVisibility.ExpandedOnly))
                .Classes("discord-control","discord-dock-action");
    }
}
