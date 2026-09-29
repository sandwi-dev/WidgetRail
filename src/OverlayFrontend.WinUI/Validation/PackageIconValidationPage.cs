using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PackageIconValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Package icon checks pending" };
    private readonly List<string> checks = [];
    private readonly List<string> diagnostics = [];
    private readonly Dictionary<string, int> requests = [];
    private readonly TaskCompletionSource<WidgetPresentationPackageIcon> late = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long sequence;
    private string asset = "red";
    private string tintAsset = "red";
    private bool removed;
    private Task? running;
    private IDisposable? catalogPreview;
    public PackageIconValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "PackageIcon.Status");
        Content = new StackPanel { Spacing = 12, Children = { status, presenter } };
        presenter.PackageIconDiagnostic = code => diagnostics.Add(code);
        presenter.ResolvePackageIconAsync = async (id, _) =>
        {
            requests[id] = requests.GetValueOrDefault(id) + 1;
            if (id == "late") return await late.Task;
            if (id == "missing") throw new InvalidDataException();
            if (id == "broken") return new(id, "fixture", Encoding.UTF8.GetBytes("invalid SVG"));
            return Bytes(id);
        };
        Loaded += (_, _) => running ??= Environment.GetCommandLineArgs().Contains("--validate-icon-shutdown") ? RunShutdownAsync() : RunAsync();
    }
    private static WidgetPresentationPackageIcon Bytes(string id)
    {
        var opacity = id == "alpha" ? " opacity=\"0.5\"" : string.Empty;
        var bytes = Encoding.UTF8.GetBytes($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\"><rect x=\"1\" y=\"1\" width=\"18\" height=\"18\" fill=\"{(id == "red" ? "#e73932" : "#269ced")}\"{opacity}/><circle cx=\"10\" cy=\"10\" r=\"5\" fill=\"#ffffff\"{opacity}/></svg>");
        return new(id, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), bytes);
    }
    private async Task RunAsync()
    {
        try
        {
            Apply();
            await Until(() => Find("Widget.icon") is WidgetPackageIconView { Content: ImageIcon { Source: SvgImageSource } });
            var icon = (WidgetPackageIconView)Find("Widget.icon")!;
            var original = (ImageIcon)icon.Content;
            Check(original.ActualWidth > 0 || original.Width > 0, "native SVG icon has bounded size");
            Check(AutomationProperties.GetName(icon) == "Album", "native package icon retains authored accessible name");
            await Until(() => Find("Widget.tint") is WidgetPackageIconView { Content: ImageIcon native } && NativePackageIconTint.For(native) is not null);
            var tintView = (WidgetPackageIconView)Find("Widget.tint")!;
            var tintedIcon = (ImageIcon)tintView.Content;
            var tinted = NativePackageIconTint.For(tintedIcon)!;
            var cache = NativePackageIconTintCache.For(presenter.XamlRoot);
            var preparations = cache.PreparationCount;
            Check(tinted.CurrentColor.A > 0, "ThemeTint publishes native alpha mask with inherited foreground");
            var brush = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 40, 240, 80));
            tintView.Foreground = brush;
            await Until(() => tinted.CurrentColor == brush.Color);
            Check(tinted.CurrentColor == brush.Color, "theme brush replacement updates compositor tint");
            brush.Color = Windows.UI.Color.FromArgb(128, 20, 80, 240); brush.Opacity = .5;
            Check(tinted.CurrentColor.A == 64 && tinted.CurrentColor.B == 240, "brush color alpha and opacity update without modifying SVG alpha");
            Check(cache.PreparationCount == preparations && diagnostics.Count == 0, "foreground changes reuse bounded native raster cache");
            tintView.ClearValue(Control.ForegroundProperty);
            var root = (FrameworkElement)XamlRoot.Content;
            var previousTheme = root.RequestedTheme;
            root.RequestedTheme = ElementTheme.Light;
            await Until(() => tinted.CurrentColor.R < 128);
            root.RequestedTheme = ElementTheme.Dark;
            await Until(() => tinted.CurrentColor.R > 128);
            Check(cache.PreparationCount == preparations, "Light and Dark themes change native tint without rerasterizing");
            root.RequestedTheme = previousTheme;
            var button = (Button)Find("Widget.button")!;
            await Until(() => Descendants(button).OfType<ImageIcon>().Any());
            Check(Descendants(button).OfType<TextBlock>().Any(text => text.Text == "Play") && button.IsTabStop,
                "button keeps native activation label and package icon together");
            var count = requests.GetValueOrDefault("red");
            Apply();
            Check(ReferenceEquals(original, icon.Content) && requests.GetValueOrDefault("red") == count,
                "unrelated publication retains decoded icon and demand");
            var picker = (Button)Find("Widget.picker")!; picker.Focus(FocusState.Keyboard); presenter.ActivateFocused();
            await Until(() => presenter.HasTransientControl && FocusManager.GetFocusedElement(XamlRoot) is ToggleMenuFlyoutItem);
            var selected = (ToggleMenuFlyoutItem)FocusManager.GetFocusedElement(XamlRoot);
            await Until(() => selected.Icon is ImageIcon { Source: SvgImageSource });
            Check(selected.Text == "Original" && selected.IsChecked, "Select native option keeps checkmark label and declared SVG icon");
            presenter.MoveFocus(FocusNavigationDirection.Down);
            Check(FocusManager.GetFocusedElement(XamlRoot) is ToggleMenuFlyoutItem { Icon: FontIcon }, "Select semantic glyph option uses native icon");
            presenter.MoveFocus(FocusNavigationDirection.Down);
            var tintOption = (ToggleMenuFlyoutItem)FocusManager.GetFocusedElement(XamlRoot);
            await Until(() => tintOption.Icon is ImageIcon optionIcon && NativePackageIconTint.For(optionIcon) is not null);
            Check(true, "Select ThemeTint option uses the same native mask path");
            Check(cache.PreparationCount == preparations, "Select and inline icon share one mask by admitted content hash");
            presenter.DismissTransientControl();
            asset = "late"; Apply();
            Check(icon.Content is FontIcon, "replacement immediately clears prior asset to semantic fallback");
            asset = "blue"; Apply();
            await Until(() => icon.Content is ImageIcon);
            var replacement = icon.Content;
            late.SetResult(Bytes("red")); await Task.Delay(75);
            Check(ReferenceEquals(replacement, icon.Content), "late asset response cannot replace new icon identity");
            asset = "broken"; Apply(); await Until(() => diagnostics.Contains("package_icon_decode_failed") || diagnostics.Contains("package_icon_unavailable"));
            Check(icon.Content is FontIcon, "native decode failure retains semantic fallback");
            asset = "missing"; Apply(); await Until(() => diagnostics.Contains("package_icon_unavailable"));
            Check(icon.Content is FontIcon, "missing inventory or decode leaves semantic fallback without failing widget");
            byte[]? raster = null; int rasterWidth = 0, rasterHeight = 0;
            cache.RasterizedForTesting = (bytes, width, height) => { raster = bytes.ToArray(); rasterWidth = width; rasterHeight = height; };
            tintAsset = "alpha"; Apply();
            await Until(() => raster is not null && tintView.Content is ImageIcon alphaIcon && NativePackageIconTint.For(alphaIcon) is not null);
            cache.RasterizedForTesting = null;
            byte Alpha(int x, int y) => raster![(y * rasterWidth + x) * 4 + 3];
            Check(Alpha(0, 0) == 0 && Math.Abs(Alpha(rasterWidth / 10, rasterHeight / 10) - 128) <= 2 &&
                Math.Abs(Alpha(rasterWidth / 2, rasterHeight / 2) - 192) <= 2,
                "native raster preserves transparent edge half-opacity fill and overlapping alpha");
            var alphaBeforeResize = cache.PreparationCount;
            tintView.Width = tintView.Height = 160;
            await Until(() => cache.PreparationCount > alphaBeforeResize);
            Check(cache.ReservedBytes <= 8 * 1024 * 1024, "larger native icon uses bounded higher-resolution mask bucket");
            tintView.Width = tintView.Height = 64;
            var held = new List<NativePackageIconTintCache.Lease>();
            var full = false;
            try
            {
                for (var index = 0; index < 12; ++index)
                {
                    var bytes = Encoding.UTF8.GetBytes($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\"><rect width=\"20\" height=\"20\" fill=\"#{index:X6}\" /></svg>");
                    var payload = new WidgetPresentationPackageIcon("budget", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes);
                    try { held.Add(await cache.AcquireAsync(payload, 512, CancellationToken.None)); }
                    catch (InvalidOperationException error) when (error.Message.Contains("budget", StringComparison.Ordinal)) { full = true; break; }
                }
                Check(full && held.Count > 0 && cache.ReservedBytes <= 8 * 1024 * 1024,
                    "active native mask leases stop admission at aggregate memory budget");
                Check(held.All(lease => lease.Surface.DecodedPhysicalSize.Width > 0), "budget pressure never evicts active mask surfaces");
            }
            finally { foreach (var lease in held) lease.Dispose(); }
            using (await cache.AcquireAsync(Bytes("eviction"), 512, CancellationToken.None))
                Check(cache.ReservedBytes <= 8 * 1024 * 1024, "released native masks become evictable for later demand");
            removed = true; Apply();
            Check(Find("Widget.icon") is null, "removed icon retires native demand");
            if (Environment.GetCommandLineArgs().Contains("--probe-svg-mask", StringComparer.Ordinal))
            { await ProbeMaskAsync((SvgImageSource)original.Source); await ProbeRasterMaskAsync(); }
            if (Environment.GetCommandLineArgs().Contains("--probe-svg-raster", StringComparer.Ordinal))
                await PackageIconRasterProbe.RunAsync((StackPanel)Content, (SvgImageSource)original.Source);
            removed = false; asset = "red"; Apply();
            await Until(() => Find("Widget.icon") is WidgetPackageIconView { Content: ImageIcon });
            await Until(() => tintView.ActualHeight <= tintView.FontSize + 1);
            Check(true, "removing explicit icon size restores intrinsic bounds without retaining old child size");
            catalogPreview = await CatalogIconValidation.RunAsync((StackPanel)Content, Check);
            status.Text = $"Passed {checks.Count} package icon checks"; Write(new { result = "passed", checks });
        }
        catch (Exception error) { status.Text = "Package icon validation failed: " + error.Message; Write(new { result = "failed", checks, error = error.ToString() }); }
    }
    private async Task ProbeMaskAsync(SvgImageSource source)
    {
        var host = (StackPanel)Content;
        var row = new Grid { Width = 144, Height = 64, HorizontalAlignment = HorizontalAlignment.Left };
        var image = new Image { Source = source, Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left };
        var target = new Border { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(image); row.Children.Add(target); host.Children.Add(row);
        await Until(() => image.IsLoaded && image.ActualWidth > 0);
        await Task.Delay(100);
        var mask = image.GetAlphaMask();
        Check(mask is not null, "SVG GetAlphaMask returns a brush; pixel result requires separate inspection");
        var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(target).Compositor;
        var brush = compositor.CreateMaskBrush(); brush.Mask = mask; brush.Source = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 40, 240, 80));
        var visual = compositor.CreateSpriteVisual(); visual.Size = new(64, 64); visual.Brush = brush;
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(target, visual);
        await Task.Delay(200);
    }
    private async Task ProbeRasterMaskAsync()
    {
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY9CYduI/AAT2AoYAkFFrAAAAAElFTkSuQmCC");
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream)) { writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); }
        stream.Seek(0); var source = new BitmapImage(); await source.SetSourceAsync(stream);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        var image = new Image { Source = source, Width = 64, Height = 64 };
        var target = new Border { Width = 64, Height = 64 };
        var solid = new Border { Width = 64, Height = 64 };
        row.Children.Add(image); row.Children.Add(target); row.Children.Add(solid); ((StackPanel)Content).Children.Add(row);
        await Until(() => image.IsLoaded && image.ActualWidth > 0); await Task.Delay(100);
        var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(target).Compositor;
        var mask = compositor.CreateMaskBrush(); mask.Mask = image.GetAlphaMask(); mask.Source = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 40, 240, 80));
        var visual = compositor.CreateSpriteVisual(); visual.Size = new(64, 64); visual.Brush = mask;
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(target, visual);
        var direct = compositor.CreateSpriteVisual(); direct.Size = new(64, 64); direct.Brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 40, 240, 80));
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(solid, direct);
        await Task.Delay(200);
    }
    private void Apply()
    {
        ViewNode Icon(string id, string name, WidgetPackageIconColorMode mode, string key) => new() { Id = id, Kind = ViewNodeKind.Icon,
            Glyph = WidgetGlyph.Music, AccessibilityLabel = name, PackageIcon = new(key, mode) };
        var snapshot = new ViewSnapshot { WidgetInstanceId = "icons.instance", Sequence = ++sequence, ActiveInputScopeId = "page", InitialFocusId = "button",
            Root = new() { Id = "page", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
                removed ? new() { Id = "removed", Kind = ViewNodeKind.Text, Text = "Removed" } : Icon("icon", "Album", WidgetPackageIconColorMode.OriginalColor, asset),
                Icon("tint", "Tint", WidgetPackageIconColorMode.ThemeTint, tintAsset),
                new() { Id = "button", Kind = ViewNodeKind.Button, Text = "Play", ActionId = "play", Glyph = WidgetGlyph.Play, PackageIcon = new("red", WidgetPackageIconColorMode.OriginalColor) },
                new() { Id = "picker", Kind = ViewNodeKind.Select, Text = "Choose", AccessibilityLabel = "Choose", AccessibilityValue = "Original", SelectOptions = (WidgetSelectOption[])[
                    new("first", "Original", "choose", IsSelected: true, Glyph: WidgetGlyph.Music) { PackageIcon = new("red", WidgetPackageIconColorMode.OriginalColor) },
                    new("second", "Semantic", "choose.second", Glyph: WidgetGlyph.Play),
                    new("third", "Theme tint", "choose.third", Glyph: WidgetGlyph.Refresh) { PackageIcon = new("red", WidgetPackageIconColorMode.ThemeTint) }] }] } };
        var descriptor = new BridgeWidgetDescriptor { Id = "icons", Name = "Icons", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = "runtime", PresentationGeneration = "presentation", PackageContentDigest = "fixture", Icon = WidgetGlyph.Music };
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(error => error.Message)));
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, new Dictionary<string, BridgeNodeRenderStyles>()));
    }
    private FrameworkElement? Find(string id) => Descendants(presenter).OfType<FrameworkElement>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 150; ++i) { if (condition()) return; await Task.Delay(20); } throw new TimeoutException("Icon condition did not settle"); }
    private void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
    private static void Write<T>(T value)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "package-icon-result.json"), JsonSerializer.Serialize(value));
    }
    public ValueTask DisposeAsync() { catalogPreview?.Dispose(); return presenter.DisposeAsync(); }
}
