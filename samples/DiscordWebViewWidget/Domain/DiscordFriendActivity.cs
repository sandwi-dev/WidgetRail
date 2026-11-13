namespace WidgetRail.Samples.DiscordWebView.Domain;

/// <summary>Visible, account-scoped friend activity. No provider session secrets or arbitrary URLs.</summary>
internal sealed record DiscordFriendActivity(string UserId,string Name,string Status,string Kind,string Title,string Details,
    string? ChannelId,string? GuildId,string? ChannelName,string? GuildName,bool CanJoinVoice,
    string? StreamKey,bool CanWatch,long? StartedAt=null,string? AvatarUrl=null);

internal static class DiscordAvatar
{
    internal static bool IsAllowed(string? value,string userId)
    {
        if(value is null)return true;
        if(value.Length>160)return false;
        var prefix=$"https://cdn.discordapp.com/avatars/{userId}/";
        const string suffix=".png?size=64";
        if(!value.StartsWith(prefix,StringComparison.Ordinal)||!value.EndsWith(suffix,StringComparison.Ordinal))return false;
        var hash=value.AsSpan(prefix.Length,value.Length-prefix.Length-suffix.Length);
        if(hash.StartsWith("a_",StringComparison.Ordinal))hash=hash[2..];
        if(hash.Length!=32)return false;
        foreach(var ch in hash)if(ch is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))return false;
        return true;
    }
}
