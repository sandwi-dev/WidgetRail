namespace WidgetRail.WidgetProtocol;

/// <summary>A file source carried only by artwork resolution, never by view declarations.</summary>
public sealed record ArtworkFileReference(string Path, string Revision, bool PackageRelative = false)
{
    // Set only by the trusted bridge when admitting a package resource.
    public string? AllowedRoot { get; init; }
}

public static class ArtworkFileContract
{
    public static bool IsValid(ArtworkFileReference file)
    {
        if (file.Path is not { Length: > 0 and <= 1024 } path ||
            file.Revision is not { Length: > 0 and <= 128 } ||
            file.Revision.Any(char.IsControl) || path.Any(char.IsControl))
            return false;
        if (file.PackageRelative)
            return file.AllowedRoot is null && !path.Contains('\\') && !path.Contains(':') &&
                !path.StartsWith('/') && path.Split('/').All(part => part is not ("" or "." or ".."));
        return IsLocalPath(path) && (file.AllowedRoot is null || IsLocalPath(file.AllowedRoot));
    }

    public static bool IsLocalPath(string path) => path.Length is >= 3 and <= 1024 &&
        char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/' &&
        path.IndexOf(':', 2) < 0 && !path.Any(char.IsControl);
}
