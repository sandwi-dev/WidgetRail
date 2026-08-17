namespace WidgetRail.WidgetBridge;

internal sealed class BridgeFrameWriteBoundary
{
    internal static readonly TimeSpan WriteDeadline = TimeSpan.FromSeconds(4);
    private readonly IBridgeFrameWriteAdapter _adapter;

    internal BridgeFrameWriteBoundary(IBridgeFrameWriteAdapter adapter) =>
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

    internal async Task WriteAsync(
        BridgeEnvelope envelope,
        CancellationToken admissionCancellation)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var gateEntered = false;
        var sessionCancellation = _adapter.SessionCancellation;
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(
            sessionCancellation, admissionCancellation);
        try
        {
            await _adapter.AcquireWriterAsync(admission.Token).ConfigureAwait(false);
            gateEntered = true;
            admission.Token.ThrowIfCancellationRequested();
            var channel = _adapter.Channel ?? throw new InvalidOperationException(
                "Native host is not connected.");
            using var writeDeadline = _adapter.CreateDeadline(WriteDeadline) ??
                throw new InvalidOperationException(
                    "Frame write deadline factory returned null.");
            try
            {
                // Admission cancellation is intentionally excluded after the
                // serialized writer is acquired. Once the header can be
                // emitted, only session termination or the fixed deadline may
                // interrupt this exact frame.
                await channel.WriteAsync(envelope, writeDeadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !sessionCancellation.IsCancellationRequested &&
                writeDeadline.IsCancellationRequested)
            {
                _adapter.AbortSession();
                throw;
            }
        }
        finally
        {
            if (gateEntered) _adapter.ReleaseWriter();
        }
    }
}

internal interface IBridgeFrameWriteAdapter
{
    BridgeFrameChannel? Channel { get; }
    CancellationToken SessionCancellation { get; }
    Task AcquireWriterAsync(CancellationToken cancellationToken);
    void ReleaseWriter();
    CancellationTokenSource CreateDeadline(TimeSpan timeout);
    void AbortSession();
}
