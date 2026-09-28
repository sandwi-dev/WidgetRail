using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Autonomous native style checks; no controller owner, launch or OS-theme changes.</summary>
internal sealed partial class WidgetStylesValidationPage : Page, IAsyncDisposable
{
    private readonly TextBlock status = new() { Text = "Style checks pending", TextWrapping = TextWrapping.Wrap,
        MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel host = new() { Spacing = 10 };
    private readonly WidgetViewPresenter presenter = new();
    private readonly Button outside = new() { Content = "Outside widget" };
    private long sequence;
    private bool started;
    private int actions;
    private readonly List<string> checks = [];
    internal WidgetStylesValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Styles.Status");
        AutomationProperties.SetAutomationId(outside, "Styles.Outside");
        host.Children.Add(status); host.Children.Add(outside); host.Children.Add(presenter); Content = host;
        presenter.DispatchActionAsync = _ => { ++actions; return Task.CompletedTask; };
        Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            try { await RunAsync(); status.Text = $"PASS: {checks.Count} native style checks\n" + string.Join("\n", checks); }
            catch (Exception error) { status.Text = "FAIL: " + error; }
            finally { WidgetViewPresenter.SetHighContrastStyleOverride(null); }
        };
    }

    private async Task RunAsync()
    {
        WidgetViewPresenter.SetHighContrastStyleOverride(false);
        var styles = Styles();
        Apply(styles);
        await Wait(() => Find<Button>("Widget.button") is not null);
        var button = Find<Button>("Widget.button")!;
        var root = Find<Grid>("Widget.root")!;
        var text = Find<TextBlock>("Widget.text")!;
        outside.Focus(FocusState.Programmatic);
        await Wait(() => button.FocusState == FocusState.Unfocused && ColorOf(button.Background) == Color.FromArgb(255, 16, 32, 48));
        Check(root.Padding == new Thickness(8, 2, 4, 6) && root.CornerRadius == new CornerRadius(7), "panel padding and corner radius use native properties");
        Check(root.BorderThickness == new Thickness(1, 3, 1, 1) && ColorOf(root.BorderBrush) == Color.FromArgb(255, 68, 85, 102), "uniform border color and per-edge widths apply");
        Check(ColorOf(root.Background) == Color.FromArgb(68, 17, 34, 51) && Near(root.Opacity, .8),
            $"CSS RGBA order and subtree opacity remain distinct (color={ColorOf(root.Background)}, opacity={root.Opacity:R})");
        Check(ColorOf(text.Foreground) == Color.FromArgb(128, 255, 0, 0) && text.FontSize == 20 && text.FontWeight.Weight == 700 &&
            text.FontFamily.Source == "Consolas" && text.CharacterSpacing == 100 && text.TextAlignment == TextAlignment.Right,
            "text color typography and em letter spacing apply");
        Check(button.UseSystemFocusVisuals, "native focus visuals remain enabled");
        button.Focus(FocusState.Programmatic);
        await Wait(() => ColorOf(button.Background) == Color.FromArgb(255, 64, 96, 128));
        Check(ColorOf(button.Foreground) == Color.FromArgb(255, 255, 238, 0), "native focus selects the complete focused style");
        await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
        Check(ColorOf(button.Background) == Color.FromArgb(255, 171, 205, 239) && actions == 1, "controller press shares styles without duplicating action admission");
        await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Repeated);
        Check(ColorOf(button.Background) == Color.FromArgb(255, 171, 205, 239) && actions == 1, "repeat preserves pressed presentation without reactivation");
        Apply(styles);
        Check(ReferenceEquals(button, Find<Button>("Widget.button")) && ColorOf(button.Background) == Color.FromArgb(255, 171, 205, 239),
            "ordinary snapshots preserve the native control and held presentation");
        await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        Check(ColorOf(button.Background) == Color.FromArgb(255, 64, 96, 128), "release restores focused presentation");

        WidgetViewPresenter.SetHighContrastStyleOverride(true);
        await Wait(() => root.Opacity == 1);
        Check(ColorOf(root.Background).A == 255 && ColorOf(button.Foreground) != ColorOf(button.Background), "high contrast uses opaque system colors with readable foreground");
        WidgetViewPresenter.SetHighContrastStyleOverride(false);
        await Wait(() => Near(root.Opacity, .8));
        Check(ColorOf(root.Background) == Color.FromArgb(68, 17, 34, 51), "leaving high contrast restores authored colors");
        Apply(new Dictionary<string, BridgeNodeRenderStyles>());
        Check(ReferenceEquals(root.ReadLocalValue(Panel.BackgroundProperty), DependencyProperty.UnsetValue) &&
            ReferenceEquals(button.ReadLocalValue(Control.PaddingProperty), DependencyProperty.UnsetValue) &&
            ReferenceEquals(text.ReadLocalValue(TextBlock.FontFamilyProperty), DependencyProperty.UnsetValue),
            "removed styles clear owned local values instead of installing transparent defaults");
        Check(root.Opacity == 1 && button.UseSystemFocusVisuals, "removal restores opacity and keeps native focus policy");

        var nativeButton = new Button { Content = "Resource restoration" };
        var originalResources = nativeButton.Resources;
        originalResources["Sentinel"] = "retained";
        host.Children.Add(nativeButton);
        using (var adapter = new NativeComputedStyleAdapter(nativeButton))
        {
            adapter.Update(styles["button"]);
            Check((string)nativeButton.Resources["Sentinel"] == "retained" && !ReferenceEquals(nativeButton.Resources, originalResources),
                "native Button resource wrapping preserves existing resource lookup");
            adapter.Update(null);
            Check(ReferenceEquals(nativeButton.Resources, originalResources) && (string)originalResources["Sentinel"] == "retained",
                "native Button resources restore by identity");
        }
        host.Children.Remove(nativeButton);
        await ModalDefaultsAsync();
        await IndexedRootAsync(styles["button"]);
        var glyph = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 18, Glyph = "\uE10F" };
        using (var adapter = new NativeComputedStyleAdapter(glyph, false))
        {
            adapter.Update(styles["text"]);
            Check(glyph.FontFamily.Source == "Segoe Fluent Icons" && glyph.FontSize == 20, "glyph font size and color do not replace its explicit font family");
            adapter.Update(null);
            Check(glyph.FontSize == 18, "glyph style removal restores its original local size");
        }
        var retired = button;
        presenter.ResetPressedStyles();
        Apply(styles, includeButton: false);
        Check(Find<Button>("Widget.button") is null && NativeComputedStyleAdapter.For(retired) is null,
            "removed controls retire their shared style owner");
        await ResponsiveAndPostersAsync();
        await NativeGridMeasureAsync();
        await NativeScaleAsync();
        await LoadingIndicatorGeometryAsync();
        await AccessibilityPreferencesAsync();
        await NativeFocusDecorationAsync();
        await NativeSectionTransitionsAsync();
        await NativeIndexedBoxAsync();
        await NativeActionSurfaceMeasureAsync();
        await NativeDepthAsync();
        await NativeTypographyAsync();
        if (App.ValidationFixturePath is { } fixture) await PlayniteProductionLayoutAsync(fixture);
    }

    private async Task ModalDefaultsAsync()
    {
        var panel = new WidgetModalPanel { Height = 60 };
        panel.Children.Add(new TextBlock { Text = "Modal background style" }); host.Children.Add(panel);
        panel.UpdateLayout();
        using var adapter = new NativeComputedStyleAdapter(panel, false);
        var styles = Compute("#modal { background: rgba(12, 24, 36, 0.8); padding: 4px; }", "modal", "stack");
        adapter.Update(styles);
        Check(ColorOf(panel.Background) == Color.FromArgb(204, 12, 24, 36) && panel.Opacity == 1,
            "modal background alpha does not fade child text or scrim");
        adapter.Update(null);
        Check(ReferenceEquals(panel.ReadLocalValue(Panel.BackgroundProperty), DependencyProperty.UnsetValue) && ColorOf(panel.Background).A == 255 && panel.Padding == new Thickness(20),
            "modal removal restores its native ThemeResource-backed defaults");
        panel.RequestedTheme = ElementTheme.Light;
        await Task.Delay(40);
        var light = ColorOf(panel.Background);
        panel.RequestedTheme = ElementTheme.Dark;
        await Wait(() => ColorOf(panel.Background) != light);
        Check(ColorOf(panel.Background).A == 255, "restored modal defaults still follow native theme changes");
        host.Children.Remove(panel);
    }

    private async Task IndexedRootAsync(BridgeNodeRenderStyles styles)
    {
        var container = new ListViewItem { IsTabStop = true, Height = 52 };
        var fragment = new WidgetViewPresenter(presentationOnly: true);
        fragment.UseIndexedContainerStyles();
        var node = new ViewNode { Id = "row", Kind = ViewNodeKind.Button, Text = "Indexed row", ActionId = "row" };
        var rowStyles = styles with
        {
            Base = styles.Base.Concat(new[] { new KeyValuePair<string, BridgeComputedStyleValue>("opacity", new() { Kind = WrssValueKind.Number, Text = "0.6", Number = .6 }) }).ToDictionary(pair => pair.Key, pair => pair.Value),
        };
        var frame = CreateFrame(node, new Dictionary<string, BridgeNodeRenderStyles> { ["row"] = rowStyles });
        fragment.ApplyFragment(frame, node, "row");
        container.Content = fragment; host.Children.Add(container);
        using var adapter = new NativeComputedStyleAdapter(container);
        adapter.InteractionChanged += fragment.SetIndexedRootInteraction;
        adapter.Update(rowStyles);
        try
        {
            Check(Near(container.Opacity, .6) && ((FrameworkElement)fragment.Content).Opacity == 1,
                "indexed container owns root opacity once; row fragment owns only root typography");
            container.Focus(FocusState.Programmatic);
            await Wait(() => ColorOf(container.Foreground) == Color.FromArgb(255, 255, 238, 0));
            Check(ColorOf(((TextBlock)fragment.Content).Foreground) == ColorOf(container.Foreground),
                "indexed focused typography receives the native container state");
        }
        finally { adapter.Dispose(); host.Children.Remove(container); await fragment.DisposeAsync(); }
    }

    private void Apply(IReadOnlyDictionary<string, BridgeNodeRenderStyles> styles, bool includeButton = true)
    {
        var children = new List<ViewNode> { new() { Id = "text", Kind = ViewNodeKind.Text, Text = "Computed native text" } };
        if (includeButton) children.Add(new() { Id = "button", Kind = ViewNodeKind.Button, Text = "Native styled button", ActionId = "activate" });
        presenter.Apply(CreateFrame(new() { Id = "root", Kind = ViewNodeKind.Stack, Children = children }, styles));
    }
    private WidgetPresentationFrame CreateFrame(ViewNode root, IReadOnlyDictionary<string, BridgeNodeRenderStyles> styles)
    {
        var snapshot = new ViewSnapshot { WidgetInstanceId = "styles.instance", Sequence = ++sequence, ActiveInputScopeId = root.Id, Root = root };
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        var descriptor = new BridgeWidgetDescriptor { Id = "styles", Name = "Styles", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = "runtime", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        return new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1, descriptor.InstanceId,
            snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), styles);
    }
    private static Dictionary<string, BridgeNodeRenderStyles> Styles()
    {
        const string css = """
            #root { background: #1234; border-color: #445566; border-width: 1px; border-top-width: 3px; padding: 2px 4px 6px 8px; corner-radius: 7px; opacity: 0.8; }
            #text { color: rgba(100%, 0%, 0%, 50%); font-size: 20px; font-weight: 700; font-family: Consolas; letter-spacing: 0.1em; text-align: end; }
            #button { background: #102030; color: #fff; border-color: #ffffff20; border-width: 2px; padding: 6px; }
            #button:focused { background: #406080; color: #fe0; }
            #button:pressed { background: #abcdef; color: #fff; }
            """;
        return new() { ["root"] = Compute(css, "root", "stack"), ["text"] = Compute(css, "text", "text"), ["button"] = Compute(css, "button", "button") };
    }
    private static BridgeNodeRenderStyles Compute(string css, string id, string role)
    {
        var result = WrssThemeCompiler.Compile(new[] { WrssParser.Parse(css, "native-style-check.wrss").Document });
        var theme = result.Theme ?? throw new InvalidOperationException("Style fixture failed compilation.");
        IReadOnlyDictionary<string, BridgeComputedStyleValue> Resolve(params WrssPseudoState[] states) =>
            theme.Resolve(new(role, id, new HashSet<string>(), states.ToHashSet())).Properties.ToDictionary(pair => pair.Key,
                pair => new BridgeComputedStyleValue { Kind = pair.Value.Kind, Text = pair.Value.Text, Number = pair.Value.Number, Unit = pair.Value.Unit });
        return new() { Base = Resolve(), Focused = Resolve(WrssPseudoState.Focused), Pressed = Resolve(WrssPseudoState.Focused, WrssPseudoState.Pressed) };
    }
    private T? Find<T>(string id) where T : FrameworkElement
    {
        var pending = new Stack<DependencyObject>(); pending.Push(presenter);
        while (pending.TryPop(out var node))
        {
            if (node is T element && AutomationProperties.GetAutomationId(element) == id) return element;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); ++i) pending.Push(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }
    private static Color ColorOf(Brush? brush) => brush is SolidColorBrush solid ? solid.Color : default;
    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.000001;
    private void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
    private static async Task Wait(Func<bool> predicate)
    {
        var end = Environment.TickCount64 + 5000;
        while (!predicate()) { if (Environment.TickCount64 > end) throw new TimeoutException("Native style state did not settle."); await Task.Delay(16); }
    }
    public ValueTask DisposeAsync()
    {
        foreach (var specimen in depthSpecimens) specimen.Dispose();
        depthSpecimens.Clear();
        return presenter.DisposeAsync();
    }
}
