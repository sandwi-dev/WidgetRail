using System.Runtime.InteropServices.WindowsRuntime;
using WidgetRail.WidgetProtocol;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal static class NativeArtworkFile
{
    internal static async Task<(IRandomAccessStream Stream, long Length)> OpenAsync(ArtworkFileReference source, CancellationToken token)
    {
        var opened = await ArtworkFileStream.OpenAsync(source, token).ConfigureAwait(false);
        try { return (opened.Stream.AsRandomAccessStream(), opened.Length); }
        catch { opened.Stream.Dispose(); throw; }
    }
}
