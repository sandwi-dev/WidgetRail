using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.Playnite;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLocalBinding(int SchemaVersion, string BindingId, string ProfileId,
    string ProgramDirectory, string DataDirectory);

/// <summary>The selected local profile, separate from both provider credentials and old Bridge's selection.</summary>
internal sealed class PlayniteLocalConnectionStore(string stateRoot) : IPlayniteLocalConnectionSource
{
    internal const string CredentialPrefix = "WidgetRail/PlayniteLibrary/LocalIntegration/v1/";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string path = Path.Combine(stateRoot, "playnite-local-connection.json");
    internal FileStream AcquireSetupLease()
    {
        PlayniteSetupFiles.NoLinks(stateRoot); Directory.CreateDirectory(stateRoot);
        try { return new FileStream(Path.Combine(stateRoot, "playnite-local-connection.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new PlayniteSetupException("Another Playnite connection operation is running. Wait, then retry."); }
    }
    internal PlayniteLocalBinding? Read()
    {
        if (!File.Exists(path)) return null;
        var binding = JsonSerializer.Deserialize<PlayniteLocalBinding>(PlayniteSetupFiles.Read(path, 16384), JsonOptions);
        if (binding is null || binding.SchemaVersion != 1 || !Guid.TryParseExact(binding.BindingId, "D", out _) ||
            !Guid.TryParseExact(binding.ProfileId, "D", out _)) throw new PlayniteSetupException("The saved local connection is invalid. Reconnect Playnite.");
        _ = PlayniteSetupFiles.LocalPath(binding.ProgramDirectory);
        _ = PlayniteSetupFiles.LocalPath(binding.DataDirectory);
        return binding;
    }
    internal void Save(PlayniteLocalBinding binding) => PlayniteSetupFiles.Write(path, JsonSerializer.SerializeToUtf8Bytes(binding, JsonOptions));
    internal void Restore(PlayniteLocalBinding? previous, string expectedBindingId)
    {
        if (Read()?.BindingId != expectedBindingId) return;
        if (previous is null) File.Delete(path); else Save(previous);
    }
    internal static string PluginData(string dataDirectory) => Path.Combine(dataDirectory, "ExtensionsData", IntegrationIdentity.PluginId);
    internal static IntegrationCredentials? ReadCredentials(string dataDirectory)
    {
        var file = Path.Combine(PluginData(dataDirectory), IntegrationIdentity.CredentialFile);
        if (!File.Exists(file)) return null;
        var value = ReadRequired<IntegrationCredentials>(file, ["schemaVersion", "profileId", "token"]);
        return value?.IsValid() == true ? value : throw new PlayniteSetupException("The local integration credential is invalid. Repair the integration.");
    }
    internal static ConnectionDescriptor? ReadDescriptor(string dataDirectory)
    {
        var file = Path.Combine(PluginData(dataDirectory), IntegrationIdentity.DescriptorFile);
        if (!File.Exists(file)) return null;
        var value = ReadRequired<ConnectionDescriptor>(file,
            ["schemaVersion", "pluginId", "pluginVersion", "protocolVersion", "profileId", "instanceId", "processId", "processStartedUtcTicks", "port"]);
        return value?.IsValid() == true ? value : throw new PlayniteSetupException("The local integration endpoint is incompatible or invalid.");
    }
    private static T? ReadRequired<T>(string file, IReadOnlyList<string> required)
    {
        var bytes = PlayniteSetupFiles.Read(file, 16384);
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Object || required.Any(name =>
            !document.RootElement.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null))
            throw new PlayniteSetupException("The local integration metadata is incomplete. Reconnect Playnite.");
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
    }
    internal static bool MatchesRunningProcess(ConnectionDescriptor descriptor, string programDirectory)
    {
        try
        {
            using var process = Process.GetProcessById(descriptor.ProcessId);
            var executable = process.MainModule?.FileName;
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == descriptor.ProcessStartedUtcTicks &&
                executable is not null && Path.GetFileName(executable) is "Playnite.DesktopApp.exe" or "Playnite.FullscreenApp.exe" &&
                string.Equals(Path.GetDirectoryName(executable), programDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return false; }
    }
    internal static string CredentialTarget(PlayniteLocalBinding binding)
    {
        if (!Guid.TryParseExact(binding.ProfileId, "D", out _)) throw new PlayniteSetupException("Invalid Playnite profile identity.");
        // A copied portable profile can retain its UUID. Bind credentials to its
        // selected location too, so rotating one copy cannot overwrite another.
        var location = PlayniteSetupFiles.LocalPath(binding.DataDirectory).ToUpperInvariant();
        return CredentialPrefix + binding.ProfileId + "/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location)));
    }
    internal IPlayniteCredentialStore ForBinding(PlayniteLocalBinding binding) => new WindowsCredentialPlayniteTokenStore(CredentialTarget(binding));
    public async ValueTask<PlayniteLocalConnection?> OpenAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var binding = Read();
        if (binding is null) return null;
        var endpoint = ReadDescriptor(binding.DataDirectory);
        if (endpoint is null || endpoint.ProfileId != binding.ProfileId || !MatchesRunningProcess(endpoint, binding.ProgramDirectory))
            throw new PlayniteBridgeTransportException(isMalformed: false);
        var credential = await ForBinding(binding).ReadAsync(token).ConfigureAwait(false);
        if (credential is null) return null;
        bool Current()
        {
            try { return Read()?.BindingId == binding.BindingId && ReadDescriptor(binding.DataDirectory)?.InstanceId == endpoint.InstanceId; }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or PlayniteSetupException) { return false; }
        }
        if (!Current()) throw new PlayniteBridgeTransportException(isMalformed: false);
        return new(binding.BindingId, endpoint, credential, Current);
    }
}

internal sealed class PlayniteProfileCredentialStore(PlayniteLocalConnectionStore connections) : IPlayniteCredentialStore
{
    internal IPlayniteCredentialStore ForBinding(PlayniteLocalBinding binding) => connections.ForBinding(binding);
    public ValueTask<string?> ReadAsync(CancellationToken token) => connections.Read() is { } binding
        ? ForBinding(binding).ReadAsync(token) : ValueTask.FromResult<string?>(null);
    public async ValueTask SaveAsync(string credential, CancellationToken token)
    {
        using var lease = connections.AcquireSetupLease();
        var binding = connections.Read() ?? throw new PlayniteSetupException("Choose a Playnite profile through setup before updating its credential.");
        await ForBinding(binding).SaveAsync(credential, token).ConfigureAwait(false);
    }
    public async ValueTask DeleteAsync(CancellationToken token)
    {
        using var lease = connections.AcquireSetupLease();
        if (connections.Read() is { } binding) await ForBinding(binding).DeleteAsync(token).ConfigureAwait(false);
    }
}
