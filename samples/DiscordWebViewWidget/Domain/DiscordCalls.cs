namespace WidgetRail.Samples.DiscordWebView.Domain;

internal sealed record DiscordCallInvitation(string Id,string ChannelId,string Name);
internal sealed record DiscordPrivateCall(string ChannelId,string Name,string Phase);
internal sealed record DiscordCalls(bool Available,bool CanStart,bool CanAnswer,bool CanDecline,
    DiscordCallInvitation[] Incoming,string[] ActiveChannels,DiscordPrivateCall? Current=null)
{
    internal bool IsValid(string? channelId)=>Incoming is {Length:<=8} && ActiveChannels is {Length:<=100} &&
        (Available || !CanStart&&!CanAnswer&&!CanDecline&&Incoming.Length==0&&ActiveChannels.Length==0&&Current is null) &&
        Incoming.All(item=>item is not null && item.Id is {Length:32} && item.Id.All(char.IsAsciiHexDigitLower) &&
            DiscordMessageValues.Id(item.ChannelId) && item.ChannelId!=channelId && DiscordMessageValues.Label(item.Name,120)) &&
        Incoming.Select(item=>item.Id).Distinct().Count()==Incoming.Length && Incoming.Select(item=>item.ChannelId).Distinct().Count()==Incoming.Length &&
        ActiveChannels.All(DiscordMessageValues.Id) && ActiveChannels.Distinct().Count()==ActiveChannels.Length &&
        (Current is null || Current.ChannelId==channelId && DiscordMessageValues.Id(Current.ChannelId) && DiscordMessageValues.Label(Current.Name,120) &&
            Current.Phase is "outgoing" or "active" or "waiting" or "unavailable");
}
