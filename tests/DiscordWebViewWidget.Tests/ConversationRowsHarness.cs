using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

// Keep existing scenario labels readable while exercising the actual indexed
// source and lease admission. The alias is test-only; production IDs are scoped.
internal static class ConversationRowsHarness
{
    internal static bool IsRow(string id)=>id.StartsWith("discord.conversation.",StringComparison.Ordinal);
    internal static ViewNode? Find(Widget widget,ViewSnapshot snapshot,string id)
    {
        if(!IsRow(id))return null;
        var suffix=id["discord.conversation.".Length..];
        var name=suffix.StartsWith("name.",StringComparison.Ordinal);
        var key=name?suffix[5..]:suffix;
        var collection=Nodes(snapshot.Root).FirstOrDefault(node=>node.Id=="discord.messages.list");
        if(collection?.IndexedCollection?.Count is not >0)return null;
        using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"conversation-test");host.PublishSnapshot();
        var index=host.LocateAsync("discord.messages.list",new(key)).AsTask().GetAwaiter().GetResult();
        if(index is null)return null;
        using var range=host.AcquireAsync("discord.messages.list",index.Value,1).AsTask().GetAwaiter().GetResult();
        var root=range.Range.Items.Single().Root;
        return name?Nodes(root).Single(node=>node.Id.EndsWith(".name",StringComparison.Ordinal)):root;
    }
    internal static async Task ActAsync(Widget widget,string id)
    {
        using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"conversation-action");host.PublishSnapshot();
        var key=id["discord.conversation.".Length..];
        var index=await host.LocateAsync("discord.messages.list",new(key))??throw new InvalidOperationException("Missing "+id);
        using var range=await host.AcquireAsync("discord.messages.list",index,1);
        if(range.RouteAction(key,ControllerButton.A)!=WidgetOperationAdmission.Enqueued)throw new InvalidOperationException("Conversation action was not admitted.");
        await WidgetTestHost.WaitForActionsAsync(widget);
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
}
