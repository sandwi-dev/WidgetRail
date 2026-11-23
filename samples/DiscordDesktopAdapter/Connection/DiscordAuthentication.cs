using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetRail.DiscordDesktop.Connection;

internal static class DiscordAuthentication
{
    internal const int Version = 1;
    internal static bool IsLowerHex(string? value, int length) => value is not null && value.Length == length &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static string RandomHex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    internal static byte[] Proof(ReadOnlySpan<byte> secret, string role, string clientNonce, string serverNonce,
        string connectionId, string clientInstance)
    {
        if (secret.Length != 32 || role is not ("server" or "client") || !IsLowerHex(clientNonce, 64) ||
            !IsLowerHex(serverNonce, 64) || !IsLowerHex(connectionId, 32) || !IsLowerHex(clientInstance, 32))
            throw new InvalidDataException("Invalid Discord authentication transcript.");
        return HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(
            $"WidgetRail.DiscordDesktop.v1\n{role}\n{clientNonce}\n{serverNonce}\n{connectionId}\n{clientInstance}"));
    }

    internal static void Shape(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Unexpected Discord handshake object.");
        var fields = value.EnumerateObject().ToArray();
        if (fields.Length != names.Length || fields.Any(property => !names.Contains(property.Name, StringComparer.Ordinal)) ||
            fields.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidDataException("Unexpected Discord handshake fields.");
    }
    internal static string Text(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String || property.GetString() is not { } text)
            throw new InvalidDataException("Unexpected Discord handshake field type.");
        return text;
    }
    internal static (string Nonce, string Instance) ClientHello(JsonElement value)
    {
        Shape(value, "type", "version", "clientNonce", "clientInstance");
        if (Text(value, "type") != "clientHello" || value.GetProperty("version").ValueKind != JsonValueKind.Number ||
            !value.GetProperty("version").TryGetInt32(out var version) || version != Version)
            throw new InvalidDataException("Unsupported Discord client hello.");
        var nonce = Text(value, "clientNonce"); var instance = Text(value, "clientInstance");
        if (!IsLowerHex(nonce, 64) || !IsLowerHex(instance, 32)) throw new InvalidDataException("Invalid Discord client identity.");
        return (nonce, instance);
    }
    internal static byte[] ClientProof(JsonElement value)
    {
        Shape(value, "type", "proof");
        var proof = Text(value, "proof");
        if (Text(value, "type") != "clientProof" || !IsLowerHex(proof, 64)) throw new InvalidDataException("Invalid Discord client proof.");
        return Convert.FromHexString(proof);
    }
}
