using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private void ScreenCaptureStarting(CoreWebView2 owner, CoreWebView2ScreenCaptureStartingEventArgs args)
    {
        // Screen sharing is an explicit, one-shot decision. A website must not
        // summon the Windows capture picker while its widget is hidden.
        args.Cancel = true;
        args.Handled = true;
        var origin = BrowserSiteIdentity.Origin(args.OriginalSourceFrameInfo.Source);
        if (!CanRequestSiteDecision || HasDialog || Choose is null || origin is null || queuedSiteDecisions >= 4) return;
        var deferral = args.GetDeferral();
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        queuedSiteDecisions++;
        RunSiteDecision(async () =>
        {
            var admitted = false;
            try
            {
                await siteDecisionGate.WaitAsync(token); admitted = true;
                if (!CanRequestSiteDecision || !ReferenceEquals(owner, core) || Choose is not { } choose) return;
                var answer = await choose(this, "Share your screen?",
                    $"{document.AccessibleName}\n{origin}\n\nChoose a window or display in the Windows picker. This website can see it until you stop sharing or stop the webapp session.",
                    (HostDialogChoice[])[new("choose", "Choose what to share")], token);
                token.ThrowIfCancellationRequested();
                if (answer == "choose" && CanRequestSiteDecision && ReferenceEquals(owner, core)) args.Cancel = false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                if (admitted) siteDecisionGate.Release();
                queuedSiteDecisions--;
                deferral.Complete();
            }
        });
    }
}
