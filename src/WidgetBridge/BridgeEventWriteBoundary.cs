namespace GameBarAlternative.WidgetBridge;

internal sealed class BridgeEventWriteBoundary
{
    internal static readonly TimeSpan WriteDeadline = TimeSpan.FromSeconds(4);
    private readonly IBridgeEventWriteAdapter _adapter;

    internal BridgeEventWriteBoundary(IBridgeEventWriteAdapter adapter) =>
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

    internal async Task WriteAsync(
        BridgeEnvelope envelope,
        CancellationToken publicationCancellation)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var gateEntered = false;
        try
        {
            var sessionCancellation = _adapter.SessionCancellation;
            using var admission = CancellationTokenSource.CreateLinkedTokenSource(
                sessionCancellation, publicationCancellation);
            await _adapter.AcquireWriterAsync(admission.Token).ConfigureAwait(false);
            gateEntered = true;
            admission.Token.ThrowIfCancellationRequested();
            var channel = _adapter.Channel ?? throw new InvalidOperationException(
                "Native host is not connected.");
            using var writeDeadline = _adapter.CreateDeadline(WriteDeadline) ??
                throw new InvalidOperationException(
                    "Event write deadline factory returned null.");
            try
            {
                await channel.WriteAsync(envelope, writeDeadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !sessionCancellation.IsCancellationRequested &&
                writeDeadline.IsCancellationRequested)
            {
                // A canceled in-flight frame may be partial. End the session
                // before any later frame can be written to the same stream.
                _adapter.AbortSession();
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or
                                               OperationCanceledException or
                                               ObjectDisposedException or
                                               InvalidOperationException)
        {
            // The main request loop owns native-host disconnect handling.
        }
        finally
        {
            if (gateEntered) _adapter.ReleaseWriter();
        }
    }
}

internal interface IBridgeEventWriteAdapter
{
    BridgeFrameChannel? Channel { get; }
    CancellationToken SessionCancellation { get; }
    Task AcquireWriterAsync(CancellationToken cancellationToken);
    void ReleaseWriter();
    CancellationTokenSource CreateDeadline(TimeSpan timeout);
    void AbortSession();
}
