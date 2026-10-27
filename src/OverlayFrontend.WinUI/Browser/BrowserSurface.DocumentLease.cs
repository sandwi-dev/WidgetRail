namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private readonly HashSet<BrowserDocumentLease> documentLeases = [];
    private bool documentLeasesStopping, documentLeasesRetiring;
    private string? CurrentBackendDocumentOrigin => !documentLeasesStopping && !retired && !faulted && !loading && !profileChanging && !sessionStopped &&
        IsBackendSession && pageAdapter?.IsReady == true && core is { } browser && hostedDefinition!.PageAdapter!.AllowsPage(browser.Source)
            ? BrowserSiteIdentity.Origin(browser.Source) : null;

    /// <summary>Host-only lifetime for work tied to one admitted backend document.</summary>
    internal BrowserDocumentLease? CaptureDocumentLease(CancellationToken token)
    {
        if (documentLeasesStopping || documentLeasesRetiring || CurrentBackendDocumentOrigin is not { } origin || core is not { } browser || token.IsCancellationRequested)
            return null;
        var epoch = pageEpoch;
        var lease = new BrowserDocumentLease(token, lifetime.Token,
            () => !documentLeasesStopping && ReferenceEquals(core, browser) && pageEpoch == epoch && CurrentBackendDocumentOrigin == origin,
            released => documentLeases.Remove(released));
        documentLeases.Add(lease);
        return lease;
    }

    private void RetireDocumentLeases()
    {
        if (documentLeasesRetiring) return;
        // Cancellation may synchronously dispose a lease. Enumerate a snapshot.
        documentLeasesRetiring = true;
        try { foreach (var lease in documentLeases.ToArray()) lease.Retire(); }
        finally { documentLeasesRetiring = false; }
    }
}

/// <summary>Use and dispose on the browser dispatcher. Cancellation never grants access to a replacement document.</summary>
internal sealed class BrowserDocumentLease : IDisposable
{
    private readonly CancellationTokenSource cancellation;
    private readonly Func<bool> current;
    private Action<BrowserDocumentLease>? released;
    private bool disposed;

    internal BrowserDocumentLease(CancellationToken request, CancellationToken lifetime, Func<bool> current, Action<BrowserDocumentLease> released)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(request, lifetime);
        Token = cancellation.Token;
        this.current = current;
        this.released = released;
    }
    internal CancellationToken Token { get; }
    internal bool IsCurrent => !disposed && !Token.IsCancellationRequested && current();
    internal void Retire() { if (!disposed) cancellation.Cancel(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation.Cancel();
        released?.Invoke(this);
        released = null;
        cancellation.Dispose();
    }
}
