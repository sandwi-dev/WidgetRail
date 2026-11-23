using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class AnimationPresentationFixture
{
    internal static async Task ServeAsync(string pipe, bool readingGroup = false, bool inlineVideo = false)
    {
        var widget = new ConfiguredWidget { Id = "animation-fixture", Name = "Animation fixture", InstanceId = inlineVideo ? "inline-video-fixture.instance" : readingGroup ? "animation-reading-fixture.instance" : "animation-fixture.instance",
            PackageId = "dev.animation", PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new('a', 64), CatalogFingerprint = new('a', 64) };
        await using var server = new WidgetBridgeServer(pipe, new([widget]));
        await server.RunAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }
    internal sealed class Probe : Widget
    {
        private readonly WidgetIndexedCollection<string, int> items;
        internal Probe(bool readingGroup = false, bool inlineVideo = false) => items = CreateIndexedCollection<string, int>("animation-source", "fixed", inlineVideo?48:1, new()
        {
            ItemKey = index => new(index==0?"gif":"message-"+index),
            ReadRange = (_, start, count, _) => ValueTask.FromResult<IReadOnlyList<int>>(Enumerable.Range(start,count).ToArray()),
            RenderItem = (_, index, ids) => index>0
                ? UI.ReadingGroup(ids.Id("row"),"Other message",ActionSurfaceOrientation.Vertical,UI.Text("Other message",ids.Id("content")))
                : inlineVideo
                ? UI.ReadingGroup("animation.row", "Video message", ActionSurfaceOrientation.Vertical,
                    UI.Text("Synthetic video", "animation.caption"),
                    UI.InlineVideo(MediaPlayerSource.WebUrl("https://example.invalid/animation.mp4"),"animation.video",options:new(Loop:true,Muted:true)),
                    UI.Button("Another action","open","animation.open"))
                : readingGroup
                ? UI.ReadingGroup("animation.row", "GIF", ActionSurfaceOrientation.Vertical,
                    UI.Text("Synthetic animation", "animation.caption"), UI.Animation("https://example.invalid/animation.mp4", "animation.video"),
                    UI.Button("View attachment", "open", "animation.open"))
                : UI.ActionSurface("open", "animation.row", "GIF", ActionSurfaceOrientation.Vertical,
                    UI.Text("Synthetic animation", "animation.caption"), UI.Animation("https://example.invalid/animation.mp4", "animation.video")),
            OnAction = (_, _, _, _) => ValueTask.CompletedTask
        });
        public override WidgetView Render() => new(UI.CollectionList("animations", items, 180, "Animations"));
    }
}
