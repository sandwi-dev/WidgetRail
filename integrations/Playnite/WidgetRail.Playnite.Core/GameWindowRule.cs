using System;
using System.IO;
using System.Runtime.Serialization;

namespace WidgetRail.Playnite;

/// <summary>Identifies an already running image. Never use the stored value as a launch command.</summary>
[DataContract]
public sealed class GameWindowRule
{
    [DataMember(Order = 1)] public string? PackageFamily { get; private set; }
    [DataMember(Order = 2)] public string Image { get; private set; } = "";

    public static GameWindowRule? FromImage(string executable, string? packageFamily = null, string? packageRoot = null)
    {
        if (!IsAbsolute(executable) ||
            !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        var image = Path.GetFullPath(executable);
        if (packageFamily is null)
            return packageRoot is null ? new GameWindowRule { Image = image } : null;
        if (string.IsNullOrWhiteSpace(packageFamily) || !IsAbsolute(packageRoot)) return null;
        var prefix = Path.GetFullPath(packageRoot!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!image.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return new GameWindowRule { PackageFamily = packageFamily, Image = image.Substring(prefix.Length) };
    }

    private static bool IsAbsolute(string? path) => path != null && (path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'));

    public bool Matches(GameWindowRule? live) => live is not null && !string.IsNullOrWhiteSpace(Image) &&
        string.Equals(PackageFamily, live.PackageFamily, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Image, live.Image, StringComparison.OrdinalIgnoreCase);
}
