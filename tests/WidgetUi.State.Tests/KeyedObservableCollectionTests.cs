using System.Collections.Specialized;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.Tests.WidgetUi.State;

[TestClass]
public sealed class KeyedObservableCollectionTests
{
    private static readonly CollectionAuthority Authority = new("worker-1", "playnite", "library");

    [TestMethod]
    public void PageUpdatesKeepWrappersAndEmitOnlyGranularChanges()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        var events = Observe(model);
        model.Apply(Revision(0, "b", "c"));
        var b = model.Items[0];
        var c = model.Items[1];
        events.Clear();
        model.Apply(Revision(1, "a", "b", "c", "d"));
        Assert.AreSame(b, model.Items[1]);
        Assert.AreSame(c, model.Items[2]);
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add }, events.Select(e => e.Action).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 3 }, events.Select(e => e.NewStartingIndex).ToArray());
        events.Clear();
        model.Apply(Revision(2, "b", "c"));
        Assert.AreSame(b, model.Items[0]);
        Assert.AreSame(c, model.Items[1]);
        Assert.IsTrue(events.All(e => e.Action == NotifyCollectionChangedAction.Remove));
    }

    [TestMethod]
    public void PayloadUpdateNotifiesStableEntryWithoutReplacingCollectionItem()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        model.Apply(Revision(0, "a"));
        var entry = model.Items[0];
        var events = Observe(model);
        var properties = new List<string?>();
        entry.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        model.Apply(new(Authority, 0, 1, [new("a", "new-title")]));
        Assert.AreSame(entry, model.Items[0]);
        Assert.AreEqual("new-title", entry.Value);
        CollectionAssert.AreEqual(new[] { "Value" }, properties);
        Assert.AreEqual(0, events.Count);
        model.Apply(new(Authority, 0, 2, [new("a", "new-title")]));
        Assert.AreEqual(1, properties.Count);
    }

    [TestMethod]
    public void ArbitraryReorderAndInsertionsPreserveSurvivingObjects()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        model.Apply(Revision(0, "a", "b", "c", "d"));
        var original = model.Items.ToDictionary(e => e.Key);
        var events = Observe(model);
        model.Apply(Revision(1, "d", "x", "b", "a"));
        CollectionAssert.AreEqual(new[] { "d", "x", "b", "a" }, model.Items.Select(e => e.Key).ToArray());
        foreach (var key in new[] { "a", "b", "d" })
        {
            Assert.IsTrue(model.TryGetEntry(key, out var retained));
            Assert.AreSame(original[key], retained);
        }
        Assert.IsFalse(model.TryGetEntry("c", out _));
        Assert.IsTrue(events.Any(e => e.Action == NotifyCollectionChangedAction.Move));
        Assert.IsFalse(events.Any(e => e.Action is NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Replace));
    }

    [TestMethod]
    public void StaleAndForeignRevisionsCannotRollBackMembership()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        model.Apply(new(Authority, 2, 8, [new("current", "current")]));
        var events = Observe(model);
        Assert.AreEqual(CollectionRevisionResult.Stale, model.Apply(new(Authority, 2, 8, [])));
        Assert.AreEqual(CollectionRevisionResult.Stale, model.Apply(new(Authority, 2, 7, [])));
        Assert.AreEqual(CollectionRevisionResult.Stale, model.Apply(new(Authority, 1, 1000, [])));
        Assert.AreEqual(CollectionRevisionResult.WrongAuthority, model.Apply(new(Authority with { RuntimeId = "retired" }, 9, 1000, [])));
        Assert.AreEqual("current", model.Items[0].Key);
        Assert.AreEqual(0, events.Count);
        Assert.AreEqual(2L, model.Generation);
        Assert.AreEqual(8L, model.Revision);
    }

    [TestMethod]
    public void NewGenerationRetiresEvenSameKeysAndAllowsRevisionRestart()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        model.Apply(Revision(99, "a"));
        var retired = model.Items[0];
        var events = Observe(model);
        Assert.AreEqual(CollectionRevisionResult.Applied, model.Apply(new(Authority, 1, 0, [new("a", "new-query")])));
        Assert.AreNotSame(retired, model.Items[0]);
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add }, events.Select(e => e.Action).ToArray());
        Assert.AreEqual(CollectionRevisionResult.Stale, model.Apply(Revision(100, "old")));
    }

    [TestMethod]
    public void DuplicateKeysAreRejectedBeforeAnyMutationAndRevisionCopiesInput()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        model.Apply(Revision(0, "original"));
        var events = Observe(model);
        Assert.ThrowsExactly<ArgumentException>(() => new KeyedCollectionRevision<string>(Authority, 0, 1,
            [new("same", "one"), new("same", "two")]));
        Assert.AreEqual("original", model.Items[0].Key);
        Assert.AreEqual(0, events.Count);
        var source = new List<KeyedCollectionItem<string>> { new("safe", "value") };
        var revision = new KeyedCollectionRevision<string>(Authority, 0, 2, source);
        source.Clear();
        model.Apply(revision);
        Assert.AreEqual("safe", model.Items[0].Key);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<KeyedCollectionItem<string>>)revision.Items).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ObservableCollectionEntry<string>>)model.Items).Clear());
    }

    [TestMethod]
    public void NotificationHandlersObserveGranularOrderAndLookupMatchesEachChange()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        model.Apply(Revision(0, "a", "b", "c"));
        var replay = model.Items.ToList();
        ((INotifyCollectionChanged)model.Items).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Remove) replay.RemoveAt(e.OldStartingIndex);
            else if (e.Action == NotifyCollectionChangedAction.Add) replay.Insert(e.NewStartingIndex, (ObservableCollectionEntry<string>)e.NewItems![0]!);
            else if (e.Action == NotifyCollectionChangedAction.Move)
            {
                var entry = replay[e.OldStartingIndex];
                replay.RemoveAt(e.OldStartingIndex);
                replay.Insert(e.NewStartingIndex, entry);
            }
            else Assert.Fail("Unexpected reset/replace.");
            CollectionAssert.AreEqual(replay, model.Items.ToList());
            foreach (var entry in model.Items)
            {
                Assert.IsTrue(model.TryGetEntry(entry.Key, out var lookup));
                Assert.AreSame(entry, lookup);
            }
        };
        model.Apply(Revision(1, "c", "new", "a"));
        model.Apply(Revision(2));
    }

    [TestMethod]
    public void ReentrantMutationIsRejectedWithoutPreventingOuterUpdateWhenHandled()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        ((INotifyCollectionChanged)model.Items).CollectionChanged += (_, _) =>
            Assert.ThrowsExactly<InvalidOperationException>(() => model.Apply(Revision(2, "reentrant")));
        model.Apply(Revision(0, "a", "b"));
        Assert.AreEqual(2, model.Items.Count);
        Assert.AreEqual(0L, model.Revision);
    }

    [TestMethod]
    public void SubscriberFailureFaultsModelInsteadOfContinuingPartialUpdate()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        ((INotifyCollectionChanged)model.Items).CollectionChanged += (_, _) => throw new ApplicationException("subscriber failed");
        Assert.ThrowsExactly<ApplicationException>(() => model.Apply(Revision(0, "a", "b")));
        Assert.ThrowsExactly<InvalidOperationException>(() => model.Apply(Revision(1, "c")));
    }

    [TestMethod]
    public void MutationOnAnotherThreadIsRejected()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { model.Apply(Revision(0, "a")); }
            catch (Exception exception) { failure = exception; }
        });
        thread.Start();
        thread.Join();
        Assert.IsInstanceOfType<InvalidOperationException>(failure);
        Assert.AreEqual(0, model.Items.Count);
    }

    [TestMethod]
    public void MixedPagingAndReorderingMaintainsReferenceModelAcrossManyUpdates()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        var random = new Random(3921);
        for (var revision = 0; revision < 300; ++revision)
        {
            var before = model.Items.ToDictionary(e => e.Key);
            var selected = Enumerable.Range(0, 40).Where(_ => random.Next(3) != 0)
                .OrderBy(_ => random.Next()).Select(i => new KeyedCollectionItem<string>($"key-{i}", $"value-{revision}-{i}")).ToArray();
            model.Apply(new(Authority, 0, revision, selected));
            CollectionAssert.AreEqual(selected.Select(e => e.Key).ToArray(), model.Items.Select(e => e.Key).ToArray());
            CollectionAssert.AreEqual(selected.Select(e => e.Value).ToArray(), model.Items.Select(e => e.Value).ToArray());
            foreach (var entry in model.Items)
                if (before.TryGetValue(entry.Key, out var previous)) Assert.AreSame(previous, entry);
        }
    }

    [TestMethod]
    public void LargeOverlappingPageWindowDoesNotEmitMovesOrReset()
    {
        var model = new KeyedObservableCollection<string>(Authority);
        model.Apply(Revision(0, Enumerable.Range(0, 5000).Select(i => $"key-{i}").ToArray()));
        var retained = model.Items[2500];
        var events = Observe(model);
        model.Apply(Revision(1, Enumerable.Range(1000, 5000).Select(i => $"key-{i}").ToArray()));
        Assert.AreSame(retained, model.Items[1500]);
        Assert.AreEqual(2000, events.Count);
        Assert.AreEqual(1000, events.Count(e => e.Action == NotifyCollectionChangedAction.Add));
        Assert.AreEqual(1000, events.Count(e => e.Action == NotifyCollectionChangedAction.Remove));
    }

    private static KeyedCollectionRevision<string> Revision(long revision, params string[] keys) =>
        new(Authority, 0, revision, keys.Select(key => new KeyedCollectionItem<string>(key, key)));

    private static List<NotifyCollectionChangedEventArgs> Observe(KeyedObservableCollection<string> model)
    {
        var events = new List<NotifyCollectionChangedEventArgs>();
        ((INotifyCollectionChanged)model.Items).CollectionChanged += (_, e) => events.Add(e);
        return events;
    }
}
