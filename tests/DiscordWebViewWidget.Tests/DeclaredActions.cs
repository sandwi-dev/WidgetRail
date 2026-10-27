using WidgetRail.WidgetProtocol;

// Test callers resolve the action actually declared by a stable control, just
// like the host. Explicit stale-action tests retain and replay the old ID.
internal static class DeclaredActions
{
    internal static string Resolve(ViewSnapshot snapshot,string action)
    {
        var id=action switch
        {
            "mute"=>"discord.main.mute","deafen"=>"discord.main.deafen","leave"=>"discord.main.leave",
            "pin-call"=>"discord.voice.pin","share" or "stopShare"=>"discord.share.start",
            "unwatch"=>"discord.streams.stop",
            _ when action.StartsWith("watch.",StringComparison.Ordinal)=>"discord.stream."+action[6..],
            _=>null
        };
        if(id is null)return action;
        var value=Nodes(snapshot.Root).Single(node=>node.Id==id).ActionId;
        var method=action.StartsWith("watch.",StringComparison.Ordinal)?"watch":action;
        if(value is null||!value.StartsWith(method+".",StringComparison.Ordinal))
            throw new InvalidOperationException("The requested action is not currently declared: "+action);
        return value;
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
}
