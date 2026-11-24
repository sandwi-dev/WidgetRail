using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal sealed class BackgroundSurfaceBridgeFixtureWidget : Widget
{
    internal const string ArtworkHandle = "background-surface-test.artwork";
    internal const string FirstFocusArtworkHandle = "background-surface-test.focus.first";
    internal const string SecondFocusArtworkHandle = "background-surface-test.focus.second";
    internal const string MissingArtworkHandle = "background-surface-test.missing";
    internal static ReadOnlyMemory<byte> ArtworkBytes { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+Xh8ftQAAAABJRU5ErkJggg==");

    public override WidgetView Render() => new(
        UI.BackgroundSurface(
                UI.Stack(
                        "background-surface-test.foreground",
                        UI.Artwork(new(MissingArtworkHandle), "background-surface-test.missing-image", "Unavailable preview"),
                        UI.Text("BACKGROUND SURFACE", "background-surface-test.eyebrow")
                            .Classes("background-surface-test-text"),
                        UI.Text("Generic artwork isolation", "background-surface-test.title")
                            .Classes("background-surface-test-text"),
                        UI.Row(
                            "background-surface-test.actions",
                            UI.Button(
                                    "First background",
                                    "background-surface-test.first",
                                    "background-surface-test.first")
                                .FocusBackground(new WidgetArtworkHandle(FirstFocusArtworkHandle)),
                            UI.Button(
                                    "Second background",
                                    "background-surface-test.second",
                                    "background-surface-test.second")
                                .FocusBackground(new WidgetArtworkHandle(SecondFocusArtworkHandle))))
                    .Classes("background-surface-test-foreground"),
                "background-surface-test.root",
                BackgroundSurfaceArtwork.FromHandle(
                    new WidgetArtworkHandle(ArtworkHandle), ImageFit.Cover))
            .UseFocusedDescendantArtwork()
            .Classes("background-surface-test-root"),
        InitialFocusId: "background-surface-test.first",
        ActiveInputScopeId: "background-surface-test.root",
        Surface: new WidgetSurfaceHints
        {
            Mode = WidgetSurfaceMode.Standard,
            Appearance = WidgetSurfaceAppearance.Transparent,
            PreferredWidth = 640,
            PreferredHeight = 420,
            MinimumWidth = 320,
            MinimumHeight = 240,
        });

    public override ValueTask<WidgetArtworkSource?> OnResolveArtworkSourceAsync(
        WidgetArtworkHandle handle, CancellationToken cancellationToken = default) =>
        handle.Value == MissingArtworkHandle
            ? ValueTask.FromResult<WidgetArtworkSource?>(new WidgetFileArtwork("assets/missing.png", "revision-1", packageRelative: true))
            : base.OnResolveArtworkSourceAsync(handle, cancellationToken);

    public override ValueTask<WidgetEncodedArtwork?> OnResolveArtworkAsync(
        WidgetArtworkHandle handle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<WidgetEncodedArtwork?>(
            handle.Value is ArtworkHandle or FirstFocusArtworkHandle or SecondFocusArtworkHandle
                ? new WidgetEncodedArtwork(WidgetArtworkContentType.Png, ArtworkBytes)
                : null);
    }
}
