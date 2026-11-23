using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Streaming.Adaptive;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using WidgetRail.RemoteMediaTransport;
using WinHttpClient = Windows.Web.Http.HttpClient;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class NativeDashMediaSource : IAsyncDisposable
{
    // Windows must not issue an unmediated request, including during manifest creation.
    private sealed partial class RejectNetwork : IHttpFilter
    {
        public IAsyncOperationWithProgress<Windows.Web.Http.HttpResponseMessage, HttpProgress> SendRequestAsync(
            Windows.Web.Http.HttpRequestMessage request) => AsyncInfo.Run<Windows.Web.Http.HttpResponseMessage, HttpProgress>(
                (_, _) => Task.FromException<Windows.Web.Http.HttpResponseMessage>(new IOException("Unmapped adaptive media request.")));
        public void Dispose() { }
    }

    private readonly AdaptiveMediaSource adaptive;
    private readonly PublicMediaClient downloads;
    private readonly WinHttpClient nativeClient;
    private readonly PublicDashManifest manifest;
    private readonly PublicMediaPolicy policy;
    private readonly CancellationTokenSource lifetime;
    private readonly object gate = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool retiring;
    private int pending;
    private long remaining = 128 * 1024 * 1024;
    private const int SegmentLimit = 8 * 1024 * 1024;
    internal MediaSource Source { get; }

    private NativeDashMediaSource(AdaptiveMediaSource adaptive, WinHttpClient nativeClient, PublicDashManifest manifest,
        PublicMediaPolicy policy, PublicMediaClient downloads, CancellationToken token)
    {
        this.adaptive = adaptive;
        this.downloads = downloads;
        this.nativeClient = nativeClient;
        this.manifest = manifest;
        this.policy = policy;
        Source = MediaSource.CreateFromAdaptiveMediaSource(adaptive);
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        adaptive.DownloadRequested += DownloadRequested;
    }

    internal static Task<NativeDashMediaSource> OpenAsync(Uri uri, CancellationToken token) =>
        OpenAsync(uri, NativePublicMediaTransport.Client, token);

    private static async Task<NativeDashMediaSource> OpenAsync(Uri uri, PublicMediaClient downloads, CancellationToken token)
    {
        var policy = PublicMediaPolicy.SameOrigin(uri);
        var raw = await downloads.DownloadAsync(uri, policy, 128 * 1024, token);
        var manifest = PublicDashManifest.Parse(raw.EffectiveUri, raw.Bytes);
        var native = new WinHttpClient(new RejectNetwork());
        AdaptiveMediaSource? adaptive = null;
        try
        {
            using var bytes = new MemoryStream(manifest.Manifest, writable: false);
            using var input = bytes.AsInputStream();
            var result = await AdaptiveMediaSource.CreateFromStreamAsync(input, PublicDashManifest.PlaybackUri,
                "application/dash+xml", native).AsTask(token);
            adaptive = result.MediaSource;
            if (result.Status != AdaptiveMediaSourceCreationStatus.Success || adaptive is null)
                throw new InvalidDataException("Windows could not open the trailer.");
            token.ThrowIfCancellationRequested();
            return new(adaptive, native, manifest, policy, downloads, token);
        }
        catch { adaptive?.Dispose(); native.Dispose(); throw; }
    }

    private async void DownloadRequested(AdaptiveMediaSource sender, AdaptiveMediaSourceDownloadRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        var reserved = 0;
        var charged = 0;
        try
        {
            if (!manifest.TryResolve(args.ResourceUri, out var source) || source is null ||
                args.ResourceByteRangeLength.HasValue || args.ResourceByteRangeOffset.HasValue)
                throw new InvalidDataException("Unknown trailer segment.");
            lock (gate)
            {
                if (retiring || pending >= 8 || remaining <= 0) throw new IOException("Trailer fetch limit reached.");
                reserved = (int)Math.Min(remaining, SegmentLimit);
                remaining -= reserved;
                pending++;
            }
            // A failed or interrupted transfer consumes its reservation as its byte count is unknown.
            charged = reserved;
            var bytes = await downloads.DownloadAsync(source, policy, reserved, lifetime.Token).ConfigureAwait(false);
            charged = bytes.Bytes.Length;
            lifetime.Token.ThrowIfCancellationRequested();
            args.Result.Buffer = bytes.Bytes.ToArray().AsBuffer();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            args.Result.ExtendedStatus = unchecked((uint)0x80004005);
        }
        finally
        {
            try { deferral.Complete(); }
            finally
            {
                if (reserved > 0) lock (gate)
                {
                    remaining += reserved - charged;
                    if (--pending == 0 && retiring) drained.TrySetResult();
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            retiring = true;
            if (pending == 0) drained.TrySetResult();
        }
        adaptive.DownloadRequested -= DownloadRequested;
        lifetime.Cancel();
        await drained.Task;
        try { Source.Dispose(); }
        finally { adaptive.Dispose(); nativeClient.Dispose(); lifetime.Dispose(); }
    }
}
