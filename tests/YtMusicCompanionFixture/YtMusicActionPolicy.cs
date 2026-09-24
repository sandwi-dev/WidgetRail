namespace WidgetRail.Samples.YtMusicWidget;

internal enum YtMusicActionKind
{
    None,
    Connect,
    Pair,
    Refresh,
    Command,
}

internal readonly record struct YtMusicActionRoute(
    YtMusicActionKind Kind,
    YtMusicCommand? Command = null,
    string Status = "");

internal static class YtMusicActionPolicy
{
    internal const string ConnectId = "connect";
    internal const string PairId = "pair";
    internal const string RefreshId = "refresh";

    internal static YtMusicActionRoute Resolve(string actionId) => actionId switch
    {
        ConnectId => new(YtMusicActionKind.Connect),
        PairId => new(YtMusicActionKind.Pair),
        RefreshId => new(YtMusicActionKind.Refresh, Status: "Refreshing now playing…"),
        "toggle-playback" => Command(YtMusicCommand.TogglePlayback, "Toggling playback…"),
        "previous" => Command(YtMusicCommand.Previous, "Previous track…"),
        "next" => Command(YtMusicCommand.Next, "Next track…"),
        "like" => Command(YtMusicCommand.Like, "Updating like…"),
        "dislike" => Command(YtMusicCommand.Dislike, "Updating dislike…"),
        "shuffle" => Command(YtMusicCommand.Shuffle, "Toggling shuffle…"),
        "repeat" => Command(YtMusicCommand.Repeat, "Changing repeat mode…"),
        _ => new(YtMusicActionKind.None),
    };

    private static YtMusicActionRoute Command(
        YtMusicCommand command,
        string status) => new(YtMusicActionKind.Command, command, status);
}
