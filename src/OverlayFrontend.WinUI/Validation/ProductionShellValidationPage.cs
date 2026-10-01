using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class ProductionShellValidationPage : Page, IAsyncDisposable
{
    private readonly OverlayShellPage shell = new(new(Path.GetTempPath(), Path.GetTempPath(), Path.GetTempPath()), startService: false);
    private readonly OverlayScaleRoot scale = new();
    private readonly List<string> checks = [];
    private long sequence;
    internal ProductionShellValidationPage()
    {
        scale.Children.Add(shell); Content = scale;
        shell.InitializeShellChrome();
        Loaded += async (_, _) =>
        {
            try { await RunAsync(); Write(new { result = "passed", checks }); }
            catch (Exception error) { Write(new { result = "failed", checks, error = error.ToString() }); }
        };
    }

    private async Task RunAsync()
    {
        var catalog = Enumerable.Range(0, 18).Select(index => new BridgeWidgetDescriptor
        { Id = "fixture." + index, Name = "Widget " + index, Icon = WidgetGlyph.Music,
            InstanceId = "fixture.instance." + index, RuntimeGeneration = "runtime", PresentationGeneration = "presentation", PackageContentDigest = "" }).ToArray();
        foreach (var zoom in new[] { 1d, 1.25, .85 })
        foreach (var position in Enum.GetValues<OverlayPosition>())
        {
            var appearance = AppearanceSettings.Default with { InterfaceScale = zoom, OverlayPosition = position };
            scale.InterfaceScale = zoom;
            Rect? railBounds = null, guideBounds = null;
            foreach (var mode in new[] { WidgetSurfaceMode.Compact, WidgetSurfaceMode.Wide, WidgetSurfaceMode.Standard })
            {
                var hints = new WidgetSurfaceHints { Mode = mode, HeightMode = mode == WidgetSurfaceMode.Standard ? WidgetSurfaceAxisMode.FillAvailable : WidgetSurfaceAxisMode.Preferred };
                var snapshot = new ViewSnapshot { WidgetInstanceId = catalog[0].InstanceId, Sequence = ++sequence,
                    ActiveInputScopeId = "root", Surface = hints, Root = new() { Id = "root", Kind = ViewNodeKind.Stack,
                        Shortcuts = (ControllerShortcut[])[new(ControllerButton.Y, "refresh", Label: "Fixture refresh")],
                        Children = (ViewNode[])[new() { Id = "text", Kind = ViewNodeKind.Text, Text = "Production widget content" }] } };
                var frame = new WidgetPresentationFrame(new(catalog[0].Id, "runtime", "presentation", 1,
                    snapshot.WidgetInstanceId, sequence, "root"), catalog[0], snapshot, new Dictionary<string, BridgeNodeRenderStyles>());
                shell.ApplyLayoutFixture(frame, appearance, catalog);
                shell.ConfigureProductionViewport(new(scale.ActualWidth / zoom, scale.ActualHeight / zoom));
                shell.UpdateLayout();
                await Task.Delay(30);
                var tray = Find("Overlay.Tray")!;
                var guide = Find("Overlay.TrayHelp")!;
                var currentRail = Bounds(tray); var currentGuide = Bounds(guide);
                if (railBounds is { } before) Check(Same(before, currentRail) && Same(guideBounds!.Value, currentGuide),
                    $"rail/guide remain stationary across {mode} at {zoom}/{position}");
                else { railBounds = currentRail; guideBounds = currentGuide; }
                Check(currentGuide.Bottom <= currentRail.Top && currentGuide.Top >= 0, "guide sits above rail within work-area layout");
                var widget = Find("Overlay.Widget")!;
                Check(Bounds(widget).Bottom <= currentGuide.Top + 1, "widget content remains above fixed guide");
            }
        }
        var list = (ListView)Find("Overlay.Tray")!;
        Check(list.ItemsPanel is not null && list.Items.Count == 18, "native ListView owns complete catalog and virtualization");
        var item = (ListViewItem)list.ContainerFromIndex(0);
        Check(Math.Abs(item.ActualWidth - item.ActualHeight) < 1, "native rail item has square bounded geometry");
        Check(Find("Overlay.Recovery")?.Visibility == Visibility.Collapsed, "normal presentation does not reserve a visible recovery/status row");
        Check(Find("Overlay.TrayNext")?.Visibility == Visibility.Visible, "large catalog exposes named overflow control");
        Check(!Descendants(shell).OfType<TextBlock>().Any(text => text.Text == "Widget 0" && IsVisible(text)),
            "rail shows icons while retaining native accessible item names");
        Check(AutomationProperties.GetName(item) == "Widget 0", "icon-only tray preserves accessible catalog identity");
        item.Focus(FocusState.Keyboard);
        var nextButton = (Button)Find("Overlay.TrayNext")!;
        var nextPeer = FrameworkElementAutomationPeer.CreatePeerForElement(nextButton);
        ((IInvokeProvider)nextPeer.GetPattern(PatternInterface.Invoke)).Invoke();
        await Wait(() => list.SelectedIndex > 0);
        Check(list.ContainerFromIndex(list.SelectedIndex) is ListViewItem { FocusState: not FocusState.Unfocused },
            "overflow moves native focus into the next catalog page");
        var previousButton = (Button)Find("Overlay.TrayPrevious")!;
        await Wait(() => previousButton.IsEnabled);
        ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(previousButton).GetPattern(PatternInterface.Invoke)).Invoke();
        await Wait(() => list.SelectedIndex == 0);
        Check(list.ContainerFromIndex(0) is ListViewItem { FocusState: not FocusState.Unfocused },
            "previous overflow restores exact native item focus");
        Check(!Descendants(shell).OfType<FrameworkElement>().Any(element => AutomationProperties.GetAutomationId(element) == "Shell.Close"),
            "production root contains no validation Close control");
        shell.ReportFailure(new InvalidOperationException("internal detail"));
        Check(Find("Overlay.Recovery")?.Visibility == Visibility.Visible &&
            ((TextBlock)Find("Overlay.Status")!).Text != "internal detail", "failures use conditional bounded recovery instead of raw exception chrome");
        checks.AddRange(await shell.ValidateRecoveryGuideFixtureAsync());
        var radialScaleArgument = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith("--radial-fixture-scale=", StringComparison.Ordinal));
        if (radialScaleArgument is not null && double.TryParse(radialScaleArgument.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture, out var radialScale) && radialScale is >= AppearanceSettings.MinimumInterfaceScale and <= AppearanceSettings.MaximumInterfaceScale)
        {
            scale.InterfaceScale = radialScale; scale.UpdateLayout();
            Check(scale.InterfaceScale == radialScale, "requested radial interface scale is applied without clamping");
            var textArgument = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith("--radial-fixture-text-scale=", StringComparison.Ordinal));
            var textScale = textArgument is not null && double.TryParse(textArgument.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture, out var requestedText) ? Math.Clamp(requestedText, 1, 2) : 1;
            shell.ConfigureRadialFixtureScale(radialScale, new(scale.ActualWidth / radialScale, scale.ActualHeight / radialScale), textScale);
        }
        checks.AddRange(await shell.ValidateRadialFixtureAsync());
        Rect Bounds(FrameworkElement element) => element.TransformToVisual(shell).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
    }
    private FrameworkElement? Find(string id) => Descendants(shell).OfType<FrameworkElement>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static bool Same(Rect a, Rect b) => Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1 && Math.Abs(a.Width - b.Width) < 1 && Math.Abs(a.Height - b.Height) < 1;
    private static bool IsVisible(FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }
    private static async Task Wait(Func<bool> condition)
    {
        var end = Environment.TickCount64 + 5000;
        while (!condition()) { if (Environment.TickCount64 > end) throw new TimeoutException("Production native chrome did not settle."); await Task.Delay(16); }
    }
    private void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    private static void Write(object result)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "production-shell-result.json"), JsonSerializer.Serialize(result));
    }
    public ValueTask DisposeAsync() => new(shell.DisposeLayoutFixtureAsync());
}
