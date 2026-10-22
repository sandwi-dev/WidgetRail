using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal sealed partial class IndexedOwnedBridgeProbeWidget
{
    private WidgetLoadedCollection<int, int>? loadedProbe;
    private FocusGroupEntryRequest? loadedEntry;
    private int loadedIdentity, loadedContent;
    private WidgetView RenderLoadedProbe() => new(UI.Stack("root",
        UI.Text("Loaded collection lifecycle", "status"), UI.Text("Calls: 0", "calls"),
        UI.Row("loaded.tools",
            UI.Button("Content", "loaded-content", "loaded-content"),
            UI.Button("Reverse", "loaded-reverse", "loaded-reverse"),
            UI.Button("Context", "loaded-context", "loaded-context"),
            UI.Button("Focus 75", "loaded-exact", "loaded-exact"),
            UI.Button("Remove 75", "loaded-remove", "loaded-remove")),
        UI.CollectionList("items", loadedProbe!, 48, "Loaded items"))
        .Shortcut(ControllerButton.X, actionId: "loaded-content")
        .Shortcut(ControllerButton.RightBumper, actionId: "loaded-reverse")
        .Shortcut(ControllerButton.Menu, actionId: "loaded-context")
        .Shortcut(ControllerButton.LeftBumper, actionId: "loaded-exact")
        .Shortcut(ControllerButton.RightTrigger, actionId: "loaded-remove"), "items")
        { FocusGroupEntryRequest = loadedEntry };

    private void HandleLoadedProbe(string action)
    {
        loadedProbe ??= CreateLoadedCollection<int, int>("loaded-probe", "0", 0, Enumerable.Range(0, 100).ToArray(), new()
        {
            PreserveOccurrences = true,
            ItemKey = item => new("item." + item),
            RenderItem = (content, item, ids) => UI.Button($"Loaded {item} v{content}", "loaded-open", ids.Id("row")),
            OnAction = (_, _, _, _) => ValueTask.CompletedTask,
        });
        loadedEntry = null;
        if (action == "loaded-content") ++loadedContent;
        if (action == "loaded-context") ++loadedIdentity;
        var items = action == "loaded-reverse" ? loadedProbe.Items.Reverse().ToArray() :
            action == "loaded-remove" ? loadedProbe.Items.Where(item => item != 75).ToArray() : loadedProbe.Items;
        loadedProbe.Publish(loadedIdentity.ToString(), loadedContent, items);
        if (action is "loaded" or "loaded-exact")
        {
            var index = loadedProbe.Items.ToList().IndexOf(75);
            if (index >= 0) loadedEntry = CreateFocusRequest().EnterCollection(loadedProbe, "items",
                loadedProbe.FocusTarget("items", new("item.75"), index));
        }
        Invalidate();
    }
}
