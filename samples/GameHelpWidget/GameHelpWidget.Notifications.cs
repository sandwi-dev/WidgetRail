using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameHelp;

public sealed partial class GameHelpWidget
{
    public static WidgetIntentContract OpenResultContract { get; } = new("community.widgetrail.game-help.result.open", 1,
        JsonDocument.Parse("""{"type":"object","additionalProperties":false,"properties":{"resultId":{"type":"string","minLength":1,"maxLength":20}},"required":["resultId"]}""").RootElement.Clone());
    private static readonly CompiledWidgetIntentContract resultContract = CompiledWidgetIntentContract.Create(OpenResultContract);

    public override ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsActive || !request.IsWellFormed() || request.ContractId != resultContract.Id || request.Version != resultContract.Version ||
            request.SchemaDigest != resultContract.SchemaDigest || !resultContract.Accepts(request.Payload) ||
            !long.TryParse(request.Payload.GetProperty("resultId").GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < 1)
            return ValueTask.FromResult(WidgetIntentResult.Rejected);
        // A trimmed or cleared result opens the conversation quietly; it never reissues the question.
        model.Update(state => state with
        {
            Settings = false, LibraryOpen = false, CaptureOptions = false, PreviewReadOnly = true,
            ConfirmNew = false, ConfirmDelete = false,
            Reveal = state.Messages.Any(message => message.Id == id && message.Role == "assistant")
                ? new(CreateFocusRequest().Id, "help.transcript", "help.message." + id) : null,
            FocusGroup = "help.compose-focus", FocusRequest = CreateFocusRequest().Id,
        });
        return ValueTask.FromResult(WidgetIntentResult.Accepted);
    }
    private async Task NotifyResultAsync(long id, CancellationToken token)
    {
        try
        {
            await HostServices.Notifications.ShowAsync(new("answer-ready", "Your answer is ready")
            {
                CategoryId = "results", Body = "Open Game Help to continue your conversation.",
                Visibility = WidgetNotificationVisibility.SourceHidden,
                OpenIntent = WidgetIntentRequest.Create(OpenResultContract,
                    JsonSerializer.SerializeToElement(new { resultId = id.ToString(CultureInfo.InvariantCulture) }), WidgetIntentPresentation.OpenWidget),
            }, token);
        }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or OperationCanceledException)
        { /* Optional delivery must never turn a completed answer into a failed request. */ }
    }
}
