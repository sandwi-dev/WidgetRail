using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    internal Func<BrowserSurface, CancellationToken, Task<bool>>? PrepareScreenPicker { get; set; }
    private bool nativeDialogPointerAccess;
    internal void SetNativeDialogPointerAccess(bool allowed)
    {
        nativeDialogPointerAccess = allowed;
        if (!retired) web.IsHitTestVisible = input || visible && allowed;
    }
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
                    $"{document.AccessibleName}\n{origin}\n\nThe webapp temporarily expands for the sharing picker, then returns here. Move the pointer with the left stick, click with A, scroll with the right stick, and cancel with B. Mouse and keyboard also work. This website can see the selection and hear any audio you enable until you stop sharing or stop the webapp session.",
                    (HostDialogChoice[])[new("choose", "Choose what to share")], token);
#if ENABLE_WIDGET_VALIDATION
                FixturePermissionTrace.Add($"screen-answer={answer} canComplete={CanCompleteSiteDecision} cancelled={token.IsCancellationRequested}");
#endif
                token.ThrowIfCancellationRequested();
                accepted = answer == "choose" && CanCompleteSiteDecision && ReferenceEquals(owner, core);
                if (accepted)
                    accepted = PrepareScreenPicker is { } prepare && await prepare(this, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                if (admitted) siteDecisionGate.Release();
                CompleteSiteDecision(origin!);
#if ENABLE_WIDGET_VALIDATION
                FixturePermissionTrace.Add($"screen-complete accepted={accepted} cancelled={token.IsCancellationRequested}");
#endif
                args.Cancel = !accepted;
                deferral.Complete();
            }
        });
    }
}
