using System.Diagnostics;
using System.Text.Json;

namespace WidgetRail.Samples.GameHelp;

/// <summary>Bounded request metadata only; never persists user or provider content.</summary>
internal sealed class GameHelpRequestDiagnostics(bool grounding, string? directory = null)
{
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly string directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WidgetRail", "applications", "game-help");
    internal string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    internal string Stage { get; private set; } = "credentials";
    internal void SetStage(string stage)
    {
        Stage = stage;
        Write("in-progress", null);
    }
    internal void Started() => Write("started", null);
    internal void Completed() => Write("completed", null);
    internal void Cancelled() => Write("cancelled", null);

    internal GameHelpException Failed(Exception error)
    {
        Write("failed", error);
        var code = error is GameHelpException known ? known.Code :
            Stage == "search-attribution" ? "search_attribution_failed" : "request_failed";
        var message = error is GameHelpException help ? help.Message : Stage switch
        {
            "credentials" => "Game Help could not read the saved API key.",
            "capture-read" => "Game Help could not read the attached capture.",
            "response-parsing" => "Game Help could not process Gemini's response.",
            "search-attribution" => "Gemini responded, but Game Help could not prepare the search attribution.",
            _ => "Game Help could not finish the Gemini request.",
        };
        return new(code, $"{message} [Code: {code}; reference: {Id}]", httpStatus: (error as GameHelpException)?.HttpStatus);
    }

    private void Write(string outcome, Exception? error)
    {
        try
        {
            Directory.CreateDirectory(directory);
            using var output = new MemoryStream();
            using (var json = new Utf8JsonWriter(output))
            {
                json.WriteStartObject();
                json.WriteString("utc", DateTimeOffset.UtcNow);
                json.WriteString("requestId", Id);
                json.WriteString("outcome", outcome);
                json.WriteString("stage", Stage);
                json.WriteNumber("elapsedMilliseconds", elapsed.ElapsedMilliseconds);
                json.WriteBoolean("grounding", grounding);
                if (error is not null)
                {
                    json.WriteString("exceptionType", error.GetType().FullName);
                    json.WriteString("innerExceptionType", error.InnerException?.GetType().FullName);
                    json.WriteNumber("hresult", error.HResult);
                    json.WriteString("code", (error as GameHelpException)?.Code ?? "unexpected_failure");
                    if (error is GameHelpException { HttpStatus: { } status }) json.WriteNumber("httpStatus", status);
                    // Method identity is useful for unexpected failures; no messages, arguments or source paths.
                    json.WriteStartArray("frames");
                    foreach (var frame in new StackTrace(error, false).GetFrames().Take(8))
                    {
                        var method = frame.GetMethod();
                        json.WriteStringValue($"{method?.DeclaringType?.FullName}.{method?.Name}");
                    }
                    json.WriteEndArray();
                }
                json.WriteEndObject();
            }
            var bytes = output.ToArray();
            File.WriteAllBytes(Path.Combine(directory, "last-request.json"), bytes);
            if (error is not null) File.WriteAllBytes(Path.Combine(directory, "last-provider-error.json"), bytes);
        }
        // Logging must never replace the original failure, including in trimmed publications.
        catch (Exception loggingError) when (loggingError is not OutOfMemoryException) { }
    }
}
