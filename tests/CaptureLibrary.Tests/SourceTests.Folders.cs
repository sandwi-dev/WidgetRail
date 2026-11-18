using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class SourceTests
{
    internal static async Task FolderFiltersAndFacets()
    {
        using var fixture = new Fixture();
        var otherOwner = Owner with { PackageId = "widgetrail.tests.other" };
        var otherPath = Path.Combine(fixture.Root, "second", "provider");
        Directory.CreateDirectory(otherPath);
        var first = await fixture.Store.AddSourceAsync(fixture.Provider, "Game captures", Owner, default);
        var second = await fixture.Store.AddSourceAsync(otherPath, "Game captures", otherOwner, default);
        for (var i = 0; i < 25; i++)
        {
            var name = $"capture-{i}.png"; fixture.Write(name);
            await fixture.Store.LinkAsync(new(first.Id, name), Owner, default);
        }
        File.WriteAllBytes(Path.Combine(otherPath, "capture.png"), fixture.Bytes);
        var foreign = (await fixture.Store.LinkAsync(new(second.Id, "capture.png"), otherOwner, default)).Item;
        var ownedPath = Path.Combine(fixture.Root, "original.png"); File.WriteAllBytes(ownedPath, fixture.Bytes);
        var owned = await fixture.Store.SaveAsync(new HostCaptureAttachment(new(Guid.NewGuid().ToString("N"), "image/png", 16, 16,
            fixture.Bytes.Length, 0, DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()), ownedPath), default);
        var filter = new CaptureLibraryQuery { SourceId = first.Id };
        var page = await fixture.Store.QueryAsync(new(filter, Count: 20), default);
        var tail = await fixture.Store.QueryAsync(new(filter, 20, 20, page.Revision), default);
        Check(page.TotalCount == 25 && tail.Items.Count == 5 && tail.Items.All(item => item.SourceId == first.Id), "Folder filter did not cover complete paged index");
        var folders = await fixture.Store.FoldersAsync(new(filter, tail.Revision), default);
        Check(folders.Items.Count == 3 && folders.Items.Single(item => item.SourceId == first.Id).Count == 25 &&
            folders.Items.Single(item => item.SourceId == second.Id).Count == 1 && folders.Items.Single(item => item.SourceId == "").Count == 1,
            "Folder facet must ignore its own selection and include independently owned integration folders");
        Check(folders.Items.Select(item => item.Label).Distinct().Count() == 3 && !JsonSerializer.Serialize(folders).Contains(fixture.Root),
            "Same-name folders need distinct labels without exposing absolute paths");
        Check((await fixture.Store.SourcesAsync(Owner, default)).Items.All(source => source.Id != second.Id), "Read facet broadened management source list");
        await Reject(() => fixture.Store.RemoveSourceAsync(second.Id, Owner, default), "permission_denied");
        var onlyOwned = await fixture.Store.QueryAsync(new(new() { SourceId = "" }), default);
        Check(onlyOwned.TotalCount == 1 && onlyOwned.Items.Single().Id == owned.Id, "Owned-only filter included linked originals");
        var favorite = await fixture.Store.UpdateMetadataAsync(new(foreign.Id, foreign.MetadataRevision, "Winning run", true), default);
        var combined = await fixture.Store.QueryAsync(new(new() { SourceId = second.Id, FavoritesOnly = true, Search = "winning", MediaKind = CaptureLibraryMediaKind.Screenshot }), default);
        Check(combined.TotalCount == 1 && combined.Items.Single().Id == favorite.Id, "Combined source/text/type/favorite query failed");
        Check((await fixture.Store.GroupsAsync(new(filter), default)).TotalCount == 25 &&
            (await fixture.Store.FindAsync(new(filter, foreign.Id), default)).Index is null, "Groups or anchor lookup ignored source filter");
        var emptyFacets = await fixture.Store.FoldersAsync(new(new() { MediaKind = CaptureLibraryMediaKind.Video }), default);
        Check(emptyFacets.Items.Count == 3 && emptyFacets.Items.All(item => item.Count == 0), "Empty matching folders disappeared from filter choices");
        var oldRevision = emptyFacets.Revision;
        await fixture.Store.RemoveSourceAsync(second.Id, otherOwner, default);
        Check((await fixture.Store.QueryAsync(new(new() { SourceId = second.Id }), default)).TotalCount == 0,
            "Disconnected source selection broadened the query");
        Check((await fixture.Store.FoldersAsync(new(new()), default)).Items.All(item => item.SourceId != second.Id) && File.Exists(Path.Combine(otherPath, "capture.png")),
            "Disconnect retained facet or removed original");
        await Reject(() => fixture.Store.FoldersAsync(new(new(), oldRevision), default), "capture_library_query_changed");
        await Reject(() => fixture.Store.QueryAsync(new(new() { SourceId = "../provider" }), default), "invalid_payload");

        var host = new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.Folders,
            async (request, token) => await fixture.Store.FoldersAsync(request, token)).Build();
        Check((await host.CaptureLibrary.FoldersAsync(new())).Items.Count == 2, "SDK facet response rejected valid folders");
    }
}
