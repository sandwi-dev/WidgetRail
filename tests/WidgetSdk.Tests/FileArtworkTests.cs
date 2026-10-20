using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class FileArtworkTests
{
    internal static async Task Contract()
    {
        foreach (var path in new[] { @"\\server\image.png", "file:///C:/image.png", @"C:\image.png:secret", "image.png" })
        {
            try { _ = new WidgetFileArtwork(path, "v1"); throw new Exception("Invalid local path accepted."); }
            catch (ArgumentException) { }
        }
        foreach (var path in new[] { "../image.png", "/image.png", "a/../../image.png", @"a\image.png" })
        {
            try { _ = new WidgetFileArtwork(path, "v1", packageRelative: true); throw new Exception("Invalid package path accepted."); }
            catch (ArgumentException) { }
        }
        var package = new WidgetFileArtwork("assets/image.png", "v1", packageRelative: true);
        Check(package.PackageRelative);
        using var widget = new Fixture();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "files.instance");
        using var lease = await host.AcquireAsync("rows", 0, 1);
        var source = await lease.ResolveArtworkSourceAsync("one", new("cover"));
        Check(source is WidgetFileArtwork { Path: @"C:\images\cover.png", Revision: "v1" });
        Check(await lease.ResolveArtworkAsync("one", new("cover")) is WidgetEncodedArtwork);
        Check(await lease.ResolveArtworkSourceAsync("one", new("forged")) is null);
        lease.Dispose();
        Check(await lease.ResolveArtworkSourceAsync("one", new("cover")) is null);
        await WidgetTestHost.DestroyAsync(widget);
        using var fileOnly = new Fixture(fileOnly: true);
        await WidgetTestHost.InitializeAsync(fileOnly);
        await WidgetTestHost.SetLifecycleStateAsync(fileOnly, WidgetLifecycleState.Interactive);
        using var fileHost = WidgetTestHost.CreateIndexedCollectionHost(fileOnly, "file-only.instance");
        using var fileLease = await fileHost.AcquireAsync("rows", 0, 1);
        Check(await fileLease.ResolveArtworkSourceAsync("one", new("cover")) is WidgetFileArtwork);
        Check(await fileLease.ResolveArtworkAsync("one", new("cover")) is null);
        await WidgetTestHost.DestroyAsync(fileOnly);
    }

    private static void Check(bool value) { if (!value) throw new Exception("File artwork contract failed."); }
    private sealed class Fixture : Widget, IDisposable
    {
        private readonly WidgetIndexedCollection<string, string> source;
        internal Fixture(bool fileOnly = false) => source = CreateIndexedCollection<string, string>("source", "query", 1, new()
        {
            ReadRange = (_, _, _, _) => ValueTask.FromResult<IReadOnlyList<string>>(["one"]),
            ItemKey = item => new(item),
            RenderItem = (_, _, _) => UI.ActionSurface("open", "item", "Item", ActionSurfaceOrientation.Vertical,
                UI.Artwork(new("cover"), "image", "Cover")),
            OnAction = (_, _, _, _) => ValueTask.CompletedTask,
            ResolveArtwork = fileOnly ? null : (_, _, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png,
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNgYGBgAAAABQABeqhXUAAAAABJRU5ErkJggg=="))),
            ResolveArtworkSource = (_, _, _, _) => ValueTask.FromResult<WidgetArtworkSource?>(new WidgetFileArtwork(@"C:\images\cover.png", "v1")),
        });
        public override WidgetView Render() => new(UI.CollectionGrid("rows", source, 100, 100, "Images"));
        public void Dispose() { }
    }
}
