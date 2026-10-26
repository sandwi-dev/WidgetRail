using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAppLibraryProvider;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class DiscordCaptureProbePage : Page, IAsyncDisposable
{
    private readonly WebView2 web = new();
    private readonly ComboBox sources = new() { Header = "Application window", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleSwitch audio = new() { Header = "Share application audio" };
    private readonly Button start = new() { Content = "Start", IsEnabled = false }, stop = new() { Content = "Stop", IsEnabled = false };
    private readonly TextBlock status = new() { Text = "Preparing local WebRTC test…", TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly WindowsTaskWindowCatalog windows = new();
    private readonly string assets;
    private WebViewLiveCapture? capture;
    private bool ready, initialized, busy, reading, disposed;
    private readonly bool selfTest = Environment.GetCommandLineArgs().Contains("--capture-probe-selftest");
    private sealed record Choice(TaskWindowSummary Window) { public override string ToString() => Window.ApplicationName + " — " + Window.Title; }
    internal DiscordCaptureProbePage(string assets)
    {
        this.assets = Path.GetFullPath(assets);
        var refresh = new Button { Content = "Refresh applications" };
        var root = new Grid { Padding = new Thickness(16), RowSpacing = 12 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
        root.Children.Add(new StackPanel { Spacing = 12, Children = { sources, audio,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { refresh, start, stop } }, status } });
        Grid.SetRow(web, 1); root.Children.Add(web); Content = root;
        Loaded += async (_, _) => { if (initialized) return; initialized = true; await GuardAsync(InitializeAsync); };
        refresh.Click += async (_, _) => await GuardAsync(RefreshAsync);
        start.Click += async (_, _) => await GuardAsync(StartAsync);
        stop.Click += async (_, _) => await GuardAsync(StopAsync);
        timer.Tick += async (_, _) => await ReadStatsAsync();
    }
    private async Task GuardAsync(Func<Task> action)
    {
        if (busy || disposed) return;
        busy = true; start.IsEnabled = stop.IsEnabled = sources.IsEnabled = audio.IsEnabled = false;
        try { await action(); }
        catch (Exception e) { status.Text = $"{e.GetType().Name}: {e.Message}"; }
        finally { busy = false; start.IsEnabled = ready && !disposed; stop.IsEnabled = capture?.IsRunning == true; sources.IsEnabled = audio.IsEnabled = !disposed; }
    }
    private async Task InitializeAsync()
    {
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(assets, "profile"), null);
        await web.EnsureCoreWebView2Async(environment);
        var core = web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("capture-probe.widgetrail.test", assets, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, e) => e.Cancel = e.Uri != "https://capture-probe.widgetrail.test/capture.html";
        core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        core.WebMessageReceived += (_, e) =>
        {
            if (e.Source != "https://capture-probe.widgetrail.test/capture.html") return;
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (doc.RootElement.TryGetProperty("ready", out var value)) loaded.TrySetResult(value.GetBoolean());
        };
        capture = new(core, DispatcherQueue, "https://capture-probe.widgetrail.test");
        core.Navigate("https://capture-probe.widgetrail.test/capture.html");
        ready = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await RefreshAsync(); timer.Start();
        status.Text = "Choose an application. Media stays in this local WebRTC test.";
        if (selfTest)
        {
            var fixture = sources.Items.Cast<Choice>().SingleOrDefault(c => c.Window.Title == "WidgetRail synthetic capture target");
            if (fixture is null) throw new InvalidOperationException("The synthetic capture fixture must be running.");
            sources.SelectedItem = fixture;
            audio.IsOn = Environment.GetCommandLineArgs().Contains("--capture-probe-audio");
            await StartAsync();
        }
    }
    private async Task RefreshAsync()
    {
        if (capture?.IsRunning == true) return;
        sources.Items.Clear();
        foreach (var window in await windows.GetWindowsAsync(CancellationToken.None))
            if (!window.IsMinimized && window.PreviewTarget?.ProcessId != Environment.ProcessId) sources.Items.Add(new Choice(window));
    }
    private async Task StartAsync()
    {
        if (!ready || sources.SelectedItem is not Choice choice || choice.Window.PreviewTarget is not { } target) return;
        await capture!.StartAsync(target, audio.IsOn);
        var result = await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = "captureProbe.connect()", awaitPromise = true, returnByValue = true }));
        using var parsed = JsonDocument.Parse(result);
        if (parsed.RootElement.TryGetProperty("exceptionDetails", out _)) { await capture.StopAsync(); throw new InvalidOperationException("WebRTC receiver rejected the stream."); }
        status.Text = "Capturing selected window to local WebRTC receiver. No Discord transmission.";
    }
    private async Task StopAsync()
    {
        if (capture is not null) await capture.StopAsync();
        await web.CoreWebView2.ExecuteScriptAsync("captureProbe.stop()"); status.Text = "Capture stopped.";
    }
    private async Task ReadStatsAsync()
    {
        if (!ready || disposed || reading || busy) return; reading = true;
        try
        {
            using var result = JsonDocument.Parse(await web.CoreWebView2.ExecuteScriptAsync("captureProbe.status()"));
            File.WriteAllText(Path.Combine(assets, "capture-result.json"), JsonSerializer.Serialize(new { capture = capture?.Statistics, receiver = result.RootElement }));
            if (capture?.HasEnded == true) { await StopAsync(); status.Text = "Source capture ended. Choose Start to retry."; }
        }
        catch (Exception e) { status.Text = "Statistics: " + e.GetType().Name; }
        finally { reading = false; }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; timer.Stop();
        if (capture is not null) await capture.DisposeAsync(); web.Close();
    }
}
