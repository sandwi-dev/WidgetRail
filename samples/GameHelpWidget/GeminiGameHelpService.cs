using System.Security.Cryptography;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
namespace WidgetRail.Samples.GameHelp;

/// <summary>Application-owned AI integration. The framework supplies only capture and restricted documents.</summary>
internal sealed class GeminiGameHelpService(Func<WidgetHostServices> services) : IGameHelpService, IDisposable
{
    private readonly GameHelpKeyStore keys = new();
    private readonly GeminiGameHelpClient gemini = new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(100) });
    public Task<bool> HasKeyAsync(CancellationToken token) => Task.FromResult(keys.Exists(token));
    public Task SaveKeyAsync(string key, CancellationToken token) { keys.Save(key, token); return Task.CompletedTask; }
    public Task DeleteKeyAsync(CancellationToken token) { keys.Delete(token); return Task.CompletedTask; }
    public async Task<GameHelpAnswer> AskAsync(GameHelpRequest request, CancellationToken token)
    {
        byte[]? content = null;
        ProviderDocumentReference? document = null;
        var diagnostics = new GameHelpRequestDiagnostics(request.WebGrounding);
        diagnostics.Started();
        try
        {
            var key = keys.Read(token) ?? throw new GameHelpException("key_required", "Add your Gemini API key in Game Help settings.");
            if (request.Attachment is { } attachment)
            {
                diagnostics.SetStage("capture-read");
                content = await services().Capture.ReadContentAsync(attachment, token).ConfigureAwait(false);
            }
            var answer = await gemini.AskAsync(request with { Content = content }, key, token, diagnostics.SetStage).ConfigureAwait(false);
            if (answer.SearchSuggestionsHtml is { } html)
            {
                diagnostics.SetStage("search-attribution");
                document = await services().Documents.CreateAsync(html, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            diagnostics.Completed();
            return answer with { Attribution = document, SearchSuggestionsHtml = null };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var cancelled = error is OperationCanceledException && token.IsCancellationRequested;
            var failure = cancelled ? null : diagnostics.Failed(error);
            if (cancelled) diagnostics.Cancelled();
            if (document is not null)
                try { await services().Documents.DiscardAsync(document, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException) { }
            if (failure is not null) throw failure;
            throw;
        }
        finally { if (content is not null) CryptographicOperations.ZeroMemory(content); }
    }
    public void Dispose() => gemini.Dispose();
}
