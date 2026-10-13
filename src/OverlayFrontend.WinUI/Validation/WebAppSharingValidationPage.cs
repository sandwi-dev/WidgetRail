using System.Text.Json;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Share only the named synthetic target window; never saves media or contacts a peer.</summary>
internal sealed partial class WebAppSharingValidationPage : Page, IAsyncDisposable
{
    private readonly BrowserSurface browser;
    private Process? target;
    internal WebAppSharingValidationPage(string result, string profileRoot, string sourceExecutable, Action activateParent)
    {
        if (!Path.IsPathFullyQualified(sourceExecutable) || !File.Exists(sourceExecutable)) throw new ArgumentException("A built synthetic sharing source is required.");
        Task<CoreWebView2Environment>? environment = null;
        BrowserSurface.FixturePage = _ => "<!doctype html><title>Local sharing fixture</title><body>Local screen-sharing validation. Select only the synthetic target window.</body>";
        browser = new("fixture.sharing", new("sharing", 1, "https://share.fixture.invalid/", "Local sharing fixture")
        { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp },
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, null).AsTask())
        { IsInteractionFocused = () => true };
        Content = browser;
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null; string? metadata = null;
            var targetName = "WidgetRail synthetic sharing target " + Environment.ProcessId;
            try
            {
                // The picker must see an actual visible window to select.
                var start = new ProcessStartInfo(sourceExecutable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Normal };
                start.ArgumentList.Add(targetName);
                start.ArgumentList.Add(result + ".target");
                target = Process.Start(start) ?? throw new InvalidOperationException("Synthetic target did not start.");
                var targetDeadline = DateTime.UtcNow.AddSeconds(15);
                while (!File.Exists(result + ".target"))
                { if (target.HasExited || DateTime.UtcNow > targetDeadline) throw new TimeoutException("Synthetic target did not become ready."); await Task.Delay(50); }
                activateParent();
                browser.Choose = (_, title, message, _, _) =>
                {
                    if (title != "Share your screen?" || !message.Contains("https://share.fixture.invalid", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected sharing consent.");
                    return Task.FromResult<string?>("choose");
                };
                browser.SetPresentation(true, true);
                await UntilScript("document.title==='Local sharing fixture'", 20);
                ((WebView2)browser.CompositionRoot).Focus(FocusState.Keyboard);
                await File.WriteAllTextAsync(result + ".ready", JsonSerializer.Serialize(new { target = targetName, pid = Environment.ProcessId, targetPid = target.Id }));
                await browser.EvaluateGestureFixtureAsync("""
                    window.shareState='pending';
                    navigator.mediaDevices.getDisplayMedia({video:true,audio:false}).then(async stream=>{
                      window.shared=stream;
                      const track=stream.getVideoTracks()[0];
                      window.shareSettings=track.getSettings();
                      if(shareSettings.displaySurface!=='window'){stream.getTracks().forEach(t=>t.stop());window.shareState='wrong-source';return;}
                      const video=document.createElement('video');video.muted=true;video.srcObject=stream;
                      video.style='width:160px;height:100px';document.body.append(video);await video.play();
                      video.requestVideoFrameCallback(()=>window.shareState='ready');
                    },e=>window.shareState=e.name);
                    """);
                await UntilScript("window.shareState!=='pending'", 90);
                Check(await browser.EvaluateFixtureAsync("window.shareState==='ready'") == "true", "Window sharing delivers a decoded frame from the explicitly selected synthetic target");
                metadata = await browser.EvaluateFixtureAsync("JSON.stringify({surface:shareSettings.displaySurface,width:shareSettings.width,height:shareSettings.height,audio:shared.getAudioTracks().length})");
                await browser.EvaluateFixtureAsync("shared.getTracks().forEach(t=>t.stop())");
                Check(await browser.EvaluateFixtureAsync("shared.getTracks().every(t=>t.readyState==='ended')") == "true", "Website can stop the sharing tracks cleanly");
            }
            catch (Exception failure) { error = failure.ToString(); }
            await DisposeAsync();
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, metadata, error }));
            async Task UntilScript(string script, int seconds)
            {
                var end = DateTime.UtcNow.AddSeconds(seconds);
                while (!browser.IsReady || await browser.EvaluateFixtureAsync(script) != "true")
                { if (DateTime.UtcNow > end) throw new TimeoutException("Sharing fixture did not reach expected state."); await Task.Delay(100); }
            }
            void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
        };
    }
    public async ValueTask DisposeAsync()
    {
        await browser.DisposeAsync();
        if (target is { } process)
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            }
            process.Dispose(); target = null;
        }
    }
}
