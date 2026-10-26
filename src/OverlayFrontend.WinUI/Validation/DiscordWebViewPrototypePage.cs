using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAppLibraryProvider;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

// Deliberately isolated development gate, not a new widget-facing browser API.
internal sealed partial class DiscordWebViewPrototypePage : Page, IAsyncDisposable
{
    private readonly WebView2 web = new();
    private readonly TextBlock status = new() { Text = "Opening isolated Discord profile…", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox search = new() { PlaceholderText = "Search voice channels" };
    private readonly ComboBox channels = new() { Header = "Voice channel", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button join = new() { Content = "Join selected channel" }, leave = new() { Content = "Disconnect" };
    private readonly Button mute = new() { Content = "Mute" }, deafen = new() { Content = "Deafen" };
    private readonly Button share = new() { Content = "Share application…" }, stop = new() { Content = "Stop sharing" };
    private readonly Button microphone = new() { Content = "Enable microphone for this session" };
    private readonly ComboBox streams = new() { Header = "Incoming streams", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button watch = new() { Content = "Open floating stream" };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly string assets, owner = Guid.NewGuid().ToString("N");
    private readonly WindowsTaskWindowCatalog windows = new();
    private WebViewLiveCapture? capture;
    private DiscordIncomingProbe? incoming;
    private string? incomingLease, incomingAccount, incomingChannel;
    private string? captureAccount, captureChannel;
    private JsonElement state;
    private bool disposed, reading, busy, initialized, microphoneAllowed;
    private string? failureText;
    private readonly List<Channel> channelData = [];
    private sealed record Channel(string Id, string Guild, string Name) { public override string ToString() => Guild + " / " + Name; }
    private sealed record WindowChoice(TaskWindowSummary Window) { public override string ToString() => Window.ApplicationName + " — " + Window.Title; }
    private sealed record StreamChoice(string Key, string Name) { public override string ToString() => Name; }
    internal DiscordWebViewPrototypePage(string assets)
    {
        this.assets = Path.GetFullPath(assets);
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(16), Children =
            { new TextBlock { Text = "Discord WebView2 prototype", FontSize = 20 }, status, search, channels, join,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { mute, deafen } }, leave, share, stop, streams, watch, microphone } };
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(340) }); grid.ColumnDefinitions.Add(new());
        grid.Children.Add(new ScrollViewer { Content = panel }); Grid.SetColumn(web, 1); grid.Children.Add(web); Content = grid;
        Loaded += async (_, _) => { if (initialized) return; initialized = true; try { await InitializeAsync(); } catch (Exception e) { Failure("Initialize", e); } };
        search.TextChanged += (_, _) => FilterChannels();
        join.Click += async (_, _) =>
        {
            if (!microphoneAllowed) { status.Text = "Enable microphone for this session before joining. You can remain muted in the call."; microphone.Focus(FocusState.Programmatic); return; }
            if (channels.SelectedItem is Channel c) await ActAsync("join", new { account = Account, target = c.Id });
        };
        leave.Click += async (_, _) => await ActAsync("leave", CallArgs);
        mute.Click += async (_, _) => await ActAsync("mute", new { account = Account, channelId = ChannelId, value = !Flag("muted") });
        deafen.Click += async (_, _) => await ActAsync("deafen", new { account = Account, channelId = ChannelId, value = !Flag("deafened") });
        share.Click += async (_, _) => await ChooseCaptureAsync();
        watch.Click += async (_, _) => await WatchAsync();
        stop.Click += async (_, _) => await ActAsync("stopShare", CallArgs);
        timer.Tick += async (_, _) => await RefreshAsync();
        microphone.Click += async (_, _) =>
        {
            if (microphoneAllowed || busy || disposed) return;
            try
            {
                await web.CoreWebView2.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Microphone, "https://discord.com", CoreWebView2PermissionState.Default);
                microphoneAllowed = true; microphone.Content = "Microphone enabled for this session";
                status.Text = "Microphone enabled. Join your channel; Mute controls transmission.";
            }
            catch (Exception e) { Failure("Microphone", e); }
        };
        SetEnabled(false);
    }
    private string? Account => String("account");
    private string? ChannelId => String("channelId");
    private object CallArgs => new { account = Account, channelId = ChannelId };
    private string? String(string key) => state.ValueKind == JsonValueKind.Object && state.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private bool Flag(string key) => state.ValueKind == JsonValueKind.Object && state.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private async Task InitializeAsync()
    {
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(assets, "discord-profile"), null);
        await web.EnsureCoreWebView2Async(environment);
        var core = web.CoreWebView2;
        core.NavigationStarting += (_, e) => { if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) || u.Scheme != "https" || u.Host != "discord.com") e.Cancel = true; };
        core.NewWindowRequested += (_, e) => { e.Handled = true; status.Text = "External links are disabled in this isolated prototype."; };
        core.PermissionRequested += (_, e) =>
        {
            e.State = microphoneAllowed && e.PermissionKind == CoreWebView2PermissionKind.Microphone &&
                Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.GetLeftPart(UriPartial.Authority) == "https://discord.com"
                ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            e.SavesInProfile = false; e.Handled = true;
            File.WriteAllText(Path.Combine(assets, "permission-result.json"), JsonSerializer.Serialize(new { kind = e.PermissionKind.ToString(), allowed = e.State == CoreWebView2PermissionState.Allow }));
        };
        core.ScreenCaptureStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.DownloadStarting += (_, e) => e.Cancel = true;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(await File.ReadAllTextAsync(Path.Combine(assets, "native-media.js")));
        core.NavigationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess || disposed) return;
            try
            {
                await core.ExecuteScriptAsync(await File.ReadAllTextAsync(Path.Combine(assets, "webpack.js")));
                await core.ExecuteScriptAsync(await File.ReadAllTextAsync(Path.Combine(assets, "discord-adapter.js")));
                timer.Start(); await RefreshAsync();
            }
            catch (Exception error) { Failure("Adapter", error); }
        };
        capture = new(core, DispatcherQueue, "https://discord.com");
        core.Navigate("https://discord.com/app");
    }
    // CDP is local in-process; no remote-debugging socket or browser token export.
    private async Task<JsonElement> EvaluateAsync(string expression)
    {
        using var response = JsonDocument.Parse(await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true })));
        if (response.RootElement.TryGetProperty("exceptionDetails", out _)) throw new InvalidOperationException("Page adapter failed.");
        return response.RootElement.GetProperty("result").GetProperty("value").Clone();
    }
    private Task<JsonElement> InvokeAsync(string method, object? args = null) => EvaluateAsync(
        "WidgetRailDiscordPrototype.invoke(" + JsonSerializer.Serialize(new { method, args, owner }) + ")");
    private async Task RefreshAsync()
    {
        if (reading || busy || disposed) return;
        reading = true;
        try
        {
            var result = await InvokeAsync("snapshot");
            if (!result.GetProperty("ok").GetBoolean()) throw new InvalidOperationException("Adapter unavailable.");
            state = result.GetProperty("value").Clone();
            var media = await EvaluateAsync("WidgetRailNativeMedia.keep()");
            var discovery = await EvaluateAsync("WidgetRailWebWebpack.diagnostics()");
            var ready = Flag("ready");
            status.Text = !ready ? "Sign in to Discord in this separate profile." : ChannelId is null ? "Connected. Choose a voice channel." : "Voice: " + String("channelName") + (Flag("sharing") ? " · Sharing" : "");
            if (String("shareError") is { } shareError) failureText = "Share: " + shareError;
            if (failureText is not null) status.Text += "\n" + failureText;
            channelData.Clear();
            foreach (var c in state.GetProperty("channels").EnumerateArray()) channelData.Add(new(c.GetProperty("id").GetString()!, c.GetProperty("guild").GetString()!, c.GetProperty("name").GetString()!));
            FilterChannels(); SetEnabled(ready);
            var nextStreams = state.GetProperty("streams").EnumerateArray().Select(s => new StreamChoice(s.GetProperty("key").GetString()!, s.GetProperty("name").GetString()!)).ToArray();
            if (!streams.Items.Cast<StreamChoice>().SequenceEqual(nextStreams))
            {
                var selectedKey = (streams.SelectedItem as StreamChoice)?.Key;
                streams.Items.Clear(); foreach (var stream in nextStreams) streams.Items.Add(stream);
                streams.SelectedItem = nextStreams.FirstOrDefault(s => s.Key == selectedKey) ?? nextStreams.FirstOrDefault();
            }
            watch.IsEnabled = !busy && nextStreams.Length > 0;
            JsonElement? incomingStats = null;
            if (incoming is not null && incomingLease is not null)
            {
                if (Account != incomingAccount || ChannelId != incomingChannel) await StopIncomingAsync();
                else
                {
                    await InvokeAsync("keep", new { account = incomingAccount, channelId = incomingChannel, lease = incomingLease });
                    incomingStats = await incoming.StatusAsync();
                }
            }
            mute.Content = Flag("muted") ? "Unmute" : "Mute"; deafen.Content = Flag("deafened") ? "Undeafen" : "Deafen";
            if (capture?.HasEnded == true)
            {
                if (Flag("sharing")) await InvokeAsync("stopShare", CallArgs);
                await capture.StopAsync();
                status.Text = "Source capture ended. Choose Share application to retry.";
            }
            if (capture?.IsRunning == true && (Account != captureAccount || ChannelId != captureChannel)) await capture.StopAsync();
            // No account, guild/channel names, credentials or window titles in diagnostics.
            File.WriteAllText(Path.Combine(assets, "discord-result.json"), JsonSerializer.Serialize(new { ready, inCall = ChannelId is not null,
                sharing = Flag("sharing"), muted = Flag("muted"), capabilities = state.GetProperty("capabilities"), channelCount = channelData.Count,
                capture = capture?.Statistics, media, discovery, incoming = incomingStats,
                layout = new { ActualWidth, ActualHeight, IsLoaded, visible = XamlRoot?.IsHostVisible,
                    windowVisible = App.Window.AppWindow.IsVisible, webWidth = web.ActualWidth, webHeight = web.ActualHeight } }));
        }
        catch (Exception e) { SetEnabled(false); status.Text = $"Reconnecting… {e.GetType().Name}"; }
        finally { reading = false; }
    }
    private void FilterChannels()
    {
        var selected = (channels.SelectedItem as Channel)?.Id;
        var matching = channelData.Where(c => c.ToString().Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (channels.Items.Cast<Channel>().SequenceEqual(matching)) return;
        channels.Items.Clear(); foreach (var c in matching) channels.Items.Add(c);
        channels.SelectedItem = matching.FirstOrDefault(c => c.Id == selected);
        if (channels.SelectedItem is null && matching.Length > 0) channels.SelectedIndex = 0;
    }
    private void SetEnabled(bool ready)
    {
        // Keep the focused control attached/enabled during a command. Each
        // handler rejects duplicates; disabling it transfers keyboard focus to
        // an unrelated enabled action (including microphone consent).
        if (busy) { status.Text = "Working…"; return; }
        bool Cap(string key) => state.ValueKind == JsonValueKind.Object && state.TryGetProperty("capabilities", out var caps) && caps.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
        join.IsEnabled = ready && !busy && Cap("voice"); leave.IsEnabled = ready && ChannelId is not null && !busy && Cap("voice");
        mute.IsEnabled = ready && ChannelId is not null && !busy && Cap("mute"); deafen.IsEnabled = ready && ChannelId is not null && !busy && Cap("deafen");
        share.IsEnabled = ready && ChannelId is not null && !busy && Cap("share") && !Flag("sharing") && !Flag("sharePending");
        stop.IsEnabled = ready && (Flag("sharing") || capture?.IsRunning == true) && !busy;
        watch.IsEnabled = ready && !busy && streams.Items.Count > 0 && Cap("watch");
    }
    private async Task ActAsync(string method, object args)
    {
        if (busy || disposed) return; busy = true; failureText = null; SetEnabled(false);
        try
        {
            var r = await InvokeAsync(method, args);
            if (!r.GetProperty("ok").GetBoolean()) failureText = "Action unavailable: " + r.GetProperty("code").GetString();
            if (method is "stopShare" or "leave") { if (capture is not null) await capture.StopAsync(); }
            if (method == "leave") await StopIncomingAsync();
        }
        catch (Exception e) { Failure("Action", e); }
        finally { busy = false; await RefreshAsync(); }
    }
    private async Task ChooseCaptureAsync()
    {
        if (busy || disposed || ChannelId is null) return;
        var account = Account; var channel = ChannelId;
        busy = true; SetEnabled(false);
        try
        {
            var list = new ComboBox { Header = "Application window", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var w in await windows.GetWindowsAsync(CancellationToken.None))
                if (!w.IsMinimized && w.PreviewTarget is { } t && t.ProcessId != Environment.ProcessId) list.Items.Add(new WindowChoice(w));
            var audio = new ToggleSwitch { Header = "Share application audio", IsOn = false };
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Share application", PrimaryButtonText = "Start sharing", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false,
                Content = new StackPanel { Spacing = 16, Children = { list, audio } } };
            list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is WindowChoice;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || list.SelectedItem is not WindowChoice choice || disposed) return;
            // The adapter rechecks account/call after the user closes the chooser.
            captureAccount = account; captureChannel = channel;
            await capture!.StartAsync(choice.Window.PreviewTarget!, audio.IsOn);
            var result = await InvokeAsync("share", new { account, channelId = channel });
            if (!result.GetProperty("ok").GetBoolean()) { await capture.StopAsync(); failureText = "Sharing unavailable: " + result.GetProperty("code").GetString(); }
        }
        catch (Exception e) { if (capture is not null) await capture.StopAsync(); Failure("Share", e); }
        finally { busy = false; await RefreshAsync(); }
    }
    private async Task WatchAsync()
    {
        if (busy || disposed || streams.SelectedItem is not StreamChoice selected) return;
        busy = true; failureText = null; SetEnabled(false);
        try
        {
            await StopIncomingAsync();
            incomingLease = Guid.NewGuid().ToString("N"); incomingAccount = Account; incomingChannel = ChannelId;
            var offer = await InvokeAsync("watch", new { account = incomingAccount, channelId = incomingChannel, key = selected.Key, lease = incomingLease });
            if (!offer.GetProperty("ok").GetBoolean()) throw new InvalidOperationException("Incoming stream is unavailable.");
            var receiver = incoming = new DiscordIncomingProbe();
            receiver.Closed += async () => { if (ReferenceEquals(incoming, receiver)) await StopIncomingAsync(); };
            var answer = await receiver.OpenAsync(assets, web.CoreWebView2.Environment, offer.GetProperty("value").GetProperty("offer"));
            var accepted = await InvokeAsync("answer", new { account = incomingAccount, channelId = incomingChannel, lease = incomingLease, answer });
            if (!accepted.GetProperty("ok").GetBoolean()) throw new InvalidOperationException("Incoming stream changed during connection.");
        }
        catch (Exception e) { await StopIncomingAsync(); Failure("Watch", e); }
        finally { busy = false; await RefreshAsync(); }
    }
    private async Task StopIncomingAsync()
    {
        var receiver = incoming; var lease = incomingLease; var account = incomingAccount; var channel = incomingChannel;
        incoming = null; incomingLease = incomingAccount = incomingChannel = null;
        if (receiver is not null) await receiver.DisposeAsync();
        if (lease is not null)
        {
            try { await InvokeAsync("unwatch", new { account, channelId = channel, lease }); }
            catch (Exception e) { Failure("Stop viewing", e); }
        }
    }
    private void Failure(string action, Exception e) { failureText = $"{action}: {e.GetType().Name} (0x{e.HResult:X8})"; status.Text = failureText; }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; timer.Stop();
        await StopIncomingAsync();
        if (capture is not null) await capture.DisposeAsync();
        web.Close();
    }
}
