using System.Text.RegularExpressions;

namespace GameBarAlternative.LauncherExperienceCatalog;

public static partial class LauncherExperienceIdentity
{
    public const int MaximumLength = 128;

    public static bool IsValidId(string? value) =>
        value is { Length: > 0 and <= MaximumLength } && IdPattern().IsMatch(value);

    public static bool IsValidPublisher(string? value) =>
        value is { Length: > 0 and <= MaximumLength } && PublisherPattern().IsMatch(value);

    public static bool IsOwnedByPublisher(string id, string publisher) =>
        string.Equals(id, publisher, StringComparison.Ordinal) ||
        id.StartsWith(publisher + ".", StringComparison.Ordinal);

    public static bool TryParseCanonicalVersion(string? value, out Version? version)
    {
        version = null;
        return value is { Length: > 0 and <= 64 } &&
               Version.TryParse(value, out version) &&
               string.Equals(version.ToString(), value, StringComparison.Ordinal);
    }

    [GeneratedRegex("\\A[a-z0-9](?:[a-z0-9._-]{0,127})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("\\A[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*)+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PublisherPattern();
}
