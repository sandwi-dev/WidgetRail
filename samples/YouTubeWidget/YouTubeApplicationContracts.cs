using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YouTubeWidget;

public sealed record YouTubeConfigurationSummary(bool IsConfigured);

public sealed record YouTubeSearchItem(
    string VideoId,
    string Title,
    string Channel,
    string Duration,
    string ThumbnailUrl);

public sealed record YouTubeSearchPage(
    IReadOnlyList<YouTubeSearchItem> Items,
    string? NextPageToken,
    int? TotalResults);

public interface IYouTubeApplicationService : IAsyncDisposable
{
    ValueTask<YouTubeConfigurationSummary> GetConfigurationAsync(
        CancellationToken cancellationToken);

    ValueTask ConfigureApiKeyAsync(string apiKey, CancellationToken cancellationToken);

    ValueTask DeleteApiKeyAsync(CancellationToken cancellationToken);

    ValueTask OpenGoogleCloudConsoleAsync(CancellationToken cancellationToken);

    ValueTask<YouTubeSearchPage> SearchAsync(
        string query,
        string? pageToken,
        int pageSize,
        CancellationToken cancellationToken);
}

public sealed class YouTubeApplicationException : Exception
{
    public YouTubeApplicationException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64 ||
            code.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
            throw new ArgumentException("Error codes must be bounded stable tokens.", nameof(code));
        Code = code;
    }

    public string Code { get; }
}

internal sealed class UnavailableYouTubeApplicationService : IYouTubeApplicationService
{
    public ValueTask<YouTubeConfigurationSummary> GetConfigurationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new YouTubeConfigurationSummary(false));
    }

    public ValueTask ConfigureApiKeyAsync(string apiKey, CancellationToken cancellationToken) =>
        ValueTask.FromException(new YouTubeApplicationException(
            "setup_unavailable", "API-key setup requires the installed full-trust package."));

    public ValueTask DeleteApiKeyAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask OpenGoogleCloudConsoleAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask<YouTubeSearchPage> SearchAsync(
        string query, string? pageToken, int pageSize, CancellationToken cancellationToken) =>
        ValueTask.FromException<YouTubeSearchPage>(new YouTubeApplicationException(
            "setup_required", "Configure a YouTube Data API key before searching."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
