using WidgetRail.Samples.YtMusicWidget.Standalone;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

if (args is ["--export-layout", var directory])
{
    Directory.CreateDirectory(directory);
    var (widget, service) = await Start();
    try
    {
        foreach (var playing in new[] { false, true })
        {
            service.SetPlayback(playing);
            await File.WriteAllBytesAsync(Path.Combine(directory, playing ? "playing.snapshot.json" : "empty.snapshot.json"),
                SnapshotJson.Serialize(widget.Render().CreateSnapshot("layout", 1)));
            if (playing)
            {
                var snapshot = widget.Render().CreateSnapshot("layout", 2);
                var pin = snapshot.PinnedLayouts.Single();
                await File.WriteAllBytesAsync(Path.Combine(directory, "pinned-player.snapshot.json"), SnapshotJson.Serialize(snapshot with
                { Root = pin.Root!, Surface = pin.Surface, InitialFocusId = pin.InitialFocusId, ActiveInputScopeId = pin.ActiveInputScopeId!,
                    PinnedLayouts = [], FocusGroupEntryRequest = null }));
            }
        }
        await RowAction(widget, "next.0");
        await Until(() => Nodes(widget.Render().CreateSnapshot("layout", 2).Root)
            .Any(n => n.Text == "Added to play next"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "status-short.snapshot.json"),
            SnapshotJson.Serialize(widget.Render().CreateSnapshot("layout", 2)));
        service.SetPlayerError("Playback is temporarily unavailable. Reconnect your account or try another song. " +
            "This deliberately long status must stay inside the header without moving the player or browsing panels.");
        await File.WriteAllBytesAsync(Path.Combine(directory, "status-long.snapshot.json"),
            SnapshotJson.Serialize(widget.Render().CreateSnapshot("layout", 3)));
        service.SetPlayerError(null);
        service.PageOverride = new("Your playlists", Enumerable.Range(0, 30).Select(i => new MusicItem("PL" + i, "playlist", "Playlist " + i)).ToArray());
        await widget.OnActionAsync(new("tab.library", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null && Nodes(widget.Render().CreateSnapshot("layout", 1).Root).Any(n => n.Text == "Your playlists"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "library.snapshot.json"), SnapshotJson.Serialize(widget.Render().CreateSnapshot("layout", 1)));
        service.PageOverride = new("Home", Enumerable.Range(0, 6).Select(i => new MusicItem("song" + i, "song", "Home song " + i, Artwork: "https://example.invalid/fixture.png", Section: i < 4 ? "Quick picks" : "For you")).ToArray());
        await widget.OnActionAsync(new("tab.home", "test"));
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => BrowseScroll(widget).IndexedGroups?.Any(group => group.Header == "For you") == true);
        await File.WriteAllBytesAsync(Path.Combine(directory, "home.snapshot.json"), SnapshotJson.Serialize(widget.Render().CreateSnapshot("layout", 1)));
        await widget.OnActionAsync(new("tab.library", "test"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "library-return.snapshot.json"), SnapshotJson.Serialize(widget.Render().CreateSnapshot("layout", 2)));
        service.PageOverride = new("Your playlists", Enumerable.Range(0, 30).Select(i => new MusicItem("NEW" + i, "playlist", "Updated playlist " + i)).ToArray());
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        await File.WriteAllBytesAsync(Path.Combine(directory, "library-refresh.snapshot.json"), SnapshotJson.Serialize(widget.Render().CreateSnapshot("layout", 3)));
        await widget.OnActionAsync(new("tab.search", "test"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "search.snapshot.json"), SnapshotJson.Serialize(widget.Render().CreateSnapshot("layout", 4)));
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
    var fixtureTheme = WidgetRail.Tests.RendererFixtureExporter.CompileTheme(AppContext.BaseDirectory, "standalone.wrss");
    foreach (var path in Directory.EnumerateFiles(directory, "*.snapshot.json"))
        WidgetRail.Tests.RendererFixtureExporter.Write(path.Replace(".snapshot.json", ".renderer.json", StringComparison.Ordinal),
            SnapshotJson.Deserialize(await File.ReadAllBytesAsync(path)), fixtureTheme);
    return 0;
}

var tests = new List<(string, Func<Task>)>
{
    ("Playback recovery refreshes, skips, stops and rejects stale events", PlaybackRecoveryTests.Run),
    ("Compact pin declares artwork and admits the same transport shortcuts as the main player", PinnedPlayer),
    ("Section content and navigation share motion identity without moving the player", async () =>
    {
        var (widget, service) = await Start();
        try
        {
            foreach (var section in new[] { "home", "library", "queue" })
            {
                await widget.OnActionAsync(new("tab." + section, "test"));
                var snapshot = widget.Render().CreateSnapshot("motion", 1);
                var nodes = Nodes(snapshot.Root).ToArray();
                var motions = nodes.Where(node => node.Transition is not null).Select(node => node.Transition!).ToArray();
                Check(motions.Count(motion => motion.Kind == WidgetTransitionKind.Content) == 1, "one browsing transition");
                Check(motions.All(motion => motion.GroupId == "music.nav" && motion.Key == section), "shared section clock");
                service.SetPlayback(true);
                var updated = widget.Render().CreateSnapshot("motion", 2);
                Check(Nodes(updated.Root).Where(node => node.Transition is not null).All(node => node.Transition!.Key == section), "playback retains section identity");
                Check(nodes.Where(node => node.Id.Contains("player", StringComparison.Ordinal)).All(node => node.Transition is null), "player stays stationary");
            }
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }),
    ("Indexed rows preserve duplicate occurrences and reject replaced query actions", IndexedAuthority),
    ("Queue ends, repeats one only on automatic end, and wraps only with repeat all", QueuePolicy),
    ("Play next inserts after the current song and preserves the remaining queue", QueueInsertion),
    ("Playlist insertion preserves order and current occurrences within the queue limit", PlaylistInsertion),
    ("Playlist Play next remains responsive and reports limits and failures", () => CollectionNextAction("playlist", "PLtest")),
    ("Album Play next remains responsive and reports limits and failures", () => CollectionNextAction("album", "MPRE_test")),
    ("Song menus expose Play next and dispatch without starting radio", PlayNextAction),
    ("Shuffle preserves current and played entries and retains every occurrence", ShufflePolicy),
    ("All pages and pinned layout produce valid bounded snapshots", Snapshots),
    ("Radio selection acknowledges immediately and leaves transport enabled", RadioDoesNotBlock),
    ("A late search response cannot replace a newer page", StalePage),
    ("Sign-in survives closing the overlay", AuthLifetime),
    ("Background actions cannot start playback", BackgroundInput),
    ("Protocol rejects oversized and incomplete messages", BoundedProtocol),
    ("Theme compiles without unsupported declarations", Theme),
    ("Empty setup has one clear primary action and no empty playback controls", CompactSetup),
    ("Browsing uses four horizontal tabs and one focus target per song", ControllerRows),
    ("Controller Y opens settings, B returns, and X works from a song", ControllerRouting),
    ("Indexed browsing reads deep items directly and restores exact occurrence", CursorTraversal),
    ("Sections retain indexed query identity and focus groups until an explicit refresh", RetainedSections),
    ("Library filters wait on the selected filter then enter results without remembering toolbar focus", LibraryFilterFocus),
    ("New searches reset results while a late search cannot replace a retained section", RetainedSearch),
    ("Reconnect and disconnect invalidate retained account pages", AccountPages),
    ("Library controls stay in one row, Home keeps sections, and player hides scrollbar", BrowsePresentation),
    ("Queue refreshes after background changes without resetting on playback ticks", QueueRefresh),
    ("Scrubber commands convert milliseconds to player seconds", SeekUnits),
    ("Sign-in can be canceled from the controller without waiting for its timeout", CancelAuth),
};
if (args.Length == 2 && args[0] == "--package-root")
{
    tests.Add(("Production Python launch prevents late-import writes", () => LateImportDoesNotWrite(args[1])));
    tests.Add(("Installed provider and muted radio playback leave the sealed package unchanged", () => ImmutablePackage(args[1])));
}
var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static Task QueuePolicy()
{
    Check(MusicQueue.Next(0, -1, "all", true) == -1, "Empty queue");
    Check(MusicQueue.Next(3, 2, "off", true) == -1, "End must stop");
    Check(MusicQueue.Next(3, 2, "all", true) == 0, "Repeat all");
    Check(MusicQueue.Next(3, 1, "one", true) == 1, "Repeat one on ended");
    Check(MusicQueue.Next(3, 1, "one", false) == 2, "Explicit next escapes repeat one");
    return Task.CompletedTask;
}
static MusicState QueueState(IReadOnlyList<MusicItem> queue, int index, bool shuffle = false) =>
    new(true, queue, index, shuffle, "off", new("playing", true));

static Task QueueInsertion()
{
    var songs = Enumerable.Range(0, 4).Select(i => new MusicItem(i.ToString(), "song", "Song " + i)).ToArray();
    var added = new MusicItem("added", "song", "Added");
    var next = MusicQueue.AddNext(QueueState(songs, 1), songs, [added]);
    Check(next.State.Queue.SequenceEqual(new[] { songs[0], songs[1], added, songs[2], songs[3] }) && next.State.Index == 1,
        "Play next reordered or replaced the existing queue");
    Check(MusicQueue.AddNext(MusicState.Empty, [], [added]).State.Queue.SequenceEqual(new[] { added }), "Empty queue insertion failed");
    var full = Enumerable.Range(0, MusicQueue.MaximumItems).Select(i => new MusicItem(i.ToString(), "song", "Song " + i)).ToArray();
    foreach (var index in new[] { 0, 250, 498, 499 })
    {
        var insertion = MusicQueue.AddNext(QueueState(full, index), full, [added]).State;
        var removed = index == full.Length - 1 ? full[0] : full[^1];
        Check(insertion.Queue.Count == MusicQueue.MaximumItems && ReferenceEquals(insertion.Current, full[index]),
            "Full queue lost its bound or current song");
        Check(insertion.Queue[insertion.Index + 1] == added && !insertion.Queue.Any(item => ReferenceEquals(item, removed)),
            "Full queue removed the wrong tail entry or lost the added song");
        Check(insertion.Queue.Where(item => item.Id != added.Id).SequenceEqual(full.Where(item => !ReferenceEquals(item, removed))),
            "Tail replacement reordered the retained songs");
    }
    return Task.CompletedTask;
}

static Task PlaylistInsertion()
{
    var original = Enumerable.Range(0, 500).Select(i => new MusicItem("same-id", "song", "Occurrence " + i)).ToArray();
    foreach (var queueSize in new[] { 0, 4, 500 })
    foreach (var count in new[] { 1, 3, 499, 500, 700 })
    foreach (var shuffle in new[] { false, true })
    {
        var queue = original.Take(queueSize).ToArray();
        var playlist = Enumerable.Range(0, count).Select(i => new MusicItem("playlist-" + i, "song", "Playlist song " + i)).ToArray();
        foreach (var index in queueSize == 0 ? new[] { -1 } : new[] { 0, queueSize / 2, queueSize - 1 })
        {
            var active = shuffle ? MusicQueue.Shuffled(queue, index, new Random(13)) : queue;
            var state = QueueState(active, index, shuffle);
            var result = MusicQueue.AddNext(state, queue, playlist);
            var next = result.State;
            var added = Math.Min(count, queueSize == 0 ? 500 : 499);
            Check(result.Result.AddedCount == added && next.Queue.Count <= 500, "Oversized playlist did not respect insertion capacity");
            Check(result.Result.QueueLimitReached == (next.Queue.Count == 500), "Queue capacity feedback is wrong");
            Check(next.Player == state.Player && next.Shuffle == state.Shuffle, "Queue edit changed playback or shuffle state");
            var start = queueSize == 0 ? 0 : next.Index + 1;
            Check(next.Queue.Skip(start).Take(added).SequenceEqual(playlist.Take(added)), "Playlist order was reversed, shuffled or truncated at the wrong end");
            if (queueSize != 0) Check(ReferenceEquals(next.Current, state.Current), "Playlist insertion removed the current occurrence");
            else Check(next.Index == 0, "Empty queue did not select the playlist's first song");
            var originalIndex = queueSize == 0 ? -1 : Array.FindIndex(result.OriginalQueue, item => ReferenceEquals(item, state.Current));
            Check(result.OriginalQueue.Skip(originalIndex + 1).Take(added).SequenceEqual(playlist.Take(added)), "Turning shuffle off would lose playlist placement");
            Check(new HashSet<MusicItem>(next.Queue, ReferenceEqualityComparer.Instance).SetEquals(result.OriginalQueue),
                "Shuffled and original orders retained different occurrences");
            var retained = next.Queue.Where(item => item.Id == "same-id").ToArray();
            Check(retained.SequenceEqual(active.Where(item => retained.Any(kept => ReferenceEquals(item, kept)))), "Eviction reordered retained entries");
            var evicted = active.Length + added - next.Queue.Count;
            var tail = Math.Min(evicted, active.Length - index - 1);
            var expectedRetained = active.Skip(evicted - tail).Take(active.Length - evicted);
            Check(retained.SequenceEqual(expectedRetained), "Eviction did not trim the tail then the oldest played entries");
        }
    }
    var duplicate = original[0];
    var repeated = MusicQueue.AddNext(QueueState([duplicate], 0), [duplicate], [duplicate, duplicate]);
    Check(repeated.State.Queue.Distinct(ReferenceEqualityComparer.Instance).Count() == 3,
        "Repeated song insertion reused occurrence identities");
    try { MusicQueue.AddNext(MusicState.Empty, [], []); throw new Exception("Empty playlist accepted"); }
    catch (ArgumentException) { }
    return Task.CompletedTask;
}

static async Task CollectionNextAction(string kind, string id)
{
    var (widget, service) = await Start();
    try
    {
        service.PageOverride = new("Collections", [new(id, kind, "Test collection"), new("UCartist", "artist", "Artist")]);
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        var view = widget.Render().CreateSnapshot("playlist-next", 1);
        var collection = (await Rows(widget)).Single(n => n.Id == "item.0");
        Check(collection.ContextActions.Any(action => action.ActionId == "next.0") && !collection.ContextActions.Any(action => action.ActionId == "radio.0"),
            "Collection menu has missing Play next or unsupported radio");
        Check(!(await Rows(widget)).Single(n => n.Id == "item.1").ContextActions.Any(), "Artist menu was expanded unintentionally");
        service.PendingNext = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await RowAction(widget, "next.0");
        await Until(() => service.NextSongs.Count == 1);
        await widget.OnActionAsync(new("player.toggle", "test"));
        await Until(() => service.Commands.Contains("toggle"));
        Check(service.NextSongs.Single().Id == id && service.NextSongs.Single().Kind == kind && service.RadioCalls == 0, "Wrong collection action dispatched");
        service.PendingNext.SetResult(new(499, true));
        await Until(() => Nodes(widget.Render().CreateSnapshot("playlist-next", 2).Root).Any(n => n.Text == "Added 499 songs to play next (500-song queue limit)"));
        service.PendingNext = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await RowAction(widget, "next.0");
        await Until(() => service.NextSongs.Count == 2);
        service.PendingNext.SetException(new IOException("empty_collection"));
        await Until(() => Nodes(widget.Render().CreateSnapshot("playlist-next", 3).Root).Any(n => n.Text?.StartsWith($"Could not add this {kind}", StringComparison.Ordinal) == true));
    }
    finally { service.PendingNext?.TrySetCanceled(); await WidgetTestHost.DestroyAsync(widget); }
}

static async Task PlayNextAction()
{
    var (widget, service) = await Start();
    try
    {
        service.ReplaceQueue(Enumerable.Range(0, MusicQueue.MaximumItems).Select(i => new MusicItem(i.ToString(), "song", "Full queue song " + i)).ToArray());
        var first = (await Rows(widget)).Single(n => n.Id == "item.0");
        Check(first.ContextActions.Any(action => action.ActionId == "next.0" && action.Label == "Play next"), "Play next is missing from song options");
        await RowAction(widget, "next.0");
        await Until(() => service.NextSongs.Count == 1);
        Check(service.NextSongs.Single().Title == "Song 0" && service.RadioCalls == 0, "Play next started radio or selected another song");
        await widget.OnActionAsync(new("tab.queue", "test"));
        await Until(() => BrowseScroll(widget).IndexedCollection is { Count: > 0 });
        var playing = (await Rows(widget))[0];
        Check(playing.IsSelected == true && Nodes(playing).Any(n => n.Text == "Now playing"), "Queue lost its persistent current-item state");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static Task ShufflePolicy()
{
    var items = Enumerable.Range(0, 100).Select(i => new MusicItem(i.ToString(), "song", "Song")).ToArray();
    var result = MusicQueue.Shuffled(items, 20, new Random(23));
    Check(result.Take(21).SequenceEqual(items.Take(21)), "Played prefix changed");
    Check(result.Select(i => i.Id).Order().SequenceEqual(items.Select(i => i.Id).Order()), "Lost or duplicated tracks");
    Check(!result.SequenceEqual(items), "Remaining queue was not shuffled");
    return Task.CompletedTask;
}
static async Task<(StandaloneMusicWidget, FakeService)> Start()
{
    var service = new FakeService();
    var widget = new StandaloneMusicWidget(service);
    await WidgetTestHost.InitializeAsync(widget);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await Until(() => widget.Render().FocusGroupEntryRequest is not null && BrowseScroll(widget).IndexedCollection is { Count: > 0 });
    return (widget, service);
}
static IEnumerable<ViewNode> Nodes(ViewNode root) => new[] { root }.Concat(root.Children.SelectMany(Nodes));
static async Task<IReadOnlyList<ViewNode>> Rows(StandaloneMusicWidget widget, int start = 0, int? count = null)
{
    using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "rows");
    var declaration = Nodes(host.CurrentSnapshot.Root).Single(node => node.IndexedCollection is not null);
    using var lease = await host.AcquireAsync(declaration.Id, start, Math.Min(count ?? declaration.IndexedCollection!.Count, 64));
    return lease.Range.Items.Select(item => item.Root).ToArray();
}
static async Task RowAction(StandaloneMusicWidget widget, string action, ControllerButton button = ControllerButton.A)
{
    var index = int.Parse(action[(action.IndexOf('.') + 1)..]);
    using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "action");
    var declaration = Nodes(host.CurrentSnapshot.Root).Single(node => node.IndexedCollection is not null);
    using var lease = await host.AcquireAsync(declaration.Id, index, 1);
    var item = lease.Range.Items[0];
    var admission = action.StartsWith("item.", StringComparison.Ordinal)
        ? lease.RouteAction(item.Key, button)
        : lease.RouteAction(item.Key, button, contextActionOwnerId: item.Root.Id, contextActionId: action);
    Check(admission == WidgetOperationAdmission.Enqueued, "Indexed row action was not admitted");
}
static async Task Until(Func<bool> condition)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!condition()) await Task.Delay(10, deadline.Token);
}
static async Task PinnedPlayer()
{
    var (widget, service) = await Start();
    try
    {
        service.SetPlayback(true);
        var host = WidgetTestHost.CreatePinnedLayoutHost(widget, "music.pin");
        var snapshot = host.CurrentSnapshot;
        Check(snapshot.Surface is { PreferredWidth: 840, PreferredHeight: 580 }, "Main player proportions changed unexpectedly");
        var pin = snapshot.PinnedLayouts.Single();
        Check(pin.Surface is { PreferredWidth: 360, PreferredHeight: 440, MinimumWidth: 320, MinimumHeight: 300 }, "Pin must default to room for artwork while preserving smaller saved sizes");
        Check(pin.InitialFocusId == "player.pinned.toggle", "Pin entry should focus the play control");
        Check(Nodes(pin.Root!).Any(node => node.Id == "player.pinned.artwork" && node.Kind == ViewNodeKind.Image &&
            node.ImageSource == "https://example.invalid/fixture.png"), "Pinned artwork must be declared, not omitted");
        Check(await host.SelectAsync(pin.Id), "Pin selection was rejected");
        foreach (var (button, action) in new[] { (ControllerButton.X, "toggle"), (ControllerButton.LeftBumper, "previous"), (ControllerButton.RightBumper, "next") })
        {
            Check(pin.Root!.Shortcuts.Any(shortcut => shortcut.Button == button && shortcut.ActionId == "player." + action), "Missing pinned transport shortcut");
            Check(await host.RouteActionAsync(button, pin.InitialFocusId!), "Pinned shortcut was not admitted");
            await Until(() => service.Commands.Contains(action));
        }
        Check(await host.RevokeAsync(), "Pin selection did not revoke");
        Check(!await host.RouteActionAsync(ControllerButton.X, pin.InitialFocusId!), "Retired pin accepted a shortcut");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task Snapshots()
{
    var (widget, service) = await Start();
    try
    {
        foreach (var tab in new[] { "search", "library", "queue", "setup", "home" })
        {
            await widget.OnActionAsync(new("tab." + tab, "test"));
            var snapshot = widget.Render().CreateSnapshot("test", 1);
            Check(snapshot.PinnedLayouts.Count == 1, "Missing pinned player");
            Check(ViewSnapshotValidator.Validate(snapshot).Count == 0, "Invalid parent snapshot");
            Check(Nodes(snapshot.Root).Count() < 140, "Rows expanded into the ordinary page");
        }
        await Until(() => BrowseScroll(widget).IndexedCollection is { Count: 30 });
        Check((await Rows(widget, 29, 1))[0].ActionId == "item.29", "Final item cannot be demanded directly");
        service.PageOverride = new("Album list", [new("MPRE.test", "album", "Test album")]);
        await widget.OnActionAsync(new("refresh", "refresh"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        await RowAction(widget, "item.0");
        await Until(() => service.BrowseCalls.Contains("album:MPRE.test"));
        var detail = widget.RenderSnapshot("test", 4);
        Check(!Nodes(detail.Root).Any(n => n.Kind == ViewNodeKind.Button && n.ActionId == "back"), "Playlist still has a visible Back button");
        Check(await widget.OnControllerInputAsync(new(ControllerButton.B, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, detail.InitialFocusId, ActiveInputScopeId: detail.ActiveInputScopeId, SnapshotSequence: detail.Sequence)), "Controller Back no longer returns");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task RadioDoesNotBlock()
{
    var (widget, service) = await Start();
    try
    {
        service.PendingRadio = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await RowAction(widget, "radio.0");
        await Until(() => service.RadioCalls == 1);
        await widget.OnActionAsync(new("player.toggle", "player.main.toggle"));
        await Until(() => service.Commands.Contains("toggle"));
        var toggle = Nodes(widget.Render().CreateSnapshot("test", 1).Root).Single(n => n.Id == "player.main.toggle");
        Check(toggle.IsDisabled != true && toggle.IsBusy != true, "Transport blocked by catalogue work");
        service.PendingRadio.TrySetResult();
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}
static async Task StalePage()
{
    var (widget, service) = await Start();
    try
    {
        service.PendingSearch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await widget.OnActionAsync(new("tab.search", "test"));
        await widget.OnActionAsync(new("search", "search") { CommittedText = "old query" });
        await Until(() => service.SearchCalls == 1);
        await widget.OnActionAsync(new("tab.library", "test"));
        service.PendingSearch.SetResult(new("Stale search", [new("old", "song", "Stale search song")]));
        // The SDK drains the superseded operation before starting its replacement.
        await Until(() => Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Text == "Library result"));
        Check(!Nodes(widget.Render().CreateSnapshot("test", 2).Root).Any(n => n.Text?.Contains("Stale search", StringComparison.Ordinal) == true), "Late result overwrote library");
    }
    finally { service.PendingSearch?.TrySetResult(new("Canceled", [])); await WidgetTestHost.DestroyAsync(widget); }
}
static async Task AuthLifetime()
{
    var (widget, service) = await Start();
    try
    {
        service.PendingAuth = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await widget.OnActionAsync(new("signin", "signin"));
        await Until(() => service.AuthCalls == 1);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        Check(!service.AuthToken.IsCancellationRequested, "Closing overlay cancelled sign-in");
        service.PendingAuth.SetResult("Connected");
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await Until(() => Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Text == "Connected"));
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}
static async Task BackgroundInput()
{
    var (widget, service) = await Start();
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    await widget.OnActionAsync(new("radio.0", "radio.0"));
    Check(service.RadioCalls == 0, "Hidden widget accepted radio action");
    await WidgetTestHost.DestroyAsync(widget);
    Check(service.Disposed, "Service was not disposed");
}
static async Task BoundedProtocol()
{
    Check(await JsonProcess.ReadLineAsync(new StringReader("abc\r\n"), default) == "abc", "Line framing");
    foreach (var value in new[] { "truncated", new string('a', JsonProcess.MaximumLine + 1) + "\n" })
    {
        try { await JsonProcess.ReadLineAsync(new StringReader(value), default); throw new Exception("Invalid frame accepted"); }
        catch (IOException) { }
    }
}
static Task Theme()
{
    var parsed = WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "standalone.wrss")), "standalone.wrss");
    Check(parsed.IsValid, string.Join("\n", parsed.Diagnostics));
    var compiled = WrssThemeCompiler.Compile([parsed.Document]);
    Check(compiled.IsValid, string.Join("\n", compiled.Diagnostics));
    var classes = new HashSet<string> { "music-track" };
    var idle = compiled.Theme!.Resolve(new WrssElement("actionSurface", StyleClasses: classes));
    var current = compiled.Theme.Resolve(new WrssElement("actionSurface", StyleClasses: classes,
        PseudoStates: new HashSet<WrssPseudoState> { WrssPseudoState.Selected }));
    Check(idle.Get("background")?.Text != current.Get("background")?.Text && current.Get("border-width")?.Text == "2px",
        "Current queue item has no persistent visual highlight");
    return Task.CompletedTask;
}

static async Task CompactSetup()
{
    var service = new FakeService();
    await service.DisconnectAsync(default);
    var widget = new StandaloneMusicWidget(service);
    try
    {
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await Until(() => Nodes(widget.Render().CreateSnapshot("setup", 1).Root).Any(n => n.Id == "setup.primary"));
        var view = widget.Render().CreateSnapshot("setup", 2);
        Check(view.Surface!.PreferredHeight <= 440, "Setup uses the full browsing height");
        Check(view.InitialFocusId == "setup.primary", "Setup starts away from its primary action");
        Check(!Nodes(view.Root).Any(n => n.Kind == ViewNodeKind.Slider || n.ActionId?.StartsWith("player.", StringComparison.Ordinal) == true), "Empty playback controls still rendered");
        Check(!Nodes(view.Root).Any(n => n.ActionId == "disconnect"), "Disconnected account exposes an unusable disconnect control");
        Check(!Nodes(view.Root).Any(n => n.Id == "music.nav.compact"), "Setup still contains the browsing navigation");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task ControllerRows()
{
    var (widget, service) = await Start();
    try
    {
        var view = widget.Render().CreateSnapshot("test", 1);
        var rows = await Rows(widget);
        Check(rows.Count == 30 && rows.All(row => row.Kind == ViewNodeKind.ActionSurface && Nodes(row).Count(node => node.IsFocusable) == 1), "Each song must have one action target");
        Check(rows.All(row => row.Focus?.Left is null), "Home posters override horizontal grid navigation");
        Check(BrowseScroll(widget).CollectionLayout?.Kind == CollectionLayoutKind.AdaptiveGrid, "Home is not a native indexed grid");
        Check(view.FocusGroupEntryRequest?.GroupId == BrowseScroll(widget).Id, "Loaded content did not enter its indexed group");
        Check(Nodes(view.Root).Count(node => node.ActionId?.StartsWith("tab.") == true) >= 4, "Section tabs missing");
        Check(Nodes(view.Root).Single(node => node.Id == "music.panes").Children.Count == 2, "Player and browse columns missing");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task ControllerRouting()
{
    var (widget, service) = await Start();
    try
    {
        await RowAction(widget, "item.0", ControllerButton.X);
        await Until(() => service.Commands.Contains("toggle"));
        await RowAction(widget, "item.0", ControllerButton.Y);
        await Until(() => Nodes(widget.Render().CreateSnapshot("controller", 12).Root).Any(n => n.Id == "setup.primary"));
        var view = widget.RenderSnapshot("controller", 13);
        Check(await widget.OnControllerInputAsync(new(ControllerButton.B, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, "setup.primary", ActiveInputScopeId: view.ActiveInputScopeId, SnapshotSequence: view.Sequence)), "B did not return from settings");
        await Until(() => BrowseScroll(widget).IndexedCollection is { Count: > 0 });
        Check(widget.Render().FocusGroupEntryRequest?.GroupId == BrowseScroll(widget).Id, "Settings return lost logical content focus");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task IndexedAuthority()
{
    var (widget, service) = await Start();
    try
    {
        service.PageOverride = new("Duplicates", [new("same", "song", "First occurrence"), new("same", "song", "Second occurrence")]);
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "authority");
        var declaration = Nodes(host.CurrentSnapshot.Root).Single(node => node.IndexedCollection is not null);
        using var old = await host.AcquireAsync(declaration.Id, 0, 2);
        Check(old.Range.Items[0].Key != old.Range.Items[1].Key, "Duplicate songs share occurrence identity");
        var second = old.Range.Items[1];
        Check(old.RouteAction(second.Key, ControllerButton.A, contextActionOwnerId: second.Root.Id, contextActionId: "next.1") == WidgetOperationAdmission.Enqueued, "Second occurrence action rejected");
        await Until(() => service.NextSongs.Count == 1);
        Check(service.NextSongs.Single().Title == "Second occurrence", "Index action resolved wrong duplicate");
        service.PlaybackTick(); host.PublishSnapshot();
        using var tick = await host.AcquireAsync(declaration.Id, 1, 1);
        Check(tick.Range.Items[0].Key == second.Key && tick.Range.Source == old.Range.Source, "Playback tick retired unchanged items");
        service.PageOverride = new("Replacement", [new("new", "song", "Replacement song")]);
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        host.PublishSnapshot();
        Check(old.RouteAction(second.Key, ControllerButton.A) is null, "Old query invoked a replacement row");
        await widget.OnActionAsync(new("next.0", "item.0"));
        Check(service.NextSongs.Count == 1, "Parent-only action invented indexed row authority");
        using var current = await host.AcquireAsync(declaration.Id, 0, 1);
        var row = current.Range.Items[0];
        Check(current.RouteAction(row.Key, ControllerButton.A, contextActionOwnerId: row.Root.Id, contextActionId: "next.0") == WidgetOperationAdmission.Enqueued, "Replacement unavailable");
        await Until(() => service.NextSongs.Count == 2);
        Check(service.NextSongs.Any(item => item.Title == "Replacement song"), "Replacement action resolved wrong captured data");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task CursorTraversal()
{
    var (widget, service) = await Start();
    try
    {
        service.PageOverride = new("Many albums", Enumerable.Range(0, 500).Select(i => new MusicItem("album" + i, "album", "Album " + i)).ToArray());
        await widget.OnActionAsync(new("refresh", "refresh"));
        await Until(() => BrowseScroll(widget).IndexedCollection is { Count: 500 });
        var before = BrowseScroll(widget).IndexedCollection;
        using (var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "large-styles"))
        {
            var theme = WidgetRail.Tests.RendererFixtureExporter.CompileTheme(AppContext.BaseDirectory, "standalone.wrss");
            _ = WidgetRail.WidgetBridge.BridgeRenderStyleResolver.Resolve(host.CurrentSnapshot, theme);
            using var page = await host.AcquireAsync(BrowseScroll(widget).Id, 0, 32);
            _ = WidgetRail.WidgetBridge.BridgeRenderStyleResolver.ResolveRange(page.Range, theme);
        }
        foreach (var index in new[] { 0, 499, 120, 2, 250 })
            Check((await Rows(widget, index, 1))[0].ActionId == "item." + index, "Direct random access returned wrong occurrence");
        Check(BrowseScroll(widget).Children.Count == 0 && BrowseScroll(widget).ScrollNearEndActionId is null, "Legacy cursor/tree still present");
        await RowAction(widget, "item.499");
        await Until(() => service.BrowseCalls.Contains("album:album499") && widget.Render().FocusGroupEntryRequest is not null);
        await widget.OnActionAsync(new("back", "test"));
        Check(widget.Render().FocusGroupEntryRequest?.IndexedItem is { Index: 499 }, "Deep Back lost exact occurrence");
        Check(BrowseScroll(widget).IndexedCollection == before, "Back replaced retained query");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static ViewNode BrowseScroll(StandaloneMusicWidget widget) => Nodes(widget.Render().CreateSnapshot("focus", 1).Root)
    .Single(n => n.Id.StartsWith("music.scroll.", StringComparison.Ordinal));

static async Task RetainedSections()
{
    var (widget, service) = await Start();
    try
    {
        service.PageOverride = new("Saved playlists", Enumerable.Range(0, 150).Select(i => new MusicItem("PL" + i, "playlist", "Playlist " + i)).ToArray());
        await widget.OnActionAsync(new("tab.library", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        var before = BrowseScroll(widget);
        var last = (await Rows(widget, 149, 1))[0];
        var calls = service.BrowseCalls.Count;
        foreach (var destination in new[] { "home", "search", "queue" })
        {
            await widget.OnActionAsync(new("tab." + destination, "test"));
            await Until(() => widget.Render().FocusGroupEntryRequest is not null);
            Check(BrowseScroll(widget).Id != before.Id, "Independent sections share collection identity");
            await widget.OnActionAsync(new("tab.library", "test"));
            Check(BrowseScroll(widget).IndexedCollection == before.IndexedCollection, "Section switch reset query");
            Check((await Rows(widget, 149, 1))[0].CollectionItemKey == last.CollectionItemKey, "Section switch changed occurrence");
        }
        Check(service.BrowseCalls.Count == calls, "Loaded sections refetched data");
        await widget.OnActionAsync(new("library.songs", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        var songs = BrowseScroll(widget);
        await widget.OnActionAsync(new("tab.home", "test")); await widget.OnActionAsync(new("tab.library", "test"));
        Check(BrowseScroll(widget).Id == songs.Id && BrowseScroll(widget).IndexedCollection == songs.IndexedCollection, "Library lost last filter");
        await widget.OnActionAsync(new("library.playlists", "test"));
        Check(BrowseScroll(widget).IndexedCollection == before.IndexedCollection, "Filter switch lost query");
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        Check(BrowseScroll(widget).IndexedCollection!.QueryGeneration > before.IndexedCollection!.QueryGeneration, "Refresh did not retire query");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task LibraryFilterFocus()
{
    var (widget, service) = await Start();
    try
    {
        await widget.OnActionAsync(new("tab.library", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        service.PendingLibrary = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await widget.OnActionAsync(new("library.songs", "library.songs"));
        await Until(() => service.BrowseCalls.Contains("library:songs"));
        var loading = widget.Render().CreateSnapshot("filters", 1);
        Check(loading.InitialFocusId == "library.songs", "Loading Songs moved focus to another filter");
        Check(loading.FocusGroupEntryRequest is null, "Loading entered the toolbar as if it were results");
        Check(!Nodes(loading.Root).Any(n => n.InitialChildFocusId?.StartsWith("library.", StringComparison.Ordinal) == true),
            "A loading list remembers Library toolbar focus");
        service.PendingLibrary.SetResult(new("Saved songs", [new("song", "song", "Saved song")]));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        var loaded = widget.Render().CreateSnapshot("filters", 2);
        var group = Nodes(loaded.Root).Single(n => n.Id == loaded.FocusGroupEntryRequest!.GroupId);
        Check(group.Kind == ViewNodeKind.IndexedCollection && loaded.InitialFocusId == group.Id,
            "Loaded Songs did not enter its first result");
        Check(!Nodes(group).Any(n => n.Id.StartsWith("library.", StringComparison.Ordinal)), "Filter controls remain in list focus memory");
        Check((await Rows(widget, 0, 1))[0].Focus?.Up == "library.songs", "Up from Songs returns to the wrong filter");
        await widget.OnActionAsync(new("library.albums", "library.albums"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        await widget.OnActionAsync(new("library.songs", "library.songs"));
        Check(widget.Render().FocusGroupEntryRequest!.GroupId == group.Id, "Revisiting Songs lost its list focus group");
        service.PendingLibrary = null;
        service.PageOverride = new("Empty songs", []);
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        var empty = widget.Render().CreateSnapshot("filters", 3);
        Check(empty.InitialFocusId == "empty.search" && Nodes(empty.Root).Single(n => n.Id == empty.FocusGroupEntryRequest!.GroupId).InitialChildFocusId == "empty.search",
            "Empty Library results lack a valid focus target");
    }
    finally { service.PendingLibrary?.TrySetResult(new("Canceled", [])); await WidgetTestHost.DestroyAsync(widget); }
}

static async Task RetainedSearch()
{
    var (widget, service) = await Start();
    try
    {
        await widget.OnActionAsync(new("tab.search", "test"));
        await widget.OnActionAsync(new("search", "search") { CommittedText = "first query" });
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        var before = BrowseScroll(widget);
        await widget.OnActionAsync(new("tab.home", "test"));
        var home = BrowseScroll(widget);
        await widget.OnActionAsync(new("tab.search", "test"));
        Check(service.SearchCalls == 1 && BrowseScroll(widget).IndexedCollection == before.IndexedCollection, "Returning to Search refreshed its cursor");
        await widget.OnActionAsync(new("search", "search") { CommittedText = "new query" });
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        Check(service.SearchCalls == 2 && BrowseScroll(widget).IndexedCollection?.QueryGeneration != before.IndexedCollection?.QueryGeneration, "New query did not reset old results");
        service.PendingSearch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await widget.OnActionAsync(new("search", "search") { CommittedText = "slow query" });
        await Until(() => service.SearchCalls == 3);
        await widget.OnActionAsync(new("tab.home", "test"));
        service.PendingSearch.SetResult(new("Stale results", [new("old", "song", "Stale song")]));
        service.PendingSearch = null;
        // A completed replacement drains the old page operation before we inspect Home again.
        await widget.OnActionAsync(new("tab.search", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        await widget.OnActionAsync(new("tab.home", "test"));
        Check(BrowseScroll(widget).IndexedCollection == home.IndexedCollection &&
            !Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Text == "Stale song"), "Late search replaced retained Home");
    }
    finally { service.PendingSearch?.TrySetResult(new("Canceled", [])); await WidgetTestHost.DestroyAsync(widget); }
}

static async Task AccountPages()
{
    var (widget, service) = await Start();
    try
    {
        service.PageOverride = new("Old account", [new("old", "playlist", "Old account playlist")]);
        await widget.OnActionAsync(new("tab.library", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        var before = BrowseScroll(widget);
        await widget.OnActionAsync(new("tab.home", "test"));
        service.PageOverride = new("New account", [new("new", "playlist", "New account playlist")]);
        await widget.OnActionAsync(new("signin", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null && service.BrowseCalls.Count(call => call.StartsWith("home:")) >= 2);
        await widget.OnActionAsync(new("tab.library", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        Check(BrowseScroll(widget).IndexedCollection?.QueryGeneration != before.IndexedCollection?.QueryGeneration &&
            !(await Rows(widget)).SelectMany(Nodes).Any(n => n.Text == "Old account playlist"), "Reconnect reused old account results");
        service.PageOverride = new("Signed out", []);
        await widget.OnActionAsync(new("disconnect", "test"));
        await Until(() => !service.State.Connected && widget.Render().FocusGroupEntryRequest is not null);
        await widget.OnActionAsync(new("tab.home", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        Check(BrowseScroll(widget).IndexedCollection is null, "Disconnect retained private Home results");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task BrowsePresentation()
{
    var (widget, service) = await Start();
    try
    {
        service.PageOverride = new("Library result", [new("PL.fixture", "playlist", "Fixture playlist")]);
        await widget.OnActionAsync(new("tab.library", "test"));
        await Until(() => BrowseScroll(widget).IndexedCollection is { Count: > 0 });
        var view = widget.Render().CreateSnapshot("ui", 2);
        var toolbar = Nodes(view.Root).Single(n => n.Id == "library.toolbar");
        Check(BrowseScroll(widget).CollectionLayout is { Kind: CollectionLayoutKind.List }, "Library must declare native rows");
        Check((await Rows(widget)).All(row => row.CollectionItemKey is not null && row.Kind == ViewNodeKind.ActionSurface), "Logical rows lost key/action");
        Check(toolbar.Kind == ViewNodeKind.Row && toolbar.Children[0].Id == "library.filters" && toolbar.Children[1].Id == "refresh", "Refresh not beside filters");
        Check(Nodes(view.Root).Single(n => n.Id == "player.main.scroll").ShowScrollbar == false, "Player scrollbar returned");
        service.PageOverride = new("Home", [new("a", "song", "First", Section: "Quick picks"), new("b", "song", "Second", Section: "Quick picks"), new("c", "playlist", "Mix", Section: "For you")]);
        await widget.OnActionAsync(new("tab.home", "test")); await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => BrowseScroll(widget).IndexedGroups?.Count == 2);
        Check(BrowseScroll(widget).IndexedGroups!.Select(group => group.Header).SequenceEqual(new[] { "Quick picks", "For you" }), "Home section order changed");
        Check(BrowseScroll(widget).IndexedGroups!.Select(group => group.Count).SequenceEqual(new[] { 2, 1 }), "Home group membership changed");
        Check((await Rows(widget)).All(row => row.ActionSurfacePresentation == ActionSurfacePresentation.Poster), "Home lost posters");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task QueueRefresh()
{
    var (widget, service) = await Start();
    try
    {
        await widget.OnActionAsync(new("tab.queue", "test"));
        var before = BrowseScroll(widget).IndexedCollection;
        var original = (await Rows(widget))[0].CollectionItemKey;
        service.PlaybackTick();
        Check(before == BrowseScroll(widget).IndexedCollection && (await Rows(widget))[0].CollectionItemKey == original,
            "Progress tick changed query/content or occurrence");
        await RowAction(widget, "item.0");
        await Until(() => service.Commands.Contains("queue"));
        Check(service.LastValue == 0, "Queue selection lost its captured index");
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        service.ReplaceQueue([new("new", "song", "New queue song")]);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        Check(BrowseScroll(widget).IndexedCollection!.QueryGeneration > before!.QueryGeneration, "Background queue replacement not published");
        Check(Nodes((await Rows(widget))[0]).Any(node => node.Text == "New queue song"), "Old queue retained");
        await using var realService = new MusicService(AppContext.BaseDirectory);
        await realService.SelectQueueItemAsync([new("stale", "song", "Stale queue")], 0, default);
        Check(realService.State.Current is null, "Stale queue selection started playback or changed current item");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task SeekUnits()
{
    var (widget, service) = await Start();
    try
    {
        await widget.OnActionAsync(new("player.seek", "player.main.seek.slider") { RequestedValue = 12500 });
        await Until(() => service.Commands.Contains("seek"));
        Check(service.LastValue == 12.5, "Scrubber milliseconds were not converted to player seconds");
        await widget.OnActionAsync(new("player.volume", "player.main.volume") { RequestedValue = .35 });
        await Until(() => service.Commands.Contains("volume"));
        Check(service.LastValue == .35, "Volume was incorrectly scaled");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task CancelAuth()
{
    var (widget, service) = await Start();
    try
    {
        service.PendingAuth = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await widget.OnActionAsync(new("tab.setup", "settings.open"));
        await widget.OnActionAsync(new("signin", "setup.primary"));
        await Until(() => service.AuthCalls == 1);
        var primary = Nodes(widget.Render().CreateSnapshot("cancel", 1).Root).Single(n => n.Id == "setup.primary");
        Check(primary.ActionId == "signin.cancel" && primary.IsDisabled != true, "Waiting for sign-in leaves no usable primary control");
        await widget.OnActionAsync(new("signin.cancel", "setup.primary"));
        await Until(() => service.CancelCalls == 1 && service.AuthToken.IsCancellationRequested);
        await Until(() => Nodes(widget.Render().CreateSnapshot("cancel", 2).Root).Single(n => n.Id == "setup.primary").ActionId != "signin.cancel");
    }
    finally { service.PendingAuth?.TrySetResult("Canceled"); await WidgetTestHost.DestroyAsync(widget); }
}

static SortedDictionary<string, string> PackageHashes(string root) => new(
    Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
        file => Path.GetRelativePath(root, file),
        file => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))),
    StringComparer.Ordinal);

static async Task ImmutablePackage(string root)
{
    var before = PackageHashes(root);
    try { await LivePackage(root); }
    finally
    {
        var after = PackageHashes(root);
        Check(before.SequenceEqual(after), "Playback mutated the installed package: " +
            string.Join(", ", after.Keys.Except(before.Keys).Concat(before.Keys.Where(key => !after.TryGetValue(key, out var hash) || hash != before[key]))));
    }
}

static async Task LateImportDoesNotWrite(string root)
{
    // Exercise the production launch with an offline service fixture. Fresh source
    // modules intentionally have no pycache, including the lazily imported solver.
    var temporary = Path.Combine(Path.GetTempPath(), "WidgetRail-YtMusic-import-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temporary);
    try
    {
        var source = Path.Combine(root, "payload", "python");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (file.Contains("__pycache__", StringComparison.Ordinal) || file.EndsWith(".pyc", StringComparison.Ordinal)) continue;
            var target = Path.Combine(temporary, "python", Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        Directory.CreateDirectory(Path.Combine(temporary, "service"));
        File.WriteAllText(Path.Combine(temporary, "service", "service.py"), """
            import json, sys
            for line in sys.stdin:
                request = json.loads(line)
                import yt_dlp_ejs.yt.solver
                print(json.dumps({"id": request["id"], "result": {"connected": False}}), flush=True)
            """);
        var before = PackageHashes(temporary);
        await using (var service = new MusicService(temporary))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await service.InitializeAsync(timeout.Token);
        }
        Check(before.SequenceEqual(PackageHashes(temporary)), "Production launch wrote cache files during late solver import");
        // Negative control: the old launch flags must reproduce the mutation in
        // this disposable fixture, proving the check exercises a writable import.
        var oldLaunch = new System.Diagnostics.ProcessStartInfo(Path.Combine(temporary, "python", "python.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-I", "-c", "import yt_dlp_ejs.yt.solver" }) oldLaunch.ArgumentList.Add(argument);
        using var control = System.Diagnostics.Process.Start(oldLaunch)!;
        await control.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Check(control.ExitCode == 0 && !before.SequenceEqual(PackageHashes(temporary)),
            "Old flags did not reproduce the cache mutation; the regression fixture is ineffective");
    }
    finally { Directory.Delete(temporary, recursive: true); }
}

static async Task LivePackage(string root)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    await using var service = new MusicService(Path.Combine(Path.GetFullPath(root), "payload"), initialVolume: 0);
    await service.InitializeAsync(timeout.Token);
    var results = await service.BrowseAsync("search", "Beethoven symphony", timeout.Token);
    var song = results.Items.First(item => item.Kind == "song");
    await service.RadioAsync(song, timeout.Token);
    while (!service.State.Player.Playing || service.State.Player.Duration <= 0)
    {
        Check(service.State.Player.Error is null, service.State.Player.Error ?? "Player error");
        await Task.Delay(50, timeout.Token);
    }
    Check(service.State.Queue.Count > 1, "Radio did not populate a queue");
    Check(service.State.Player.Volume == 0, "Test playback was not muted");
    var firstOccurrence = service.State.Current!;
    await service.PlayNextAsync(firstOccurrence, timeout.Token);
    await service.CommandAsync("next", null, timeout.Token);
    Check(!ReferenceEquals(firstOccurrence, service.State.Current), "Repeated song lost its queue occurrence identity");
    var previous = service.State;
    var queued = results.Items.First(item => item.Kind == "song" && item.Id != song.Id);
    await service.CommandAsync("shuffle", null, timeout.Token);
    previous = service.State;
    await service.PlayNextAsync(queued, timeout.Token);
    Check(service.State.Current == previous.Current && service.State.Queue[service.State.Index + 1] == queued,
        "Play next interrupted the current track or inserted at the wrong location");
    Check(service.State.Queue.Where((_, index) => index != service.State.Index + 1).SequenceEqual(previous.Queue),
        "Play next changed the remaining queue");
    await service.CommandAsync("shuffle", null, timeout.Token);
    Check(ReferenceEquals(service.State.Current, previous.Current), "Turning shuffle off jumped to an earlier occurrence of the same song");
    Check(service.State.Queue[service.State.Index + 1] == queued, "Turning shuffle off lost Play next placement");
    await service.CommandAsync("next", null, timeout.Token);
    await Until(() => service.State.Player.TrackId == queued.Id && service.State.Player.Playing && service.State.Player.Duration > 0);
    var fullQueue = Enumerable.Range(0, MusicQueue.MaximumItems).Select(i => song with { Title = "Occurrence " + i }).ToArray();
    await service.PlayAsync(fullQueue, 0, timeout.Token);
    await service.CommandAsync("shuffle", null, timeout.Token);
    var evicted = service.State.Queue[^1];
    await service.PlayNextAsync(queued, timeout.Token);
    Check(service.State.Queue.Count == MusicQueue.MaximumItems && service.State.Queue[1] == queued &&
        !service.State.Queue.Any(item => ReferenceEquals(item, evicted)), "Full shuffled queue did not replace its tail");
    await service.CommandAsync("shuffle", null, timeout.Token);
    Check(service.State.Queue.Count == MusicQueue.MaximumItems && service.State.Queue[1] == queued &&
        !service.State.Queue.Any(item => ReferenceEquals(item, evicted)), "Unshuffle restored the evicted song");
    await service.PlayAsync(fullQueue, fullQueue.Length - 1, timeout.Token);
    var lastCurrent = service.State.Current;
    await service.PlayNextAsync(queued, timeout.Token);
    Check(service.State.Queue.Count == MusicQueue.MaximumItems && ReferenceEquals(service.State.Current, lastCurrent) &&
        service.State.Queue[service.State.Index + 1] == queued && !service.State.Queue.Any(item => ReferenceEquals(item, fullQueue[0])),
        "Full queue ending lost the current/next song or retained its oldest entry");
    await service.CommandAsync("pause", null, timeout.Token);
    await Until(() => !service.State.Player.Playing);
    await service.CommandAsync("toggle", null, timeout.Token);
    await Until(() => service.State.Player.Playing);
    await service.CommandAsync("pause", null, timeout.Token);
}

sealed class FakeService : IMusicService
{
    public MusicState State { get; private set; } = new(true, [new("M7lc1UVf-VE", "song", "Playing")], 0, false, "off", new("M7lc1UVf-VE", true));
    public event Action? Changed;
    public void PlaybackTick() { State = State with { Player = State.Player with { Position = State.Player.Position + 1 } }; Changed?.Invoke(); }
    public void SetPlayerError(string? error) { State = State with { Player = State.Player with { Error = error } }; Changed?.Invoke(); }
    public void ReplaceQueue(IReadOnlyList<MusicItem> queue) { State = State with { Queue = queue, Index = 0 }; Changed?.Invoke(); }
    public void SetPlayback(bool playing)
    {
        State = playing ? new(true, [new("test", "song", "A song title", "Artist name", "https://example.invalid/fixture.png")], 0, false, "off", new("test", true, Position: 15, Duration: 196))
            : new(true, [], -1, false, "off", new());
        Changed?.Invoke();
    }
    public TaskCompletionSource? PendingRadio;
    public TaskCompletionSource<PlayNextResult>? PendingNext;
    public TaskCompletionSource<MusicPage>? PendingSearch;
    public TaskCompletionSource<MusicPage>? PendingLibrary;
    public TaskCompletionSource<string>? PendingAuth;
    public int RadioCalls, SearchCalls, AuthCalls, CancelCalls;
    public readonly System.Collections.Concurrent.ConcurrentBag<string> BrowseCalls = [];
    public CancellationToken AuthToken;
    public bool Disposed;
    public double? LastValue;
    public MusicPage? PageOverride;
    public readonly System.Collections.Concurrent.ConcurrentBag<string> Commands = [];
    public readonly System.Collections.Concurrent.ConcurrentBag<MusicItem> NextSongs = [];
    public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
    public Task<string> SignInAsync(CancellationToken token) { AuthCalls++; AuthToken = token; return PendingAuth?.Task.WaitAsync(token) ?? Task.FromResult("Connected"); }
    public Task CancelSignInAsync(CancellationToken token) { CancelCalls++; return Task.CompletedTask; }
    public Task DisconnectAsync(CancellationToken token) { State = MusicState.Empty; Changed?.Invoke(); return Task.CompletedTask; }
    public async Task<MusicPage> BrowseAsync(string kind, string value, CancellationToken token)
    {
        BrowseCalls.Add(kind + ":" + value);
        if (PageOverride is not null) return PageOverride;
        if (kind == "search")
        {
            SearchCalls++;
            return PendingSearch is not null ? await PendingSearch.Task : new MusicPage("Search results", [new("result", "song", "Search song")]);
        }
        if (kind == "library") return PendingLibrary is not null ? await PendingLibrary.Task : new MusicPage("Library result", []);
        return new MusicPage("Home", Enumerable.Range(0, 30).Select(i => new MusicItem(i.ToString(), "song", "Song " + i)).ToArray());
    }
    public Task PlayAsync(IReadOnlyList<MusicItem> tracks, int index, CancellationToken token) => Task.CompletedTask;
    public Task SelectQueueItemAsync(IReadOnlyList<MusicItem> expectedQueue, int index, CancellationToken token)
    {
        CheckQueue(expectedQueue, index);
        Commands.Add("queue"); LastValue = index;
        return Task.CompletedTask;
    }
    private void CheckQueue(IReadOnlyList<MusicItem> queue, int index)
    { if (!ReferenceEquals(State.Queue, queue) || index < 0 || index >= queue.Count) throw new InvalidOperationException("Wrong captured queue authority"); }
    public Task<PlayNextResult> PlayNextAsync(MusicItem item, CancellationToken token) { NextSongs.Add(item); return PendingNext?.Task.WaitAsync(token) ?? Task.FromResult(new PlayNextResult(1, false)); }
    public Task RadioAsync(MusicItem song, CancellationToken token) { RadioCalls++; return PendingRadio?.Task.WaitAsync(token) ?? Task.CompletedTask; }
    public Task CommandAsync(string command, double? value, CancellationToken token) { LastValue = value; Commands.Add(command); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
