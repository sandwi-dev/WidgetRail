using System.Text;
using GameBarAlternative.AvaloniaPrototype.Remote;

namespace GameBarAlternative.AvaloniaPrototype.Presentation;

public static class SemanticAutomationIdentity
{
    public const string LauncherItemPrefix = "launcher.game.id.";
    public const int MaximumAutomationIdLength = 188;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string ForLauncherItem(RemoteWidgetItemId itemId)
    {
        var bytes = StrictUtf8.GetBytes(itemId.Value);
        var encoded = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var automationId = LauncherItemPrefix + encoded;
        if (automationId.Length > MaximumAutomationIdLength)
        {
            throw new InvalidOperationException("Encoded launcher identity exceeded its bounded contract.");
        }

        return automationId;
    }

    public static bool TryDecodeLauncherItem(string automationId, out RemoteWidgetItemId itemId)
    {
        itemId = default;
        if (!automationId.StartsWith(LauncherItemPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var encoded = automationId[LauncherItemPrefix.Length..].Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
        try
        {
            var value = StrictUtf8.GetString(Convert.FromBase64String(encoded));
            itemId = new RemoteWidgetItemId(value);
            return string.Equals(ForLauncherItem(itemId), automationId, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException or ArgumentException)
        {
            return false;
        }
    }
}
