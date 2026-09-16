using System.Xml;
using System.Xml.Linq;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsNetworkProvider;

internal readonly record struct WifiProfileOptions(bool? AutoConnect, bool CanManage);

/// <summary>Edits only connectionMode; preserves Windows-owned authentication and profile scope.</summary>
internal static class WindowsWifiProfilePolicy
{
    private const uint WriteAccess = 0x00070023;
    private static readonly XNamespace Wlan = "http://www.microsoft.com/networking/WLAN/profile/v1";

    internal static WifiProfileOptions Read(IWindowsNetworkNativeCalls calls, IntPtr handle,
        Guid adapter, string name)
    {
        try
        {
            if (calls.ReadWlanProfile(handle, adapter, name, out var xml, out var flags, out var access) != 0)
                return default;
            var document = Parse(xml, name);
            var mode = document.Root!.Element(Wlan + "connectionMode")?.Value;
            return new(mode == "auto" ? true : mode == "manual" ? false : null,
                (flags & 1) == 0 && (access & WriteAccess) == WriteAccess);
        }
        catch { return default; } // One unreadable profile must not break the list.
    }

    internal static void Change(IWindowsNetworkNativeCalls calls, IntPtr handle,
        Guid adapter, string name, bool? autoConnect)
    {
        DemandSuccess(calls.ReadWlanProfile(handle, adapter, name, out var xml, out var flags, out var access));
        if ((flags & 1) != 0 || (access & WriteAccess) != WriteAccess)
            throw new BrokerException("wifi_profile_policy_denied", "Windows policy prevents changes to this saved network.");
        var document = Parse(xml, name);
        if (autoConnect is null)
        {
            DemandSuccess(calls.DeleteWlanProfile(handle, adapter, name));
            return;
        }
        var mode = document.Root!.Element(Wlan + "connectionMode");
        if (mode is null || mode.Value is not ("auto" or "manual"))
            throw new BrokerException("unsupported_operation", "Windows cannot change automatic connection for this profile.");
        var value = autoConnect.Value ? "auto" : "manual";
        if (mode.Value == value) return;
        mode.Value = value;
        DemandSuccess(calls.UpdateWlanProfile(handle, adapter,
            document.ToString(SaveOptions.DisableFormatting), flags & 2));
    }

    private static XDocument Parse(string xml, string name)
    {
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != Wlan + "WLANProfile" ||
            document.Root.Element(Wlan + "name")?.Value != name)
            throw new BrokerException("resource_not_found", "The saved network has changed.");
        return document;
    }

    internal static void DemandSuccess(uint result)
    {
        if (result == 0) return;
        throw new BrokerException(result switch
        {
            5 => "wifi_profile_policy_denied",
            1168 or 2 => "resource_not_found",
            _ => "platform_unavailable",
        }, "Windows could not change this Wi-Fi connection or profile.");
    }
}
