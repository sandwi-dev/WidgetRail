using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeMediaQueuesAsync(string directory)
    {
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(1040 * scale), (int)(840 * scale)));
        var artwork = await ArtworkPngAsync(128, 128);
        presenter.ResolveArtworkAsync = (_, _) => Task.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png, artwork));
        presenter.ResolvePackageIconAsync = (icon, _) =>
        {
            var filename = icon.AssetId switch { "spotify.saved.outline" => "heart-outline.svg",
                "spotify.saved.filled" => "heart-filled.svg", "spotify.brand.full-green" => "spotify-full-green.svg", _ => null };
            if (filename is null) throw new IOException("No local icon fixture");
            var bytes = File.ReadAllBytes(Path.Combine(directory, filename));
            return Task.FromResult(new WidgetRail.WidgetPresentationSession.WidgetPresentationPackageIcon(icon.AssetId,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes));
        };
        var popup = new NativePopupTheme(); popup.Attach(presenter);
        foreach (var service in new[] { "Spotify", "YouTubeMusic" })
        {
            using var input = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory, service + ".queue.json")));
            var source = SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(input.RootElement.GetProperty("snapshot").GetRawText()));
            var collectionId = input.RootElement.GetProperty("collectionId").GetString()!;
            var rows = input.RootElement.GetProperty("rows").Deserialize<IndexedCollectionItem[]>(json)!;
            var currentInRows = input.RootElement.GetProperty("currentInRows").GetBoolean();
            Check(rows.Length == 16 && rows.Select(row => row.Key).Distinct().Count() == rows.Length,
                service + ": bounded export retains every duplicate occurrence");
            Check(rows[7].Key != rows[12].Key && rows[7].Root.Id != rows[12].Root.Id,
                service + ": duplicate songs retain separate native controls");
            Check(rows.Count(row => row.Root.IsSelected == true) == (currentInRows ? 1 : 0),
                service + ": current playback remains in its actual widget location");
            var package = WrssPackageLoader.LoadFile(Path.Combine(directory, service == "Spotify" ? "spotify.wrss" : "ytmusic.wrss"), directory);
            foreach (var themeId in new[] { ThemeIdentity.BuiltInMonoInk, ThemeIdentity.BuiltInMineralLight, "high-contrast" })
            foreach (var size in new[] { (Width: 840d, Height: 580d), (Width: 620d, Height: 400d) })
            {
                var contrast = themeId == "high-contrast";
                WidgetViewPresenter.SetHighContrastStyleOverride(contrast);
                var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Reduced,
                    Contrast = contrast ? ContrastPreference.High : AppearanceSettings.Default.Contrast, TextScale = contrast ? 1.25 : 1 };
                presenter.ApplyAppearance(appearance, false);
                presenter.Width = size.Width; presenter.Height = size.Height;
                var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, package,
                    catalog.Load(contrast ? ThemeIdentity.BuiltInMonoInk : themeId, "1.0.0").Package);
                if (!theme.IsValid) throw new InvalidDataException(string.Join("; ", theme.Diagnostics));
                ViewNode Materialize(ViewNode node)
                {
                    if (node.Kind == ViewNodeKind.IndexedCollection)
                        return new() { Id = node.Id, Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
                            StyleClasses = node.StyleClasses, InputScopeId = node.InputScopeId, VisibleWhen = node.VisibleWhen,
                            Children = node.Id == collectionId ? rows.Select(row => Materialize(row.Root)).ToArray() : [] };
                    return node with { Children = node.Children.Select(Materialize).ToArray(),
                        ImageSource = node.Kind == ViewNodeKind.Image ? null : node.ImageSource,
                        ArtworkHandle = node.Kind == ViewNodeKind.Image ? "fixture-artwork" : node.ArtworkHandle };
                }
                var snapshot = source with { Root = Materialize(source.Root), Sequence = ++sequence,
                    PinnedLayouts = [], FocusGroupEntryRequest = null, ScrollRevealRequest = null, InitialFocusId = rows[0].Root.Id };
                var styles = Nodes(snapshot.Root).ToDictionary(node => node.Id, node => Compute(theme.Theme!, node));
                foreach (var role in new[] { "body", "canvas", "panel", "hint", "tray-item" }) styles[role] = Compute(theme.Theme!, role, role, []);
                popup.Update(styles, appearance);
                var name = service + "-" + themeId + "-" + size.Width;
                var descriptor = new BridgeWidgetDescriptor { Id = "music-queue", Name = name, InstanceId = snapshot.WidgetInstanceId,
                    RuntimeGeneration = name, PresentationGeneration = name, Icon = WidgetGlyph.Music, PackageContentDigest = "" };
                var dispatched = new List<(string Action, string Source)>();
                presenter.DispatchActionAsync = request => { dispatched.Add((request.Action.ActionId, request.Action.SourceElementId)); return Task.CompletedTask; };
                presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                    snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                await Wait(() => Find<Button>("Widget." + rows[0].Root.Id) is { IsLoaded: true });
                presenter.UpdateLayout();
                Check(Descendants(presenter).OfType<ScrollViewer>().All(scroll => scroll.ExtentWidth <= scroll.ViewportWidth + 1),
                    name + ": populated queue has no horizontal overflow");
                var first = Find<Button>("Widget." + rows[0].Root.Id)!;
                Check(first.Focus(FocusState.Keyboard), name + ": first occurrence accepts native focus");
                await Reveal(first);
                // Current/long/duplicate and last rows must all be reachable without
                // invoking playback. This is geometry, not indexed reveal authority.
                foreach (var index in new[] { 2, 6, 7, 12, 15 })
                {
                    var row = Find<Button>("Widget." + rows[index].Root.Id)!;
                    row.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                    await Wait(() => Visible(row));
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first) && dispatched.Count == 0,
                        name + ": scrolling occurrence " + index + " leaves playback and focus unchanged");
                    Check(Nodes(rows[index].Root).Where(node => node.Kind == ViewNodeKind.Text).All(node =>
                    {
                        if (Find<TextBlock>("Widget." + node.Id) is not { ActualHeight: > 0, ActualWidth: > 0 } text) return false;
                        var bounds = text.TransformToVisual(row).TransformBounds(new(0, 0, text.ActualWidth, text.ActualHeight));
                        return bounds.Left >= -1 && bounds.Right <= row.ActualWidth + 1 && bounds.Top >= -1 && bounds.Bottom <= row.ActualHeight + 1;
                    }),
                        name + ": occurrence " + index + " keeps title and metadata visible");
                }
                var current = rows.SingleOrDefault(row => row.Root.IsSelected == true);
                if (current is not null) Check(Find<Button>("Widget." + current.Root.Id) is not null,
                    name + ": selected playing occurrence remains in the list");
                var owner = rows[7].Root;
                var button = Find<Button>("Widget." + owner.Id)!;
                Check(button.Focus(FocusState.Keyboard), name + ": duplicate occurrence accepts context focus");
                await Reveal(button);
                CheckIndicator(button, name + ": before menu");
                var action = owner.ContextActions.First(item => !item.IsDisabled && !item.IsBusy);
                Check(owner.ContextActions.Any(item => item.Label.Contains(service == "Spotify" ? "liked songs" : "Remove from queue", StringComparison.OrdinalIgnoreCase)),
                    name + ": expected service-supported context actions are present");
                await presenter.HandleControllerButtonAsync(ControllerButton.Menu);
                await presenter.HandleControllerButtonAsync(ControllerButton.Menu, ControllerEventPhase.Released);
                await Wait(() => presenter.HasTransientControl && FocusManager.GetFocusedElement(XamlRoot) is MenuFlyoutItem);
                Check(dispatched.Count == 0, name + ": opening the occurrence menu does not select or play");
                await presenter.HandleControllerButtonAsync(ControllerButton.B);
                await presenter.HandleControllerButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Wait(() => !presenter.HasTransientControl && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), button));
                Check(true, name + ": menu cancellation restores the exact duplicate occurrence");
                await presenter.HandleControllerButtonAsync(ControllerButton.Menu);
                await presenter.HandleControllerButtonAsync(ControllerButton.Menu, ControllerEventPhase.Released);
                await Wait(() => presenter.HasTransientControl && FocusManager.GetFocusedElement(XamlRoot) is MenuFlyoutItem);
                await presenter.HandleControllerButtonAsync(ControllerButton.A);
                await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Wait(() => dispatched.Count == 1 && !presenter.HasTransientControl);
                Check(dispatched[0] == (action.ActionId, owner.Id) && action.ActionId != owner.ActionId,
                    name + ": context activation dispatches the selected occurrence action, not playback");
                await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), button));
                await Wait(() => Descendants(button).OfType<WidgetContextIndicator>().Single().IsShown);
                CheckIndicator(button, name + ": after menu");
                if (size.Width == 620 && themeId != "high-contrast")
                    await CaptureQueue(Path.Combine(directory, name + ".png"));
            }
        }
        static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        void CheckIndicator(Button owner, string name)
        {
            var indicator = Descendants(owner).OfType<WidgetContextIndicator>().Single();
            var badge = Descendants(indicator).OfType<Border>().Single();
            Check(indicator.IsShown && !string.IsNullOrWhiteSpace(indicator.Glyph.Glyph), name + " options glyph is visible");
            var ink = ((SolidColorBrush)indicator.Glyph.Foreground).Color;
            var fill = ((SolidColorBrush)badge.Background).Color;
            static double Luminance(Windows.UI.Color color)
            {
                static double Channel(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
                return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
            }
            Check((Math.Max(Luminance(ink), Luminance(fill)) + .05) / (Math.Min(Luminance(ink), Luminance(fill)) + .05) >= 3,
                name + " options glyph has at least 3:1 contrast against its badge");
        }
        async Task Reveal(FrameworkElement element)
        { element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false }); await Wait(() => Visible(element)); }
        bool Visible(FrameworkElement element)
        {
            var bounds = element.TransformToVisual(presenter).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
            if (bounds.Top < -1 || bounds.Bottom > presenter.ActualHeight + 1 || bounds.Left < -1 || bounds.Right > presenter.ActualWidth + 1) return false;
            for (DependencyObject? parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer scroll)
                {
                    var local = element.TransformToVisual(scroll).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
                    var viewport = WidgetScrollViewport.ContentBounds(scroll);
                    if (local.Top < viewport.Top - 1 || local.Bottom > viewport.Bottom + 1 || local.Left < viewport.Left - 1 || local.Right > viewport.Right + 1) return false;
                }
            return true;
        }
        async Task CaptureQueue(string path)
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(presenter);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync(); stream.Seek(0);
            using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); await File.WriteAllBytesAsync(path, bytes);
        }
    }
}
