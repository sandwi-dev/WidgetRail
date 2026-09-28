namespace WidgetRail.Internal;

/// <summary>Reserved broker handle syntax, not proof of ownership or a live registration.</summary>
internal static class AppLibraryArtworkHandle
{
    internal static bool IsBrokerHandle(string? value) =>
        value is { Length: 44 } && value.StartsWith("library.art.", StringComparison.Ordinal) &&
        value.AsSpan(12).ToString().All(char.IsAsciiHexDigit);
}
