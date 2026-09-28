using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class ShellChromeValidationPage : Page, IAsyncDisposable
{
    private readonly ShellChromeStyles styles = new();
    private readonly ShellControllerGuide guide = new();
    private readonly ObservableCollection<BridgeWidgetDescriptor> items = [];
    private readonly List<string> checks = [];
    private bool started;
    internal ShellChromeValidationPage()
    {
        InitializeComponent();
        styles.Register(this, "body"); styles.Register(Header, "tray"); styles.Register(Title, "title");
        styles.Register(Status, "status"); styles.Register(Outside, "body"); styles.Attach(Tray);
        styles.Register(guide.BackgroundSurface, "tray");
        foreach (var element in guide.Typography) styles.Register(element, element is FontIcon ? "controller-glyph" : "hint");
        GuideHost.Content = guide; Tray.ItemsSource = items;
        for (var index = 0; index < 50; ++index) items.Add(new() { Id = "widget-" + index,
            Name = index switch { 0 => "Playnite Library", 1 => "YouTube Music", 2 => "Spotify", _ => "Widget " + index },
            InstanceId = "instance-" + index, RuntimeGeneration = "runtime", PresentationGeneration = "presentation",
            PackageContentDigest = "fixture", Icon = WidgetGlyph.Music });
        Loaded += async (_, _) => { if (!started) { started = true; await RunAsync(); } };
    }
    private void RowLoaded(object sender, RoutedEventArgs args)
    {
        var host = (ContentControl)sender;
        host.Content ??= new WidgetCatalogItemContent((_, _, _) => Task.FromException<WidgetPresentationPackageIcon>(new InvalidOperationException()));
        ((WidgetCatalogItemContent)host.Content).SetItem(host.DataContext as BridgeWidgetDescriptor);
    }

    private async Task RunAsync()
    {
        try
        {
            var appearance = AppearanceSettings.Default with { BoldText = false, Contrast = ContrastPreference.Standard };
            var neon = Palette("builtin-neon-circuit"); var redline = Palette("builtin-redline");
            styles.Update(neon, appearance, false);
            WidgetControllerPrompts.Set(ControllerFamily.Xbox); guide.SetState(false, false);
            await Until(() => Tray.ContainerFromIndex(0) is ListViewItem { IsLoaded: true, ActualWidth: > 0 } &&
                Tray.ContainerFromIndex(1) is ListViewItem { IsLoaded: true, ActualWidth: > 0 });
            var first = (ListViewItem)Tray.ContainerFromIndex(0); var second = (ListViewItem)Tray.ContainerFromIndex(1);
            var template = first.Template;
            Tray.SelectedIndex = 0; first.Focus(FocusState.Keyboard);
            await Until(() => ColorOf(first.Foreground) == Value(neon, "tray-item:selected:focused", "color"));
            Check(first.Template == template && first.IsSelected, "selected/focused roles preserve native ListViewItem template and selection");
            Check(ColorOf(Header.Background) == Value(neon, "tray", "background") && ColorOf(Title.Foreground) == Value(neon, "title", "color"),
                "actual built-in Neon Circuit palette paints shell chrome and title");
            Check(guide.HelpText.Contains("A button") && guide.HelpText.Contains("Y button"), "Xbox guide uses semantic names");
            Check(guide.Typography.OfType<FontIcon>().All(icon => icon.FontSize >= 24), "controller symbols use their glyph size, independently of hint text");
            var outline = NativeComputedStyleAdapter.For(first)?.FocusDecoration;
            Check(outline is not null, "tray authored focus outline uses existing native decoration");
            second.Focus(FocusState.Keyboard);
            await Until(() => ColorOf(second.Foreground) == Value(neon, "tray-item:focused", "color"));
            Check(first.IsSelected && !second.IsSelected, "moving focus preserves selected versus focused tray state");
            Host.UpdateLayout();
            var savedHeight = guide.ActualHeight;
            foreach (var family in new[] { ControllerFamily.Xbox, ControllerFamily.PlayStation })
            foreach (var reorder in new[] { false, true })
            foreach (var hidden in new[] { false, true })
            {
                WidgetControllerPrompts.Set(family); guide.SetState(reorder, hidden);
                Host.UpdateLayout();
                await Task.Yield();
                Check(Math.Abs(guide.ActualHeight - savedHeight) < .1, "guide extent remains stable across family/reorder/widget-focus state");
            }
            guide.SetState(false, false);
            Check(guide.HelpText.Contains("Cross button") && guide.HelpText.Contains("Triangle button") && guide.HelpText.Contains("Options button") && !guide.HelpText.Contains("Y button"),
                "PlayStation names match licensed Cross/Triangle/Options glyphs");
            WidgetControllerPrompts.Set(ControllerFamily.Unknown);
            Check(guide.HelpText.Contains("Cross button"), "unknown input does not reset the last controller family");
            Check(guide.Typography.OfType<FontIcon>().All(icon => icon.FontFamily.Source.Contains("Kenney")),
                "text role styling preserves controller glyph font families");
            for (var index = 0; index < 30; ++index) { (index % 2 == 0 ? first : second).Focus(FocusState.Keyboard); await Task.Yield(); }
            Check(first.Template == template && Tray.SelectedIndex == 0, "rapid tray focus movement does not replace templates or change activation selection");
            styles.Update(redline, appearance, false);
            await Until(() => ColorOf(Title.Foreground) == Value(redline, "title", "color"));
            Check(first.Template == template, "theme replacement reuses existing native tray items");
            var titleSize = Title.FontSize;
            styles.Update(redline, appearance with { TextScale = 1.5 }, false);
            await Until(() => Math.Abs(Title.FontSize - titleSize * 1.5) < .1);
            Check(true, "shell typography applies global text scale once");
            Host.Width = 430;
            Host.UpdateLayout();
            await Until(() => Math.Abs(Host.ActualWidth - 430) <= 1 / XamlRoot.RasterizationScale);
            var narrowHeight = guide.ActualHeight;
            guide.SetState(true, false); Host.UpdateLayout();
            Check(Math.Abs(guide.ActualHeight - narrowHeight) < .1, $"narrow scaled guide keeps its measured slot (before={narrowHeight}, after={guide.ActualHeight})");
            styles.Update(redline, appearance with { Contrast = ContrastPreference.High, Transparency = TransparencyPreference.Reduced }, false);
            await Until(() => first.UseSystemFocusVisuals && ColorOf(Header.Background).A == 255);
            Check(NativeComputedStyleAdapter.For(first)?.FocusDecoration is null, "high contrast uses native focus and opaque chrome");
            styles.Update(neon, appearance, false); Host.Width = double.NaN; guide.SetState(false, false);
            Outside.Focus(FocusState.Keyboard);
            Tray.ScrollIntoView(items[^1]);
            await Until(() => Tray.ContainerFromIndex(items.Count - 1) is ListViewItem { IsLoaded: true, ActualWidth: > 0 });
            Tray.ScrollIntoView(items[0]);
            await Until(() => Tray.ContainerFromIndex(0) is ListViewItem { IsLoaded: true, ActualWidth: > 0 });
            var restored = (ListViewItem)Tray.ContainerFromIndex(0); restored.Focus(FocusState.Keyboard);
            await Until(() => ColorOf(restored.Foreground) == Value(neon, "tray-item:selected:focused", "color"));
            Check(true, "recycled tray identity receives current palette and selected state");
            Host.Width = 430;
            var complete = "An authored command label that cannot fit beside the required host navigation controls";
            guide.SetWidgetHints([new(ControllerPrompt.X, complete, ControllerButton.X),
                new(ControllerPrompt.LeftTrigger, "Previous section", ControllerButton.LeftTrigger, Group: "triggers"),
                new(ControllerPrompt.RightTrigger, "Next section", ControllerButton.RightTrigger, Group: "triggers"),
                new(ControllerPrompt.Y, "Refresh", ControllerButton.Y)]);
            guide.SetState(false, true); Host.UpdateLayout();
            Check(guide.Opacity == 1 && guide.DisplayedHints.Any(hint => hint.Prompt == ControllerPrompt.B) &&
                guide.DisplayedHints.Any(hint => hint.Prompt == ControllerPrompt.Guide), "widget guide keeps host Back and Close visible");
            Check(!guide.DisplayedHints.Any(hint => hint.Label == complete), "native measured fitting omits complete long labels instead of truncating");
            Check(guide.DisplayedHints.Count(hint => hint.Group == "triggers") is 0 or 2, "paired section hints fit together");
            var widgetGuideHeight = guide.ActualHeight;
            guide.SetWidgetHints([new(ControllerPrompt.A, "Select", ControllerButton.A)]); Host.UpdateLayout();
            Check(Math.Abs(widgetGuideHeight - guide.ActualHeight) < .1, "popup and widget guides share stable native slot");
            var invoked = new List<ControllerGuideHint>();
            guide.Invoked += invoked.Add;
            var nativeGuideButton = ((Panel)guide.BackgroundSurface).Children.OfType<Button>().First(button => button.IsHitTestVisible);
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(nativeGuideButton);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await Until(() => invoked.Count == 1);
            Check(invoked[0].Button == ControllerButton.A, "native guide button requests one normalized input without storing an action id");
            Check(!nativeGuideButton.IsTabStop && !nativeGuideButton.AllowFocusOnInteraction, "guide pointer actions preserve widget focus authority");
            guide.SetWidgetHints(null); guide.SetState(false, false); Host.Width = double.NaN; Host.UpdateLayout();
            Result.Text = $"Passed {checks.Count} shell chrome checks · PlayStation hints";
            Write(new { result = "passed", checks });
        }
        catch (Exception error) { Result.Text = "Failed: " + error.Message; Write(new { result = "failed", checks, error = error.ToString(), width = Host.ActualWidth, guideHeight = guide.ActualHeight, guideDesired = guide.DesiredSize.Height }); }
    }

    private static IReadOnlyDictionary<string, BridgeNodeRenderStyles> Palette(string selected)
    {
        string Read(string resource) { using var stream = typeof(AppearanceSettings).Assembly.GetManifestResourceStream(resource)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var baseline = WrssParser.Parse(Read("WidgetRail.PlatformSettings.Themes.builtin-default.wrss"), "default.wrss").Document;
        var overlay = WrssParser.Parse(Read($"WidgetRail.PlatformSettings.Themes.{selected}.theme.wrss"), "theme.wrss").Document;
        var theme = WrssThemeCompiler.Compile([new WrssThemeLayer(0, [baseline]), new WrssThemeLayer(100, [overlay])]).Theme
            ?? throw new InvalidOperationException("The built-in shell fixture theme did not compile.");
        var result = new Dictionary<string, BridgeNodeRenderStyles>();
        foreach (var key in new[] { "tray", "tray-item", "tray-item:selected", "tray-item:focused", "tray-item:selected:focused", "title", "body", "hint", "controller-glyph", "status" })
        {
            var parts = key.Split(':');
            var states = parts.Skip(1).Select(value => Enum.Parse<WrssPseudoState>(value, ignoreCase: true)).ToHashSet();
            var computed = theme.Resolve(new(parts[0], "shell." + key.Replace(':', '.'), new HashSet<string>(key == "controller-glyph" ? new[] { "wrail-controller-glyph" } : Array.Empty<string>()), states));
            var values = computed.Properties.ToDictionary(pair => pair.Key, pair => new BridgeComputedStyleValue
                { Kind = pair.Value.Kind, Text = pair.Value.Text, Number = pair.Value.Number, Unit = pair.Value.Unit });
            result[key] = new() { Base = values, Focused = values, Pressed = values };
        }
        return result;
    }
    private static Color Value(IReadOnlyDictionary<string, BridgeNodeRenderStyles> palette, string role, string property) =>
        NativeComputedStyleAdapter.TryColor(palette[role].Base[property].Text, out var color) ? color : throw new InvalidOperationException("Fixture color is unresolved.");
    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
    private static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 200; ++i) { if (condition()) return; await Task.Delay(15); } throw new TimeoutException("Native shell chrome did not settle."); }
    private static void Write<T>(T value)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "shell-chrome-result.json"), JsonSerializer.Serialize(value));
    }
    public ValueTask DisposeAsync() { styles.Dispose(); guide.Dispose(); return ValueTask.CompletedTask; }
}
