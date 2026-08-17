namespace WidgetRail.WidgetProtocol;

/// <summary>
/// Defines the portable identifier grammar shared by declarative snapshots and
/// WRSS class selectors. The grammar intentionally excludes CSS escapes and
/// Unicode so every renderer can match classes ordinally and deterministically.
/// </summary>
public static class StyleClassContract
{
    public static bool IsValidIdentifier(string? className)
    {
        if (string.IsNullOrEmpty(className) ||
            className.Length > ProtocolConstants.MaximumStyleClassLength ||
            !(char.IsAsciiLetter(className[0]) || className[0] == '_'))
            return false;

        for (var index = 1; index < className.Length; index++)
        {
            var character = className[index];
            if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
                return false;
        }

        return true;
    }
}
