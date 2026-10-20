using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    internal static WidgetArtworkSource? AdmitArtworkSource(ConfiguredWidget configured, WidgetArtworkSource? source)
    {
        if (source is not WidgetFileArtwork file) return source;
        if (!ArtworkFileContract.IsValid(file.File) || file.File.AllowedRoot is not null)
            throw new BridgeProtocolException("Invalid artwork file authority.");
        if (!file.PackageRelative)
        {
            if (configured.ExecutionTrust != WidgetExecutionTrust.FullTrustCurrentUser)
                throw new BridgeProtocolException("Local artwork requires full-access execution.");
            return file;
        }
        if (configured.PackageRoot is not { } root ||
            !configured.VerifiedPackageFiles.ContainsKey(file.Path))
            throw new BridgeProtocolException("Artwork is not part of the admitted package.");
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        var prefix = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root)) + System.IO.Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new BridgeProtocolException("Artwork escaped its package.");
        return new WidgetFileArtwork(new ArtworkFileReference(path, file.Revision) { AllowedRoot = root });
    }
}
