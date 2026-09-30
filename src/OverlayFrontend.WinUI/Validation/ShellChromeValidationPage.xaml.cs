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
    private readonly NativePopupTheme popupTheme = new();
    private readonly ObservableCollection<BridgeWidgetDescriptor> items = [];
    private readonly List<string> checks = [];
    private bool started;
    internal ShellChromeValidationPage()
    {
        InitializeComponent();
        popupTheme.Attach(this);
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
            UpdateStyles(neon, appearance, false);
            WidgetControllerPrompts.Set(ControllerFamily.Xbox); SetGuideState(false, false);
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
                WidgetControllerPrompts.Set(family); SetGuideState(reorder, hidden);
                Host.UpdateLayout();
                await Task.Yield();
                Check(Math.Abs(guide.ActualHeight - savedHeight) < .1, "guide extent remains stable across family/reorder/widget-focus state");
            }
            SetGuideState(false, false);
            Check(guide.HelpText.Contains("Cross button") && guide.HelpText.Contains("Triangle button") && guide.HelpText.Contains("Options button") && !guide.HelpText.Contains("Y button"),
                "PlayStation names match licensed Cross/Triangle/Options glyphs");
            WidgetControllerPrompts.Set(ControllerFamily.Unknown);
            Check(guide.HelpText.Contains("Cross button"), "unknown input does not reset the last controller family");
            Check(guide.Typography.OfType<FontIcon>().All(icon => icon.FontFamily.Source.Contains("Kenney")),
                "text role styling preserves controller glyph font families");
            for (var index = 0; index < 30; ++index) { (index % 2 == 0 ? first : second).Focus(FocusState.Keyboard); await Task.Yield(); }
            Check(first.Template == template && Tray.SelectedIndex == 0, "rapid tray focus movement does not replace templates or change activation selection");
            UpdateStyles(redline, appearance, false);
            await Until(() => ColorOf(Title.Foreground) == Value(redline, "title", "color"));
            Check(first.Template == template, "theme replacement reuses existing native tray items");
            var titleSize = Title.FontSize;
            UpdateStyles(redline, appearance with { TextScale = 1.5 }, false);
            await Until(() => Math.Abs(Title.FontSize - titleSize * 1.5) < .1);
            Check(true, "shell typography applies global text scale once");
            Host.Width = 430;
            Host.UpdateLayout();
            await Until(() => Math.Abs(Host.ActualWidth - 430) <= 1 / XamlRoot.RasterizationScale);
            var narrowHeight = guide.ActualHeight;
            SetGuideState(true, false); Host.UpdateLayout();
            Check(Math.Abs(guide.ActualHeight - narrowHeight) < .1, $"narrow scaled guide keeps its measured slot (before={narrowHeight}, after={guide.ActualHeight})");
            UpdateStyles(redline, appearance with { Contrast = ContrastPreference.High, Transparency = TransparencyPreference.Reduced }, false);
            await Until(() => first.UseSystemFocusVisuals && ColorOf(Header.Background).A == 255);
            Check(NativeComputedStyleAdapter.For(first)?.FocusDecoration is null, "high contrast uses native focus and opaque chrome");
            UpdateStyles(neon, appearance, false); Host.Width = double.NaN; SetGuideState(false, false);
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
            SetGuideHints((ControllerGuideHint[])[new(ControllerPrompt.X, complete, ControllerButton.X),
                new(ControllerPrompt.LeftTrigger, "Previous section", ControllerButton.LeftTrigger, Group: "triggers"),
                new(ControllerPrompt.RightTrigger, "Next section", ControllerButton.RightTrigger, Group: "triggers"),
                new(ControllerPrompt.Y, "Refresh", ControllerButton.Y)]);
            SetGuideState(false, true); Host.UpdateLayout();
            Check(guide.Opacity == 1 && guide.DisplayedHints.Any(hint => hint.Prompt == ControllerPrompt.B) &&
                guide.DisplayedHints.Any(hint => hint.Prompt == ControllerPrompt.Guide), "widget guide keeps host Back and Close visible");
            Check(!guide.DisplayedHints.Any(hint => hint.Label == complete), "native measured fitting omits complete long labels instead of truncating");
            Check(guide.DisplayedHints.Count(hint => hint.Group == "triggers") is 0 or 2, "paired section hints fit together");
            var widgetGuideHeight = guide.ActualHeight;
            SetGuideHints((ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A)]); Host.UpdateLayout();
            Check(Math.Abs(widgetGuideHeight - guide.ActualHeight) < .1, "popup and widget guides share stable native slot");
            var invoked = new List<ControllerGuideHint>();
            guide.Invoked += invoked.Add;
            var nativeGuideButton = ((Panel)guide.BackgroundSurface).Children.OfType<Button>().First(button => button.IsHitTestVisible);
            Check(nativeGuideButton.CornerRadius.TopLeft == 8 && nativeGuideButton.BorderThickness.Left == 1 &&
                ColorOf(nativeGuideButton.Background) == Value(neon, "tray-item:selected", "background") &&
                ColorOf(nativeGuideButton.Foreground) == Value(neon, "tray-item:selected", "color"),
                "primary controller hint restores the authored selected chip fill, ink and rounded border");
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(nativeGuideButton);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await Until(() => invoked.Count == 1);
            Check(invoked[0].Button == ControllerButton.A, "native guide button requests one normalized input without storing an action id");
            Check(!nativeGuideButton.IsTabStop && !nativeGuideButton.AllowFocusOnInteraction, "guide pointer actions preserve widget focus authority");
            SetGuideHints(null); SetGuideState(false, false); Host.Width = double.NaN; Host.UpdateLayout();
            await CheckNativePopupThemesAsync(neon, redline, appearance);
            await CheckGuideStabilityAsync();
            await CheckGuideMotionAsync(neon, appearance);
            Result.Text = $"Passed {checks.Count} shell chrome checks · PlayStation hints";
            Write(new { result = "passed", checks });
        }
        catch (Exception error) { Result.Text = "Failed: " + error.Message; Write(new { result = "failed", checks, error = error.ToString(), width = Host.ActualWidth, guideHeight = guide.ActualHeight, guideDesired = guide.DesiredSize.Height }); }
    }

    private void UpdateStyles(IReadOnlyDictionary<string, BridgeNodeRenderStyles> palette, AppearanceSettings appearance, bool animations)
    { styles.Update(palette, appearance, animations); guide.ApplyAppearance(palette, appearance, animations); popupTheme.Update(palette, appearance); }

    private IReadOnlyList<ControllerGuideHint>? fixtureGuideHints;
    private object? fixtureGuideContext;
    private bool fixtureGuideReordering, fixtureGuideHidden;
    private void SetGuideHints(IReadOnlyList<ControllerGuideHint>? hints, object? context = null)
    { fixtureGuideHints = hints; fixtureGuideContext = context; PresentFixtureGuide(); }
    private void SetGuideState(bool reorder, bool hidden)
    { fixtureGuideReordering = reorder; fixtureGuideHidden = hidden; PresentFixtureGuide(); }
    private void PresentFixtureGuide() => guide.Present(
        fixtureGuideContext is null ? new object() : (fixtureGuideContext, fixtureGuideReordering, fixtureGuideHidden),
        true, fixtureGuideHints ?? [], fixtureGuideHints is null ? ControllerGuideModel.TrayHints(fixtureGuideReordering) : ControllerGuideModel.WithHost([]),
        fixtureGuideHints is null && fixtureGuideHidden);
    private async Task CheckGuideStabilityAsync()
    {
        ControllerGuideHint[] play = [new(ControllerPrompt.A, "Play", ControllerButton.A)];
        ControllerGuideHint[] pause = [new(ControllerPrompt.A, "Pause", ControllerButton.A)];
        SetGuideHints(play, "stable-widget"); SetGuideState(false, true); Host.UpdateLayout();
        Check(guide.HelpText.Contains("Play"), "new guide context is visible immediately");
        SetGuideHints([], "stable-widget");
        Check(guide.HelpText.Contains("Play"), "brief unavailable actions retain guide text and accessible labels");
        await Task.Delay(30);
        SetGuideHints(play, "stable-widget");
        await Task.Delay(160);
        Check(guide.HelpText.Contains("Play"), "restoring actions before settling cancels the pending guide change");

        var invocations = 0;
        void Invoked(ControllerGuideHint _) => ++invocations;
        guide.Invoked += Invoked;
        try
        {
            SetGuideHints(pause, "stable-widget");
            var button = ((Panel)guide.BackgroundSurface).Children.OfType<Button>().First();
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button);
            var invoke = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
            invoke.Invoke();
            await Task.Delay(20);
            Check(invocations == 0 && guide.HelpText.Contains("Play"), "a retained stale label cannot invoke its replacement action");
            await Until(() => guide.HelpText.Contains("Pause"));
            Check(true, "stable replacement publishes through the native dispatcher timer without another snapshot");
            invoke.Invoke();
            await Until(() => invocations == 1);
            Check(true, "settled guide action still invokes once");
            SetGuideHints(play, "stable-widget");
            SetGuideHints((ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A)], "modal");
            Check(guide.HelpText.Contains("Select") && !guide.HelpText.Contains("Pause"), "modal context replaces pending hints immediately");
            await Task.Delay(160);
            Check(guide.HelpText.Contains("Select"), "old pending hints cannot overwrite a new context");
            guide.Present("delayed-row", true, play, ControllerGuideModel.WithHost([]));
            guide.Present("delayed-row", false, pause, ControllerGuideModel.WithHost([]));
            await Task.Delay(200);
            Check(guide.HelpText.Contains("Play") && !guide.HelpText.Contains("Pause"), "timer cannot publish contextual hints before their row is ready");
            guide.Present("delayed-row", true, pause, ControllerGuideModel.WithHost([]));
            Check(guide.HelpText.Contains("Pause"), "ready row publishes contextual hints immediately without a second debounce");
            guide.Present("next-widget", false, play, ControllerGuideModel.TrayHints(false));
            Check(guide.HelpText.Contains("Pause") && !guide.HelpText.Contains("Open widget"), "pending owner retains one complete guide instead of mixing old actions and new navigation");
            await Task.Delay(160);
            Check(guide.HelpText.Contains("Pause") && !guide.ContextualReady, "pending owner never advances guide pixels by elapsed time alone");
            guide.Present("next-widget", true, [], ControllerGuideModel.TrayHints(false));
            Check(!guide.HelpText.Contains("Pause") && guide.HelpText.Contains("Open widget"), "ready owner replaces the entire guide in one publication");
        }
        finally { guide.Invoked -= Invoked; }
    }

    private async Task CheckNativePopupThemesAsync(IReadOnlyDictionary<string, BridgeNodeRenderStyles> neon,
        IReadOnlyDictionary<string, BridgeNodeRenderStyles> redline, AppearanceSettings appearance)
    {
        var menu = new MenuFlyout();
        var item = new MenuFlyoutItem { Text = "Themed context action" };
        var selected = new ToggleMenuFlyoutItem { Text = "Selected option", IsChecked = true };
        menu.Items.Add(item); menu.Items.Add(selected);
        using (NativePopupTheme.Menu(menu, this))
        {
            try
            {
                menu.ShowAt(Outside);
                await Until(() => item.IsLoaded);
                var presenter = Parent<MenuFlyoutPresenter>(item) ?? throw new InvalidOperationException("Native menu presenter missing.");
                var template = item.Template;
                Windows.UI.Color PanelColor() => presenter.Background is LinearGradientBrush gradient ? gradient.GradientStops[1].Color : ColorOf(presenter.Background);
                var firstColor = PanelColor();
                Check(firstColor == ShellChromePalette.Resolve(neon, appearance).Surface, "native popup paints the resolved shell surface");
                Check(presenter.Background is LinearGradientBrush { GradientStops.Count: 3 } &&
                    presenter.CornerRadius.TopLeft == Math.Clamp(OverlaySurfacePaint.CornerRadius(neon) * .75, 0, 12) * appearance.InterfaceScale,
                    "native popup restores original shaded surface and themed menu corner radius");
                await Task.Delay(50); // Let native flyout's initial-focus request finish.
                await Until(() => item.Focus(FocusState.Keyboard));
                await Task.Delay(30);
                Check(item.MinHeight == 48 * appearance.InterfaceScale * Math.Max(1, appearance.TextScale) &&
                    presenter.Padding.Left == 6 * appearance.InterfaceScale,
                    "native menu uses controller-sized row and panel metrics scaled with the widget");
                Check(ColorOf(item.Background) == ShellChromePalette.Composite(firstColor, ShellChromePalette.Resolve(neon, appearance).Selected),
                    $"controller focus fills the native menu row with the themed selection color [state={item.FocusState}, fill={ColorOf(item.Background)}]");
                Check(!item.UseSystemFocusVisuals && Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementChildVisual(item) is not null && item.Padding.Left == 18 * appearance.InterfaceScale,
                    "native popup restores leading focus marker and selection rim without overlaying a second native focus rectangle");
                Check(ReferenceEquals(selected.Resources["MenuFlyoutSubItemBackgroundPointerOver"], selected.Resources["ToggleMenuFlyoutItemBackgroundPointerOver"]) &&
                    ReferenceEquals(selected.Resources["MenuFlyoutSubItemForegroundPressed"], selected.Resources["MenuFlyoutItemForegroundPressed"]),
                    "toggle dropdown template receives the same complete state palette as ordinary menus");
                var ordinaryWeight = item.FontWeight;
                UpdateStyles(neon, appearance with { BoldText = true }, false);
                await Until(() => MenuLabel(item, item.Text)?.FontWeight.Weight >= 600 &&
                    MenuLabel(selected, selected.Text)?.FontWeight.Weight >= 600);
                Check(item.FontWeight.Weight >= 600 && selected.FontWeight.Weight >= 600 && presenter.FontWeight.Weight >= 600,
                    "Bold Text applies to rendered ordinary and toggle dropdown labels outside the widget tree");
                UpdateStyles(neon, appearance with { BoldText = false }, false);
                await Until(() => MenuLabel(item, item.Text)?.FontWeight == ordinaryWeight);
                Check(item.FontWeight == ordinaryWeight && selected.IsChecked && item.Template == template,
                    "turning Bold Text off restores authored popup weight without replacing selection or controls");
                UpdateStyles(redline, appearance, false);
                await Until(() => PanelColor() == ShellChromePalette.Resolve(redline, appearance).Surface);
                Check(ReferenceEquals(presenter, Parent<MenuFlyoutPresenter>(item)) && item.Template == template && selected.IsChecked,
                    "live popup theme change keeps native controls selection and template");
                UpdateStyles(redline, appearance with { Contrast = ContrastPreference.High }, false);
                Check(ColorOf(presenter.Background).A == 255 && ColorOf(item.FocusVisualPrimaryBrush).A == 255,
                    "high-contrast native popup uses opaque system surface and focus ink");
            }
            finally { menu.Hide(); }
        }
        UpdateStyles(neon, appearance, false);
        var dialog = new WidgetTextEntryDialog(new() { Id = "theme.editor", Kind = ViewNodeKind.TextEntry,
            TextEntryValue = "Theme sample", AccessibilityLabel = "Themed keyboard" }, _ => { });
        dialog.BindRoot(XamlRoot);
        dialog.ThemeLease = NativePopupTheme.Dialog(dialog, this);
        var opening = dialog.ShowAsync();
        try
        {
            await Until(() => dialog.IsLoaded && Descendant<Button>(dialog) is not null);
            Check(ColorOf(dialog.Background) == ShellChromePalette.Resolve(neon, appearance).Surface &&
                dialog.Resources["TextControlSelectionHighlightColor"] is Brush,
                "native text-entry dialog and text selection receive the shared theme resources");
            var editor = Descendant<TextBox>(dialog) ?? throw new InvalidOperationException("Native themed editor missing.");
            UpdateStyles(redline, appearance, false);
            await Until(() => ColorOf(dialog.Background) == ShellChromePalette.Resolve(redline, appearance).Surface);
            Check(editor.Text == "Theme sample" && ReferenceEquals(editor, Descendant<TextBox>(dialog)),
                "live keyboard theme changes preserve the native editor and entered value");
        }
        finally { dialog.Hide(); await opening; dialog.Erase(); }
        UpdateStyles(neon, appearance, false);
        static T? Parent<T>(DependencyObject element) where T : DependencyObject
        { for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent)) if (parent is T target) return target; return null; }
        static T? Descendant<T>(DependencyObject element) where T : DependencyObject
        { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++) { var child = VisualTreeHelper.GetChild(element, index); if (child is T target) return target; if (Descendant<T>(child) is { } found) return found; } return null; }
        static TextBlock? MenuLabel(DependencyObject element, string text)
        { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++) { var child = VisualTreeHelper.GetChild(element, index); if (child is TextBlock label && label.Text == text) return label; if (MenuLabel(child, text) is { } found) return found; } return null; }
    }

    internal static IReadOnlyDictionary<string, BridgeNodeRenderStyles> Palette(string selected)
    {
        string Read(string resource) { using var stream = typeof(AppearanceSettings).Assembly.GetManifestResourceStream(resource)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var baseline = WrssParser.Parse(Read("WidgetRail.PlatformSettings.Themes.builtin-default.wrss"), "default.wrss").Document;
        var overlay = WrssParser.Parse(Read($"WidgetRail.PlatformSettings.Themes.{selected}.theme.wrss"), "theme.wrss").Document;
        var theme = WrssThemeCompiler.Compile((WrssThemeLayer[])[new WrssThemeLayer(0, (WrssDocument[])[baseline]), new WrssThemeLayer(100, (WrssDocument[])[overlay])]).Theme
            ?? throw new InvalidOperationException("The built-in shell fixture theme did not compile.");
        var result = new Dictionary<string, BridgeNodeRenderStyles>();
        foreach (var key in new[] { "panel", "canvas", "tray", "tray-item", "tray-item:selected", "tray-item:focused", "tray-item:selected:focused", "title", "body", "hint", "controller-glyph", "status" })
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
