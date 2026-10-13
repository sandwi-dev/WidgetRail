using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    internal Func<BrowserSurface, CancellationToken, Task<bool>>? Expand { get; set; }
    private void ScreenCaptureStarting(CoreWebView2 owner, CoreWebView2ScreenCaptureStartingEventArgs args)
    {
        // Screen sharing is an explicit, one-shot decision. A website must not
        // summon the Windows capture picker while its widget is hidden.
        args.Handled = true;
        var origin = BrowserSiteIdentity.Origin(args.OriginalSourceFrameInfo.Source);
        if (Choose is null || !CanQueueSiteDecision(origin)) { args.Cancel = true; return; }
        var deferral = args.GetDeferral();
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        QueueSiteDecision(origin!);
        RunSiteDecision(async () =>
        {
            var admitted = false;
            var accepted = false;
            try
            {
                await siteDecisionGate.WaitAsync(token); admitted = true;
                if (!CanCompleteSiteDecision || !ReferenceEquals(owner, core) || Choose is not { } choose) return;
                var answer = await choose(this, "Share your screen?",
                    $"{document.AccessibleName}\n{origin}\n\nThe webapp will expand for the Windows picker. Choose a window or display using a mouse or keyboard. This website can see the selection and hear any audio you enable until you stop sharing or stop the webapp session. Press B after choosing to return to the widget.",
                    (HostDialogChoice[])[new("choose", "Choose what to share")], token);
                token.ThrowIfCancellationRequested();
                accepted = answer == "choose" && CanCompleteSiteDecision && ReferenceEquals(owner, core);
                // Chromium sizes its native chooser against the WebView. A
                // small embedded/pinned viewport can clip all source choices.
                // Expand the existing session before releasing the deferral.
                if (accepted && Expand is { } expand)
                {
                    accepted = false;
                    accepted = await expand(this, token) && CanCompleteSiteDecision && ReferenceEquals(owner, core);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                if (admitted) siteDecisionGate.Release();
                CompleteSiteDecision(origin!);
                args.Cancel = !accepted;
                deferral.Complete();
            }
        });
    }
}
