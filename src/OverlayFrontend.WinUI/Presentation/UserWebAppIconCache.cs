using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.PlatformSettings;
using Windows.Graphics.Imaging;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Bounded, lazily decoded website icons. Loading an icon never initializes a website.</summary>
internal sealed class UserWebAppIconCache(UserWebAppStore store)
{
    private readonly Dictionary<string, Task<ImageSource?>> icons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> versions = new(StringComparer.Ordinal);
    internal int Version(string id) => versions.GetValueOrDefault(id);
    internal void Prune(IReadOnlySet<string> ids)
    {
        foreach (var id in icons.Keys.Where(id => !ids.Contains(id)).ToArray()) icons.Remove(id);
        foreach (var id in versions.Keys.Where(id => !ids.Contains(id)).ToArray()) versions.Remove(id);
    }
    internal void Invalidate(string id) { icons.Remove(id); versions[id] = Version(id) + 1; }
    internal Task<ImageSource?> GetAsync(string id)
    {
        if (!icons.TryGetValue(id, out var icon)) icons[id] = icon = LoadAsync(id);
        return icon;
    }
    internal static async Task ValidateAsync(byte[] bytes)
    {
        using var data = new MemoryStream(bytes, writable: false);
        using var stream = data.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if (decoder.PixelWidth is 0 or >256 || decoder.PixelHeight is 0 or >256 || decoder.FrameCount != 1)
            throw new InvalidDataException("The website icon exceeds the supported dimensions.");
        using var bitmap = await decoder.GetSoftwareBitmapAsync();
    }
    private async Task<ImageSource?> LoadAsync(string id)
    {
        try
        {
            var bytes = await store.ReadIconAsync(id);
            if (bytes is null) return null;
            await ValidateAsync(bytes);
            using var data = new MemoryStream(bytes, writable: false);
            using var stream = data.AsRandomAccessStream();
            var image = new BitmapImage { DecodePixelWidth = 128, DecodePixelHeight = 128 };
            await image.SetSourceAsync(stream);
            return image;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Diagnostics.FrontendFailureLog.Current.Write("webapp-icon-load", error); return null; }
    }
}
