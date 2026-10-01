using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class ShellStatusValidationPage : Page, IAsyncDisposable
{
    private readonly TextBlock result = new() { Text = "Checking shared system status…", TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel stage = new() { Spacing = 16 };
    private readonly List<string> checks = [];
    private readonly ShellStatusView view;
    private readonly Queue<TaskCompletionSource<ShellStatusSnapshot>> replies = [];
    private int reads;
    private CancellationToken latestToken;
    private bool started;
    private bool retired;
    private readonly List<ShellStatusView> previews = [];

    internal ShellStatusValidationPage()
    {
        view = new(token => { ++reads; latestToken = token; return replies.Dequeue().Task; }) { Width = 188, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(result, "ShellStatus.Result");
        stage.Children.Add(result); stage.Children.Add(view); Content = stage;
        Loaded += async (_, _) => { if (!started) { started = true; await RunAsync(); } };
    }

    private async Task RunAsync()
    {
        try
        {
            var palette = Palette();
            view.ApplyAppearance(palette, AppearanceSettings.Default, true);
            await Until(() => view.IsLoaded && view.ActualWidth > 0);
            Check(reads == 0 && !view.IsTabStop && !view.IsHitTestVisible, "passive status neither polls hidden content nor steals input");
            var first = Reply(); view.SetActive(true);
            await Until(() => reads == 1);
            first.SetResult(new(ShellInternetStatus.Online, ShellBluetoothStatus.On));
            await Until(() => view.Description.Contains("Internet access") && view.Description.Contains("Bluetooth on"));
            Check(true, "visible status publishes independently from widget content");
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(view)!;
            Check(peer.GetAutomationControlType() == AutomationControlType.Text && peer.GetName() == view.Description,
                "one native accessible status summary includes clock, date and both connectivity states");
            var labels = Descendants<TextBlock>(view).ToArray();
            Check(labels.Any(label => Math.Abs(label.FontSize - 22) < .01) && labels.Any(label => Math.Abs(label.FontSize - 11) < .01),
                "compiled theme resolves separate clock and date typography");
            view.ApplyAppearance(palette, AppearanceSettings.Default with { TextScale = 1.5 }, true);
            await Until(() => labels.Any(label => Math.Abs(label.FontSize - 33) < .01));
            Check(labels.Any(label => Math.Abs(label.FontSize - 16.5) < .01), "clock, date and state marks follow global text scaling");
            view.Width = 100; stage.UpdateLayout();
            await Until(() => Math.Abs(view.ActualWidth - 100) < 1);
            Check(Descendants<FontIcon>(view).All(icon => !AncestorsVisible(icon, view)), "compact status yields symbol space before rail icon capacity");
            Check(Descendants<Viewbox>(view).Single().ActualWidth <= 76.1, "native Viewbox fits the complete clock into compact status");
            var stalled = Reply(); var pending = view.RefreshNowAsync();
            await Until(() => reads == 2);
            view.SetActive(false);
            await pending;
            Check(latestToken.IsCancellationRequested, "hiding cancels pending status observation");
            view.SetActive(true);
            await view.RefreshNowAsync();
            Check(reads == 2, "a stalled provider cannot accumulate duplicate requests across hide and reopen");
            stalled.SetResult(new(ShellInternetStatus.Offline, ShellBluetoothStatus.Off));
            await Task.Yield();
            Check(view.Description.Contains("Bluetooth on"), "late hidden completion cannot overwrite the visible status");
            var third = Reply(); var refreshing = view.RefreshNowAsync();
            await Until(() => reads == 3);
            third.SetResult(new(ShellInternetStatus.Limited, ShellBluetoothStatus.Unavailable));
            await refreshing;
            Check(view.Description.Contains("sign-in required") && view.Description.Contains("not available"), "limited connectivity and missing radio stay distinguishable");
            var ignored = Reply(); _ = view.RefreshNowAsync();
            await Until(() => reads == 4);
            await view.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); retired = true;
            Check(latestToken.IsCancellationRequested && !ignored.Task.IsCompleted, "teardown does not wait indefinitely for an unresponsive status provider");
            ignored.SetException(new IOException("late provider failure"));
            stage.Children.Remove(view);
            foreach (var width in new[] { 188d, 100d })
            {
                var preview = new ShellStatusView(_ => Task.FromResult(new ShellStatusSnapshot(ShellInternetStatus.Online, ShellBluetoothStatus.On)))
                    { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
                preview.ApplyAppearance(palette, AppearanceSettings.Default, true);
                previews.Add(preview); stage.Children.Add(preview); preview.SetActive(true);
            }
            result.Text = $"PASS: {checks.Count} shell status checks";
            Save(null);
        }
        catch (Exception error) { result.Text = "FAIL: " + error.Message; Save(error.ToString()); }
    }

    private TaskCompletionSource<ShellStatusSnapshot> Reply()
    { var source = new TaskCompletionSource<ShellStatusSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously); replies.Enqueue(source); return source; }
    private void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
    private static async Task Until(Func<bool> test)
    { for (var index = 0; index < 200; ++index) { if (test()) return; await Task.Delay(15); } throw new TimeoutException("Native status did not settle."); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); ++i)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static bool AncestorsVisible(UIElement child, UIElement root)
    {
        for (DependencyObject? node = child; node is not null; node = VisualTreeHelper.GetParent(node))
        { if (node is UIElement { Visibility: Visibility.Collapsed }) return false; if (ReferenceEquals(node, root)) break; }
        return true;
    }
    private static IReadOnlyDictionary<string, BridgeNodeRenderStyles> Palette()
    {
        using var stream = typeof(AppearanceSettings).Assembly.GetManifestResourceStream("WidgetRail.PlatformSettings.Themes.builtin-default.wrss")!;
        using var reader = new StreamReader(stream);
        var document = WrssParser.Parse(reader.ReadToEnd(), "default.wrss").Document;
        var theme = WrssThemeCompiler.Compile((WrssThemeLayer[])[new WrssThemeLayer(0, (WrssDocument[])[document])]).Theme!;
        return new[] { "tray", "tray-clock", "tray-date", "tray-status-icon" }.ToDictionary(role => role, role =>
        {
            var computed = theme.Resolve(new(role == "tray" ? "tray" : "status", "shell." + role,
                new HashSet<string> { "wrail-" + role }, new HashSet<WrssPseudoState>()));
            var map = computed.Properties.ToDictionary(pair => pair.Key, pair => new BridgeComputedStyleValue
            { Kind = pair.Value.Kind, Text = pair.Value.Text, Number = pair.Value.Number, Unit = pair.Value.Unit });
            return new BridgeNodeRenderStyles { Base = map, Focused = map, Pressed = map };
        });
    }
    private void Save(string? error)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "shell-status-result.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, passed = error is null, checks, error }));
    }
    public async ValueTask DisposeAsync()
    {
        if (!retired) { retired = true; await view.DisposeAsync(); }
        foreach (var preview in previews) await preview.DisposeAsync();
        previews.Clear();
    }
}
