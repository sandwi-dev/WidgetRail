using System.Security.Cryptography;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetBridge;

internal static class MediaPlayerSourceResolver
{
    internal static async Task<HostMediaPlayerSource> ResolveAsync(ConfiguredWidget widget, MediaPlayerDefinition media, CancellationToken token)
    {
        if (!media.IsWellFormed()) throw new BridgeProtocolException("Invalid media source.");
        var source = media.Source;
        if (source.Kind == MediaPlayerSourceKind.WebUrl) return new(media, source.Location!);
        if (source.Kind == MediaPlayerSourceKind.LocalFile)
        {
            if (widget.ExecutionTrust != WidgetExecutionTrust.FullTrustCurrentUser)
                throw new BridgeProtocolException("Local media files require a full-trust widget.");
            var path = Path.GetFullPath(source.Location!);
            if (!MediaPlayerSource.IsLocalPath(path) || !File.Exists(path)) throw new BridgeProtocolException("The local media file is unavailable.");
            return new(media, new Uri(path).AbsoluteUri);
        }
        if (source.Kind != MediaPlayerSourceKind.PackageAsset || widget.PackageRoot is null ||
            !widget.VerifiedPackageFiles.TryGetValue(source.Location!, out var verified))
            throw new BridgeProtocolException("Media is absent from the sealed package inventory.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(widget.PackageRoot));
        var asset = Path.GetFullPath(source.Location!.Replace('/', Path.DirectorySeparatorChar), root);
        if (!asset.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new BridgeProtocolException("Media escaped the package root.");
        var current = root;
        foreach (var segment in new[] { "" }.Concat(source.Location.Split('/')))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new BridgeProtocolException("Media traverses a reparse point.");
        }
        await using var stream = new FileStream(asset, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (stream.Length != verified.Length || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream, token)), verified.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new BridgeProtocolException("Media changed after package verification.");
        return new(media, new Uri(asset).AbsoluteUri);
    }
}
