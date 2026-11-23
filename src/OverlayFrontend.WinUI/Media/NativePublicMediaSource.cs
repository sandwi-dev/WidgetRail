using System.Runtime.InteropServices.WindowsRuntime;
using WidgetRail.RemoteMediaTransport;
using WidgetRail.WidgetProtocol;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>A checked container and its independently owned native reader lifetime.</summary>
internal sealed partial class NativePublicMediaSource : IAsyncDisposable
{
    static partial void ObserveOpenStage(string stage);
    private readonly NativePublicMediaStream stream;
    private readonly IRandomAccessStream cursor;
    internal MediaSource Source { get; }
    private readonly string contentType;

    internal sealed record BufferedMedia(PublicMediaBlockSource.BufferedSnapshot Bytes, string ContentType);
    internal BufferedMedia? TryTakeBufferedSnapshot() => stream.TryTakeBufferedSnapshot() is { } bytes ? new(bytes, contentType) : null;

    private NativePublicMediaSource(NativePublicMediaStream stream, IRandomAccessStream cursor, MediaSource source, string contentType)
    { this.stream = stream; this.cursor = cursor; Source = source; this.contentType = contentType; }

    internal static async Task<NativePublicMediaSource> OpenAsync(Uri uri, CancellationToken token, BufferedMedia? buffered = null)
    {
        using var admission = await NativePublicMediaTransport.AdmitDecodeAsync(token);
        ObserveOpenStage("transport-admitted");
        var blocks = buffered?.Bytes.Open(token) ?? await PublicMediaBlockSource.OpenAsync(NativePublicMediaTransport.Client, uri,
            PublicMediaPolicy.SameOrigin(uri), new() { BlockBytes = 256 * 1024 }, token);
        ObserveOpenStage("first-block-ready");
        NativePublicMediaStream? stream = null;
        IRandomAccessStream? cursor = null;
        MediaSource? source = null;
        try
        {
            var type = buffered?.ContentType ?? await ReadContainerTypeAsync(blocks, token);
            stream = new(blocks, type);
            cursor = stream.CreateView();
            if (buffered is null) await ValidateTracksAsync(cursor, token);
            else ObserveOpenStage("checked-bytes-resumed");
            token.ThrowIfCancellationRequested();
            cursor.Seek(0);
            source = MediaSource.CreateFromStream(cursor, type);
            return new(stream, cursor, source, type);
        }
        catch
        {
            source?.Dispose(); cursor?.Dispose();
            if (stream is not null) await stream.DisposeAsync();
            else await blocks.DisposeAsync();
            throw;
        }
    }

    private static async Task<string> ReadContainerTypeAsync(PublicMediaBlockSource blocks, CancellationToken token)
    {
        var header = new byte[64];
        var read = await blocks.ReadAtAsync(0, header, token);
        var type = PublicMediaFormat.Identify(header.AsSpan(0, read));
        if (type is not ("video/mp4" or "video/webm" or "audio/ogg" or "audio/mpeg"))
            throw new InvalidDataException("Unsupported public media container.");
        return type;
    }

    private static async Task ValidateTracksAsync(IRandomAccessStream cursor, CancellationToken token)
    {
        // Container metadata is inspected before starting a playback decoder.
        // A misleading extension/MIME type never selects a codec or external URL.
        using var metadataTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        metadataTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        var profile = await MediaEncodingProfile.CreateFromStreamAsync(cursor).AsTask(metadataTimeout.Token);
        ObserveOpenStage("metadata-read");
        var hasVideo = profile.Video is { Width: > 0, Height: > 0 };
        var hasAudio = profile.Audio is { ChannelCount: > 0, SampleRate: > 0 };
        if (profile.Video is { } video && (video.Width > 0 || video.Height > 0))
        {
            if (!hasVideo || video.Width > ProtocolConstants.MaximumEncodedArtworkDimension ||
                video.Height > ProtocolConstants.MaximumEncodedArtworkDimension ||
                (ulong)video.Width * video.Height > ProtocolConstants.MaximumEncodedArtworkPixels ||
                video.FrameRate.Denominator == 0 || (double)video.FrameRate.Numerator / video.FrameRate.Denominator > 120)
                throw new InvalidDataException("Public video exceeds its decode bounds.");
        }
        if (profile.Audio is { } audio && (audio.ChannelCount > 0 || audio.SampleRate > 0) &&
            (!hasAudio || audio.ChannelCount > 8 || audio.SampleRate > 192000))
            throw new InvalidDataException("Public audio exceeds its decode bounds.");
        if (!hasVideo && !hasAudio)
            throw new InvalidDataException("Public media has no supported bounded track.");
    }

    public async ValueTask DisposeAsync()
    {
        try { Source.Dispose(); }
        finally
        {
            try { cursor.Dispose(); }
            finally { await stream.DisposeAsync(); }
        }
    }
}
