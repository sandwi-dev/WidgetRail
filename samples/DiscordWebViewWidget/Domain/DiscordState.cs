namespace WidgetRail.Samples.DiscordWebView.Domain;

internal sealed record DiscordCapabilities(bool Voice, bool Mute, bool Deafen, bool Watch, bool Share, bool StopShare);
internal sealed record DiscordChannel(string Id, string GuildId, string Name, string Guild);
internal sealed record DiscordParticipant(string Id, string Name, bool Muted, bool Deafened, bool Speaking,string? AvatarUrl=null,DiscordStreamAudio? Audio=null);
internal sealed record DiscordStreamAudio(bool Available, bool Muted, double? Volume);
internal sealed record DiscordStream(string Key, string UserId, string Name, DiscordStreamAudio? Audio=null,bool Watching=false);
internal sealed record DiscordOutgoingViewers(string Key,string[] UserIds);
internal sealed record DiscordShareQuality(int Resolution,int FrameRate)
{
    internal bool IsSupported=>Resolution is 480 or 720 or 1080 && FrameRate is 15 or 30 or 60 &&
        (Resolution!=1080||FrameRate!=60);
    internal static DiscordShareQuality[] Baseline=>[new(480,15),new(480,30),new(720,15),new(720,30)];
}
internal sealed record DiscordState(int Revision, bool Ready, string Account, DiscordCapabilities Capabilities,
    DiscordChannel[] Channels, string? ChannelId, string ChannelName, bool Muted, bool Deafened,
    DiscordParticipant[] Participants, DiscordStream[] Streams, bool Sharing, bool SharePending, string? ShareError, string RelayState, DiscordFriendActivity[]? Activities=null, string ActivityAvailability="unavailable", bool ActivitiesTruncated=false,
    DiscordShareQuality[]? ShareQualities=null,string ShareQualityAvailability="unavailable",DiscordShareQuality? PreferredShareQuality=null,
    string VoiceConnection="unavailable",string? ShareQualityWarning=null,string NotificationPermission="unavailable",
    DiscordOutgoingViewers? OutgoingViewers=null,string DesktopNotifications="unavailable",DiscordMessagingState? Messaging=null,
    string? ConnectionState=null,DiscordCalls? Calls=null)
{
    internal string EffectiveConnectionState => ConnectionState ?? (Ready ? "ready" : "initializing");
    public DiscordFriendActivity[] Activities { get; init; }=Activities??[];
    public DiscordShareQuality[] ShareQualities {get;init;}=ShareQualities??DiscordShareQuality.Baseline;
    internal void Validate()
    {
        if(Calls is not null && (!Calls.IsValid(ChannelId) || !Ready&&Calls.Available))
            throw new InvalidDataException("invalid_call_state");
        if(ConnectionState is not(null or "initializing" or "signed-out" or "ready" or "reconnecting" or "unavailable") ||
            ConnectionState is not null && Ready!=(ConnectionState=="ready"))
            throw new InvalidDataException("invalid_connection_state");
        if(Ready && Messaging is not null)Messaging.Validate(Account);
        if(DesktopNotifications is not("enabled" or "disabled" or "unavailable"))
            throw new InvalidDataException("invalid_desktop_notifications");
        if(OutgoingViewers is {} viewers && (!Sharing||ChannelId is null||Participants is null||
            viewers.Key is not {Length:>0 and <=100}||!OwnStreamKey(viewers.Key)||
            viewers.UserIds is null||viewers.UserIds.Length>100||
            viewers.UserIds.Any(id=>!Id(id)||id==Account||!Participants.Any(person=>person?.Id==id))||
            viewers.UserIds.Distinct().Count()!=viewers.UserIds.Length))
            throw new InvalidDataException("invalid_outgoing_viewers");
        if (NotificationPermission is not ("default" or "granted" or "denied" or "unavailable"))
            throw new InvalidDataException("invalid_notification_permission");
        if(ShareQualityWarning is not(null or "share_quality_unavailable" or "share_quality_unconfirmed" or "share_content_preference_unconfirmed"))
            throw new InvalidDataException("invalid_share_quality_warning");
        if(VoiceConnection is not("disconnected" or "connecting" or "connected" or "reconnecting" or "unavailable") ||
            VoiceConnection is "connected" or "connecting" or "reconnecting" && ChannelId is null)
            throw new InvalidDataException("invalid_voice_connection_state");
        if(ShareQualities is null || ShareQualities.Length>8 || ShareQualities.Any(q=>q is null||!q.IsSupported) ||
            ShareQualities.Distinct().Count()!=ShareQualities.Length || ShareQualityAvailability is not("ready" or "unavailable") ||
            ShareQualityAvailability=="unavailable" && ShareQualities.Any(q=>!DiscordShareQuality.Baseline.Contains(q)) ||
            PreferredShareQuality is {IsSupported:false})
            throw new InvalidDataException("invalid_share_quality_state");
        if(Activities is null || Activities.Length>50 || ActivityAvailability is not("ready" or "unavailable" or "disconnected") ||
            ActivityAvailability!="ready" && (Activities.Length!=0 || ActivitiesTruncated) ||
            Activities.Any(a=>a is null || !Id(a.UserId) || !Label(a.Name) || !Label(a.Title) || !Label(a.Details) || !DiscordAvatar.IsAllowed(a.AvatarUrl,a.UserId) ||
                a.Status is not("online" or "idle" or "dnd" or "offline") ||
                a.Kind is not("playing" or "streaming" or "listening" or "watching" or "competing" or "voice") ||
                a.ChannelId is not null && !Id(a.ChannelId) || a.GuildId is not null && !Id(a.GuildId) ||
                a.ChannelName is not null && !Label(a.ChannelName) || a.GuildName is not null && !Label(a.GuildName) ||
                (a.ChannelId is null)!=(a.GuildId is null) || (a.ChannelId is null)!=(a.ChannelName is null) || (a.ChannelId is null)!=(a.GuildName is null) ||
                a.CanJoinVoice && (a.ChannelId is null || a.GuildId is null || a.ChannelId==ChannelId) ||
                a.StreamKey is not null && (a.StreamKey.Length is <1 or >100 || a.StreamKey.Any(char.IsControl) || a.ChannelId is null || a.StreamKey!=$"guild:{a.GuildId}:{a.ChannelId}:{a.UserId}") ||
                a.CanWatch && (a.StreamKey is null || a.ChannelId is null || a.ChannelId!=ChannelId) ||
                a.StartedAt is <0 or >253402300799999) ||
            Activities.Select(a=>a.UserId).Distinct(StringComparer.Ordinal).Count()!=Activities.Length)
            throw new InvalidDataException("invalid_activity_state");
        if (Revision != 1 || Capabilities is null || Channels is null || Participants is null || Streams is null ||
            Channels.Length > 1000 || Participants.Length > 100 || Streams.Length > 50 || Account is null || Account.Length > 32 ||
            Channels.Any(c => c is null || !Id(c.Id) || !Id(c.GuildId) || !Label(c.Name) || !Label(c.Guild)) ||
            Participants.Any(p => p is null || !Id(p.Id) || !Label(p.Name) || !DiscordAvatar.IsAllowed(p.AvatarUrl,p.Id) ||
                p.Audio is { } audio && (!AudioValid(audio) || p.Id==Account&&audio.Available)) ||
            Streams.Any(s => s is null || !Id(s.UserId) || !Label(s.Name) || s.Key is not { Length: > 0 and <= 100 } ||
                s.Audio?.Volume is { } volume && (!double.IsFinite(volume) || volume is <0 or >100)) ||
            Channels.Select(c => c.Id).Distinct().Count() != Channels.Length ||
            Streams.Select(s => s.Key).Distinct().Count() != Streams.Length)
            throw new InvalidDataException("invalid_adapter_state");
    }
    private static bool Id(string? id) => id is { Length: > 0 and <= 32 } && id.All(char.IsAsciiDigit);
    private bool OwnStreamKey(string key)=>key.Split(':') switch
    {
        ["call",var channel,var account]=>channel==ChannelId&&account==Account,
        ["guild",var guild,var channel,var account]=>Id(guild)&&channel==ChannelId&&account==Account,
        _=>false
    };
    private static bool AudioValid(DiscordStreamAudio audio)=>audio.Available
        ?audio.Volume is { } volume&&double.IsFinite(volume)&&volume is >=0 and <=100
        :audio.Volume is null&&!audio.Muted;
    private static bool Label(string? label) => label is { Length: <= 120 } && !label.Any(char.IsControl);
}
