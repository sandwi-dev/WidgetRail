using WidgetRail.Samples.DiscordWebView.Domain;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private static string ConnectionTitle(DiscordState? data) => data?.EffectiveConnectionState switch
    {
        "signed-out" => "Sign in to Discord",
        "reconnecting" => "Reconnecting to Discord…",
        "unavailable" => "Discord is unavailable",
        _ => "Connecting to Discord…"
    };

    private static string ConnectionHelp(DiscordState? data) => data?.EffectiveConnectionState switch
    {
        "signed-out" => "Open Discord to sign in.",
        "unavailable" => "Open Discord to check the connection, then try Refresh.",
        _ => ""
    };
}
