using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// Creates validated hierarchical IDs for widget elements, actions, input
/// scopes, and operation keys.
/// </summary>
public static class WidgetIds
{
    /// <summary>Starts a hierarchy at one valid protocol identifier.</summary>
    public static WidgetIdScope Scope(string rootId) => new(rootId);
}

/// <summary>
/// A validated stable-ID prefix. Scope and leaf segments intentionally reject
/// dots so every hierarchy boundary remains visible in the resulting ID.
/// </summary>
public sealed class WidgetIdScope
{
    private const int MaximumSegmentLength = 64;
    private const int MaximumDurableKeyLength = 4096;
    private const int FingerprintBytes = 12;

    internal WidgetIdScope(string prefix)
    {
        StableIdentifier.Validate(prefix, nameof(prefix));
        Prefix = prefix;
    }

    /// <summary>The complete validated prefix represented by this scope.</summary>
    public string Prefix { get; }

    /// <summary>Creates a validated child scope.</summary>
    public WidgetIdScope Scope(string segment) =>
        new(StableIdentifier.Child(Prefix, ValidateSegment(segment, nameof(segment))));

    /// <summary>Creates one validated leaf ID under this scope.</summary>
    public string Id(string name) =>
        StableIdentifier.Child(Prefix, ValidateSegment(name, nameof(name)));

    /// <summary>
    /// Creates a deterministic opaque leaf ID for a durable domain key. The
    /// key is hashed rather than embedded so provider identity and unsafe
    /// characters never cross into the declarative snapshot.
    /// </summary>
    public string KeyedId(string name, string durableKey)
    {
        var safeName = ValidateSegment(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(durableKey);
        if (durableKey.Length > MaximumDurableKeyLength)
            throw new ArgumentException(
                $"A durable key may not exceed {MaximumDurableKeyLength} characters.",
                nameof(durableKey));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(durableKey));
        var fingerprint = Convert.ToHexString(bytes.AsSpan(0, FingerprintBytes))
            .ToLowerInvariant();
        return StableIdentifier.Child(Prefix, $"{safeName}-{fingerprint}");
    }

    public override string ToString() => Prefix;

    private static string ValidateSegment(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A stable-ID segment is required.", parameterName);
        if (value.Length > MaximumSegmentLength)
            throw new ArgumentException(
                $"A stable-ID segment may not exceed {MaximumSegmentLength} characters.",
                parameterName);
        if (!value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            throw new ArgumentException(
                "A stable-ID segment may contain only ASCII letters, digits, '-' and '_'.",
                parameterName);
        return value;
    }
}
