using WidgetRail.WidgetProtocol;

internal static partial class GalleryTests
{
    internal static async Task QuickControlsKeepsGalleryIdentity()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        var before = fixture.Collection!.IndexedCollection;
        Check(fixture.Nodes.SelectMany(node => node.Shortcuts ?? []).Any(shortcut => shortcut.Button == ControllerButton.LeftStick && shortcut.ActionId == "capture-library.focus-controls"),
            "Gallery L3 controls shortcut missing");
        await fixture.Action("capture-library.focus-controls");
        var request = fixture.View().FocusGroupEntryRequest;
        Check(request is { GroupId: "capture-library.search-row", TargetId: "capture-library.search" } && fixture.Collection!.IndexedCollection == before,
            "L3 must issue native focus intent without replacing gallery query");
        Check(fixture.View().FocusGroupEntryRequest?.RequestId == request!.RequestId, "Rendering regenerated L3 focus intent");
        await fixture.Action("capture-library.selection.toggle");
        await fixture.Action("capture-library.focus-controls");
        Check(fixture.Collection?.IndexedSelection is { } && fixture.View().FocusGroupEntryRequest!.RequestId > request.RequestId,
            "L3 should remain available in selection mode with a fresh one-shot intent");
        await fixture.Action("capture-library.settings");
        await fixture.Until(() => fixture.Node("capture-library.settings.page") is not null && !fixture.Busy);
        Check(!fixture.Nodes.SelectMany(node => node.Shortcuts ?? []).Any(shortcut => shortcut.ActionId == "capture-library.focus-controls"),
            "Gallery shortcut leaked into settings");
    }

    private static ViewNode FolderPicker(GalleryFixture fixture) => fixture.Node("capture-library.filters.toggle")!.FocusPresentation!
        .Children.SelectMany(node => node.Children).Single(node => node.Id == "capture-library.folder-filter");

    internal static async Task FolderFilterRemainsExplicitAfterDisconnect()
    {
        await using var fixture = new GalleryFixture();
        var sourceId = new string('c', 32);
        fixture.Sources.Add(new(sourceId, "Achievement captures", true, 1, 0, @"C:\Game captures"));
        fixture.Items[0] = fixture.Items[0] with { SourceId = sourceId };
        await fixture.Start();
        var picker = FolderPicker(fixture);
        var action = picker.SelectOptions.Single(option => option.Label == "Achievement captures").ActionId;
        await fixture.Action(action);
        await fixture.Until(() => fixture.Collection?.IndexedCollection?.Count == 1);
        Check(fixture.LastQuery?.SourceId == sourceId && fixture.Node("capture-library.active.folder")?.Text == "Achievement captures ×",
            "Folder selection did not reach query or visible filter chip");
        fixture.Sources.Clear(); fixture.Items.RemoveAll(item => item.SourceId == sourceId); fixture.Notify();
        await fixture.Until(() => fixture.Node("capture-library.empty") is not null);
        Check(fixture.LastQuery?.SourceId == sourceId && FolderPicker(fixture).SelectOptions.Any(option => option.IsSelected && option.IsDisabled),
            "Disconnected folder must remain explicit, not silently select all folders");
        await fixture.Action("capture-library.folder-filter.all");
        await fixture.Until(() => fixture.Collection?.IndexedCollection?.Count == 2);
        Check(fixture.LastQuery?.SourceId is null && fixture.Node("capture-library.active.folder") is null, "Clear folder filter did not restore all captures");
        await fixture.Action("capture-library.folder-filter.pick.owned");
        await fixture.Until(() => fixture.LastQuery?.SourceId == "");
        Check(FolderPicker(fixture).SelectOptions.Count(option => option.IsSelected) == 1,
            "Owned folder selection must be distinct from All folders");
    }
}
