namespace GameBarAlternative.WidgetSdk;

/// <summary>
/// Mirrors the declarative protocol's stable-identifier contract for IDs that
/// the SDK synthesizes on behalf of widget authors. Keeping this check at the
/// composite boundary prevents an otherwise valid-looking component from
/// failing later when its snapshot is created.
/// </summary>
internal static class StableIdentifier
{
    // ViewSnapshotValidator is the wire-contract authority. Keep this private
    // mirror deliberately small so the public protocol surface is unchanged.
    private const int MaximumLength = 128;

    internal static string Child(string parentId, string suffix, string parameterName = "id")
    {
        Validate(parentId, parameterName);
        ValidateSuffix(suffix);

        var childId = string.Concat(parentId, ".", suffix);
        Validate(childId, parameterName);
        return childId;
    }

    internal static void Validate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A stable identifier is required.", parameterName);
        if (value.Length > MaximumLength)
            throw new ArgumentException(
                $"A stable identifier may not exceed {MaximumLength} characters.",
                parameterName);
        if (!value.All(IsIdentifierCharacter))
            throw new ArgumentException(
                "A stable identifier may contain only ASCII letters, digits, '.', '-' and '_'.",
                parameterName);
    }

    private static void ValidateSuffix(string? suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix) || !suffix.All(IsIdentifierCharacter))
            throw new InvalidOperationException("An SDK-generated stable-ID suffix is invalid.");
    }

    private static bool IsIdentifierCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.';
}
