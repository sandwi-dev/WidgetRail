using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class EmbeddedMediaValidationPage
{
    private bool galleryReopen;

    private ViewNode GalleryReopenRoot()
    {
        var root = new ViewNode { Id = "page", InputScopeId = "page", Kind = ViewNodeKind.Stack };
        if (declaration is null) return root;
        var source = MediaPlayerSource.EmbeddedSession(declaration.Id, "fixture");
        var media = new MediaPlayerDefinition(source, new(AutoPlay: true, Muted: true))
        {
            Gallery = (MediaPlayerGalleryItem[])[new("trailer", "Trailer", source) { PublicThumbnail = "https://example.invalid/poster.png" }],
            InlineCarousel = true,
        };
        return root with { Children = (ViewNode[])[new() { Id = "gallery", Kind = ViewNodeKind.MediaPlayer, MediaPlayer = media, AccessibilityLabel = "Trailer gallery" }] };
    }

    private async Task CheckGalleryReopenAsync(EmbeddedMediaSession template)
    {
        galleryReopen = true;
        parkingLayer = new();
        ((StackPanel)Content).Children.Add(parkingLayer);
        mediaOwner = new(session!, parkingLayer, runtime) { Diagnostic = (_, code) => diagnostics.Add(code) };
        ownerPresenter = new() { Session = session, MediaOwner = mediaOwner, Failed = error => throw error };
        viewport.Children.Add(ownerPresenter);
        mediaOwner.SetHostState(Descriptor.Id, true, true);
        var cycles = 0;
        var settledBrowserPids = new List<uint>();
        var openTimes = new List<long>();
        async Task Open()
        {
            declaration = template with { Id = "gallery-session-" + ++cycles };
            ownerPresenter.Apply(await SnapshotAsync());
        }
        async Task Close()
        {
            declaration = null;
            ownerPresenter.Apply(await SnapshotAsync());
            Check(mediaOwner.ResidentCount == 0, "Closed gallery released media ownership " + cycles);
        }
        async Task Settle()
        {
            var timer = Stopwatch.StartNew();
            await Until(() => events.Any(e => e.SessionId == declaration!.Id && e.MediaKey == "fixture" && e.State == EmbeddedMediaPlaybackState.Ready));
            var browser = mediaOwner.BrowserForValidation(Descriptor.Id)!;
#pragma warning disable WUI4001 // The adapter acknowledged the load on this controller.
            var core = browser.CoreWebView2;
#pragma warning restore WUI4001
            settledBrowserPids.Add(core.BrowserProcessId);
            var playing = await core.ExecuteScriptAsync("document.querySelector('audio').muted=true;document.querySelector('audio').play();true");
            await Task.Delay(200);
            Check(playing == "true" && await core.ExecuteScriptAsync("!document.querySelector('audio').paused && document.querySelector('audio').currentTime>0") == "true",
                "Reopened gallery plays local media " + cycles);
            openTimes.Add(timer.ElapsedMilliseconds);
        }
        await Open(); await Settle(); await Close();
        for (var i = 0; i < 32; ++i)
        {
            await Open();
            await Task.Delay((i % 4) switch { 0 => 1, 1 => 20, 2 => 65, _ => 140 });
            await Close();
        }
        for (var i = 0; i < 4; ++i) { await Open(); await Settle(); await Close(); }
        // Reopen on both sides of WebView2's asynchronous last-controller shutdown.
        foreach (var delay in new[] { 150, 250, 300, 500, 900 })
        {
            await Task.Delay(delay, lifetime.Token);
            await Open(); await Settle(); await Close();
        }
        await mediaOwner.RetireWidgetAsync(Descriptor.Id);
        var idle = Stopwatch.StartNew();
        try
        {
            using var browser = Process.GetProcessById(checked((int)settledBrowserPids[^1]));
            await browser.WaitForExitAsync(lifetime.Token).WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (ArgumentException) { /* The last controller's browser already exited. */ }
        var idleExitMs = idle.ElapsedMilliseconds;
        await Open(); await Settle(); await Close();
        await mediaOwner.RetireWidgetAsync(Descriptor.Id);
        Check(parkingLayer.Children.Count == 0 && mediaOwner.GetFailure(Descriptor.Id) is null, "Final gallery close drains all controllers");
        Check(!diagnostics.Any(s => s.StartsWith("media-process ") || s is "media-retirement-failed" or "media-session-capacity" or "media-document-admission-failed"), "No process fault, capacity failure or failed retirement");
        Write(new { passed = true, phase = "gallery-closed", cycles, creations = mediaOwner.BrowserCreationCount, settledBrowserPids, openTimes, idleExitMs, checks, diagnostics });
    }
}
