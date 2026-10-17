using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private WebSessionDefinition? hostedDefinition;
    private WebSessionPageAdapterHost? pageAdapter;
    private string? pageActionStyle;
    private IReadOnlyList<BridgeWebSessionResource> hostedResources = [];
    private Uri? localResource;
    private readonly SemaphoreSlim hostedNavigationGate = new(1, 1);
    private readonly TaskCompletionSource hostedNavigation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<string> hostedMessages = Channel.CreateBounded<string>(WebSessionContract.MaximumQueuedMessages);
    internal bool IsHostedPackage => hostedDefinition?.Source.Kind == WebSessionSourceKind.Package;
    internal bool IsHostedSession => hostedDefinition is not null;
    internal ChannelReader<string> HostedMessages => hostedMessages.Reader;

    internal void ConfigureHostedSession(WebSessionDefinition definition, IReadOnlyList<BridgeWebSessionResource> resources)
    {
        if (initializing is not null || core is not null) throw new InvalidOperationException("Session must be configured before creating its browser.");
        hostedDefinition = definition;
        if (IsHostedPackage) profile = "pkg-" + profile;
        hostedResources = resources;
        UpdateChrome();
    }

    internal async Task StartHostedSessionAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await PrepareCoreAsync(linked.Token);
        if (appliedNavigation == 0) { appliedNavigation = document.NavigationId; core!.Navigate(document.Url); }
        // Remote navigation may require visible login/challenge interaction or
        // display a recoverable network error. Return the initialized session so
        // its author can attach a view; only packaged RPC pages require readiness.
        if(IsHostedPackage)await hostedNavigation.Task.WaitAsync(TimeSpan.FromSeconds(25), linked.Token);
    }

    internal void ApplyHostedView(WebBrowserDocument view) =>
        document = document with { InteractionMode = view.InteractionMode, AccessibleName = view.AccessibleName };

    internal async Task PostHostedMessageAsync(string encoded)
    {
        if(pageAdapter is not null){await pageAdapter.PostMessageAsync(Encoding.UTF8.GetString(WebSessionContract.DecodeMessage(encoded)));return;}
        PostHostedMessage(encoded);
    }
    internal void PostHostedMessage(string encoded)
    {
        if (!IsHostedPackage || !IsReady || !IsHostedDocument(core!.Source) || loading) throw new InvalidOperationException("Session page is unavailable.");
        core.PostWebMessageAsJson(TraceHostedRequest(Encoding.UTF8.GetString(WebSessionContract.DecodeMessage(encoded))));
    }

    internal void AuthorizeHostedResource(string url)
    {
        if (!IsHostedPackage || !WebSessionContract.IsLocalResource(url)) throw new InvalidOperationException("Invalid session resource.");
        // One exact resource at a time; changing tracks retires the previous stream authority.
        localResource = new Uri(url);
    }

    internal async Task NavigateHostedAsync(string destination, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await hostedNavigationGate.WaitAsync(linked.Token);
        try { await NavigateHostedCoreAsync(destination, linked.Token); }
        finally { hostedNavigationGate.Release(); }
    }

    private async Task NavigateHostedCoreAsync(string destination, CancellationToken token)
    {
        if (core is null || hostedDefinition is null) throw new InvalidOperationException("Session page is unavailable.");
        var url = IsHostedPackage ? new Uri(new Uri(document.Url).GetLeftPart(UriPartial.Authority) + "/" + destination).AbsoluteUri : destination;
        if (IsHostedPackage ? !IsHostedDocument(url) : !WebBrowserDocument.IsWebUrl(url)) throw new InvalidOperationException("Invalid session destination.");
        if(!IsHostedPackage){token.ThrowIfCancellationRequested();core.Navigate(url);return;}
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = core;
        ulong? navigationId = null;
        void Starting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (navigationId is null && Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri == new Uri(url))
                navigationId = args.NavigationId;
            else if (navigationId is { } expected && args.NavigationId != expected && !args.Cancel)
                completion.TrySetException(new InvalidOperationException("Session navigation was superseded."));
        }
        void Completed(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.NavigationId != navigationId) return;
            if (args.IsSuccess) completion.TrySetResult();
            else completion.TrySetException(new InvalidOperationException("Session navigation failed."));
        }
        browser.NavigationStarting += Starting;
        browser.NavigationCompleted += Completed;
        try { browser.Navigate(url); await completion.Task.WaitAsync(TimeSpan.FromSeconds(25), token); }
        finally { browser.NavigationStarting -= Starting; browser.NavigationCompleted -= Completed; }
    }

    private bool IsHostedDocument(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.GetLeftPart(UriPartial.Authority) == new Uri(document.Url).GetLeftPart(UriPartial.Authority) &&
        hostedResources.Any(resource => resource.ContentType == "text/html" && resource.Path == Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));

    private async Task ConfigureHostedCoreAsync(CoreWebView2Environment env)
    {
        if (hostedDefinition is null) return;
        core!.NavigationCompleted += (sender, args) =>
        {
            if (args.IsSuccess)
            {
                hostedThemeAppliedRevision=0;
                _=ApplyHostedPageThemeAsync();
                hostedNavigation.TrySetResult();
            }
            else if(IsHostedPackage)hostedNavigation.TrySetException(new InvalidOperationException("Hosted page failed to load."));
        };
        core.ProcessFailed += (_, args) =>
        {
            if (RequiresPageRecovery(args.ProcessFailedKind))
                FailHostedSession("Hosted browser process stopped.");
        };
        if (hostedDefinition.PageAdapter is { } adapter)
        {
            var resource=hostedResources.SingleOrDefault();
            if(resource is null||resource.Path!=adapter.ScriptAsset||resource.ContentType!="text/javascript"||resource.ContentBase64.Length>90000)
                throw new InvalidOperationException("Page adapter resource is unavailable.");
            var scriptBytes=Convert.FromBase64String(resource.ContentBase64);
            if(scriptBytes.Length>64*1024||!Convert.ToHexString(SHA256.HashData(scriptBytes)).Equals(resource.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Page adapter resource is invalid.");
            pageAdapter=new(core,adapter,Encoding.UTF8.GetString(scriptBytes),()=>!retired&&!faulted&&visible&&input&&!loading,json=>
            {
                var encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                try {WebSessionContract.DecodeMessage(encoded);}
                catch(ArgumentException){FailHostedSession("Page adapter result exceeds its bound.");return;}
                if(!hostedMessages.Writer.TryWrite(encoded))FailHostedSession("Page adapter message queue exceeded its bound.");
            },()=>!retired&&!faulted&&visible&&!loading);
            if(pageActionStyle is not null)pageAdapter.SetStyle(pageActionStyle);
            await pageAdapter.StartAsync();
        }
        if (!IsHostedPackage) return;
        var definition = hostedDefinition;
        var origin = new Uri(document.Url).GetLeftPart(UriPartial.Authority);
        var assets = new Dictionary<string, (byte[] Bytes, string ContentType)>(StringComparer.Ordinal);
        long total = 0;
        if (hostedResources.Count != definition.Source.Resources.Count) throw new InvalidOperationException("Session resources are incomplete.");
        foreach (var resource in hostedResources)
        {
            if (!definition.Source.Resources.Any(r => r.Path == resource.Path && r.ContentType == resource.ContentType) ||
                resource.ContentBase64.Length > (ProtocolConstants.MaximumEmbeddedMediaResourceBytes + 2) / 3 * 4)
                throw new InvalidOperationException("Invalid session resource.");
            var bytes = Convert.FromBase64String(resource.ContentBase64);
            if (bytes.Length > ProtocolConstants.MaximumEmbeddedMediaResourceBytes ||
                (total += bytes.Length) > ProtocolConstants.MaximumEmbeddedMediaAggregateBytes ||
                !Convert.ToHexString(SHA256.HashData(bytes)).Equals(resource.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !assets.TryAdd(resource.Path, (bytes, resource.ContentType)))
                throw new InvalidOperationException("Invalid session resource digest or size.");
        }
        core.Settings.IsWebMessageEnabled = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) &&
                uri.GetLeftPart(UriPartial.Authority) == origin)
            {
                var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
                if (args.Request.Method is "GET" or "HEAD" && assets.TryGetValue(path, out var asset))
                {
                    args.Response = env.CreateWebResourceResponse(new MemoryStream(asset.Bytes, writable: false).AsRandomAccessStream(), 200, "OK",
                        "Content-Type: " + asset.ContentType + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n" +
                        (asset.ContentType == "text/html" ? "Content-Security-Policy: " + HostedContentPolicy() : ""));
                    return;
                }
            }
            else if (HostedNetworkAllowed(args.Request.Uri) || uri is not null && uri == localResource &&
                args.ResourceContext == CoreWebView2WebResourceContext.Media && args.Request.Method is "GET" or "HEAD") return;
            WebSessionTrace.Record(widgetId,"resource-denied",$"context={args.ResourceContext}");
            args.Response = env.CreateWebResourceResponse(null, 403, "Forbidden", "");
        };
        core.NavigationStarting += (_, args) =>
        {
            if (!IsHostedDocument(args.Uri)) args.Cancel = true;
            else if (hostedNavigation.Task.IsCompleted && !hostedMessages.Writer.TryWrite(Convert.ToBase64String(
                Encoding.UTF8.GetBytes("{\"widgetRail\":1,\"kind\":\"navigation\"}")))) FailHostedSession("Hosted message queue exceeded its bound.");
        };
        core.WebMessageReceived += (_, args) =>
        {
            if (retired || faulted || core is null || args.Source != core.Source || !IsHostedDocument(args.Source)) return;
            try
            {
                var json = args.WebMessageAsJson;
                if (TraceHostedReply(json)) return;
                var bytes = Encoding.UTF8.GetBytes(json);
                if (bytes.Length > WebSessionContract.MaximumMessageBytes ||
                    !hostedMessages.Writer.TryWrite(Convert.ToBase64String(bytes)))
                    FailHostedSession("Hosted page message queue exceeded its bound.");
            }
            catch (Exception error) when (error is ArgumentException or System.Runtime.InteropServices.COMException)
            { FailHostedSession("Hosted page sent an invalid message."); }
        };
        try { await ConfigureHostedTraceAsync(); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or NotImplementedException)
        { WebSessionTrace.Record(widgetId,"trace-unavailable",$"error={error.GetType().Name}"); }
        if (definition.Source.AllowAutoplay)
            await core.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Autoplay, origin, CoreWebView2PermissionState.Allow);
    }

    private bool HostedNetworkAllowed(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Scheme is not ("https" or "wss") || !uri.IsDefaultPort) return false;
        return hostedDefinition!.Source.NetworkOrigins.Any(rule =>
        {
            var wildcard = rule.StartsWith("https://*.", StringComparison.Ordinal);
            var host = new Uri(wildcard ? "https://" + rule[10..] : rule).Host;
            return uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || wildcard && uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
        });
    }

    private string HostedContentPolicy()
    {
        var origins = string.Join(" ", hostedDefinition!.Source.NetworkOrigins);
        var connections = string.Join(" ", hostedDefinition.Source.NetworkOrigins.SelectMany(origin =>
            new[] { origin, "wss://" + origin[8..] }));
        // CSP covers WebSockets/workers as well as ordinary requests; resource interception
        // additionally admits only the exact current loopback media URL granted by the broker.
        return "default-src 'none'; script-src 'self' 'unsafe-inline' " + origins +
            // Local fetches still pass the exact sealed-asset GET/HEAD allowlist above.
            "; style-src 'self' 'unsafe-inline'; connect-src 'self' " + connections +
            "; media-src 'self' blob: http://127.0.0.1:* " + origins +
            "; img-src 'self' data: " + origins + "; font-src 'self'; frame-src " + (origins.Length == 0 ? "'none'" : origins) +
            "; worker-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
    }

    private bool TryHostedPermission(CoreWebView2PermissionRequestedEventArgs args)
    {
        if (!IsHostedPackage) return false;
        args.Handled = true;
        args.SavesInProfile = false;
        args.State = hostedDefinition!.Source.AllowAutoplay && args.PermissionKind == CoreWebView2PermissionKind.Autoplay &&
            (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri.GetLeftPart(UriPartial.Authority) == new Uri(document.Url).GetLeftPart(UriPartial.Authority) || HostedNetworkAllowed(args.Uri))
                ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        WebSessionTrace.Record(widgetId,"permission",$"kind={args.PermissionKind} state={args.State}");
        return true;
    }

    private void FailHostedSession(string reason)
    {
        var error = new InvalidOperationException(reason);
        hostedNavigation.TrySetException(error);
        hostedMessages.Writer.TryComplete(error);
    }
}
