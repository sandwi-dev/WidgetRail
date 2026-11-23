using WidgetRail.RemoteMediaTransport;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>Process-owned anonymous transport budget shared by opted-in images and media readers.</summary>
internal static class NativePublicMediaTransport
{
    // Individual presentation lifetimes cancel both queued and active operations.
    // The connection pool lives for the frontend process; it has no profile state.
    internal static PublicMediaClient Client { get; } = new();
    private static readonly SemaphoreSlim decodeSlots = new(4, 4);

    internal static async Task<IDisposable> AdmitDecodeAsync(CancellationToken token)
    {
        // Pending work is already owned and bounded by the presenter's retained
        // image demands (or the player's slot). Wait before acquiring bytes.
        // Rejecting a normal viewport burst would permanently cache an empty
        // image as a failed resource; stale demands instead cancel their wait.
        await decodeSlots.WaitAsync(token);
        return new DecodeLease();
    }

    private sealed class DecodeLease : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            decodeSlots.Release();
        }
    }
}
