using GameBarAlternative.WidgetSdk;

internal static class WidgetIdsTests
{
    internal static Task Run()
    {
        HierarchiesAreExplicitAndValidated();
        DurableKeysBecomeBoundedOpaqueIds();
        InvalidSegmentsFailAtTheAuthoringBoundary();
        return Task.CompletedTask;
    }

    private static void HierarchiesAreExplicitAndValidated()
    {
        var player = WidgetIds.Scope("spotify").Scope("wide").Scope("player");

        Equal("spotify.wide.player", player.Prefix);
        Equal("spotify.wide.player.play-toggle", player.Id("play-toggle"));
        Equal(player.Prefix, player.ToString());
    }

    private static void DurableKeysBecomeBoundedOpaqueIds()
    {
        var items = WidgetIds.Scope("spotify").Scope("playlist");
        const string providerKey = "spotify:track:secret/provider value";

        var first = items.KeyedId("track", providerKey);
        var repeated = items.KeyedId("track", providerKey);
        var different = items.KeyedId("track", providerKey + "-other");

        Equal(first, repeated);
        False(first == different, "Different durable keys produced the same test ID.");
        False(first.Contains(providerKey, StringComparison.Ordinal),
            "A keyed ID exposed the provider's durable identity.");
        True(first.StartsWith("spotify.playlist.track-", StringComparison.Ordinal),
            "The keyed ID lost its readable semantic prefix.");
        True(first.Length <= 128, "The keyed ID exceeded the wire-contract bound.");
    }

    private static void InvalidSegmentsFailAtTheAuthoringBoundary()
    {
        AssertThrows<ArgumentException>(() => WidgetIds.Scope("root").Scope("nested.scope"));
        AssertThrows<ArgumentException>(() => WidgetIds.Scope("root").Id("bad value"));
        AssertThrows<ArgumentException>(() => WidgetIds.Scope("root").KeyedId("item", ""));
        AssertThrows<ArgumentException>(() => WidgetIds.Scope(new string('a', 120)).Id("overflow"));
        AssertThrows<ArgumentException>(() =>
            WidgetIds.Scope("root").KeyedId("item", new string('k', 4097)));
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Equal<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void False(bool condition, string message) => True(!condition, message);
}
