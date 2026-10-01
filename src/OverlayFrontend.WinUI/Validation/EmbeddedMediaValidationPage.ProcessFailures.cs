using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class EmbeddedMediaValidationPage
{
    private async Task CheckNativeBrowserRecoveryAsync(EmbeddedMediaSession definition)
    {
        var browser = OwnerBrowser();
        declaration = declaration! with { SupportedPresentations = new[] { MediaPresentationKind.OverlayFullscreen } };
        ownerPresenter!.Apply(await SnapshotAsync());
        var created = mediaOwner!.BrowserCreationCount;
        await FaultFullscreenOwnerAsync(browser, core =>
        {
            using var process = System.Diagnostics.Process.GetProcessById(checked((int)core.BrowserProcessId));
            if (process.ProcessName != "msedgewebview2" || !core.Environment.GetProcessInfos().Any(info =>
                info.ProcessId == core.BrowserProcessId && info.Kind == CoreWebView2ProcessKind.Browser))
                throw new InvalidOperationException("Fixture browser process identity changed.");
            process.Kill();
        }, "media-browser-process-failed");
        Check(diagnostics.Any(line => line.Contains("kind=BrowserProcessExited", StringComparison.Ordinal)),
            "fatal browser exit records the native process kind rather than only a generic media failure");
        declaration = null; ownerPresenter.Apply(await SnapshotAsync());
        await Until(() => mediaOwner.ResidentCount == 0);
        declaration = definition with { Id = "owner-player", PendingCommand = null };
        ownerPresenter.Apply(await SnapshotAsync());
        await Until(() => mediaOwner.IsReady(Descriptor.Id));
        Check(mediaOwner.GetFailure(Descriptor.Id) is null && !ReferenceEquals(browser, OwnerBrowser()) &&
            mediaOwner.BrowserCreationCount == created + 1,
            "explicit recovery after browser-process exit creates exactly one fresh ready WebView");
    }

    private async Task CheckNativeGpuRecoveryAsync()
    {
        foreach (var candidate in System.Diagnostics.Process.GetProcessesByName("OverlayFrontend.WinUI"))
        {
            using (candidate)
                if (candidate.Id != Environment.ProcessId) throw new InvalidOperationException("Process-failure checks require an isolated frontend.");
        }
        // Opt-in isolated fixture only: never enumerate/terminate a system GPU,
        // other browser, or arbitrary process. The native WebView environment
        // supplies the exact sandboxed GPU subprocess owned by this fixture.
        var browser = OwnerBrowser();
#pragma warning disable WUI4001 // Called only after the owner reports adapter readiness.
        var core = browser.CoreWebView2;
#pragma warning restore WUI4001
        var processInfo = core.Environment.GetProcessInfos().Single(info => info.Kind == CoreWebView2ProcessKind.Gpu);
        var observed = new TaskCompletionSource<CoreWebView2ProcessFailedKind>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Failed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args) => observed.TrySetResult(args.ProcessFailedKind);
        core.ProcessFailed += Failed;
        var creations = mediaOwner!.BrowserCreationCount;
        var admissions = resolveCount;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(checked((int)processInfo.ProcessId));
            if (process.ProcessName != "msedgewebview2" || !core.Environment.GetProcessInfos().Any(info =>
                info.ProcessId == processInfo.ProcessId && info.Kind == CoreWebView2ProcessKind.Gpu))
                throw new InvalidOperationException("Fixture GPU subprocess identity changed.");
            process.Kill();
            await process.WaitForExitAsync(lifetime.Token);
            var kind = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
            Check(kind == CoreWebView2ProcessFailedKind.GpuProcessExited,
                "isolated WebView GPU termination produces its genuine process-recovery notification");
            await Task.Delay(300, lifetime.Token);
            Check(mediaOwner.IsReady(Descriptor.Id) && mediaOwner.GetFailure(Descriptor.Id) is null &&
                ReferenceEquals(OwnerBrowser(), browser) && creations == mediaOwner.BrowserCreationCount && admissions == resolveCount,
                "automatic GPU recovery retains the admitted browser instead of terminating widget media");
            Check(await core.ExecuteScriptAsync("document.readyState === 'complete'") == "true",
                "the same sealed document remains script-responsive after native GPU recovery");
            Write(new { passed = true, phase = "gpu-recovered", checks, diagnostics });
            await Task.Delay(1200, lifetime.Token);
        }
        finally
        {
            try { core.ProcessFailed -= Failed; }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        }
    }
}
