using System.Diagnostics;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class TextSuggestionTests
{
    internal static Task Run()
    {
        var before = new WidgetView(UI.TextEntry("", "Search", "search", "search", 100).Suggest("gems"))
            .CreateSnapshot("suggestion-test", 1);
        Check(before.ProtocolVersion == ProtocolConstants.TextSuggestionsVersion, "Suggestions were not version gated");
        var after = before with { Sequence = 2, Root = before.Root with { TextEntrySuggestions = new("other", 2, 3) } };
        var generation = new string('b', 32);
        var update = WidgetPresentationDiff.Create(before, after, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Check(update is not null, "Suggestion source update was lost");
        var applied = PresentationUpdateMaterializer.Apply(before, PresentationUpdateJson.Deserialize(PresentationUpdateJson.Serialize(update!)), generation);
        Check(SnapshotJson.Serialize(applied).SequenceEqual(SnapshotJson.Serialize(after)), "Source limits lost in incremental transport");
        Check(ViewSnapshotValidator.Validate(before with { Root = before.Root with { TextEntryInputKind = TextEntryInputKind.Sensitive } }).Count > 0, "Sensitive field accepted suggestions");
        Check(ViewSnapshotValidator.Validate(before with { Root = before.Root with { TextEntrySuggestions = new("gems", 1, 1000) } }).Count > 0, "Unbounded result count accepted");
        var query = new TextSuggestionQuery("search", "gems", Guid.NewGuid().ToString("N"), 1, "fire", 6, 100);
        Check(TextSuggestionContract.Matches(before.Root, query), "Matching query was rejected");
        Check(!TextSuggestionContract.Matches(before.Root, query with { SourceKey = "old" }), "Old source accepted");
        Check(!TextSuggestionContract.Matches(before.Root, query with { MaximumValueLength = 512 }), "Caller expanded field length");
        Check(!TextSuggestionContract.IsValid(query with { Text = "\ud800" }), "Invalid Unicode accepted");
        var options = new TextSuggestionSource("remote", MinimumLength: 2, MaximumResults: 4)
            { DebounceMilliseconds = 350, MinimumRequestIntervalMilliseconds = 750, TimeoutMilliseconds = 5000 };
        var configured = new WidgetView(UI.TextEntry("", "Search", "search", "search", 100).Suggest(options))
            .CreateSnapshot("suggestion-test", 2);
        Check(configured.ProtocolVersion == ProtocolConstants.TextSuggestionTimingVersion, "Custom timing did not raise the required protocol");
        var timedQuery = query with { SourceKey = options.Key, MaximumResults = 4, TimeoutMilliseconds = 5000 };
        Check(TextSuggestionContract.Matches(configured.Root, timedQuery), "Configured deadline did not match the source");
        Check(!TextSuggestionContract.Matches(configured.Root, timedQuery with { TimeoutMilliseconds = 2000 }), "Caller changed the source deadline");
        var roundTrip = SnapshotJson.Deserialize(SnapshotJson.Serialize(configured));
        Check(roundTrip.Root.TextEntrySuggestions == options, "Timing options lost during serialization");
        foreach (var invalid in new[] { options with { DebounceMilliseconds = -1 }, options with { DebounceMilliseconds = 1001 },
            options with { MinimumRequestIntervalMilliseconds = 0 }, options with { MinimumRequestIntervalMilliseconds = 2001 },
            options with { TimeoutMilliseconds = 0 }, options with { TimeoutMilliseconds = 5001 } })
            Check(!TextSuggestionContract.IsValid(invalid) && ViewSnapshotValidator.Validate(configured with
                { Root = configured.Root with { TextEntrySuggestions = invalid } }).Count > 0, "Unsafe timing accepted");
        var timedAfter = configured with { Sequence = 3, Root = configured.Root with { TextEntrySuggestions = options with { TimeoutMilliseconds = 3000 } } };
        var timedUpdate = WidgetPresentationDiff.Create(configured, timedAfter, generation, 2, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update!;
        Check(SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(configured,
            PresentationUpdateJson.Deserialize(PresentationUpdateJson.Serialize(timedUpdate)), generation)).SequenceEqual(SnapshotJson.Serialize(timedAfter)),
            "Timing-only update lost its authority-changing configuration");
        var catalog = new TextSuggestionCatalog([
            new("a", "Vaal Fireball", "Vaal Fireball"), new("b", "Fireball", "Fireball"),
            new("c", "Ice Tipped Spear", "Ice Tipped Spear"), new("d", "Firestorm", "Firestorm")]);
        Check(catalog.Search("fire").Items.Select(item => item.Id).SequenceEqual(new[] { "b", "d", "a" }), "Prefix/substring order changed");
        Check(catalog.Search("ICE TIPPED").Items.Single().Value == "Ice Tipped Spear", "Whole-entry matching lost spaces or case handling");
        Check(catalog.Search(query with { MaximumValueLength = 8 }).Items.Single().Value == "Fireball", "Helper ignored target field length");
        Check(catalog.Search("missing").Items.Count == 0, "No match invented a replacement");
        var gameNames = new TextSuggestionCatalog([new("game", "Elden Ring", "Elden Ring")]);
        var paths = new TextSuggestionCatalog([new("path", "Documents / Builds", "C:\\Documents\\Builds")]);
        Check(gameNames.Search("elden").Items.Count == 1 && paths.Search("builds").Items.Single().Value == "C:\\Documents\\Builds", "Catalog is domain-specific");
        var many = new TextSuggestionCatalog(Enumerable.Range(0, 10000).Select(i => new TextSuggestion("entry" + i, "Entry " + i.ToString("D5"), "Entry " + i)));
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) Check(many.Search("0999").Items.Count <= 6, "Result cap escaped");
        Console.WriteLine($"PERF suggestions: 100 queries over 10,000 entries in {timer.Elapsed.TotalMilliseconds:F1} ms; bounded to 6 results");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { many.Search("none", cancellationToken: cancellation.Token); throw new Exception("Cancelled catalog search ran"); }
        catch (OperationCanceledException) { }
        return Task.CompletedTask;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
