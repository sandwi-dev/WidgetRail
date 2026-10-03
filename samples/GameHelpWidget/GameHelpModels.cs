using WidgetRail.WidgetProtocol;
namespace WidgetRail.Samples.GameHelp;

public sealed record GameHelpTurn(string Role, string Text);
public sealed record GameHelpSource(string Title, string Url);
public sealed record GameHelpReplyOption(string Label, string Message);
public sealed record GameHelpAnswer(string Answer, IReadOnlyList<string> Observations, string Uncertainty,
    IReadOnlyList<GameHelpReplyOption> ReplyOptions, IReadOnlyList<string> SearchQueries, IReadOnlyList<GameHelpSource> Sources,
    ProviderDocumentReference? Attribution = null,
    [property: System.Text.Json.Serialization.JsonIgnore] IReadOnlyList<string>? SearchSuggestionsHtml = null,
    string DetailedSolution = "")
{
    public IReadOnlyList<GameHelpCitation> Citations { get; init; } = [];
}
public sealed record GameHelpRequest(string ApplicationName, string WindowTitle, string SpoilerPreference,
    IReadOnlyList<GameHelpTurn> Conversation, bool WebGrounding, CaptureAttachment? Attachment = null, [property: System.Text.Json.Serialization.JsonIgnore] byte[]? Content = null);
public sealed class GameHelpException(string code, string message, Exception? inner = null, int? httpStatus = null) : Exception(message, inner)
{
    public string Code { get; } = code;
    public int? HttpStatus { get; } = httpStatus;
}

public static class GameHelpContract
{
    public static bool IsValidRequest(GameHelpRequest? request) => request is not null &&
        Text(request.ApplicationName, 120) && Text(request.WindowTitle, 240, true) &&
        request.SpoilerPreference is "Hints first" or "Balanced" or "Full solution" &&
        request.Conversation is { Count: >= 1 and <= 24 } &&
        request.Conversation.All(turn => turn is not null && turn.Role is "user" or "assistant" && Text(turn.Text, 6000)) &&
        request.Conversation.Sum(turn => turn.Text.Length) <= 24000 &&
        (request.Attachment is null || request.Attachment.IsWellFormed());

    public static bool IsValidAnswer(GameHelpAnswer? answer) => answer is not null && Text(answer.Answer, 6000) && Text(answer.DetailedSolution, 6000, true) &&
        Text(answer.Uncertainty, 800, true) && Strings(answer.Observations, 6, 320) &&
        answer.ReplyOptions is { Count: <= 5 } && answer.ReplyOptions.All(option => option is not null &&
            Text(option.Label, 120) && Text(option.Message, 600)) && Strings(answer.SearchQueries, 3, 240) &&
        answer.Sources is { Count: <= GameHelpCitations.MaximumAnnotations } && answer.Sources.All(source => source is not null &&
            Text(source.Title, 240) && WebBrowserDocument.IsWebUrl(source.Url)) &&
        answer.Citations is { Count: <= 4096 } && answer.Citations.All(citation => citation is not null && citation.Field is { Length: > 0 and <= 64 } &&
            citation.SourceNumber > 0 && citation.SourceNumber <= answer.Sources.Count &&
            GameHelpCitations.FieldText(answer, citation.Field) is { } text && citation.Start < citation.End &&
            GameHelpCitations.Boundary(text, citation.Start) && GameHelpCitations.Boundary(text, citation.End)) &&
        (answer.Attribution is null || answer.Attribution.IsWellFormed());

    private static bool Strings(IReadOnlyList<string>? values, int count, int length) =>
        values is not null && values.Count <= count && values.All(value => Text(value, length));
    private static bool Text(string? value, int length, bool empty = false) => value is not null &&
        value.Length <= length && (empty || !string.IsNullOrWhiteSpace(value)) &&
        !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
}
