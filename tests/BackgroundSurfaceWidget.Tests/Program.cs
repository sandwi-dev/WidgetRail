using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.Samples.BackgroundSurfaceWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

var widget = new BackgroundSurfaceTestWidget();
var snapshot = widget.RenderSnapshot("background-surface-test.instance", 1);

Require(snapshot.ProtocolVersion == ProtocolConstants.BackgroundSurfaceVersion,
    "The isolation snapshot did not require the BackgroundSurface protocol version.");
Require(snapshot.Root.Kind == ViewNodeKind.BackgroundSurface,
    "The isolation snapshot root is not BackgroundSurface.");
Require(snapshot.Root.Id == "background-surface-test.root",
    "The isolation snapshot changed its root identity.");
Require(snapshot.Root.ArtworkHandle == BackgroundSurfaceTestWidget.ArtworkHandle,
    "The isolation snapshot did not publish the exact artwork handle.");
Require(snapshot.Root.ImageFit == ImageFit.Cover,
    "The isolation snapshot did not publish Cover fitting.");
Require(snapshot.Root.Children is [{ Id: "background-surface-test.foreground" }],
    "BackgroundSurface did not retain exactly one foreground subtree.");
Require(snapshot.InitialFocusId == "background-surface-test.action" &&
        snapshot.ActiveInputScopeId == "background-surface-test.root",
    "The semantic foreground lost its exact input authority.");
Require(snapshot.Surface?.Appearance == WidgetSurfaceAppearance.Transparent,
    "The isolation surface must not cover its own background artwork.");

var artwork = await widget.OnResolveArtworkAsync(
    new WidgetArtworkHandle(BackgroundSurfaceTestWidget.ArtworkHandle));
Require(artwork is not null && artwork.ContentType == WidgetArtworkContentType.Png,
    "The exact resource request did not resolve to deterministic PNG artwork.");
Require(artwork!.Bytes.Span.SequenceEqual(BackgroundSurfaceTestWidget.ArtworkBytes.Span),
    "The exact resource request changed the deterministic source bytes.");
Require(Convert.ToHexString(SHA256.HashData(artwork.Bytes.Span)) ==
        "0D2B241678375D0F94272168622ECB4CB8243E576F757EAC1863406955BC26A5",
    "The deterministic artwork byte hash changed.");
Require(await widget.OnResolveArtworkAsync(new WidgetArtworkHandle("unknown")) is null,
    "An unknown resource handle resolved unexpectedly.");

var manifest = ManifestJson.Deserialize(
    File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "manifest.json")));
Require(WidgetManifestValidator.Validate(manifest).Count == 0,
    "The physical-test package manifest is invalid.");
Require(manifest.Id == "widgetrail.tests.background-surface" &&
        manifest.Entrypoint.Assembly == "payload/BackgroundSurfaceWidget.dll" &&
        manifest.Entrypoint.Type == typeof(BackgroundSurfaceTestWidget).FullName,
    "The physical-test package does not target the isolation widget.");
var style = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "styles", "default.wrss"));
Require(style.Contains("object-fit: cover", StringComparison.Ordinal) &&
        style.Contains("background: transparent", StringComparison.Ordinal),
    "The physical-test style does not preserve the visible full-surface artwork path.");
Require(File.Exists(Path.Combine(AppContext.BaseDirectory, "sample", "Build-CommunityPackage.ps1")),
    "The physical-test package build path was not published with the test.");

Console.WriteLine("PASS minimal BackgroundSurface snapshot and exact artwork request");
Console.WriteLine("BackgroundSurface widget tests: 1/1 passed.");
return 0;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
