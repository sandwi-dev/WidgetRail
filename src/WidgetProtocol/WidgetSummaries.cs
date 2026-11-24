using System.Text.Json;

namespace WidgetRail.WidgetProtocol;

public static class WidgetSummaryScope
{
    public const string Prefix = "widgets.summary.read.v1:";
    public static string ForProvider(string provider)
    {
        if (!WidgetManifestValidator.IsValidPackageIdentity(provider) ||
            Prefix.Length + provider.Length > ProtocolConstants.MaximumCapabilityIdLength)
            throw new ArgumentException("Invalid summary provider.", nameof(provider));
        return Prefix + provider;
    }

    public static string? Provider(string capability) =>
        capability.StartsWith(Prefix, StringComparison.Ordinal) && capability.Length <= ProtocolConstants.MaximumCapabilityIdLength &&
        WidgetManifestValidator.IsValidPackageIdentity(capability[Prefix.Length..]) ? capability[Prefix.Length..] : null;
}

public sealed record ReadWidgetSummary(string Provider);
public sealed record PublishWidgetSummary(string Audience, long Sequence, JsonElement? Value);
public sealed record WidgetSummaryValue(JsonElement? Value, string? ReaderProof = null)
{
    public bool ProviderRunning { get; init; }
}
public sealed record CheckWidgetSummaryReader(string Proof);
public sealed record WidgetSummaryMicrophone(string Reader, string Token, WebSessionReference Session);
public sealed record WidgetSummaryPin(string Reader, string Token, string LayoutId);
