using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class EmbeddedMediaValidationPage
{
    private async Task CheckSharedRuntimeAsync(WidgetPresentationEmbeddedMediaDocument document)
    {
        var separate = Environment.GetCommandLineArgs().Contains("--webview-separate-baseline");
        var browserRuntime = separate ? new Web.HostedWebViewEnvironment(Path.Combine(
            Path.GetTempPath(), "WidgetRail-validation", "baseline-" + Guid.NewGuid().ToString("N"))) : runtime;
        BrowserSurface? page = null;
        var host = (StackPanel)Content;
        BrowserSurface.FixturePage = _ => "<!doctype html><title>Shared runtime fixture</title><button>Local synthetic page</button>";
        var app = new WebBrowserDocument("shared", 1, new EmbeddedMediaRequestPolicy(document, declaration!).EntryUri, "Shared runtime fixture")
            { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp };
        try
        {
            await OpenBrowser();
            var media = MediaCore();
            var web = BrowserCore();
            Check((media.BrowserProcessId == web.BrowserProcessId) == !separate,
                separate ? "Baseline uses separate browser processes" : "Browser and sealed media share one browser process");
            Check(media.Profile.IsInPrivateModeEnabled && !web.Profile.IsInPrivateModeEnabled && media.Profile.ProfileName != web.Profile.ProfileName,
                "Media stays private and distinct from the persistent webapp profile");
            await web.ExecuteScriptAsync("localStorage.setItem('isolation','web'); document.cookie='isolation=web;path=/;Secure';");
            Check(await media.ExecuteScriptAsync("localStorage.getItem('isolation')===null && !document.cookie.includes('isolation=web')") == "true",
                "Same-origin media cannot read browser storage or cookies");
            await media.ExecuteScriptAsync("localStorage.setItem('isolation','media'); document.cookie='isolation=media;path=/;Secure';");
            Check(await web.ExecuteScriptAsync("localStorage.getItem('isolation')==='web' && !document.cookie.includes('isolation=media')") == "true",
                "Same-origin browser cannot read private media storage or cookies");
            var permissionOrigin = new Uri(app.Url).GetLeftPart(UriPartial.Authority);
            await web.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Microphone, permissionOrigin, CoreWebView2PermissionState.Allow);
            Check(!(await media.Profile.GetNonDefaultPermissionSettingsAsync()).Any(setting =>
                setting.PermissionKind == CoreWebView2PermissionKind.Microphone && setting.PermissionState == CoreWebView2PermissionState.Allow),
                "Browser permission grants do not leak into media profiles");
            var processes = new[] { media.Environment, web.Environment }.SelectMany(env => env.GetProcessInfos())
                .DistinctBy(info => info.ProcessId).ToArray();
            var processSnapshot = processes.Select(info =>
            {
                using var process = Process.GetProcessById((int)info.ProcessId);
                return new { pid = info.ProcessId, kind = info.Kind.ToString(), privateBytes = process.PrivateMemorySize64 };
            }).ToArray();
            Write(new { passed = true, phase = "process-count", separate, processSnapshot, checks });
            if (separate) { Write(new { passed = true, phase = "complete", separate, processSnapshot, checks }); return; }

            surface!.UpdatePresentation(false, false);
            await Task.Delay(300);
            surface.UpdatePresentation(true, true);
            await Task.Delay(Web.HostedWebViewMemoryPolicy.IdleDelay + TimeSpan.FromMilliseconds(250));
            Check(media.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal,
                "Brief media hides cancel trimming without changing playback");
            surface.SetPassivePin(true); surface.UpdatePresentation(true, false);
            await Task.Delay(Web.HostedWebViewMemoryPolicy.IdleDelay + TimeSpan.FromMilliseconds(250));
            Check(media.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Low && !media.IsSuspended,
                "Visible passive media pin enters Low without suspension");
            Check(web.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal,
                "Media Low target does not change active browser target in the shared process");
            Check(await media.ExecuteScriptAsync("!document.getElementById('audio').paused") == "true",
                "Synthetic audio keeps playing in passive Low mode");
            surface.UpdatePresentation(true, true);
            Check(media.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal,
                "Pin input admission restores Normal synchronously");
            surface.SetPassivePin(false); surface.UpdatePresentation(false, false);
            page!.SetPresentation(false, false);
            await Task.Delay(Web.HostedWebViewMemoryPolicy.IdleDelay + TimeSpan.FromMilliseconds(250));
            Check(media.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Low && web.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Low &&
                !media.IsSuspended && !web.IsSuspended, "Both hidden controllers trim independently without suspension");
            var start = await media.ExecuteScriptAsync("document.getElementById('audio').currentTime");
            await Task.Delay(400);
            Check(start != await media.ExecuteScriptAsync("document.getElementById('audio').currentTime"), "Playback clock advances while hidden in Low mode");
            surface.UpdatePresentation(true, true); page.SetPresentation(true, true);
            Check(media.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal && web.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal,
                "Returning both surfaces restores Normal without reload");

            // This environment is unique to this synthetic fixture, never the user's profile.
            var fixtureRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WidgetRail-validation")) + Path.DirectorySeparatorChar;
            Check(Path.GetFullPath(media.Environment.UserDataFolder).StartsWith(fixtureRoot, StringComparison.OrdinalIgnoreCase),
                "Crash probe owns an isolated temporary environment");
            using (var process = Process.GetProcessById((int)media.BrowserProcessId))
            {
                Check(process.ProcessName == "msedgewebview2", "Crash target is the fixture WebView browser process");
                process.Kill(); await process.WaitForExitAsync(lifetime.Token);
            }
            for (var i = 0; i < 250 && (!surface.IsRetired || page.IsReady); ++i) await Task.Delay(20);
            Check(surface.IsRetired && !page.IsReady, "Shared browser exit invalidates both controllers safely");
            await surface.DisposeAsync(); viewport.Children.Remove(surface.Element);
            await page.DisposeAsync(); host.Children.Remove(page); page = null;
            surface = new(session!, document, runtime);
            viewport.Children.Add(surface.Element); surface.UpdatePresentation(true, true);
            await Until(() => surface.IsReady);
            await OpenBrowser();
            Check(MediaCore().BrowserProcessId == BrowserCore().BrowserProcessId, "Explicit recovery shares a fresh browser process");
            Check(await BrowserCore().ExecuteScriptAsync("localStorage.getItem('isolation')==='web'") == "true", "Persistent browser data survives shared process recovery");
            Check(await MediaCore().ExecuteScriptAsync("localStorage.getItem('isolation')===null") == "true", "Private media data is discarded when its session ends");
            Write(new { passed = true, phase = "complete", separate, processSnapshot, checks });
        }
        finally
        {
            if (page is not null) { await page.DisposeAsync(); host.Children.Remove(page); }
            BrowserSurface.FixturePage = null;
        }

        async Task OpenBrowser()
        {
            page = new(Descriptor.Id, app, browserRuntime.GetAsync) { Height = 120 };
            host.Children.Add(page); page.SetPresentation(true, true);
            await Until(() => page.IsReady && !page.IsNavigationInProgress && page.CurrentTitle == "Shared runtime fixture");
        }
#pragma warning disable WUI4001 // Only used after both production surfaces report readiness.
        CoreWebView2 MediaCore() => ((WebView2)surface!.Element).CoreWebView2;
        CoreWebView2 BrowserCore() => ((WebView2)page!.CompositionRoot).CoreWebView2;
#pragma warning restore WUI4001
    }
}
