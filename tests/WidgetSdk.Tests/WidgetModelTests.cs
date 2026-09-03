using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetModelTests
{
    public static async Task Run()
    {
        PublishesOnlyChangedState();
        await SerializesConcurrentUpdatesAsync();
        DerivesResultsFromTheCommittedRevision();
        CustomComparerDefinesSemanticCollectionEquality();
        FailedUpdaterDoesNotCommitOrPublish();
        await ContainsObserversAndStopsInvalidatingAfterDestroyAsync();
        RejectsInvalidInputs();
    }

    private static void CustomComparerDefinesSemanticCollectionEquality()
    {
        var model = WidgetModel<CollectionState>.CreateForTesting(
            new CollectionState(["one", "two"], "ready"),
            new CollectionStateComparer());

        var equal = model.Set(new(["one", "two"], "ready"));
        False(equal.Changed,
            "The configured sequence comparer did not suppress an equal collection.");
        Equal(0L, equal.Revision);

        var changed = model.Set(new(["one", "three"], "ready"));
        True(changed.Changed, "A distinct collection was not committed.");
        Equal(1L, changed.Revision);
    }

    private static void FailedUpdaterDoesNotCommitOrPublish()
    {
        var model = WidgetModel<State>.CreateForTesting(new State(7, "ready"));
        var changes = 0;
        model.Changed += (_, _) => changes++;

        Throws<InvalidOperationException>(() => model.Update(_ =>
            throw new InvalidOperationException("fixture")));

        Equal(new State(7, "ready"), model.Value);
        Equal(0L, model.Snapshot.Revision);
        Equal(0, changes);
    }

    private static void PublishesOnlyChangedState()
    {
        var model = WidgetModel<State>.CreateForTesting(new State(1, "ready"));
        var changes = new List<WidgetModelChangedEventArgs<State>>();
        model.Changed += (_, change) => changes.Add(change);

        var unchanged = model.Update(state => state with { });
        False(unchanged.Changed, "An equal record replacement was reported as changed.");
        Equal(0L, unchanged.Revision);

        var changed = model.Set(new State(2, "updated"));
        True(changed.Changed, "A distinct replacement was not committed.");
        Equal(new State(1, "ready"), changed.Previous);
        Equal(new State(2, "updated"), changed.Current);
        Equal(1L, changed.Revision);
        Equal(1, changes.Count);
        Equal(0L, changes[0].Previous.Revision);
        Equal(1L, changes[0].Current.Revision);
        Equal(changed.Current, model.Value);
        Equal(new WidgetModelSnapshot<State>(changed.Current, 1), model.Snapshot);
    }

    private static async Task SerializesConcurrentUpdatesAsync()
    {
        var model = WidgetModel<State>.CreateForTesting(new State(0, "counter"));
        var tasks = Enumerable.Range(0, 200)
            .Select(_ => Task.Run(() =>
                model.Update(state => state with { Count = state.Count + 1 })))
            .ToArray();
        await Task.WhenAll(tasks);

        Equal(200, model.Value.Count);
        Equal(200L, model.Snapshot.Revision);
    }

    private static void DerivesResultsFromTheCommittedRevision()
    {
        var model = WidgetModel<State>.CreateForTesting(new State(4, "ready"));

        var update = model.Update(state =>
        {
            var next = state with { Count = state.Count + 1 };
            return (next, $"command-for-{next.Count}");
        });

        True(update.Changed, "The result-bearing mutation did not commit.");
        Equal("command-for-5", update.Result);
        Equal(5, update.Current.Count);
        Equal(1L, update.Revision);

        var noChange = model.Update(state => (state, state.Count));
        False(noChange.Changed, "An equal result-bearing update advanced revision.");
        Equal(5, noChange.Result);
        Equal(1L, noChange.Revision);
    }

    private static async Task ContainsObserversAndStopsInvalidatingAfterDestroyAsync()
    {
        var widget = new ModelWidget(new State(0, "ready"));
        var observed = 0;
        var invalidations = 0;
        widget.Model.Changed += (_, _) => throw new InvalidOperationException("observer");
        widget.Model.Changed += (_, _) => observed++;
        widget.Invalidated += (_, _) => invalidations++;
        await WidgetTestHost.InitializeAsync(widget);

        widget.Model.Update(state => state with { Count = 1 });
        Equal(1, observed);
        Equal(1, invalidations);

        await WidgetTestHost.DestroyAsync(widget);
        widget.Model.Update(state => state with { Count = 2 });
        Equal(2, observed);
        Equal(1, invalidations);
    }

    private static void RejectsInvalidInputs()
    {
        Throws<ArgumentNullException>(() => WidgetModel<State>.CreateForTesting(null!));
        var model = WidgetModel<State>.CreateForTesting(new State(0, "ready"));
        Throws<ArgumentNullException>(() => model.Update(null!));
        Throws<ArgumentNullException>(() => model.Set(null!));
        Throws<ArgumentNullException>(() => model.Update(_ => null!));
    }

    private sealed record State(int Count, string Status);

    private sealed record CollectionState(IReadOnlyList<string> ItemIds, string Status);

    private sealed class CollectionStateComparer : IEqualityComparer<CollectionState>
    {
        public bool Equals(CollectionState? left, CollectionState? right) =>
            ReferenceEquals(left, right) ||
            left is not null && right is not null &&
            left.Status == right.Status &&
            left.ItemIds.SequenceEqual(right.ItemIds, StringComparer.Ordinal);

        public int GetHashCode(CollectionState state)
        {
            var hash = new HashCode();
            hash.Add(state.Status, StringComparer.Ordinal);
            foreach (var itemId in state.ItemIds)
                hash.Add(itemId, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }

    private sealed class ModelWidget : Widget
    {
        internal ModelWidget(State initial) => Model = CreateModel(initial);

        internal WidgetModel<State> Model { get; }

        internal void SetNullFromUpdate() => Model.Update(_ => null!);

        public override WidgetView Render() => new(
            UI.Stack("model.root", UI.Text(Model.Value.Status, "model.status")));
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value, string message) => True(!value, message);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name} to be thrown.");
        }
        catch (TException)
        {
        }
    }
}
