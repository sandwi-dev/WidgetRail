using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SemanticStyleClassOwnershipTests
{
    private const string Generation = "0123456789abcdef0123456789abcdef";

    internal static Task Run()
    {
        ClassesReplaceOnlyAuthorClasses();
        CompositeRootsAndPartsRemainIntrinsic();
        SerializationWrappersPreserveOwnership();
        PresentationUpdatesCarryTheDeterministicUnion();
        IntrinsicNonReservedClassesAreIdempotent();
        RequiredClassesCannotBeMutatedThroughThePublicView();
        AuthoredWrailClassesRemainValidAndDeduplicate();
        return Task.CompletedTask;
    }

    private static void ClassesReplaceOnlyAuthorClasses()
    {
        var card = UI.Card("semantic.card", CardVariant.Subtle,
                UI.Text("Card", "semantic.card.text"))
            .Classes("package-card", "compact-card");
        Sequence(
            ["wrail-card", "wrail-card--subtle", "package-card", "compact-card"],
            card.StyleClasses);

        var replaced = card.Classes("replacement-card");
        Sequence(
            ["wrail-card", "wrail-card--subtle", "replacement-card"],
            replaced.StyleClasses);
        var appended = replaced.AddClasses(
            "replacement-card", "wrail-card", "emphasized-card");
        Sequence(
            ["wrail-card", "wrail-card--subtle", "replacement-card", "emphasized-card"],
            appended.StyleClasses);

        var primitive = UI.Stack("semantic.primitive")
            .Classes("first", "second")
            .Classes("replacement");
        Sequence(["replacement"], primitive.StyleClasses);
    }

    private static void CompositeRootsAndPartsRemainIntrinsic()
    {
        var poster = UI.PosterTile(
                "Poster", "Ready", "poster.open", "semantic.poster",
                subtitle: "Provider", metadata: "Metadata")
            .Classes("package-poster");
        var posterSnapshot = Snapshot(poster, "semantic.poster.instance", 1);
        Sequence(
            ["wrail-action-surface", "wrail-poster-tile", "package-poster"],
            posterSnapshot.Root.StyleClasses);
        Sequence(["wrail-poster-tile__scrim"],
            Find(posterSnapshot.Root, "semantic.poster.scrim").StyleClasses);
        Sequence(["wrail-poster-tile__content"],
            Find(posterSnapshot.Root, "semantic.poster.content").StyleClasses);
        Sequence(["wrail-poster-tile__title"],
            Find(posterSnapshot.Root, "semantic.poster.title").StyleClasses);

        var tile = UI.Tile("Tile", "Ready", "tile.open", "semantic.tile")
            .Classes("package-tile");
        var tileSnapshot = Snapshot(tile, "semantic.tile.instance", 1);
        Sequence(["wrail-action-surface", "wrail-tile", "package-tile"],
            tileSnapshot.Root.StyleClasses);
        Sequence(["wrail-tile__content"],
            Find(tileSnapshot.Root, "semantic.tile.content").StyleClasses);

        var scrubber = UI.Scrubber(
                TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2),
                TimeSpan.FromSeconds(5), "seek", "semantic.scrubber", "Timeline")
            .Classes("package-scrubber");
        var scrubberSnapshot = Snapshot(scrubber, "semantic.scrubber.instance", 1);
        Sequence(["wrail-scrubber", "package-scrubber"], scrubberSnapshot.Root.StyleClasses);
        Sequence(["wrail-scrubber__slider"],
            Find(scrubberSnapshot.Root, "semantic.scrubber.slider").StyleClasses);

        var background = UI.BackgroundSurface(
                UI.Text("Foreground", "semantic.background.text"),
                "semantic.background")
            .Classes("package-background");
        Sequence(["wrail-background-surface", "package-background"],
            Snapshot(background, "semantic.background.instance", 1).Root.StyleClasses);

        var toggle = UI.Switch("Enabled", true, "toggle", "semantic.switch")
            .Classes("package-switch");
        Sequence(["wrail-switch", "wrail-switch--on", "package-switch"],
            Snapshot(toggle, "semantic.switch.instance", 1).Root.StyleClasses);

        var stepper = UI.Stepper(
                "Scale", "100%", "scale.down", "scale.up", "semantic.stepper")
            .Classes("package-stepper");
        var stepperSnapshot = Snapshot(stepper, "semantic.stepper.instance", 1);
        Sequence(["wrail-stepper", "package-stepper"], stepperSnapshot.Root.StyleClasses);
        Sequence(["wrail-stepper__button", "wrail-stepper__button--decrement"],
            Find(stepperSnapshot.Root, "semantic.stepper.decrement").StyleClasses);
    }

    private static void SerializationWrappersPreserveOwnership()
    {
        WidgetElement wrapped = UI.PosterTile(
                "Wrapped", "Ready", "wrapped.open", "semantic.wrapped")
            .Classes("first-author")
            .CollectionItem(new WidgetCollectionItemKey("wrapped-key"))
            .FocusBackground(new WidgetArtworkHandle("wrapped-artwork"))
            .PresentOnFocus(UI.Text("Wrapped details", "semantic.wrapped.details"))
            .VisibleWhen(ResponsiveVisibility.ExpandedOnly)
            .Classes("replacement-author");
        var node = wrapped.ToProtocolNode();
        Sequence(
            ["wrail-action-surface", "wrail-poster-tile", "replacement-author"],
            node.StyleClasses);
        Equal("wrapped-key", node.CollectionItemKey);
        Equal("wrapped-artwork", node.FocusBackgroundArtworkHandle);
        Equal(ResponsiveVisibility.ExpandedOnly, node.VisibleWhen);
        Equal("semantic.wrapped.details", node.FocusPresentation?.Id);
    }

    private static void PresentationUpdatesCarryTheDeterministicUnion()
    {
        var previous = Snapshot(
            UI.Card("semantic.update", UI.Text("Value", "semantic.update.text"))
                .Classes("old-author"),
            "semantic.update.instance", 1) with
        {
            ProtocolVersion = ProtocolConstants.AtomicPresentationUpdateVersion,
        };
        var current = Snapshot(
            UI.Card("semantic.update", UI.Text("Value", "semantic.update.text"))
                .Classes("new-author"),
            "semantic.update.instance", 2) with
        {
            ProtocolVersion = ProtocolConstants.AtomicPresentationUpdateVersion,
        };
        var publication = WidgetPresentationDiff.Create(
            previous, current, Generation, 1,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        True(publication.IsUpdate, "Style union change did not remain an incremental update.");
        var admitted = PresentationUpdateMaterializer.Apply(
            previous, publication.Update!, Generation);
        Sequence(["wrail-card", "wrail-card--raised", "new-author"],
            admitted.Root.StyleClasses);
    }

    private static void IntrinsicNonReservedClassesAreIdempotent()
    {
        var expected = new[]
        {
            "loading-indicator",
            "loading-indicator-compact",
            "package-loading",
        };
        var indicator = UI.LoadingIndicator(
            "semantic.loading", "Loading", LoadingIndicatorSize.Compact);

        Sequence(expected, indicator.Classes(
            "loading-indicator", "loading-indicator-compact", "package-loading")
            .StyleClasses);
        Sequence(expected, indicator.Classes("package-loading").AddClasses(
            "loading-indicator", "loading-indicator-compact", "package-loading")
            .StyleClasses);
        Sequence(expected, (indicator with
        {
            StyleClasses =
            [
                "loading-indicator",
                "loading-indicator-compact",
                "package-loading",
            ],
        }).StyleClasses);

        var repeatedDirectAuthor = indicator with
        {
            StyleClasses = ["package-loading", "package-loading"],
        };
        Sequence(expected, repeatedDirectAuthor.StyleClasses);

        var resized = indicator.Classes("package-loading") with
        {
            Size = LoadingIndicatorSize.Large,
        };
        Sequence(
            ["loading-indicator", "loading-indicator-large", "package-loading"],
            resized.StyleClasses);
        var wrapped = resized
            .CollectionItem(new WidgetCollectionItemKey("loading-key"))
            .VisibleWhen(ResponsiveVisibility.ExpandedOnly)
            .ToProtocolNode();
        Sequence(
            ["loading-indicator", "loading-indicator-large", "package-loading"],
            wrapped.StyleClasses);
        Equal(LoadingIndicatorSize.Large, wrapped.IndicatorSize);
        Throws<ArgumentOutOfRangeException>(() => _ = indicator with
        {
            Size = (LoadingIndicatorSize)999,
        });
    }

    private static void RequiredClassesCannotBeMutatedThroughThePublicView()
    {
        var card = UI.Card(
            "semantic.immutable", UI.Text("Value", "semantic.immutable.text"));
        var exposed = card.StyleClasses;
        True(exposed is not string[],
            "Required style classes leaked through a mutable array.");
        True(exposed is IList<string>,
            "The required-class immutability test could not exercise list mutation.");
        var mutableView = (IList<string>)exposed;
        Throws<NotSupportedException>(() => mutableView[0] = "replacement");
        Throws<NotSupportedException>(() => mutableView.Remove("wrail-card"));
        Sequence(["wrail-card", "wrail-card--raised"], card.StyleClasses);

        var combined = card.Classes("package-card").StyleClasses;
        True(combined is not string[],
            "The required and author class union leaked through a mutable array.");
        Throws<NotSupportedException>(() => ((IList<string>)combined)[0] = "replacement");
        Sequence(
            ["wrail-card", "wrail-card--raised", "package-card"],
            card.Classes("package-card").StyleClasses);
    }

    private static void AuthoredWrailClassesRemainValidAndDeduplicate()
    {
        var primitive = UI.Stack("semantic.primitive-wrail")
            .Classes(
                "wrail-controller-hint__key",
                "package-key",
                "wrail-controller-hint__key")
            .AddClasses("package-key", "wrail-package-variant", "wrail-package-variant");
        Sequence(
            ["wrail-controller-hint__key", "package-key", "wrail-package-variant"],
            primitive.StyleClasses);

        var card = UI.Card("semantic.required-wrail")
            .Classes("wrail-card", "wrail-package-card", "wrail-package-card");
        Sequence(
            ["wrail-card", "wrail-card--raised", "wrail-package-card"],
            card.StyleClasses);

        var direct = UI.Stack("semantic.direct") with
        {
            StyleClasses =
            [
                "wrail-action-surface",
                "direct-author",
                "wrail-action-surface",
                "direct-author",
            ],
        };
        Sequence(["wrail-action-surface", "direct-author"], direct.StyleClasses);

        var maximumAuthors = Enumerable.Range(
                0, ProtocolConstants.MaximumStyleClassCount - 2)
            .Select(index => $"author-{index}")
            .ToArray();
        Equal(ProtocolConstants.MaximumStyleClassCount,
            UI.Card("semantic.maximum").Classes(maximumAuthors).StyleClasses.Count);
        Throws<ArgumentException>(() => UI.Card("semantic.overflow")
            .Classes([.. maximumAuthors, "overflow"]));
    }

    private static ViewSnapshot Snapshot(WidgetElement root, string instance, long sequence) =>
        new WidgetView(root).CreateSnapshot(instance, sequence);

    private static ViewNode Find(ViewNode root, string id) =>
        Flatten(root).Single(node => node.Id == id);

    private static IEnumerable<ViewNode> Flatten(ViewNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Flatten(child))
            yield return nested;
    }

    private static void Sequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
