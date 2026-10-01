using System.Runtime.InteropServices.WindowsRuntime;
using WidgetRail.WidgetProtocol;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Bounded native HTTP acquisition, including responses without Content-Length.</summary>
internal static class NativeArtworkStream
{
    internal static async Task<IRandomAccessStream> OpenAsync(Uri uri, CancellationToken token)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Artwork requires an HTTPS source.");
        using var filter = new Windows.Web.Http.Filters.HttpBaseProtocolFilter { AllowUI = false };
        using var client = new Windows.Web.Http.HttpClient(filter);
        using var response = await client.GetAsync(uri, Windows.Web.Http.HttpCompletionOption.ResponseHeadersRead).AsTask(token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > ProtocolConstants.MaximumEncodedArtworkBytes)
            throw new InvalidDataException("Artwork exceeds its encoded resource bound.");
        using var input = await response.Content.ReadAsInputStreamAsync().AsTask(token).ConfigureAwait(false);
        return await ReadBoundedAsync(input, token).ConfigureAwait(false);
    }

    internal static async Task<IRandomAccessStream> ReadBoundedAsync(IInputStream input, CancellationToken token)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            var buffer = new Windows.Storage.Streams.Buffer(64 * 1024);
            ulong total = 0;
            while (true)
            {
                // Read at most the remaining bound plus one byte to distinguish
                // a complete exact-bound response from an oversized/chunked one.
                var count = (uint)Math.Min(buffer.Capacity, ProtocolConstants.MaximumEncodedArtworkBytes - total + 1);
                var read = await input.ReadAsync(buffer, count, InputStreamOptions.Partial).AsTask(token).ConfigureAwait(false);
                if (read.Length == 0) break;
                total += read.Length;
                if (total > ProtocolConstants.MaximumEncodedArtworkBytes)
                    throw new InvalidDataException("Artwork exceeds its encoded resource bound.");
                await stream.WriteAsync(read).AsTask(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            stream.Seek(0);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
