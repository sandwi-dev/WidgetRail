using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.BackgroundSurfaceWidget;

/// <summary>
/// Capability-free isolation fixture for the generic BackgroundSurface resource and paint path.
/// </summary>
public sealed class BackgroundSurfaceTestWidget : Widget
{
    public const string ArtworkHandle = "background-surface-test.artwork";
    public static ReadOnlyMemory<byte> ArtworkBytes { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+Xh8ftQAAAABJRU5ErkJggg==");

    public override WidgetView Render() => new(
        UI.BackgroundSurface(
                UI.Stack(
                        "background-surface-test.foreground",
                        UI.Text("BACKGROUND SURFACE", "background-surface-test.eyebrow")
                            .Classes("background-surface-test-text"),
                        UI.Text("Generic artwork isolation", "background-surface-test.title")
                            .Classes("background-surface-test-text"),
                        UI.Button(
                            "Foreground action",
                            "background-surface-test.activate",
                            "background-surface-test.action"))
                    .Classes("background-surface-test-foreground"),
                "background-surface-test.root",
                BackgroundSurfaceArtwork.FromHandle(
                    new WidgetArtworkHandle(ArtworkHandle), ImageFit.Cover))
            .Classes("background-surface-test-root"),
        InitialFocusId: "background-surface-test.action",
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

    public override ValueTask<WidgetEncodedArtwork?> OnResolveArtworkAsync(
        WidgetArtworkHandle handle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<WidgetEncodedArtwork?>(
            string.Equals(handle.Value, ArtworkHandle, StringComparison.Ordinal)
                ? new WidgetEncodedArtwork(WidgetArtworkContentType.Png, ArtworkBytes)
                : null);
    }
}
