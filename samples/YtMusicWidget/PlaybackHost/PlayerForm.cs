using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WidgetRail.YtMusicPlaybackHost;

internal sealed class PlayerForm : Form
{
    private const string Page = "https://ytmusic.widgetrail.internal/player";
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Action<object> _emit;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _profile;
    private Uri? _media;
    private long _generation;
    private Process? _browser;
    private bool _stopping;

    internal PlayerForm(Action<object> emit, string profile)
    {
        _emit = emit;
        _profile = profile;
        ClientSize = new Size(1, 1);
        Location = new Point(-32000, -32000);
        ShowInTaskbar = false;
        Opacity = 0;
        FormBorderStyle = FormBorderStyle.None;
        Controls.Add(_web);
        Shown += async (_, _) => await InitializeAsync();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, _profile);
            await _web.EnsureCoreWebView2Async(environment);
            var core = _web.CoreWebView2;
            _browser = Process.GetProcessById(checked((int)core.BrowserProcessId));
            _ = _browser.SafeHandle; // Retain the owned process identity, not a reusable PID.
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (args.Request.Uri == Page)
                {
                    var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("YtMusicPlaybackHost.player.html")!;
                    args.Response = environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
                }
                else if (_media is null || !Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) || uri != _media)
                    args.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            };
            core.NavigationStarting += (_, args) => args.Cancel = args.Uri != Page;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) =>
            {
                args.State = args.PermissionKind == CoreWebView2PermissionKind.Autoplay && args.Uri.StartsWith("https://ytmusic.widgetrail.internal/", StringComparison.Ordinal)
                    ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                args.SavesInProfile = false;
                args.Handled = true;
            };
            core.ServerCertificateErrorDetected += (_, args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            core.ProcessFailed += (_, _) => { _emit(new { @event = "failed" }); Stop(); };
            core.WebMessageReceived += (_, args) =>
            {
                if (args.Source != Page) return;
                using var document = JsonDocument.Parse(args.WebMessageAsJson);
                var value = document.RootElement;
                if (value.TryGetProperty("event", out var kind) && kind.GetString() == "ready") _ready.TrySetResult();
                else _emit(value.Clone());
            };
            await core.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Autoplay,
                "https://ytmusic.widgetrail.internal", CoreWebView2PermissionState.Allow);
            core.Navigate(Page);
        }
        catch (Exception) { _ready.TrySetException(new IOException("Player initialization failed.")); _emit(new { @event = "failed" }); }
    }

    internal async void HandleRequest(JsonElement request)
    {
        var id = request.GetProperty("id").GetInt64();
        try
        {
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(25));
            var method = request.GetProperty("method").GetString();
            var payload = request.GetProperty("args");
            if (method == "ready") { _emit(new { id, result = new { } }); return; }
            if (method == "load")
            {
                var generation = payload.GetProperty("generation").GetInt64();
                if (generation < _generation) { _emit(new { id, result = new { } }); return; }
                if (!Uri.TryCreate(payload.GetProperty("url").GetString(), UriKind.Absolute, out var uri) ||
                    uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port <= 0 || uri.UserInfo.Length != 0)
                    throw new IOException("Invalid stream source.");
                _media = uri;
                _generation = generation;
            }
            else if (method is not ("toggle" or "play" or "pause" or "seek" or "volume" or "stop"))
                throw new IOException("Unknown playback command.");
            _web.CoreWebView2.PostWebMessageAsJson(request.GetRawText());
        }
        catch (Exception) { _emit(new { id, error = "player_unavailable" }); }
    }

    internal async void Stop()
    {
        if (_stopping) return;
        _stopping = true;
        _web.Dispose();
        // Drain Chromium before exiting; the parent then removes the profile after
        // this process releases WebView2's own remaining file handles.
        if (_browser is not null)
        {
            try { await _browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException)
            {
                // This browser belongs to our unique profile and has no other widget views.
                try { _browser.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await _browser.WaitForExitAsync();
            }
        }
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _web.Dispose();
            _browser?.Dispose();
            // The parent deletes its unique profile after this process has released all handles.
        }
        base.Dispose(disposing);
    }
}
