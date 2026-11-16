using System.Text.Json;
namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteCaptureSettings(string Availability,string? Revision,bool Editing,
    IReadOnlyDictionary<string,JsonElement> Values,IReadOnlyDictionary<string,string[]> Options,string? ConnectionId)
{
    internal static PlayniteCaptureSettings Unavailable { get; } = new("unsupported",null,false,new Dictionary<string,JsonElement>(),new Dictionary<string,string[]>(),null);
}
internal sealed record PlayniteCaptureIntegration(string ProfileId,IReadOnlyList<string> Roots,IReadOnlyList<string> ProviderRoots,string ConnectionId);
internal sealed class PlayniteCaptureSettingsRejectedException(string code) : Exception(code)
{ internal string Code { get; } = code; }

internal sealed partial class PlayniteBridgeClient
{
    public async ValueTask<PlayniteCaptureSettings> GetCaptureSettingsAsync(CancellationToken token)
    {
        if(!UsesLocalConnection)return PlayniteCaptureSettings.Unavailable;
        var response=await SendRequiredAsync(new(PlayniteBridgeCommandKind.CaptureSettings),token).ConfigureAwait(false);
        if(response.StatusCode==404)return PlayniteCaptureSettings.Unavailable;
        EnsureSuccess(response);return ParseCaptureSettings(response);
    }
    public async ValueTask<PlayniteCaptureSettings> UpdateCaptureSettingsAsync(PlayniteCaptureSettings current,IReadOnlyDictionary<string,JsonElement> patch,CancellationToken token)
    {
        if(current.ConnectionId==null||current.Revision==null||patch.Count is 0 or >24)throw new ArgumentException("Capture settings are not current.");
        var response=await SendRequiredAsync(new(PlayniteBridgeCommandKind.UpdateCaptureSettings,JsonBody:JsonSerializer.Serialize(new{expectedRevision=current.Revision,settings=patch}))
            {ExpectedConnectionId=current.ConnectionId,CommandId=Guid.NewGuid().ToString("D")},token).ConfigureAwait(false);
        if(response.ConnectionId!=current.ConnectionId)throw new PlayniteBridgeDataException("capture_connection_changed");
        if(response.StatusCode is >=400 and <500)
        {
            using var error=JsonDocument.Parse(response.Body);
            throw new PlayniteCaptureSettingsRejectedException(error.RootElement.TryGetProperty("error",out var detail)?OptionalString(detail,"code",64)??"capture_settings_rejected":"capture_settings_rejected");
        }
        EnsureSuccess(response);
        return ParseCaptureSettings(response);
    }
    public async ValueTask<PlayniteCaptureSettings> GetCaptureSettingsForConnectionAsync(string connectionId,CancellationToken token)
    {
        var response=await SendRequiredAsync(new(PlayniteBridgeCommandKind.CaptureSettings){ExpectedConnectionId=connectionId},token).ConfigureAwait(false);
        if(response.ConnectionId!=connectionId)throw new PlayniteBridgeDataException("capture_connection_changed");
        EnsureSuccess(response);return ParseCaptureSettings(response);
    }
    public async ValueTask<PlayniteCaptureIntegration?> GetCaptureIntegrationAsync(CancellationToken token)
    {
        if(!UsesLocalConnection)return null;
        var response=await SendRequiredAsync(new(PlayniteBridgeCommandKind.CaptureIntegration),token).ConfigureAwait(false);
        if(response.StatusCode==404)return null;
        EnsureSuccess(response);return ParseCaptureIntegration(response);
    }
    public async ValueTask<PlayniteCaptureIntegration> ConfigureCaptureIntegrationAsync(PlayniteCaptureIntegration current,string path,bool enabled,CancellationToken token)
    {
        var response=await SendRequiredAsync(new(PlayniteBridgeCommandKind.ConfigureCaptureIntegration,JsonBody:JsonSerializer.Serialize(new{path,enabled}))
            {ExpectedConnectionId=current.ConnectionId,CommandId=Guid.NewGuid().ToString("D")},token).ConfigureAwait(false);
        EnsureSuccess(response);if(response.ConnectionId!=current.ConnectionId)throw new PlayniteBridgeDataException("capture_connection_changed");
        return ParseCaptureIntegration(response);
    }
    private static PlayniteCaptureSettings ParseCaptureSettings(PlayniteBridgeResponse response)
    {
        using var json=JsonDocument.Parse(response.Body,new(){MaxDepth=8});var root=json.RootElement;
        var availability=OptionalString(root,"availability",32);
        if(availability is "missing" or "unsupported")return PlayniteCaptureSettings.Unavailable with{Availability=availability,ConnectionId=response.ConnectionId};
        if(availability!="ready"||OptionalString(root,"revision",64) is not {Length:64} revision||!TryRequiredBoolean(root,"editing",out var editing)||
            !root.TryGetProperty("settings",out var settings)||settings.ValueKind!=JsonValueKind.Object||settings.EnumerateObject().Count()>24||
            !root.TryGetProperty("options",out var options)||options.ValueKind!=JsonValueKind.Object||options.EnumerateObject().Count()>8)
            throw new PlayniteBridgeDataException("invalid_capture_settings");
        var values=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var value in settings.EnumerateObject())
        {
            if(value.Name.Length>48||value.Value.ValueKind is not(JsonValueKind.True or JsonValueKind.False or JsonValueKind.String or JsonValueKind.Number)||
                value.Value.ValueKind==JsonValueKind.String&&value.Value.GetString()!.Length>1024)throw new PlayniteBridgeDataException("invalid_capture_settings");
            values.Add(value.Name,value.Value.Clone());
        }
        var choices=new Dictionary<string,string[]>(StringComparer.Ordinal);
        foreach(var value in options.EnumerateObject())
        {
            if(value.Name.Length>48||value.Value.ValueKind!=JsonValueKind.Array||value.Value.GetArrayLength()>32||
                value.Value.EnumerateArray().Any(item=>item.ValueKind!=JsonValueKind.String||item.GetString()!.Length>64))throw new PlayniteBridgeDataException("invalid_capture_settings");
            choices.Add(value.Name,value.Value.EnumerateArray().Select(item=>item.GetString()!).ToArray());
        }
        return new(availability,revision,editing,values,choices,response.ConnectionId);
    }
    private static PlayniteCaptureIntegration ParseCaptureIntegration(PlayniteBridgeResponse response)
    {
        using var json=JsonDocument.Parse(response.Body,new(){MaxDepth=4});var root=json.RootElement;
        if(response.ConnectionId==null||OptionalString(root,"profileId",36) is not { } profile||!Guid.TryParseExact(profile,"D",out _))throw new PlayniteBridgeDataException("invalid_capture_integration");
        string[] Paths(string key,int maximum)
        {
            if(!root.TryGetProperty(key,out var list)||list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()>maximum||list.EnumerateArray().Any(item=>item.ValueKind!=JsonValueKind.String||item.GetString()!.Length>1024))throw new PlayniteBridgeDataException("invalid_capture_integration");
            return list.EnumerateArray().Select(item=>item.GetString()!).ToArray();
        }
        return new(profile,Paths("roots",8),Paths("providerRoots",10000),response.ConnectionId);
    }
}
