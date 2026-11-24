using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.Integrations;

internal static class DiscordActivityIntegration
{
    internal static readonly JsonElement OpenPayload = JsonSerializer.SerializeToElement(new { packageId = Provider });
    internal static WidgetIntentContract Open => WidgetIntentContracts.SummaryProvider;

    private static readonly CompiledWidgetIntentContract compiled = CompiledWidgetIntentContract.Create(Open);
    internal const string Provider = "widgetrail.samples.discord-controls";
    internal const string Audience = "widgetrail.samples.playnite-library";
    internal static WidgetIntentContract Perform => WidgetIntentContracts.SummaryActionV2;
    private static readonly CompiledWidgetIntentContract action = CompiledWidgetIntentContract.Create(Perform);

    private static readonly CompiledWidgetIntentContract legacyAction = CompiledWidgetIntentContract.Create(WidgetIntentContracts.SummaryAction);
    internal static string? ActionToken(WidgetIntentRequest request) =>
        request.ContractId == action.Id && request.IsWellFormed() &&
        (request.Version == action.Version && request.SchemaDigest == action.SchemaDigest && action.Accepts(request.Payload) ||
         request.Version == legacyAction.Version && request.SchemaDigest == legacyAction.SchemaDigest && legacyAction.Accepts(request.Payload)) ? request.Payload.GetProperty("token").GetString() : null;

    internal static bool IsOpenRequest(WidgetIntentRequest request) =>
        request.ContractId == compiled.Id && request.Version == compiled.Version &&
        request.SchemaDigest == compiled.SchemaDigest && request.IsWellFormed() && compiled.Accepts(request.Payload) &&
        request.Payload.GetProperty("packageId").GetString() == Provider;
}

internal enum DiscordSummaryAction { JoinVoice, WatchAndPin }

internal sealed record DiscordActivityPerson(string Key, string Name, string Detail, string? Avatar,
    bool Streaming, DiscordSummaryAction? Action, string? Token)
{
    internal string? ActionLabel => Action switch
    {
        DiscordSummaryAction.JoinVoice => "Join voice",
        DiscordSummaryAction.WatchAndPin => "Watch & pin",
        _ => null
    };
}

internal sealed record DiscordActivitySummary(string Session, string Mode, string Title, int Count,
    bool Truncated, DiscordActivityPerson[] People)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ConnectionFailed { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    internal string? ReaderProof { get; init; }
}
