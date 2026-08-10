using GameBarAlternative.WidgetBridge;

internal sealed class BridgeTestFrameReader
{
    private readonly BridgeFrameChannel _channel;
    private readonly Action _abortConnection;
    private int _activeRead;
    private int _canceled;
    private int _terminal;

    internal BridgeTestFrameReader(
        BridgeFrameChannel channel,
        Action abortConnection)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _abortConnection = abortConnection ??
            throw new ArgumentNullException(nameof(abortConnection));
    }

    internal bool IsTerminal => Volatile.Read(ref _terminal) != 0;
    internal bool WasCanceled => Volatile.Read(ref _canceled) != 0;

    internal async Task<BridgeEnvelope> ReadAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = new CancellationTokenSource(timeout);
        return await ReadAsync(deadline.Token).ConfigureAwait(false);
    }

    internal async Task<BridgeEnvelope> ReadAsync(CancellationToken cancellationToken)
    {
        if (IsTerminal)
            throw new InvalidOperationException("The bridge test reader is terminal.");
        if (Interlocked.CompareExchange(ref _activeRead, 1, 0) != 0)
            throw new InvalidOperationException("A bridge test read is already active.");

        using var cancellation = cancellationToken.UnsafeRegister(
            static state => ((BridgeTestFrameReader)state!).Terminate(), this);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _channel.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            cancellationToken.IsCancellationRequested &&
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            Volatile.Write(ref _canceled, 1);
            Terminate();
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or
                                               ObjectDisposedException or
                                               BridgeProtocolException)
        {
            Terminate();
            throw;
        }
        finally
        {
            Volatile.Write(ref _activeRead, 0);
        }
    }

    private void Terminate()
    {
        if (Interlocked.Exchange(ref _terminal, 1) != 0) return;
        try
        {
            _abortConnection();
        }
        catch (Exception exception) when (exception is IOException or
                                               ObjectDisposedException)
        {
        }
    }
}
