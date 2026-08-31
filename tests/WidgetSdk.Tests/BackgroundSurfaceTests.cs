using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class BackgroundSurfaceTests
{
    private const string Png =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
        "AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";

    internal static Task Run()
    {
        var foreground = UI.Stack("surface.content",
            UI.Button("Open", "open", "surface.open"));
        var snapshot = new WidgetView(
            UI.BackgroundSurface(
                foreground,
                "surface",
                BackgroundSurfaceArtwork.FromInlinePng(Png, ImageFit.Cover)),
            InitialFocusId: "surface.open").CreateSnapshot("surface.instance", 1);

        Equal(ProtocolConstants.BackgroundSurfaceVersion, snapshot.ProtocolVersion);
        Equal(ViewNodeKind.BackgroundSurface, snapshot.Root.Kind);
        Equal(ImageFit.Cover, snapshot.Root.ImageFit);
        Equal("surface.content", snapshot.Root.Children.Single().Id);
        True(!snapshot.Root.IsFocusable, "The paint-only surface gained focus authority.");
        Equal("surface.open", snapshot.InitialFocusId);

        var fallback = new WidgetView(
            UI.BackgroundSurface(foreground, "fallback"),
            InitialFocusId: "surface.open").CreateSnapshot("fallback.instance", 1);
        Equal(ProtocolConstants.BackgroundSurfaceVersion, fallback.ProtocolVersion);
        True(fallback.Root.ImageSource is null && fallback.Root.ArtworkHandle is null &&
             fallback.Root.ImageFit is null,
            "The no-artwork fallback must preserve identical foreground geometry.");

        var invalid = snapshot with
        {
            Root = snapshot.Root with { Children = [.. snapshot.Root.Children, snapshot.Root.Children[0]] },
        };
        True(ViewSnapshotValidator.Validate(invalid).Any(error =>
                error.Code == "background_surface_child_count"),
            "BackgroundSurface accepted more than one foreground subtree.");
        return Task.CompletedTask;
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
