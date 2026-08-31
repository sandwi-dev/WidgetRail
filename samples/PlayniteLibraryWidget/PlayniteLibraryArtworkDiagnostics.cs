namespace WidgetRail.Samples.PlayniteLibrary;

internal interface IPlayniteLibraryArtworkDiagnostics
{
    void Record(string stage, string code, int count, string sizeClass);
}

internal static class PlayniteLibraryArtworkDiagnostics
{
    internal static IPlayniteLibraryArtworkDiagnostics None { get; } =
        new NullDiagnostics();

    internal static bool TryEncode(
        string stage,
        string code,
        int count,
        string sizeClass,
        out string line)
    {
        if (!IsToken(stage) || !IsToken(code) || !IsToken(sizeClass) || count is < 1 or > 128)
        {
            line = string.Empty;
            return false;
        }

        line = string.Concat(
            "stage=", stage,
            " code=", code,
            " count=", count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            " size-class=", sizeClass);
        return true;
    }

    private static bool IsToken(string value) =>
        value.Length is > 0 and <= 48 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_');

    private sealed class NullDiagnostics : IPlayniteLibraryArtworkDiagnostics
    {
        public void Record(string stage, string code, int count, string sizeClass)
        {
        }
    }
}
