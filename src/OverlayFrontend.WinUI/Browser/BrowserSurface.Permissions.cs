using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal delegate Task<string?> BrowserChoiceHandler(BrowserSurface source, string title, string message,
    IReadOnlyList<HostDialogChoice> choices, CancellationToken token, bool focusFirstChoice = false);

internal sealed partial class BrowserSurface
{
    internal BrowserChoiceHandler? Choose { get; set; }
    internal bool CanRequestSiteDecision => !retired && !faulted && visible && input && !IsProviderContent;
    // Native/modal UI temporarily owns input. A confirmed decision may complete
    // before the shell's foreground notification restores browser input.
    internal bool CanCompleteSiteDecision => !retired && !faulted && visible && !profileChanging && !IsProviderContent;
    private readonly SemaphoreSlim siteDecisionGate = new(1, 1);
    private CancellationTokenSource? siteDecisions;
    private int queuedSiteDecisions;
    private readonly Dictionary<string, int> queuedSiteOrigins = new(StringComparer.Ordinal);
    private readonly HashSet<Task> pendingSiteDecisions = [];

    private bool CanQueueSiteDecision(string? origin) => origin is not null && !HasDialog && queuedSiteDecisions < 4 &&
        (CanRequestSiteDecision || CanCompleteSiteDecision && queuedSiteOrigins.ContainsKey(origin));
    private void QueueSiteDecision(string origin)
    { queuedSiteOrigins[origin] = queuedSiteOrigins.GetValueOrDefault(origin) + 1; queuedSiteDecisions++; }
    private void CompleteSiteDecision(string origin)
    { if (--queuedSiteOrigins[origin] == 0) queuedSiteOrigins.Remove(origin); queuedSiteDecisions--; }

    private void RunSiteDecision(Func<Task> decision)
    {
        var pending = ObserveAsync(decision);
        if (pending.IsCompleted) return;
        pendingSiteDecisions.Add(pending);
        _ = ForgetAsync();
        async Task ForgetAsync()
        {
            await pending;
            pendingSiteDecisions.Remove(pending);
            // Host dialog teardown restores the slot's focus before the native
            // browser deferral finishes. Reconcile once HasDialog clears, using
            // current input ownership; never re-enter a pin the user has left.
            RefreshInteractionFocus();
        }
    }

    private void CancelSiteDecisions()
    {
        var previous = siteDecisions;
        siteDecisions = null;
        previous?.Cancel(); previous?.Dispose();
    }

    private void PermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
    {
#if ENABLE_WIDGET_VALIDATION
        if (FixturePermissionTrace.Count < 16)
            FixturePermissionTrace.Add($"kind={args.PermissionKind} canAsk={CanRequestSiteDecision} choose={Choose is not null} dialog={HasDialog} queued={queuedSiteDecisions} secureOrigin={BrowserSiteIdentity.Origin(args.Uri) is not null}");
#endif
        args.Handled = true;
        args.State = CoreWebView2PermissionState.Deny;
        args.SavesInProfile = false;
        var origin = BrowserSiteIdentity.Origin(args.Uri);
        var description = PermissionDescription(args.PermissionKind);
        if (Choose is null || description is null || !CanQueueSiteDecision(origin)) return;
        var deferral = args.GetDeferral();
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        QueueSiteDecision(origin!);
        RunSiteDecision(async () =>
        {
            var admitted = false;
            try
            {
                await siteDecisionGate.WaitAsync(token); admitted = true;
                if (!CanCompleteSiteDecision || !ReferenceEquals(sender, core) || Choose is not { } choose) return;
                var answer = await choose(this, "Website permission", $"{document.AccessibleName}\n{origin}\n\nAllow this website to {description}?",
                    (HostDialogChoice[])[new("once", "Allow once"), new("allow", "Always allow for this profile"), new("deny", "Block for this profile")], token);
                token.ThrowIfCancellationRequested();
                if (!CanCompleteSiteDecision || !ReferenceEquals(sender, core)) return;
                args.State = answer is "once" or "allow" ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                args.SavesInProfile = answer is "allow" or "deny";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                CompleteSiteDecision(origin!);
                if (admitted) siteDecisionGate.Release();
                deferral.Complete();
            }
        });
    }

#if ENABLE_WIDGET_VALIDATION
    internal List<string> FixturePermissionTrace { get; } = [];
#endif

    private static string? PermissionDescription(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Microphone => "use your microphone",
        CoreWebView2PermissionKind.Camera => "use your camera",
        CoreWebView2PermissionKind.Geolocation => "access your location",
        CoreWebView2PermissionKind.Notifications => "show notifications",
        CoreWebView2PermissionKind.ClipboardRead => "read your clipboard",
        CoreWebView2PermissionKind.Autoplay => "play audio automatically",
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => "request multiple downloads (each file still requires a save location)",
        _ => null,
    };
}
