using System.Text;
using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>Opaque host-owned restricted HTML presentation belonging to one widget lifetime.</summary>
public sealed record ProviderDocumentReference(string Id, string Provider)
{
    public const string RestrictedHtml = "restricted-html";
    public const string HostOrigin = "https://widgetrail-provider.invalid/";
    public string Url => HostOrigin + Id;
    public bool IsWellFormed() => CaptureLimits.ValidToken(Id) && Provider == RestrictedHtml;
}

[JsonConverter(typeof(ProviderDocumentContentJsonConverter))]
public sealed record ProviderDocumentContent(IReadOnlyList<string> Html)
{
    public const int MaximumCharacters = 64 * 1024;
    internal const int MaximumUtf8Bytes = 64 * 1024;
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static bool IsValid(IReadOnlyList<string>? html)
    {
        if (html is not { Count: > 0 and <= 5 } ||
            html.Any(value => string.IsNullOrWhiteSpace(value) || value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) ||
            html.Sum(value => (long)value.Length) > MaximumCharacters) return false;
        try { return html.Sum(value => (long)StrictUtf8.GetByteCount(value)) <= MaximumUtf8Bytes; }
        catch (EncoderFallbackException) { return false; }
    }
}
