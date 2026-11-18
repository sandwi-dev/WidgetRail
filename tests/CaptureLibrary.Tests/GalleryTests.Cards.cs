using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task CardCompositionAndContext()
    {
        await using var fixture = new GalleryFixture();
        fixture.Items[0] = fixture.Items[0] with { IsFavorite = true };
        fixture.Items[1] = fixture.Items[1] with { Media = fixture.Items[1].Media with
            { ContentType = "video/mp4", DurationSeconds = 3661, Origin = CaptureAttachmentOrigin.SavedLibrary } };
        fixture.Items[2] = fixture.Items[2] with { Available = false };
        await fixture.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "gallery-card-style", 100); fixture.Host = host;
        using var lease = await host.AcquireAsync("capture-library.entries", 0, 3);
        var favorite = lease.Range.Items[0].Root;
        var video = lease.Range.Items[1].Root;
        var unavailable = lease.Range.Items[2].Root;
        Check(Flatten(favorite).Single(node => node.StyleClasses.Contains("capture-card-title")).Text == "Boss fight" &&
            Flatten(favorite).Single(node => node.StyleClasses.Contains("capture-card-favorite")).Text == "★",
            "Favorite must not alter or indent the capture title");
        Check(favorite.AccessibilityLabel?.Contains("Favorite") == true && video.AccessibilityLabel?.Contains("Clip · 1:01:01") == true &&
            unavailable.AccessibilityLabel?.Contains("Unavailable") == true, "Composed cards lost spoken type, duration, favorite or unavailable state");
        Check(Flatten(video).Any(node => node.StyleClasses.Contains("capture-card-info") && node.Text?.Contains(" · ")==true) &&
            Flatten(video).Any(node => node.StyleClasses.Contains("capture-duration-badge") && node.Children.Any(child=>child.Text=="1:01:01")),
            "Compact date/time metadata or long recording duration badge is missing");
        Check(Flatten(unavailable).Any(node => node.StyleClasses.Contains("capture-thumbnail-placeholder") && node.StyleClasses.Contains("capture-density-1")),
            "Unavailable media must occupy the same thumbnail extent");
        Check(lease.Range.Items.All(item => item.Root.ActionId == "capture-library.open-item" && item.Root.ContextMenuButton == ControllerButton.Menu &&
            item.Root.ContextActions.Count == 1 && item.Root.ContextActions[0].ActionId == "capture-library.item.favorite"),
            "Card primary action or native context-menu ownership changed");
        Check(fixture.View().Root.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.X && shortcut.ActionId == "capture-library.video"),
            "Card context menu stole the existing Record clip shortcut");
        Check(fixture.Opened == 0 && lease.Range.Items.SelectMany(item => Flatten(item.Root)).All(node => node.Kind != ViewNodeKind.MediaPlayer),
            "Gallery realization opened full media or created a player per card");
    }

    internal static async Task CardFavoriteUsesExactRevision()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "gallery-card-favorite", 100); fixture.Host = host;
        using var lease = await host.AcquireAsync("capture-library.entries", 0, 3);
        var item = lease.Range.Items[0];
        var before = fixture.Collection!.IndexedCollection!;
        Check(lease.RouteAction(item.Key, ControllerButton.Menu, contextActionOwnerId: item.Root.Id,
            contextActionId: "capture-library.item.favorite") is not null, "Native item context action rejected");
        await fixture.Until(() => fixture.Items[0].IsFavorite && !fixture.Busy);
        Check(fixture.Collection!.IndexedCollection is {} after && after.QueryGeneration == before.QueryGeneration &&
            after.ContentRevision > before.ContentRevision && fixture.View().FocusGroupEntryRequest is null,
            "A favorite-only edit must refresh content without rebuilding the query or redirecting focus");
        Check(fixture.Items[0].MetadataRevision == 1 && fixture.Items[0].Title == "Boss fight" && fixture.Opened == 0 &&
            fixture.Node("capture-library.preview") is null, "Favorite changed title, opened media or navigated away");
        using var refreshed = await host.AcquireAsync("capture-library.entries", 0, 3);
        Check(refreshed.Range.Items[0].Root.ContextActions.Single().Label == "Remove favorite", "Favorite feedback did not reconcile the card declaration");
        await fixture.Action("capture-library.search", "no matching capture");
        await fixture.Until(() => fixture.Collection is null && !fixture.Busy);
        Check(refreshed.RouteAction(item.Key, ControllerButton.Menu, contextActionOwnerId: item.Root.Id,
            contextActionId: "capture-library.item.favorite") is null && fixture.Items[0].IsFavorite,
            "Retired query context action could edit a capture");
    }

    internal static async Task CardFavoriteRejectsStaleMetadataAndPreservesSelection()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "gallery-card-stale", 100); fixture.Host = host;
        using (var lease = await host.AcquireAsync("capture-library.entries", 0, 3))
        {
            var item = lease.Range.Items[0];
            fixture.Items[0] = fixture.Items[0] with { Title = "Updated elsewhere", MetadataRevision = 1 };
            Check(lease.RouteAction(item.Key, ControllerButton.Menu, contextActionOwnerId: item.Root.Id,
                contextActionId: "capture-library.item.favorite") is not null, "Stale metadata fixture action was not admitted");
            await fixture.Until(() => fixture.LastFeedback?.Contains("changed elsewhere") == true && !fixture.Busy);
            Check(!fixture.Items[0].IsFavorite && fixture.Items[0].Title == "Updated elsewhere" && fixture.Items[0].MetadataRevision == 1,
                "Old favorite action overwrote newer metadata");
        }
        await fixture.Action("capture-library.selection.toggle");
        host.PublishSnapshot();
        using var selected = await host.AcquireAsync("capture-library.entries", 0, 3);
        var row = selected.Range.Items[0];
        Check(row.Root.ContextActions.Single().IsDisabled, "Individual favorite should not invalidate an active batch selection");
        var epoch = fixture.Collection!.IndexedSelection!.Epoch;
        Check(selected.RouteSelection(row.Key, new(epoch, IndexedSelectionGesture.Toggle)) is not null, "Card context action replaced selection semantics");
        await fixture.Until(() => fixture.Collection!.IndexedSelection!.Count == 1);
        Check(selected.RouteAction(row.Key, ControllerButton.Menu, contextActionOwnerId: row.Root.Id,
            contextActionId: "capture-library.item.favorite") is null && fixture.Collection!.IndexedSelection!.Count == 1,
            "Disabled favorite mutated or cleared batch selection");
    }

    private static IEnumerable<ViewNode> Flatten(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
