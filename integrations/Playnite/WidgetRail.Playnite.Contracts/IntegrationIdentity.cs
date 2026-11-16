using System;
using System.Collections.Generic;

namespace WidgetRail.Playnite;

public static class IntegrationIdentity
{
    public const string PluginId = "950de770-1c2f-4a48-ac2a-b62d866f7503";
    public const string ExtensionId = "WidgetRail_Playnite_950de770";
    public const string ModuleName = "WidgetRail.Playnite.dll";
    public const string Version = "1.2.0";
    public const int ProtocolVersion = 1;
    public const string DescriptorFile = "connection.json";
    public const string CredentialFile = "auth.json";
    public const int FirstPort = 19822;
    public const int LastPort = 19831;
    public const int MaximumRequestBytes = 1024 * 1024;
    public const int MaximumResponseBytes = 8 * 1024 * 1024;
    public const int MaximumConcurrentRequests = 8;
    public static IReadOnlyList<string> RequiredCapabilities { get; } = Array.AsReadOnly(new[]
        { "connection.v1", "library.v1", "artwork.v1", "artwork.manifest.v1", "mutations.v1" });

    public static IReadOnlyList<int> PortCandidates(int? preferred = null, int? previous = null)
    {
        var ports = new List<int>();
        void Add(int? value)
        {
            if (!value.HasValue) return;
            if (value.Value < 1024 || value.Value > 65535 || value.Value == 19821)
                throw new ArgumentOutOfRangeException(nameof(preferred), "Choose an unprivileged port other than original Bridge's 19821.");
            if (!ports.Contains(value.Value)) ports.Add(value.Value);
        }
        Add(preferred); Add(previous);
        for (var port = FirstPort; port <= LastPort; port++) Add(port);
        return ports;
    }
}
