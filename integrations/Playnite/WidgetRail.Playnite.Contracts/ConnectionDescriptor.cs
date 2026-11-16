using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace WidgetRail.Playnite;

[DataContract]
public sealed class ConnectionDescriptor
{
    [DataMember(Name = "schemaVersion", Order = 0)] public int SchemaVersion { get; set; } = 1;
    [DataMember(Name = "pluginId", Order = 1)] public string PluginId { get; set; } = IntegrationIdentity.PluginId;
    [DataMember(Name = "pluginVersion", Order = 2)] public string PluginVersion { get; set; } = IntegrationIdentity.Version;
    [DataMember(Name = "protocolVersion", Order = 3)] public int ProtocolVersion { get; set; } = IntegrationIdentity.ProtocolVersion;
    [DataMember(Name = "profileId", Order = 4)] public string ProfileId { get; set; } = "";
    [DataMember(Name = "instanceId", Order = 5)] public string InstanceId { get; set; } = Guid.NewGuid().ToString("D");
    [DataMember(Name = "processId", Order = 6)] public int ProcessId { get; set; }
    [DataMember(Name = "processStartedUtcTicks", Order = 7)] public long ProcessStartedUtcTicks { get; set; }
    [DataMember(Name = "port", Order = 8)] public int Port { get; set; }

    public bool IsValid() => SchemaVersion == 1 && PluginId == IntegrationIdentity.PluginId &&
        ProtocolVersion == IntegrationIdentity.ProtocolVersion && PluginVersion != null && PluginVersion.Length <= 32 &&
        System.Version.TryParse(PluginVersion, out _) && Guid.TryParseExact(ProfileId, "D", out _) &&
        Guid.TryParseExact(InstanceId, "D", out _) && ProcessId > 0 && ProcessStartedUtcTicks > 0 &&
        Port >= 1024 && Port <= 65535 && Port != 19821;

    public static ConnectionDescriptor Create(string profileId, int port)
    {
        using (var process = Process.GetCurrentProcess())
            return new ConnectionDescriptor { ProfileId = profileId, Port = port, ProcessId = process.Id,
                ProcessStartedUtcTicks = process.StartTime.ToUniversalTime().Ticks };
    }
}

[DataContract]
public sealed class IntegrationCredentials
{
    [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [DataMember(Name = "profileId")] public string ProfileId { get; set; } = Guid.NewGuid().ToString("D");
    [DataMember(Name = "token")] public string Token { get; set; } = "";
    public bool IsValid() => SchemaVersion == 1 && Guid.TryParseExact(ProfileId, "D", out _) && ValidToken(Token);
    public static bool ValidToken(string value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (var c in value) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
        return true;
    }
    public static IntegrationCredentials Create()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return new IntegrationCredentials { Token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant() };
    }
}

