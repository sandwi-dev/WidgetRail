using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    // Only these browser permissions may use a widget-initiated backend decision.
    private static CoreWebView2PermissionKind NativePermission(WebSessionPermissionKind kind) => kind switch
    {
        WebSessionPermissionKind.Microphone => CoreWebView2PermissionKind.Microphone,
        WebSessionPermissionKind.Notifications => CoreWebView2PermissionKind.Notifications,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static string BackendPermissionDescription(WebSessionPermissionKind kind) => PermissionDescription(NativePermission(kind))!;
    private readonly Dictionary<CoreWebView2PermissionKind, WebSessionPermissionConsent> backendPermissions = new()
    {
        [NativePermission(WebSessionPermissionKind.Microphone)] = new(),
        [NativePermission(WebSessionPermissionKind.Notifications)] = new()
    };
    private readonly HashSet<Task> backendPermissionOperations = [];
    internal bool IsBackendSession => hostedDefinition?.PageAdapter?.Mode == WebSessionPageAdapterMode.SessionBackend;

    private void RetireBackendPermissions()
    {
        foreach (var consent in backendPermissions.Values) consent.Retire();
    }

    internal async Task<WebSessionPermissionResult> RequestBackendPermissionAsync(WebSessionPermissionKind kind,
        Func<string, CancellationToken, Task<string?>> choose, Func<bool> admitted, CancellationToken token)
    {
        using var documentLease = CaptureDocumentLease(token);
        if (documentLease is null || CurrentBackendDocumentOrigin is not { } origin || core is not { } browser ||
            !admitted() || !Enum.IsDefined(kind) || backendPermissionOperations.Count != 0)
            return new(WebSessionPermissionState.Unavailable);
        var epoch = pageEpoch;
        var nativeKind = NativePermission(kind);
        var consent = backendPermissions[nativeKind];
        bool Current() => documentLease.IsCurrent && admitted();

        async Task<SitePermissionPreference> Read(CancellationToken cancellation)
        {
            var saved = await browser.Profile.GetNonDefaultPermissionSettingsAsync().AsTask().WaitAsync(cancellation);
            var setting = saved.FirstOrDefault(p => p.PermissionKind == nativeKind &&
                BrowserSiteIdentity.Origin(p.PermissionOrigin) == origin);
            return setting?.PermissionState switch
            {
                CoreWebView2PermissionState.Allow => SitePermissionPreference.Allow,
                CoreWebView2PermissionState.Deny => SitePermissionPreference.Deny,
                _ => SitePermissionPreference.Default
            };
        }

        async Task<WebSessionPermissionResult> Request()
        {
            var result = await consent.RequestAsync(origin, epoch, Current, Read,
                cancellation => choose(origin, cancellation), documentLease.Token);
            if (result.State != WebSessionPermissionState.Granted || kind != WebSessionPermissionKind.Notifications)
                return result;
            // A notification producer may check Notification.permission before using it.
            // Have WebView2 apply the user's decision now, without reloading the page.
            try
            {
                if (Current() && await ApplyNotificationPermissionAsync(browser, Current, documentLease.Token) && Current())
                    return result;
                return new(Current() ? WebSessionPermissionState.Unavailable : WebSessionPermissionState.Cancelled);
            }
            finally
            {
                // The native notification permission is now authoritative. Retire the temporary
                // admission even on timeout, cancellation, or renderer failure.
                if (pageEpoch == epoch) consent.Retire();
            }
        }

        var request = Request();
        backendPermissionOperations.Add(request);
        try { return await request; }
        catch (OperationCanceledException) when (documentLease.Token.IsCancellationRequested)
        { return new(WebSessionPermissionState.Cancelled); }
        finally { backendPermissionOperations.Remove(request); }
    }

    private bool TryBackendPermission(CoreWebView2PermissionRequestedEventArgs args)
    {
        if (!IsBackendSession || !backendPermissions.TryGetValue(args.PermissionKind, out var consent)) return false;
        var origin = BrowserSiteIdentity.Origin(args.Uri);
        if (origin is not null && CurrentBackendDocumentOrigin == origin && consent.IsGranted(origin, pageEpoch, out var remember))
        {
            args.Handled = true;
            args.State = CoreWebView2PermissionState.Allow;
            args.SavesInProfile = remember;
            return true;
        }
        if (CanRequestSiteDecision) return false;
        args.Handled = true;
        args.State = CoreWebView2PermissionState.Deny;
        args.SavesInProfile = false;
        return true;
    }
}
