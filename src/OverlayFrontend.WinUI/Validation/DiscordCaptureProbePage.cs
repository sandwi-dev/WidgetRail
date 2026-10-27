using WidgetRail.OverlayFrontend.WinUI.Browser.Capture;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Web.WebView2.Core;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAppLibraryProvider;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class DiscordCaptureProbePage : Page, IAsyncDisposable
{
    private readonly WebView2 web = new();
    private readonly ComboBox sources = new() { Header = "Application window", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleSwitch audio = new() { Header = "Share application audio" };
    private readonly ComboBox quality = new() { Header="Stream quality",HorizontalAlignment=HorizontalAlignment.Stretch,
        ItemsSource=LiveCaptureQuality.Supported,SelectedItem=LiveCaptureQuality.Default };
    private readonly Button start = new() { Content = "Start", IsEnabled = false }, stop = new() { Content = "Stop", IsEnabled = false };
    private readonly TextBlock status = new() { Text = "Preparing local WebRTC test…", TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly WindowsTaskWindowCatalog windows = new();
    private readonly string assets;
    private IWebViewCaptureTransport? capture;
    private bool ready, initialized, busy, reading, disposed;
    private DateTimeOffset? stopAfter;
    private readonly bool selfTest = Environment.GetCommandLineArgs().Contains("--capture-probe-selftest");
    private sealed record Choice(TaskWindowSummary Window) { public override string ToString() => Window.ApplicationName + " — " + Window.Title; }
    internal DiscordCaptureProbePage(string assets)
    {
        this.assets = Path.GetFullPath(assets);
        var refresh = new Button { Content = "Refresh applications" };
        var root = new Grid { Padding = new Thickness(16), RowSpacing = 12 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
        AutomationProperties.SetAutomationId(quality,"CaptureProbe.Quality");
        root.Children.Add(new StackPanel { Spacing = 12, Children = { sources, audio, quality,
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
        busy = true; start.IsEnabled = stop.IsEnabled = sources.IsEnabled = audio.IsEnabled = quality.IsEnabled = false;
        try { await action(); }
        catch (Exception e)
        {
            status.Text = $"{e.GetType().Name}: {e.Message}";
            if(selfTest)File.WriteAllText(Path.Combine(assets,"probe-error.json"),JsonSerializer.Serialize(new{type=e.GetType().Name,message=e.Message}));
        }
        finally { busy = false; start.IsEnabled = ready && !disposed; stop.IsEnabled = capture?.IsRunning == true; sources.IsEnabled = audio.IsEnabled = !disposed; quality.IsEnabled=!disposed&&capture?.IsRunning!=true; }
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
        capture = Environment.GetCommandLineArgs().Contains("--capture-probe-worker")
            ? new WebViewCaptureWorker(core,DispatcherQueue,"https://capture-probe.widgetrail.test",testBlockedExit:selfTest&&Environment.GetCommandLineArgs().Contains("--capture-probe-worker-block-stop"))
            : new WebViewLiveCapture(core, DispatcherQueue, "https://capture-probe.widgetrail.test");
        core.Navigate("https://capture-probe.widgetrail.test/capture.html");
        ready = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await RefreshAsync(); timer.Start();
        status.Text = "Choose an application. Media stays in this local WebRTC test.";
        if (selfTest)
        {
            var identity = CaptureProbeSourceIdentity.FromArguments(Environment.GetCommandLineArgs());
            Choice? fixture = null;
            var sourceDeadline = Environment.TickCount64 + 10000;
            while (fixture is null)
            {
                fixture = sources.Items.Cast<Choice>().SingleOrDefault(c => identity.Matches(c.Window.PreviewTarget));
                if (fixture is not null) break;
                if (Environment.TickCount64 >= sourceDeadline) throw new InvalidOperationException("The exact runner-owned capture source is unavailable.");
                await Task.Delay(50); await RefreshAsync();
            }
            sources.SelectedItem = fixture;
            audio.IsOn = Environment.GetCommandLineArgs().Contains("--capture-probe-audio");
            var qualityArgument=Environment.GetCommandLineArgs().SingleOrDefault(a=>a.StartsWith("--capture-probe-quality=",StringComparison.Ordinal));
            if(qualityArgument is not null)quality.SelectedItem=LiveCaptureQuality.FromId(qualityArgument["--capture-probe-quality=".Length..]);
            await StartAsync();
            var stopArgument=Environment.GetCommandLineArgs().SingleOrDefault(a=>a.StartsWith("--capture-probe-stop-after=",StringComparison.Ordinal));
            if(stopArgument is not null)
            {
                if(!int.TryParse(stopArgument["--capture-probe-stop-after=".Length..],out var seconds)||seconds is <1 or >610)
                    throw new InvalidOperationException("Invalid synthetic stop deadline.");
                stopAfter=DateTimeOffset.UtcNow.AddSeconds(seconds);
            }
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
        if(quality.SelectedItem is not LiveCaptureQuality selectedQuality)throw new InvalidOperationException("Select a supported capture quality.");
        var content=Environment.GetCommandLineArgs().SingleOrDefault(argument=>argument.StartsWith("--capture-probe-content=",StringComparison.Ordinal));
        if(content is not null)selectedQuality=selectedQuality with{ContentPreference=content["--capture-probe-content=".Length..] switch
            {"motion"=>WidgetRail.WidgetProtocol.WebCaptureContentPreference.Motion,"detail"=>WidgetRail.WidgetProtocol.WebCaptureContentPreference.Detail,
                _=>throw new InvalidOperationException("Invalid synthetic content preference.")}};
        await capture!.StartAsync(target, audio.IsOn, selectedQuality);
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
            var snapshot=Path.Combine(assets,"capture-result.json");
            File.WriteAllText(snapshot+".tmp",JsonSerializer.Serialize(new { capture = capture?.Statistics, receiver = result.RootElement }));
            File.Move(snapshot+".tmp",snapshot,true);
            if(selfTest&&stopAfter is { } deadline&&DateTimeOffset.UtcNow>=deadline)
            {
                stopAfter=null;var started=System.Diagnostics.Stopwatch.StartNew();await StopAsync();
                var path=Path.Combine(assets,"stop-result.json");
                File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new{milliseconds=started.ElapsedMilliseconds,capture=capture?.Statistics}));
                File.Move(path+".tmp",path,true);
            }
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
