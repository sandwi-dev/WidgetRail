using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    internal bool IsBackendVideo => hostedDefinition?.Source.Kind == WebSessionSourceKind.BackendVideo;
    internal WebBrowserOptions VideoViewOptions => document.Options with { Chrome = BrowserChrome.OnDemand };
    private readonly Dictionary<string, (BrowserSurface View, BrowserDocumentLease Document)> pendingVideoViews = [];
    private BrowserDocumentLease? videoDocument;
    private CancellationTokenRegistration videoRetirement;
    private WebSessionPageAdapterHost? videoAdapter;
    private string? videoId;
    private bool videoBound;
    private BrowserSurface? videoSource;
    private readonly HashSet<BrowserSurface> activeVideoChildren = [];
    internal bool HasActiveVideoOutput => activeVideoChildren.Count != 0;

#if ENABLE_WIDGET_VALIDATION
    internal Task FixtureSetVideoTrackAsync(string id) => pageAdapter!.FixtureSetVideoTrackAsync(id);
#endif

    private bool TryBindBackendVideo(CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (args.Name is not { Length: > 0 } name || !pendingVideoViews.Remove(name, out var pending)) return false;
        args.Handled = true;
        if (!pending.Document.IsCurrent || !pending.View.IsReady || args.Uri != "about:blank" ||
            args.OriginalSourceFrameInfo is not { } frame || frame.ParentFrameInfo is not null ||
            BrowserSiteIdentity.Origin(frame.Source) != CurrentBackendDocumentOrigin) return true;
        args.NewWindow = pending.View.core;
        pending.View.videoBound = true;
        return true;
    }

    internal async Task StartBackendVideoAsync(BrowserSurface parent, WebSessionReference reference, CancellationToken token)
    {
        if (!IsBackendVideo || profile != parent.profile || parent.pageAdapter is not { } adapter ||
            parent.CaptureDocumentLease(lifetime.Token) is not { } documentLease)
            throw new InvalidOperationException("Backend video document unavailable.");
        videoDocument = documentLease;
        videoAdapter = adapter;
        videoId = reference.Id;
        videoRetirement = documentLease.Token.Register(() =>
        {
            // Remove the last displayed frame immediately when its authority retires.
            DetachVideoSource();
            web.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            FailHostedSession("Video source document retired.");
        });
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token, documentLease.Token);
        await PrepareCoreAsync(startup.Token);
        var name = "WidgetRailVideo" + Guid.NewGuid().ToString("N");
        parent.pendingVideoViews.Add(name, (this, documentLease));
        try
        {
            if (!await adapter.VideoViewAsync(reference.Id, name, true, startup.Token) || !videoBound || !documentLease.IsCurrent)
                throw new InvalidOperationException("Backend video view could not be attached.");
            videoSource = parent;
            parent.activeVideoChildren.Add(this);
            parent.UpdateMemoryPolicy();
        }
        finally { parent.pendingVideoViews.Remove(name); }
    }

    private void ConfigureBackendVideoCore(CoreWebView2 browser)
    {
        var settings = browser.Settings;
        settings.IsWebMessageEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        browser.IsMuted = true;
        browser.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
        browser.ServerCertificateErrorDetected += (_, e) => e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        browser.NavigationStarting += (_, e) => e.Cancel = e.Uri != "about:blank" || videoDocument?.IsCurrent != true;
        browser.NewWindowRequested += (_, e) => e.Handled = true;
        browser.PermissionRequested += (_, e) =>
        { e.Handled = true; e.SavesInProfile = false; e.State = CoreWebView2PermissionState.Deny; };
        browser.DownloadStarting += (_, e) => e.Cancel = true;
        browser.ScreenCaptureStarting += (_, e) => e.Cancel = true;
        browser.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        browser.WindowCloseRequested += (_, _) => FailHostedSession("Video view closed.");
        browser.ProcessFailed += (_, e) => { if (RequiresPageRecovery(e.ProcessFailedKind)) FailHostedSession("Video renderer stopped."); };
    }

    private void DetachVideoSource()
    {
        if (videoSource is not { } parent) return;
        videoSource = null;
        parent.activeVideoChildren.Remove(this);
        parent.UpdateMemoryPolicy();
    }

    private async Task ReleaseBackendVideoAsync()
    {
        DetachVideoSource();
        videoRetirement.Dispose();
        try
        {
            if (videoDocument?.IsCurrent == true && videoAdapter is { } adapter && videoId is { } id)
                await adapter.VideoViewAsync(id, "", false, CancellationToken.None);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Diagnostics.FrontendFailureLog.Current.Write("backend-video", null, "source_retired"); }
        finally { videoDocument?.Dispose(); videoDocument = null; videoAdapter = null; }
    }
}
