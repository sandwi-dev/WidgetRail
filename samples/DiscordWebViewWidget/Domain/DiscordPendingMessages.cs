using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.DiscordWebView.Domain;

internal sealed record DiscordPendingMessageKey(string Account,string SessionId,string ChannelId);

/// <summary>A submitted external side effect, independent of the currently visible conversation.</summary>
internal sealed class DiscordPendingMessage
{
    internal DiscordPendingMessageKey Key { get; }
    internal string Nonce { get; }
    internal string Content { get; }
    internal IReadOnlyList<WidgetFileReference> Files { get; }
    internal DiscordPendingMessage(DiscordPendingMessageKey key,string nonce,string content,IEnumerable<WidgetFileReference> files)
    {Key=key;Nonce=nonce;Content=content;Files=Array.AsReadOnly(files.ToArray());}
}

/// <summary>
/// Keeps unresolved Discord submissions until their exact nonce is resolved or
/// their account/session retires. Provider POST deduplication stays in the adapter.
/// This journal neither submits messages nor owns navigation or file release IO.
/// </summary>
internal sealed class DiscordPendingMessages
{
    internal const int MaximumAttempts=16;
    private readonly object gate=new();
    private readonly Dictionary<DiscordPendingMessageKey,DiscordPendingMessage> attempts=[];
    internal int Count { get { lock(gate)return attempts.Count; } }

    /// <summary>False returns the existing channel attempt, or null when capacity is exhausted.</summary>
    internal bool TryAdmit(DiscordPendingMessageKey key,string nonce,string content,IReadOnlyList<WidgetFileReference>? files,
        out DiscordPendingMessage? pending)
    {
        ValidateKey(key);
        if(nonce is not {Length:>0 and <=25} || !nonce.All(char.IsAsciiDigit) || content is null || content.Length>2000 ||
            content.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Invalid pending message.");
        var frozen=files?.ToArray()??[];
        if(frozen.Length>10 || frozen.Any(file=>file?.IsWellFormed()!=true) ||
            frozen.Select(file=>file.Id).Distinct(StringComparer.Ordinal).Count()!=frozen.Length || string.IsNullOrWhiteSpace(content)&&frozen.Length==0)
            throw new ArgumentException("Invalid pending message attachments.");
        lock(gate)
        {
            if(attempts.TryGetValue(key,out pending))return false;
            if(attempts.Count>=MaximumAttempts){pending=null;return false;}
            pending=new(key,nonce,content,frozen);attempts.Add(key,pending);return true;
        }
    }

    internal bool TryGet(DiscordPendingMessageKey key,out DiscordPendingMessage? pending)
    {ValidateKey(key);lock(gate)return attempts.TryGetValue(key,out pending);}

    internal bool HoldsFile(string fileId)
    {lock(gate)return attempts.Values.Any(attempt=>attempt.Files.Any(file=>file.Id==fileId));}

    internal bool TryResolve(DiscordPendingMessageKey key,string nonce,out DiscordPendingMessage? removed)
    {
        ValidateKey(key);
        lock(gate)
        {
            if(!attempts.TryGetValue(key,out var pending)||pending.Nonce!=nonce){removed=null;return false;}
            attempts.Remove(key);removed=pending;return true;
        }
    }

    /// <summary>Retains this account/session only and returns no-longer-held grants for caller cleanup.</summary>
    internal IReadOnlyList<WidgetFileReference> Reset(string account,string sessionId)
    {
        ValidateKey(new(account,sessionId,"1"));
        lock(gate)
        {
            var retired=attempts.Where(pair=>pair.Key.Account!=account||pair.Key.SessionId!=sessionId).ToArray();
            foreach(var pair in retired)attempts.Remove(pair.Key);
            var retained=attempts.Values.SelectMany(attempt=>attempt.Files).Select(file=>file.Id).ToHashSet(StringComparer.Ordinal);
            return Array.AsReadOnly(retired.SelectMany(pair=>pair.Value.Files).Where(file=>!retained.Contains(file.Id)).DistinctBy(file=>file.Id).ToArray());
        }
    }

    internal IReadOnlyList<WidgetFileReference> Clear()
    {
        lock(gate)
        {
            var files=Array.AsReadOnly(attempts.Values.SelectMany(attempt=>attempt.Files).DistinctBy(file=>file.Id).ToArray());
            attempts.Clear();return files;
        }
    }

    private static void ValidateKey(DiscordPendingMessageKey key)
    {
        if(key is null || !DiscordMessageValues.Id(key.Account)||!DiscordMessageValues.Id(key.ChannelId)||
            !new WebSessionReference(key.SessionId).IsWellFormed())throw new ArgumentException("Invalid pending message identity.");
    }
}
