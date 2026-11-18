namespace WidgetRail.Samples.DiscordWebView.Domain;

internal sealed record DiscordMessagingState(string? Account,string Availability,bool CanRead,bool CanSend,bool CanOpenDm,
    int MaximumContentLength,int MaximumPageSize,long MessageRevision=0,string? ObservedChannelId=null,long? MaximumAttachmentBytes=null,long CatalogRevision=0,
    long ObservedSinceRevision=0,long WatchEpoch=0)
{
    internal void Validate(string account)
    {
        if(Account!=account || Availability is not ("ready" or "unavailable" or "disconnected") ||
            MaximumContentLength is <1 or >2000 || MaximumPageSize is <1 or >50 || MessageRevision<0 || CatalogRevision<0 ||
            ObservedSinceRevision<0 || ObservedSinceRevision>MessageRevision || WatchEpoch<0 ||
            ObservedChannelId is not null && !DiscordMessageValues.Id(ObservedChannelId) || MaximumAttachmentBytes is <=0)
            throw DiscordMessageValues.Invalid();
    }
}

internal sealed record DiscordMessageChange(long Revision,string Kind,string Id,DiscordMessage? Message)
{
    internal bool IsValid(string channel)=>Revision>0 && DiscordMessageValues.Id(Id) &&
        (Kind=="remove"?Message is null:Kind=="upsert" && Message is not null && Message.Id==Id && Message.IsValid(channel));
}
internal sealed record DiscordMessageChangesContext(string Account,string ChannelId,long WatchEpoch,long FromRevision,long Revision,bool ResetRequired,bool HasMore);
internal sealed record DiscordMessageChanges(DiscordMessageChangesContext Context,DiscordMessageChange[] Items)
{
    internal void Validate(string account,string channel,long epoch,long after)
    {
        if(Context is null || Context.Account!=account || Context.ChannelId!=channel || Context.WatchEpoch!=epoch ||
            Context.FromRevision!=after || Context.Revision<after || Context.HasMore && Context.Revision==after ||
            Items is null || Items.Length>50 || Items.Any(item=>item is null || !item.IsValid(channel) || item.Revision<=after || item.Revision>Context.Revision) ||
            Items.Zip(Items.Skip(1)).Any(pair=>pair.First.Revision>=pair.Second.Revision))throw DiscordMessageValues.Invalid();
    }
}

internal sealed record DiscordMessageRecoveryContext(string Account,string ChannelId,long WatchEpoch,long Revision,string After,bool Complete);
internal sealed record DiscordMessageRecovery(DiscordMessageRecoveryContext Context,DiscordMessageChange[] Items)
{
    internal void Validate(string account,string channel,long epoch,IReadOnlyList<string> keys)
    {
        if(Context is null || Context.Account!=account || Context.ChannelId!=channel || Context.WatchEpoch!=epoch ||
            Context.Revision<0 || !DiscordMessageValues.Id(Context.After) || !keys.Contains(Context.After) ||
            Items is null || Items.Length>40 || Items.Select(item=>item.Id).Distinct().Count()!=Items.Length ||
            Items.Any(item=>item is null || item.Revision!=Context.Revision || !keys.Contains(item.Id) ||
                (item.Kind=="remove"?item.Message is not null:item.Kind!="upsert" || item.Message is null || item.Message.Id!=item.Id || !item.Message.IsValid(channel))) ||
            Context.Complete && Items.Length!=keys.Count)throw DiscordMessageValues.Invalid();
    }
}

internal sealed record DiscordConversation(string Id,string? ChannelId,string? UserId,string Name,string Kind,string? AvatarUrl,
    string? LastMessageId,bool Unread,bool CanSend)
{
    public string? OldestUnreadMessageId { get; init; }
    internal bool IsValid => Id is {Length:>0 and <=128} && !Id.Any(char.IsControl) &&
        Kind is "dm" or "group" or "friend" && DiscordMessageValues.Label(Name,120) &&
        (Kind=="friend" ? DiscordMessageValues.Id(UserId) : DiscordMessageValues.Id(ChannelId)) &&
        (ChannelId is null || DiscordMessageValues.Id(ChannelId)) && (UserId is null || DiscordMessageValues.Id(UserId)) &&
        (LastMessageId is null || DiscordMessageValues.Id(LastMessageId)) &&
        (OldestUnreadMessageId is null || Unread && DiscordMessageValues.Id(OldestUnreadMessageId)) && DiscordMessageValues.MediaUrl(AvatarUrl);
}

internal sealed record DiscordConversationPageContext(string Account,string Version,int TotalCount,int Offset,bool HasMore,bool Truncated=false);
internal sealed record DiscordConversationPage(DiscordConversationPageContext Context,DiscordConversation[] Items)
{
    internal void Validate(string account,int offset,int limit)
    {
        if(Context is null || Context.Account!=account || Context.Version is not {Length:>0 and <=128} ||
            Context.Offset!=offset || Context.TotalCount<0 || Items is null || Items.Length>limit ||
            Items.Any(item=>item is null || !item.IsValid) || Items.Select(item=>item.Id).Distinct().Count()!=Items.Length ||
            (long)offset+Items.Length>Context.TotalCount || Context.HasMore && Items.Length==0)
            throw DiscordMessageValues.Invalid();
    }
}

internal sealed record DiscordMessageAttachment(string Id,string FileName,string? ContentType,long ByteLength,string? Url,int? Width=null,int? Height=null)
{
    internal bool IsValid => DiscordMessageValues.Id(Id) && DiscordMessageValues.Label(FileName,256) && ByteLength>=0 &&
        ContentType is not {Length:>128} && (ContentType is null || !ContentType.Any(char.IsControl)) &&
        DiscordMessageValues.MediaUrl(Url) &&
        Width is not <1 && Height is not <1 && Width is not >65536 && Height is not >65536;
}

internal sealed record DiscordMessageMention(string Id,string Name);
internal sealed record DiscordMessageReply(string Id,string Author,string Content,bool Unavailable)
{
    internal bool IsValid=>DiscordMessageValues.Id(Id)&&DiscordMessageValues.Label(Author,120)&&DiscordMessageValues.Body(Content,240);
}
internal sealed record DiscordMessageEmbed(string Title,string Description,string? Url,string? ImageUrl)
{
    internal bool IsValid=>DiscordMessageValues.Body(Title,256)&&DiscordMessageValues.Body(Description,600)&&
        DiscordMessageValues.WebUrl(Url)&&DiscordMessageValues.MediaUrl(ImageUrl);
}
internal sealed record DiscordMessageReaction(string Name,string? EmojiId,int Count,bool IsOwn)
{
    internal bool IsValid=>DiscordMessageValues.Label(Name,64)&&(EmojiId is null||DiscordMessageValues.Id(EmojiId))&&Count is >0 and <=1000000000;
}
internal sealed record DiscordMessageSticker(string Name,string? ImageUrl)
{
    internal bool IsValid=>DiscordMessageValues.Label(Name,120)&&DiscordMessageValues.MediaUrl(ImageUrl);
}
internal sealed record DiscordMessagePoll(string Question,string[] Answers,bool Closed)
{
    internal bool IsValid=>DiscordMessageValues.Label(Question,300)&&Answers is {Length:<=10}&&Answers.All(answer=>DiscordMessageValues.Label(answer,120));
}
internal sealed record DiscordMessage(string Id,string ChannelId,string AuthorId,string AuthorName,string? AvatarUrl,
    string Content,long Timestamp,long? EditedTimestamp,string? Nonce,DiscordMessageAttachment[] Attachments,bool IsOwn,bool ContentTruncated=false)
{
    public DiscordMessageMention[] Mentions { get; init; } = [];
    public DiscordMessageReply? Reply { get; init; }
    public DiscordMessageEmbed[] Embeds { get; init; } = [];
    public DiscordMessageReaction[] Reactions { get; init; } = [];
    public DiscordMessageSticker[] Stickers { get; init; } = [];
    public DiscordMessagePoll? Poll { get; init; }
    public string? SystemText { get; init; }
    internal bool IsValid(string channel) => DiscordMessageValues.Id(Id) && ChannelId==channel && DiscordMessageValues.Id(AuthorId) &&
        (Reply is null||Reply.IsValid)&&Embeds is {Length:<=2}&&Embeds.All(item=>item is not null&&item.IsValid)&&
        Reactions is {Length:<=12}&&Reactions.All(item=>item is not null&&item.IsValid)&&
        Stickers is {Length:<=3}&&Stickers.All(item=>item is not null&&item.IsValid)&&
        (Poll is null||Poll.IsValid)&&(SystemText is null||DiscordMessageValues.Label(SystemText,240))&&
        Mentions is {Length:<=32} && Mentions.All(item=>item is not null && DiscordMessageValues.Id(item.Id) && DiscordMessageValues.Label(item.Name,120)) &&
        DiscordMessageValues.Label(AuthorName,120) && DiscordMessageValues.MediaUrl(AvatarUrl) &&
        Content is {Length:<=4000} && !Content.Any(c=>char.IsControl(c) && c is not ('\r' or '\n' or '\t')) &&
        Timestamp is >=0 and <=253402300799999 && EditedTimestamp is not <0 and not >253402300799999 &&
        (Nonce is null || Nonce.Length is >0 and <=25 && Nonce.All(char.IsAsciiDigit)) &&
        Attachments is {Length:<=10} && Attachments.All(item=>item is not null && item.IsValid) &&
        Attachments.Select(item=>item.Id).Distinct().Count()==Attachments.Length;
}

internal sealed record DiscordMessagePageContext(string Account,string ChannelId,string? Before,string? NextBefore,bool HasMore,string? After=null,string? NextAfter=null,string? Around=null);
internal sealed record DiscordMessagePage(DiscordMessagePageContext Context,DiscordMessage[] Items)
{
    internal void Validate(string account,string channel,string? before,int limit,string? after=null,string? around=null)
    {
        if(Context is null || Context.Account!=account || Context.ChannelId!=channel || Context.Before!=before || Context.After!=after || Context.Around!=around ||
            Items is null || Items.Length>limit || Items.Any(item=>item is null || !item.IsValid(channel)) ||
            Items.Select(item=>item.Id).Distinct().Count()!=Items.Length ||
            Context.NextBefore is not null && !DiscordMessageValues.Id(Context.NextBefore) ||
            Context.NextAfter is not null && !DiscordMessageValues.Id(Context.NextAfter) ||
            Context.HasMore && (after is null ? Context.NextBefore is null || Context.NextBefore==before : Context.NextAfter is null || Context.NextAfter==after))
            throw DiscordMessageValues.Invalid();
    }
}

internal sealed record DiscordOpenConversation(string Account,string ChannelId,string UserId);
internal sealed record DiscordMessageSendResult(string Account,string ChannelId,string Nonce,string Status,DiscordMessage? Message,string? ErrorCode)
{
    internal void Validate(string account,string channel,string nonce)
    {
        if(Account!=account || ChannelId!=channel || Nonce!=nonce || Status is not ("sent" or "pending" or "unknown" or "failed") ||
            Message is not null && (!Message.IsValid(channel) || Message.Nonce!=nonce || !Message.IsOwn) ||
            Status=="sent" && Message is null || ErrorCode is {Length:>80} ||
            ErrorCode is not null && !ErrorCode.All(c=>char.IsAsciiLetterLower(c)||c=='_'))
            throw DiscordMessageValues.Invalid();
    }
}

internal static class DiscordMessageValues
{
    internal static bool Id(string? value)=>value is {Length:>0 and <=32} && value.All(char.IsAsciiDigit);
    internal static bool Label(string? value,int maximum)=>value is {Length:>0} && value.Length<=maximum && !value.Any(char.IsControl);
    internal static bool Body(string? value,int maximum)=>value is not null&&value.Length<=maximum&&
        !value.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t'));
    internal static bool WebUrl(string? value)=>value is null||value.Length<=2048&&!value.Any(char.IsControl)&&
        Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https"&&uri.UserInfo.Length==0;
    internal static bool MediaUrl(string? value)=>value is null || value.Length<=2048 && Uri.TryCreate(value,UriKind.Absolute,out var uri) &&
        uri.Scheme==Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length==0 && uri.Fragment.Length==0 &&
        uri.Host is "cdn.discordapp.com" or "media.discordapp.net";
    internal static InvalidDataException Invalid()=>new("Discord returned an invalid message response.");
}
