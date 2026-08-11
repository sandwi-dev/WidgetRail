using System.Text;
using System.Security.Cryptography;

namespace GameBarAlternative.WindowsNetworkProvider;

internal sealed class ProtectedWifiProfile : IDisposable
{
    private char[] _xml;

    private byte[] _ownershipToken;

    private ProtectedWifiProfile(string name, char[] xml, byte[] ownershipToken)
    {
        Name = name;
        _xml = xml;
        _ownershipToken = ownershipToken;
    }

    internal string Name { get; }
    internal char[] Xml => _xml;
    internal byte[] OwnershipToken => _ownershipToken;

    internal byte[] TakeOwnershipToken() =>
        Interlocked.Exchange(ref _ownershipToken, []);

    internal static bool TryCreate(
        byte[] ssid,
        uint authentication,
        uint cipher,
        ReadOnlySpan<char> secret,
        out ProtectedWifiProfile? profile)
    {
        profile = null;
        if (ssid is not { Length: > 0 and <= 32 } ||
            secret.Length is < 8 or > 63 || !IsPrintableAscii(secret))
            return false;

        var authenticationName = authentication switch
        {
            7u => "WPA2PSK",
            9u => "WPA3SAE",
            _ => null,
        };
        if (authenticationName is null || cipher != 4u) return false;

        var ssidName = Encoding.UTF8.GetString(ssid);
        if (string.IsNullOrWhiteSpace(ssidName) || ssidName.Any(char.IsControl)) return false;
        var name = $"GameBarAlternative-{Guid.NewGuid():N}";
        byte[] ownershipToken = [];
        char[] xml = [];
        var offset = 0;
        try
        {
            ownershipToken = RandomNumberGenerator.GetBytes(32);
            xml = new char[2048];
            void Write(ReadOnlySpan<char> value)
            {
                if (offset + value.Length >= xml.Length)
                    throw new InvalidOperationException("Protected Wi-Fi profile exceeded its bound.");
                value.CopyTo(xml.AsSpan(offset));
                offset += value.Length;
            }
            void WriteEscaped(ReadOnlySpan<char> value)
            {
                foreach (var character in value)
                {
                    var escaped = character switch
                    {
                        '&' => "&amp;",
                        '<' => "&lt;",
                        '>' => "&gt;",
                        '"' => "&quot;",
                        '\'' => "&apos;",
                        _ => null,
                    };
                    if (escaped is not null) Write(escaped);
                    else
                    {
                        if (offset + 1 >= xml.Length)
                            throw new InvalidOperationException(
                                "Protected Wi-Fi profile exceeded its bound.");
                        xml[offset++] = character;
                    }
                }
            }

            Write("<?xml version=\"1.0\"?>");
            Write("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">");
            Write("<name>");
            WriteEscaped(name);
            Write("</name><SSIDConfig><SSID><hex>");
            Write(Convert.ToHexString(ssid));
            Write("</hex><name>");
            WriteEscaped(ssidName);
            Write("</name></SSID></SSIDConfig><connectionType>ESS</connectionType>");
            Write("<connectionMode>auto</connectionMode><MSM><security><authEncryption>");
            Write("<authentication>");
            Write(authenticationName);
            Write("</authentication><encryption>AES</encryption><useOneX>false</useOneX>");
            Write("</authEncryption><sharedKey><keyType>passPhrase</keyType>");
            Write("<protected>false</protected><keyMaterial>");
            WriteEscaped(secret);
            Write("</keyMaterial></sharedKey></security></MSM></WLANProfile>");
            profile = new ProtectedWifiProfile(name, xml, ownershipToken);
            ownershipToken = [];
            xml = [];
            return true;
        }
        finally
        {
            Array.Clear(xml);
            CryptographicOperations.ZeroMemory(ownershipToken);
        }
    }

    private static bool IsPrintableAscii(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
            if (character is < (char)32 or > (char)126) return false;
        return true;
    }

    public void Dispose()
    {
        var xml = Interlocked.Exchange(ref _xml, []);
        Array.Clear(xml);
        var ownershipToken = Interlocked.Exchange(ref _ownershipToken, []);
        CryptographicOperations.ZeroMemory(ownershipToken);
    }
}
