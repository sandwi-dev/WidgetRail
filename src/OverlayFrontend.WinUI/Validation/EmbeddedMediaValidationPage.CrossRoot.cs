using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class EmbeddedMediaValidationPage
{
    private readonly List<object> transferObservations = [];

    // Deliberately outside the production owner: this determines whether the
    // native control can retain one document across windows before admitting a
    // production compact-pin presentation. No provider or user profile is used.
    private async Task RunCrossRootProbeAsync()
    {
        var browser = (WebView2)surface!.Element;
#pragma warning disable WUI4001 // The sealed fixture awaited this surface's initialization and typed Play above.
        var core = browser.CoreWebView2;
#pragma warning restore WUI4001
        var originalRoot = browser.XamlRoot;
        var resolutions = resolveCount;
        var navigationCount = 0;
        var coreInitializations = 0;
        var documentToken = Guid.NewGuid().ToString("N");
        var peerHost = new Grid();
        PinnedWidgetWindow? peer = null;
        var peerClosed = false;
        Exception? returnFailure = null;
        void Navigating(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args) => ++navigationCount;
        void Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args) => ++coreInitializations;
        core.NavigationStarting += Navigating;
        browser.CoreWebView2Initialized += Initialized;
        try
        {
            // Only the local synthetic document receives instrumentation. The
            // public renderer never executes widget-authored diagnostic script.
            await core.ExecuteScriptAsync("""
                (()=>{
                  const media=document.getElementById('audio');
                  const p=window.__wrailTransferProbe={token:TOKEN,media,ticks:0,frames:0,
                    progressed:0,last:media.currentTime,pause:0,playing:0,loadstart:0,emptied:0};
                  for(const name of ['pause','playing','loadstart','emptied'])media.addEventListener(name,()=>p[name]++);
                  media.addEventListener('timeupdate',()=>{
                    let delta=media.currentTime-p.last;
                    if(delta<-.5&&Number.isFinite(media.duration))delta+=media.duration;
                    p.progressed+=Math.max(0,delta);p.last=media.currentTime;
                  });
                  const marker=document.createElement('p');marker.id='transfer-observation';
                  marker.style.cssText='background:#7e2268;color:white;padding:8px';document.body.append(marker);
                  p.timer=setInterval(()=>{p.ticks++;marker.textContent='Same document · tick '+p.ticks;},50);
                  const frame=()=>{p.frames++;p.raf=requestAnimationFrame(frame);};p.raf=requestAnimationFrame(frame);
                })()
                """.Replace("TOKEN", JsonSerializer.Serialize(documentToken), StringComparison.Ordinal));
            await Task.Delay(350, lifetime.Token);
            var baseline = await ObserveAsync("main-before-transfer");
            AssertDocument(baseline);
            Check(!baseline.GetProperty("paused").GetBoolean(), "cross-root baseline has one playing local silent audio element");

            peer = new("cross-root media probe");
            // A native XAML border distinguishes an invisible island from a
            // WebView-only composition failure without activating the peer.
            peer.SetContent(new Border { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Magenta),
                Padding = new Thickness(12), Child = peerHost });
            var placement = PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(), null, PinnedPlacementLimits.Default, 480, 300)
                ?? throw new InvalidOperationException("The owned media probe has no available display placement.");
            peer.Place(placement.Bounds);
            peer.CloseRequested += ReturnAndClose;
            peer.Show();
            await Until(() => peerHost.XamlRoot is not null && peerHost.IsLoaded);
            Check(!ReferenceEquals(peerHost.XamlRoot, originalRoot), "peer transfer destination is a distinct live XamlRoot");
            var parking = new Grid { Visibility = Visibility.Collapsed };
            ((Panel)Content).Children.Add(parking);
            var redirected = false;
            void RedirectLoaded(object sender, RoutedEventArgs args)
            {
                if (redirected || !ReferenceEquals(browser.Parent, peerHost)) return;
                redirected = true;
                _ = surface.MoveTo(parking, false, false);
            }
            browser.Loaded += RedirectLoaded;
            try { await surface.MoveTo(peerHost, true, false); }
            finally { browser.Loaded -= RedirectLoaded; }
            Check(redirected && ReferenceEquals(browser.Parent, parking) && browser.Visibility == Visibility.Collapsed && surface.FailureCode is null,
                "a newer hidden placement supersedes pending peer attachment without faulting the live document");
            await surface.MoveTo(viewport, true, true);
            await Until(() => browser.IsLoaded && ReferenceEquals(browser.XamlRoot, originalRoot));
            ((Panel)Content).Children.Remove(parking);
            Write(new { passed = true, phase = "cross-root-transfer-start", peerHwnd = peer.Handle.ToInt64(), checks, diagnostics, transferObservations });
            await surface.MoveTo(peerHost, true, false);
            Check(browser.IsLoaded && ReferenceEquals(browser.XamlRoot, peerHost.XamlRoot),
                "cross-root placement completes only after native WebView attachment to the passive peer");
            await Until(() => browser.IsLoaded && ReferenceEquals(browser.XamlRoot, peerHost.XamlRoot) && browser.ActualWidth >= 200);
            await Task.Delay(400, lifetime.Token);
            var transferred = await ObserveAsync("peer-after-transfer");
            AssertDocument(transferred);
            AssertContinuity(baseline, transferred);
            Check(navigationCount == 0 && coreInitializations == 0 && resolveCount == resolutions,
                "moving the existing WebView2 between XamlRoots creates no new core, navigation or resource admission");
            Write(new { passed = true, phase = "cross-root-peer", peerHwnd = peer.Handle.ToInt64(), checks, diagnostics, transferObservations });
            await Task.Delay(1200, lifetime.Token); // Bounded screenshot observation window.

            // Use the real native close request, so the browser must be detached
            // before the pinned window destroys its XamlRoot.
            if (PostCloseMessage(peer.Handle, 0x0010, 0, 0) == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            await Until(() => peerClosed || returnFailure is not null);
            if (returnFailure is not null) throw new InvalidOperationException("Closing the peer could not return its media.", returnFailure);
            await Until(() => ReferenceEquals(browser.XamlRoot, originalRoot) && browser.IsLoaded && browser.ActualWidth >= 200);
            await Task.Delay(400, lifetime.Token);
            var returned = await ObserveAsync("main-after-peer-close");
            AssertDocument(returned);
            AssertContinuity(transferred, returned);
            Check(peerClosed && ReferenceEquals(browser.Parent, viewport) && navigationCount == 0 && coreInitializations == 0 && resolveCount == resolutions,
                "native peer close restores the same browser and document before retiring the peer window");
        }
        finally
        {
            try
            {
                browser.CoreWebView2Initialized -= Initialized;
                core.NavigationStarting -= Navigating;
            }
            finally
            {
                try
                {
                    if (!retired) await surface.MoveTo(viewport, true, true);
                    else if (browser.Parent is Panel parent) parent.Children.Remove(browser);
                }
                finally { if (peer is not null) { peer.CloseRequested -= ReturnAndClose; peer.Dispose(); } }
            }
        }

        async void ReturnAndClose()
        {
            try
            {
                await surface.MoveTo(viewport, true, true);
                peer!.Dispose();
                peerClosed = true;
            }
            catch (Exception error) { returnFailure = error; }
        }

        async Task<JsonElement> ObserveAsync(string phase)
        {
#pragma warning disable WUI4001 // Already initialized fixture; identity comparison never creates a controller.
            var sameCore = ReferenceEquals(browser.CoreWebView2, core);
#pragma warning restore WUI4001
            var raw = await core.ExecuteScriptAsync("""
                (()=>{const p=window.__wrailTransferProbe,a=document.getElementById('audio');return p?{
                  token:p.token,sameAudio:p.media===a,audioElements:document.querySelectorAll('audio').length,
                  paused:a.paused,time:a.currentTime,progressed:p.progressed,ticks:p.ticks,frames:p.frames,
                  pause:p.pause,playing:p.playing,loadstart:p.loadstart,emptied:p.emptied}:null;})()
                """);
            using var value = JsonDocument.Parse(raw);
            var sample = value.RootElement.Clone();
            transferObservations.Add(new { phase, sameCore, browserProcessId = core.BrowserProcessId, navigationCount,
                coreInitializations, resourceAdmissions = resolveCount, media = sample,
                browserWidth = browser.ActualWidth, browserHeight = browser.ActualHeight,
                hostVisible = browser.XamlRoot?.IsHostVisible, peerWidth = peerHost.ActualWidth, peerHeight = peerHost.ActualHeight });
            Check(sameCore, "same CoreWebView2 identity at " + phase);
            return sample;
        }

        void AssertDocument(JsonElement value) => Check(value.ValueKind == JsonValueKind.Object &&
            value.GetProperty("token").GetString() == documentToken && value.GetProperty("sameAudio").GetBoolean() &&
            value.GetProperty("audioElements").GetInt32() == 1, "same script document and audio instance survive native transfer");

        void AssertContinuity(JsonElement before, JsonElement after) => Check(
            !after.GetProperty("paused").GetBoolean() && after.GetProperty("pause").GetInt32() == 0 &&
            after.GetProperty("loadstart").GetInt32() == 0 && after.GetProperty("emptied").GetInt32() == 0 &&
            after.GetProperty("progressed").GetDouble() > before.GetProperty("progressed").GetDouble() &&
            after.GetProperty("ticks").GetInt32() > before.GetProperty("ticks").GetInt32() &&
            after.GetProperty("frames").GetInt32() > before.GetProperty("frames").GetInt32(),
            "local audio, document timer and animation frames progress without pause or reload across transfer");
    }

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int PostCloseMessage(nint hwnd, uint message, nuint wParam, nint lParam);
}
