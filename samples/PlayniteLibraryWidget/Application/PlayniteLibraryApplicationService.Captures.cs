using System.Text.Json;
namespace WidgetRail.Samples.PlayniteLibrary;
internal sealed partial class PlayniteLibraryApplicationService
{
    public ValueTask<PlayniteCaptureSettings> GetCaptureSettingsAsync(CancellationToken token) => _client.GetCaptureSettingsAsync(token);
    public ValueTask<PlayniteCaptureSettings> GetCaptureSettingsForConnectionAsync(string connectionId,CancellationToken token) => _client.GetCaptureSettingsForConnectionAsync(connectionId,token);
    public ValueTask<PlayniteCaptureSettings> UpdateCaptureSettingsAsync(PlayniteCaptureSettings current, IReadOnlyDictionary<string,JsonElement> patch,CancellationToken token) => _client.UpdateCaptureSettingsAsync(current,patch,token);
    public ValueTask<PlayniteCaptureIntegration?> GetCaptureIntegrationAsync(CancellationToken token, string? gameId = null) => _client.GetCaptureIntegrationAsync(token, gameId);
    public ValueTask<PlayniteCaptureIntegration> ConfigureCaptureIntegrationAsync(PlayniteCaptureIntegration current,string path,bool enabled,CancellationToken token) => _client.ConfigureCaptureIntegrationAsync(current,path,enabled,token);
}
