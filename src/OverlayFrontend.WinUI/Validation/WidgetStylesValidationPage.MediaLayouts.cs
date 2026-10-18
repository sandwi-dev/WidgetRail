using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeMediaLayoutsAsync(string directory)
    {
        Exception? failure = null;
        try { await RunNativeMediaLayoutsAsync(directory); }
        catch (Exception error) { failure = error; }
        finally { WidgetViewPresenter.SetHighContrastStyleOverride(null); }
        await File.WriteAllTextAsync(Path.Combine(directory, "native-result.json"),
            JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString(),
                themeAudit = Environment.GetCommandLineArgs().Contains("--media-theme-audit"),
                scope = "Native renderer with offline artwork and empty collection viewports. High contrast is a host style override, not an OS theme change; no service or physical controller acceptance." }));
        if (failure is not null) throw failure;
    }

    private async Task RunNativeMediaLayoutsAsync(string directory)
    {
        var files = Directory.GetFiles(directory, "*.renderer.json");
        if (files.Length != 4) throw new InvalidDataException("Provide the four main/compact music renderer fixtures.");
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(1040 * scale), (int)(840 * scale)));
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        var artwork = await ArtworkPngAsync(128, 128);
        presenter.ResolveArtworkAsync = (_, _) => Task.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png, artwork));
        presenter.ResolvePackageIconAsync = (icon, _) =>
        {
            var filename = icon.AssetId switch {
                "spotify.saved.outline" => "heart-outline.svg", "spotify.saved.filled" => "heart-filled.svg",
                "spotify.brand.full-green" => "spotify-full-green.svg", _ => null };
            if (filename is null) throw new IOException("No local icon fixture");
            var bytes = File.ReadAllBytes(Path.Combine(directory, filename));
            return Task.FromResult(new WidgetPresentationPackageIcon(icon.AssetId,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes));
        };
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var themeAudit = Environment.GetCommandLineArgs().Contains("--media-theme-audit");
        var themes = themeAudit ? new[] { "original", "light", "high-contrast-large" } : new[] { "original" };
        var catalog = themeAudit ? new ThemeCatalog(new PlatformSettingsPaths(directory)) : null;
        var baselineFonts = new Dictionary<string, (string Id, double Size)>(StringComparer.Ordinal);
        foreach (var themeCase in themes)
        {
            var highContrast = themeCase == "high-contrast-large";
            WidgetViewPresenter.SetHighContrastStyleOverride(highContrast);
            presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced, TextScale = highContrast ? 1.25 : 1 }, false);
            foreach (var path in files.Order(StringComparer.Ordinal))
            {
                using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
                var snapshot = SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText()));
                // Preserve every authored layout/style; substitute only the external
                // image transport with a deterministic decoded fixture image.
                snapshot = snapshot with { Root = OfflineImages(snapshot.Root) };
                var originalStyles = document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
                var fixtureName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
                var name = themeCase == "original" ? fixtureName : fixtureName + "-" + themeCase;
                IReadOnlyDictionary<string, BridgeNodeRenderStyles> styles = originalStyles;
                if (themeCase != "original")
                {
                    // Compile the real package, platform defaults and selected user theme.
                    // Recoloring the page behind precomputed styles would not test the widget theme.
                    var entry = fixtureName.StartsWith("Spotify-", StringComparison.Ordinal) ? "spotify.wrss" : "ytmusic.wrss";
                    var package = WrssPackageLoader.LoadFile(Path.Combine(directory, entry), directory);
                    var compiled = ThemeLayerCompiler.Compile(catalog!.BuiltInDefault.Package, package,
                        catalog.Load(highContrast ? ThemeIdentity.BuiltInMonoInk : ThemeIdentity.BuiltInMineralLight, "1.0.0").Package);
                    if (!compiled.IsValid) throw new InvalidDataException(string.Join("; ", compiled.Diagnostics));
                    var resolved = new Dictionary<string, BridgeNodeRenderStyles>(StringComparer.Ordinal);
                    void Visit(ViewNode node, string prefix = "")
                    {
                        resolved[prefix + node.Id] = Compute(compiled.Theme!, node);
                        foreach (var child in node.Children) Visit(child, prefix);
                        if (node.FocusPresentation is { } fragment) Visit(fragment, prefix);
                        if (node.DefaultFocusPresentation is { } fallback) Visit(fallback, prefix);
                    }
                    Visit(snapshot.Root);
                    foreach (var layout in snapshot.PinnedLayouts)
                        if (layout.Root is { } pinnedRoot) Visit(pinnedRoot, layout.Id + "/");
                    styles = resolved;
                    Check(Walk(snapshot.Root).All(node => styles.ContainsKey(node.Id)), name + " resolves real layered styles for every rendered node");
                    if (!highContrast) Check(
                        JsonSerializer.Serialize(styles[snapshot.Root.Id]) != JsonSerializer.Serialize(originalStyles[snapshot.Root.Id]),
                        name + " applies a different widget-root palette rather than reusing default render styles");
                }
                var descriptor = new BridgeWidgetDescriptor { Id = name, Name = name, InstanceId = snapshot.WidgetInstanceId,
                    RuntimeGeneration = name, PresentationGeneration = name, Icon = WidgetGlyph.Music, PackageContentDigest = "" };
                var frame = new WidgetPresentationFrame(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                    descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles);
                presenter.Width = snapshot.Surface!.PreferredWidth!.Value;
                presenter.Height = snapshot.Surface.PreferredHeight!.Value;
                presenter.Apply(frame);
                var artId = Walk(snapshot.Root).First(node => node.Kind == ViewNodeKind.Image && node.Id.Contains("artwork", StringComparison.Ordinal)).Id;
                await Wait(() => Find<WidgetArtworkView>("Widget." + artId) is { Source: BitmapSource { PixelWidth: > 0 } });
                presenter.UpdateLayout();
                if (themeCase == "original")
                {
                    var text = Walk(snapshot.Root).Where(node => node.Kind == ViewNodeKind.Text)
                        .Select(node => (node.Id, Element: Find<TextBlock>("Widget." + node.Id)))
                        .FirstOrDefault(item => item.Element is { ActualHeight: > 0, FontSize: > 0 });
                    if (text.Element is not null) baselineFonts[fixtureName] = (text.Id, text.Element.FontSize);
                }
                if (highContrast && baselineFonts.TryGetValue(fixtureName, out var baseline))
                    Check(Find<TextBlock>("Widget." + baseline.Id) is { } enlarged && enlarged.FontSize >= baseline.Size * 1.24,
                        name + " applies enlarged text to native content rather than only recording the setting");
                Check(Descendants(presenter).OfType<ScrollViewer>().All(scroll => scroll.ExtentWidth <= scroll.ViewportWidth + 1),
                    name + " avoids horizontal scrolling at its authored viewport and text scale");
                var art = Find<WidgetArtworkView>("Widget." + artId)!;
                var authoredWidth = styles[artId].Base.GetValueOrDefault("width")?.Number ?? 96;
                var authoredHeight = styles[artId].Base.GetValueOrDefault("height")?.Number ?? 96;
                Check(art.ActualWidth >= authoredWidth - 1 && art.ActualHeight >= authoredHeight - 1,
                    name + " paints decoded player artwork at its authored size");
                foreach (var height in new[] { presenter.Height, snapshot.Surface.MinimumHeight ?? presenter.Height }.Distinct())
                {
                    presenter.Height = height;
                    presenter.UpdateLayout();
                    foreach (var node in Walk(snapshot.Root).Where(node => node.Kind == ViewNodeKind.Button))
                    {
                        if (Find<Button>("Widget." + node.Id) is not { IsLoaded: true, Visibility: Visibility.Visible } button || button.ActualWidth == 0) continue;
                        var bounds = button.TransformToVisual(presenter).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                        var inScroll = false;
                        for (DependencyObject? parent = VisualTreeHelper.GetParent(button); parent is not null && !ReferenceEquals(parent, presenter); parent = VisualTreeHelper.GetParent(parent))
                            if (parent is ScrollViewer) { inScroll = true; break; }
                        if (!inScroll) Check(bounds.Top >= -1 && bounds.Bottom <= presenter.ActualHeight + 1,
                            name + " keeps " + node.Id + " inside the vertical viewport at height " + height);
                        Check(bounds.Left >= -1 && bounds.Right <= presenter.ActualWidth + 1,
                            name + " keeps " + node.Id + " inside the horizontal viewport at height " + height);
                    }
                    foreach (var node in Walk(snapshot.Root).Where(node => node.Kind == ViewNodeKind.Button &&
                        (node.Id.EndsWith(".play-toggle", StringComparison.Ordinal) || node.Id.EndsWith(".toggle", StringComparison.Ordinal))))
                    {
                        var button = Find<Button>("Widget." + node.Id);
                        if (button is not { IsEnabled: true, IsLoaded: true }) continue;
                        Check(button.Focus(FocusState.Keyboard), name + " accepts focus on transport at height " + height);
                        // Match controller navigation's explicit bring-into-view step.
                        // Direct native Focus alone does not exercise that host path.
                        button.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                        await Wait(() =>
                        {
                            var current = button.TransformToVisual(presenter).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                            return current.Top >= -1 && current.Bottom <= presenter.ActualHeight + 1;
                        });
                        var bounds = button.TransformToVisual(presenter).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                        Check(bounds.Top >= -1 && bounds.Bottom <= presenter.ActualHeight + 1,
                            name + " reveals focused transport within the viewport at height " + height);
                    }
                }
                presenter.Height = snapshot.Surface.PreferredHeight.Value;
                presenter.UpdateLayout();
                var saveNode = Walk(snapshot.Root).FirstOrDefault(node => node.Id.EndsWith(".saved", StringComparison.Ordinal) || node.Id.EndsWith(".like", StringComparison.Ordinal));
                if (saveNode is not null && Find<Button>("Widget." + saveNode.Id) is { } save)
                {
                    save.Focus(FocusState.Keyboard);
                    save.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                    foreach (var busy in new[] { true, false })
                    {
                        var next = snapshot with { Sequence = snapshot.Sequence + (busy ? 1 : 2), Root = BusyNode(snapshot.Root) };
                        presenter.Apply(frame with { Authority = frame.Authority with { SnapshotSequence = next.Sequence }, Snapshot = next });
                        presenter.UpdateLayout();
                        Check(ReferenceEquals(save, Find<Button>("Widget." + saveNode.Id)) && save.IsEnabled && save.IsTabStop &&
                            ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), save),
                            name + " preserves the same focused save control when busy=" + busy);
                        ViewNode BusyNode(ViewNode node) => node with { IsBusy = node.Id == saveNode.Id ? busy : node.IsBusy,
                            Children = node.Children.Select(BusyNode).ToArray() };
                    }
                }
                await Task.Delay(80);
                presenter.WriteLayoutDiagnostics(Path.Combine(directory, name + ".layout.json"));
                // Keep the original four captures; one main player per additional theme
                // supplies representative visual inspection without eight redundant images.
                if (themeCase != "original" && fixtureName != "Spotify-Player") continue;
                var raster = new RenderTargetBitmap();
                await raster.RenderAsync(presenter);
                var output = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
                var folder = await output.GetParentAsync();
                var png = await folder.CreateFileAsync(name + ".png", CreationCollisionOption.ReplaceExisting);
                using var stream = await png.OpenAsync(FileAccessMode.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)raster.PixelWidth, (uint)raster.PixelHeight,
                    96, 96, (await raster.GetPixelsAsync()).ToArray());
                await encoder.FlushAsync();
            }
        }
        static IEnumerable<ViewNode> Walk(ViewNode node)
        { yield return node; foreach (var child in node.Children) foreach (var item in Walk(child)) yield return item; }
        static ViewNode OfflineImages(ViewNode node)
        {
            // Collection contents are independent of player/chrome geometry.
            // Preserve the viewport's style/visibility without its worker lease.
            if (node.Kind == ViewNodeKind.IndexedCollection)
                return new() { Id = node.Id, Kind = ViewNodeKind.Scroll, ScrollAxis = WidgetProtocol.ScrollAxis.Vertical,
                    StyleClasses = node.StyleClasses, InputScopeId = node.InputScopeId, VisibleWhen = node.VisibleWhen };
            return node with { Children = node.Children.Select(OfflineImages).ToArray(),
                ImageSource = node.Kind == ViewNodeKind.Image ? null : node.ImageSource,
                ArtworkHandle = node.Kind == ViewNodeKind.Image ? "fixture-artwork" : node.ArtworkHandle };
        }
    }
}
