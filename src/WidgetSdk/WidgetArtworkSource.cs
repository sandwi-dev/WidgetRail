using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>A lazily resolved image source. The host owns validation, decoding and display lifetime.</summary>
public abstract class WidgetArtworkSource
{
    private protected WidgetArtworkSource() { }
}

/// <summary>A file-backed image. Local paths require full access; package paths are relative to the admitted package.</summary>
public sealed class WidgetFileArtwork : WidgetArtworkSource
{
    public WidgetFileArtwork(string path, string revision, bool packageRelative = false)
        : this(new ArtworkFileReference(path, revision, packageRelative)) { }

    internal WidgetFileArtwork(ArtworkFileReference file)
    {
        if (!ArtworkFileContract.IsValid(file)) throw new ArgumentException("Invalid artwork file reference.", nameof(file));
        File = file;
    }

    public string Path => File.Path;
    public string Revision => File.Revision;
    public bool PackageRelative => File.PackageRelative;
    internal ArtworkFileReference File { get; }
}

internal static class WidgetArtworkSourceContract
{
    internal static bool IsValid(WidgetArtworkSource source) => source switch
    {
        WidgetEncodedArtwork encoded => WidgetEncodedArtworkContract.IsValid(encoded),
        WidgetFileArtwork file => ArtworkFileContract.IsValid(file.File),
        _ => false,
    };
}
