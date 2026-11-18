using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.DiscordWebView.Domain;

internal sealed record DiscordMessageDraft(string Text,WidgetFileReference[] Files);

/// <summary>Unsubmitted conversation data, separate from the external-send nonce journal.</summary>
internal sealed class DiscordMessageDrafts
{
    internal const int MaximumDrafts=16;
    private readonly object gate=new();
    private readonly Dictionary<DiscordPendingMessageKey,DiscordMessageDraft> drafts=[];
    internal bool TryStore(DiscordPendingMessageKey key,string text,IEnumerable<WidgetFileReference> files)
    {
        var frozen=files.ToArray();
        lock(gate)
        {
            if(text.Length==0&&frozen.Length==0){drafts.Remove(key);return true;}
            if(!drafts.ContainsKey(key)&&drafts.Count>=MaximumDrafts)return false;
            drafts[key]=new(text,frozen);return true;
        }
    }
    internal DiscordMessageDraft? Get(DiscordPendingMessageKey key){lock(gate)return drafts.GetValueOrDefault(key);}
    internal void Remove(DiscordPendingMessageKey key){lock(gate)drafts.Remove(key);}
    internal bool HoldsFile(string id){lock(gate)return drafts.Values.Any(draft=>draft.Files.Any(file=>file.Id==id));}
    internal WidgetFileReference[] Clear()
    {
        lock(gate){var files=drafts.Values.SelectMany(draft=>draft.Files).DistinctBy(file=>file.Id).ToArray();drafts.Clear();return files;}
    }
}
