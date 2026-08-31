using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetModelTests
{
    public static async Task Run()
    {
        await PublishesOnlyChangedStateAsync();
        await SerializesConcurrentUpdatesAsync();
        await DerivesResultsFromTheCommittedRevisionAsync();
        await CustomComparerDefinesSemanticCollectionEqualityAsync();
        await FailedUpdaterDoesNotCommitOrPublishAsync();
        await ContainsObserversAndStopsInvalidatingAfterDestroyAsync();
        RejectsInvalidInputs();
    }

    private static async Task CustomComparerDefinesSemanticCollectionEqualityAsync()
    {
        var widget = new CollectionModelWidget(
            new CollectionState(["one", "two"], "ready"));
        var invalidations = 0;
        widget.Invalidated += (_, _) => invalidations++;
        await WidgetTestHost.InitializeAsync(widget);

        var equal = widget.Model.Set(new(["one", "two"], "ready"));
        False(equal.Changed,
            "The configured sequence comparer did not suppress an equal collection.");
        Equal(0L, equal.Revision);
        Equal(0, invalidations);

        var changed = widget.Model.Set(new(["one", "three"], "ready"));
        True(changed.Changed, "A distinct collection was not committed.");
        Equal(1L, changed.Revision);
        Equal(1, invalidations);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task FailedUpdaterDoesNotCommitOrPublishAsync()
    {
        var widget = new ModelWidget(new State(7, "ready"));
        var invalidations = 0;
        var changes = 0;
        widget.Invalidated += (_, _) => invalidations++;
        widget.Model.Changed += (_, _) => changes++;
        await WidgetTestHost.InitializeAsync(widget);

        Throws<InvalidOperationException>(() => widget.Model.Update(_ =>
            throw new InvalidOperationException("fixture")));

        Equal(new State(7, "ready"), widget.Model.Value);
        Equal(0L, widget.Model.Snapshot.Revision);
        Equal(0, invalidations);
        Equal(0, changes);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task PublishesOnlyChangedStateAsync()
    {
        var widget = new ModelWidget(new State(1, "ready"));
        var invalidations = 0;
        var changes = new List<WidgetModelChangedEventArgs<State>>();
        widget.Invalidated += (_, _) => invalidations++;
        widget.Model.Changed += (_, change) => changes.Add(change);
        await WidgetTestHost.InitializeAsync(widget);

        var unchanged = widget.Model.Update(state => state with { });
        False(unchanged.Changed, "An equal record replacement was reported as changed.");
        Equal(0L, unchanged.Revision);
        Equal(0, invalidations);

        var changed = widget.Model.Set(new State(2, "updated"));
        True(changed.Changed, "A distinct replacement was not committed.");
        Equal(new State(1, "ready"), changed.Previous);
        Equal(new State(2, "updated"), changed.Current);
        Equal(1L, changed.Revision);
        Equal(1, invalidations);
        Equal(1, changes.Count);
        Equal(0L, changes[0].Previous.Revision);
        Equal(1L, changes[0].Current.Revision);
        Equal(changed.Current, widget.Model.Value);
        Equal(new WidgetModelSnapshot<State>(changed.Current, 1), widget.Model.Snapshot);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task SerializesConcurrentUpdatesAsync()
    {
        var widget = new ModelWidget(new State(0, "counter"));
        await WidgetTestHost.InitializeAsync(widget);
        var tasks = Enumerable.Range(0, 200)
            .Select(_ => Task.Run(() =>
                widget.Model.Update(state => state with { Count = state.Count + 1 })))
            .ToArray();
        await Task.WhenAll(tasks);

        Equal(200, widget.Model.Value.Count);
        Equal(200L, widget.Model.Snapshot.Revision);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task DerivesResultsFromTheCommittedRevisionAsync()
    {
        var widget = new ModelWidget(new State(4, "ready"));
        await WidgetTestHost.InitializeAsync(widget);

        var update = widget.Model.Update(state =>
        {
            var next = state with { Count = state.Count + 1 };
            return (next, $"command-for-{next.Count}");
        });

        True(update.Changed, "The result-bearing mutation did not commit.");
        Equal("command-for-5", update.Result);
        Equal(5, update.Current.Count);
        Equal(1L, update.Revision);

        var noChange = widget.Model.Update(state => (state, state.Count));
        False(noChange.Changed, "An equal result-bearing update advanced revision.");
        Equal(5, noChange.Result);
        Equal(1L, noChange.Revision);
        await WidgetTestHost.DestroyAsync(widget);
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
        var widget = new ModelWidget(new State(0, "ready"));
        Throws<ArgumentNullException>(() => widget.Model.Update(null!));
        Throws<ArgumentNullException>(() => widget.Model.Set(null!));
        Throws<ArgumentNullException>(() => widget.SetNullFromUpdate());
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

    private sealed class CollectionModelWidget : Widget
    {
        internal CollectionModelWidget(CollectionState initial) =>
            Model = CreateModel(initial, new CollectionStateComparer());

        internal WidgetModel<CollectionState> Model { get; }

        public override WidgetView Render() => new(
            UI.Stack("collection-model.root", UI.Text(
                Model.Value.Status, "collection-model.status")));
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
