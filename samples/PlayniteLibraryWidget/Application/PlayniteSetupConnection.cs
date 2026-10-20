using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteSetupConnectionKind { Ready, Offline, Authentication, WrongProfile, Incompatible, EvalUnavailable }
internal interface IPlayniteSetupConnection
{
    ValueTask<PlayniteSetupConnectionKind> CheckAsync(PlayniteSetupInstallation installation, string credential, CancellationToken token);
}
internal sealed class PlayniteSetupConnection : IPlayniteSetupConnection
{
    public async ValueTask<PlayniteSetupConnectionKind> CheckAsync(PlayniteSetupInstallation installation, string credential, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
            { BaseAddress = new Uri("http://localhost:19821/"), Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        try
        {
            using var response = await http.GetAsync("api/app/info", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return PlayniteSetupConnectionKind.Authentication;
            if (!response.IsSuccessStatusCode) return PlayniteSetupConnectionKind.Incompatible;
            using var info = await ReadAsync(response, timeout.Token).ConfigureAwait(false);
            if (info.RootElement.ValueKind != JsonValueKind.Object || !info.RootElement.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object ||
                !paths.TryGetProperty("application", out var application) || !paths.TryGetProperty("configuration", out var configuration) ||
                application.ValueKind != JsonValueKind.String || configuration.ValueKind != JsonValueKind.String)
                return PlayniteSetupConnectionKind.Incompatible;
            if (!string.Equals(PlayniteSetupFiles.LocalPath(application.GetString()!), installation.ProgramDirectory, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(PlayniteSetupFiles.LocalPath(configuration.GetString()!), installation.DataDirectory, StringComparison.OrdinalIgnoreCase))
                return PlayniteSetupConnectionKind.WrongProfile;
            using var games = await http.GetAsync("api/games?offset=0&limit=1", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!games.IsSuccessStatusCode) return PlayniteSetupConnectionKind.Incompatible;
            using var page = await ReadAsync(games, timeout.Token).ConfigureAwait(false);
            if (page.RootElement.ValueKind != JsonValueKind.Object || !page.RootElement.TryGetProperty("games", out var items) || items.ValueKind != JsonValueKind.Array)
                return PlayniteSetupConnectionKind.Incompatible;
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/eval")
            {
                Content = new StringContent("{\"code\":\"1\",\"timeoutMs\":1000,\"onUiThread\":false}", Encoding.UTF8, "application/json"),
            };
            using var eval = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!eval.IsSuccessStatusCode) return PlayniteSetupConnectionKind.EvalUnavailable;
            using var result = await ReadAsync(eval, timeout.Token).ConfigureAwait(false);
            return result.RootElement.ValueKind == JsonValueKind.Object && result.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True &&
                result.RootElement.TryGetProperty("result", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number == 1
                ? PlayniteSetupConnectionKind.Ready : PlayniteSetupConnectionKind.EvalUnavailable;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return PlayniteSetupConnectionKind.Offline; }
        catch (HttpRequestException) { return PlayniteSetupConnectionKind.Offline; }
        catch (Exception error) when (error is JsonException or IOException or PlayniteSetupException) { return PlayniteSetupConnectionKind.Incompatible; }
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        const int maximum = 256 * 1024;
        if (response.Content.Headers.ContentLength is > maximum) throw new IOException("Oversized setup response.");
        using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > maximum) throw new IOException("Oversized setup response.");
            output.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(output.ToArray(), new() { MaxDepth = 16 });
    }
}
