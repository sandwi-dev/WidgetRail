using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using WidgetRail.Playnite;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteSetupConnectionKind { Ready, Offline, Authentication, WrongProfile, Incompatible }
internal interface IPlayniteSetupConnection
{
    ValueTask<PlayniteSetupConnectionKind> CheckAsync(PlayniteSetupInstallation installation, string credential, CancellationToken token, bool verifyLibrary = true);
}
internal sealed class PlayniteSetupConnection : IPlayniteSetupConnection
{
    public async ValueTask<PlayniteSetupConnectionKind> CheckAsync(PlayniteSetupInstallation installation, string credential, CancellationToken token, bool verifyLibrary = true)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var descriptor = PlayniteLocalConnectionStore.ReadDescriptor(installation.DataDirectory);
            if (descriptor is null || !PlayniteLocalConnectionStore.MatchesRunningProcess(descriptor, installation.ProgramDirectory))
                return PlayniteSetupConnectionKind.Offline;
            using var http = new HttpClient(PlayniteBridgeHttpTransport.CreateHandler())
                { BaseAddress = new Uri($"http://127.0.0.1:{descriptor.Port}/"), Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            http.DefaultRequestHeaders.Add("X-WidgetRail-Instance", descriptor.InstanceId);
            using var response = await http.GetAsync("v1/connection", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return PlayniteSetupConnectionKind.Authentication;
            if (!response.IsSuccessStatusCode) return PlayniteSetupConnectionKind.Incompatible;
            using var info = await ReadAsync(response, timeout.Token).ConfigureAwait(false);
            var root = info.RootElement;
            bool Same(string name, string expected) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;
            if (root.ValueKind != JsonValueKind.Object || !Same("pluginId", IntegrationIdentity.PluginId) ||
                !Same("profileId", descriptor.ProfileId) || !Same("instanceId", descriptor.InstanceId) ||
                !root.TryGetProperty("protocolVersion", out var protocol) || protocol.ValueKind != JsonValueKind.Number ||
                !protocol.TryGetInt32(out var version) || version != IntegrationIdentity.ProtocolVersion ||
                !root.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Array ||
                IntegrationIdentity.RequiredCapabilities.Any(required => !capabilities.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == required)))
                return PlayniteSetupConnectionKind.Incompatible;
            if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object ||
                !paths.TryGetProperty("application", out var application) || application.ValueKind != JsonValueKind.String ||
                !paths.TryGetProperty("configuration", out var configuration) || configuration.ValueKind != JsonValueKind.String)
                return PlayniteSetupConnectionKind.Incompatible;
            if (!string.Equals(PlayniteSetupFiles.LocalPath(application.GetString()!), installation.ProgramDirectory, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(PlayniteSetupFiles.LocalPath(configuration.GetString()!), installation.DataDirectory, StringComparison.OrdinalIgnoreCase))
                return PlayniteSetupConnectionKind.WrongProfile;
            if (verifyLibrary)
            {
                using var games = await http.GetAsync("v1/games?offset=0&limit=1", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (!games.IsSuccessStatusCode) return PlayniteSetupConnectionKind.Incompatible;
                using var page = await ReadAsync(games, timeout.Token).ConfigureAwait(false);
                if (page.RootElement.ValueKind != JsonValueKind.Object || !page.RootElement.TryGetProperty("games", out var items) || items.ValueKind != JsonValueKind.Array)
                    return PlayniteSetupConnectionKind.Incompatible;
            }
            return PlayniteLocalConnectionStore.ReadDescriptor(installation.DataDirectory)?.InstanceId == descriptor.InstanceId
                ? PlayniteSetupConnectionKind.Ready : PlayniteSetupConnectionKind.Offline;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return PlayniteSetupConnectionKind.Offline; }
        catch (HttpRequestException) { return PlayniteSetupConnectionKind.Offline; }
        catch (Exception error) when (error is JsonException or IOException or PlayniteSetupException or ArgumentException or PlayniteBridgeTransportException)
        { return PlayniteSetupConnectionKind.Incompatible; }
    }
    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken token) =>
        JsonDocument.Parse(await PlayniteBridgeHttpTransport.ReadBoundedAsync(response.Content, PlayniteBridgeHttpTransport.MaximumJsonBytes, token).ConfigureAwait(false),
            new() { MaxDepth = 16 });
}