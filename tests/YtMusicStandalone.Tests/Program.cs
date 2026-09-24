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
        }
        service.PageOverride = new("Your playlists", Enumerable.Range(0, 30).Select(i => new MusicItem("PL" + i, "playlist", "Playlist " + i)).ToArray());
        await widget.OnActionAsync(new("tab.library", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null && Nodes(widget.Render().CreateSnapshot("layout", 1).Root).Any(n => n.Text == "Your playlists"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "library.snapshot.json"), SnapshotJson.Serialize(widget.Render().CreateSnapshot("layout", 1)));
        service.PageOverride = new("Home", Enumerable.Range(0, 6).Select(i => new MusicItem("song" + i, "song", "Home song " + i, Artwork: "https://example.invalid/fixture.png", Section: i < 4 ? "Quick picks" : "For you")).ToArray());
        await widget.OnActionAsync(new("tab.home", "test"));
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => Nodes(widget.Render().CreateSnapshot("layout", 1).Root).Any(n => n.Text == "For you"));
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
    return 0;
}

var tests = new List<(string, Func<Task>)>
{
    ("Queue ends, repeats one only on automatic end, and wraps only with repeat all", QueuePolicy),
    ("Play next inserts after the current song and preserves the remaining queue", QueueInsertion),
    ("Playlist insertion preserves order and current occurrences within the queue limit", PlaylistInsertion),
    ("Playlist Play next remains responsive and reports limits and failures", PlaylistNextAction),
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
    ("Cursor browsing traverses long lists and restores collection focus", CursorTraversal),
    ("Sections retain cursor windows and focus groups until an explicit refresh", RetainedSections),
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

static async Task PlaylistNextAction()
{
    var (widget, service) = await Start();
    try
    {
        service.PageOverride = new("Playlists", [new("PLtest", "playlist", "Test playlist"), new("album", "album", "Album")]);
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        var view = widget.Render().CreateSnapshot("playlist-next", 1);
        var playlist = Nodes(view.Root).Single(n => n.Id == "item.0");
        Check(playlist.ContextActions.Any(action => action.ActionId == "next.0") && !playlist.ContextActions.Any(action => action.ActionId == "radio.0"),
            "Playlist menu has missing Play next or unsupported radio");
        Check(!Nodes(view.Root).Single(n => n.Id == "item.1").ContextActions.Any(), "Album menu was expanded unintentionally");
        service.PendingNext = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatch = widget.OnActionAsync(new("next.0", "item.0"));
        Check(dispatch.IsCompletedSuccessfully, "Playlist fetching blocks host dispatch");
        await Until(() => service.NextSongs.Count == 1);
        await widget.OnActionAsync(new("player.toggle", "test"));
        await Until(() => service.Commands.Contains("toggle"));
        Check(service.NextSongs.Single().Id == "PLtest" && service.RadioCalls == 0, "Wrong playlist action dispatched");
        service.PendingNext.SetResult(new(499, true));
        await Until(() => Nodes(widget.Render().CreateSnapshot("playlist-next", 2).Root).Any(n => n.Text == "Added 499 songs to play next (500-song queue limit)"));
        service.PendingNext = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await widget.OnActionAsync(new("next.0", "item.0"));
        await Until(() => service.NextSongs.Count == 2);
        service.PendingNext.SetException(new IOException("empty_playlist"));
        await Until(() => Nodes(widget.Render().CreateSnapshot("playlist-next", 3).Root).Any(n => n.Text?.StartsWith("Could not add this playlist", StringComparison.Ordinal) == true));
    }
    finally { service.PendingNext?.TrySetCanceled(); await WidgetTestHost.DestroyAsync(widget); }
}

static async Task PlayNextAction()
{
    var (widget, service) = await Start();
    try
    {
        service.ReplaceQueue(Enumerable.Range(0, MusicQueue.MaximumItems).Select(i => new MusicItem(i.ToString(), "song", "Full queue song " + i)).ToArray());
        var first = Nodes(widget.Render().CreateSnapshot("next", 1).Root).Single(n => n.Id == "item.0");
        Check(first.ContextActions.Any(action => action.ActionId == "next.0" && action.Label == "Play next"), "Play next is missing from song options");
        await widget.OnActionAsync(new("next.0", "item.0"));
        await Until(() => service.NextSongs.Count == 1);
        Check(service.NextSongs.Single().Title == "Song 0" && service.RadioCalls == 0, "Play next started radio or selected another song");
        await widget.OnActionAsync(new("tab.queue", "test"));
        await Until(() => Nodes(widget.Render().CreateSnapshot("next", 2).Root).Any(n => n.Id == "item.0"));
        var playing = Nodes(widget.Render().CreateSnapshot("next", 3).Root).Single(n => n.Id == "item.0");
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
    await Until(() => widget.Render().FocusGroupEntryRequest is not null && Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.ActionId == "item.0"));
    return (widget, service);
}
static IEnumerable<ViewNode> Nodes(ViewNode root) => new[] { root }.Concat(root.Children.SelectMany(Nodes));
static async Task Until(Func<bool> condition)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!condition()) await Task.Delay(10, deadline.Token);
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
            Check(Nodes(snapshot.Root).Count() <= ProtocolConstants.MaximumNodeCount, "Unbounded page projection");
        }
        await Until(() => Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.ActionId == "item.0"));
        var scroll = Nodes(widget.Render().CreateSnapshot("test", 1).Root).Single(n => n.Id.StartsWith("music.scroll.", StringComparison.Ordinal));
        Check(scroll.ScrollNearEndActionId is not null, "Cursor viewport has no next-window demand");
        await widget.OnActionAsync(new(scroll.ScrollNearEndActionId!, scroll.Id));
        await Until(() => Nodes(widget.Render().CreateSnapshot("test", 2).Root).Any(n => n.ActionId == "item.29"));
        var page = widget.Render().CreateSnapshot("test", 2);
        Check(Nodes(page.Root).Any(n => n.ActionId == "item.0"), "Adjacent loading replaced existing songs");
        service.PageOverride = new("Album list", [new("MPRE.test", "album", "Test album")]);
        await widget.OnActionAsync(new("refresh", "refresh"));
        await Until(() => Nodes(widget.Render().CreateSnapshot("test", 3).Root).Any(n => n.Text == "Test album"));
        await widget.OnActionAsync(new("item.0", "item.0"));
        var detail = widget.RenderSnapshot("test", 4);
        Check(!Nodes(detail.Root).Any(n => n.Kind == ViewNodeKind.Button && n.ActionId == "back"), "Playlist still has a visible Back button");
        Check(await widget.OnControllerInputAsync(new(ControllerButton.B, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, detail.InitialFocusId, ActiveInputScopeId: detail.ActiveInputScopeId, SnapshotSequence: detail.Sequence)),
            "Controller Back no longer returns from collections");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}
static async Task RadioDoesNotBlock()
{
    var (widget, service) = await Start();
    try
    {
        service.PendingRadio = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = widget.OnActionAsync(new("radio.0", "radio.0"));
        Check(action.IsCompletedSuccessfully, "Host dispatch was held for radio response");
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
    var (widget, _) = await Start();
    try
    {
        var view = widget.Render().CreateSnapshot("rows", 1);
        var tabs = Nodes(view.Root).Single(n => n.Id == "music.nav.compact");
        Check(tabs.Kind == ViewNodeKind.Row && Nodes(tabs).Count(n => n.Kind == ViewNodeKind.Button) == 4, "Expected four horizontal destination buttons");
        var rows = Nodes(view.Root).Where(n => n.Kind == ViewNodeKind.ActionSurface && n.Id.StartsWith("item.", StringComparison.Ordinal)).ToArray();
        Check(rows.Length == 24, "The visible song window changed unexpectedly");
        Check(rows.All(n => n.ContextMenuButton == ControllerButton.Menu && n.ContextActions.Any(a => a.Label == "Start radio")), "Song radio is missing from the controller context menu");
        Check(!Nodes(view.Root).Any(n => n.Kind == ViewNodeKind.Button && n.ActionId?.StartsWith("radio.", StringComparison.Ordinal) == true), "Radio adds a focus stop beside each song");
        var panes = Nodes(view.Root).Single(n => n.Id == "music.panes");
        Check(panes.Kind == ViewNodeKind.Row && panes.Children.Count == 2, "Player and browsing are not adjacent panes");
        Check(Nodes(panes.Children[0]).Any(n => n.Id == "player.main.toggle") &&
            Nodes(panes.Children[0]).Any(n => n.Kind == ViewNodeKind.Slider), "Persistent transport or seeking is missing");
        Check(!Nodes(panes.Children[1]).Any(n => n.Kind == ViewNodeKind.Slider), "Playback sliders entered the browsing focus path");
        Check(!Nodes(view.Root).Any(n => n.ActionId == "player.open"), "Rejected mini-player route is still exposed");
        Check(rows.All(n => n.Focus?.Left is null), "Home posters override horizontal grid navigation");
        Check(Nodes(view.Root).Any(n => n.Kind == ViewNodeKind.Grid), "Home posters are not grouped in a grid");
        Check(view.FocusGroupEntryRequest is { } focus &&
            Nodes(view.Root).Any(n => n.Id == focus.GroupId && n.InitialChildFocusId == "item.0"), "Loaded content is not entered after a tab change");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task ControllerRouting()
{
    var (widget, service) = await Start();
    try
    {
        var view = widget.RenderSnapshot("controller", 10);
        Check(await widget.OnControllerInputAsync(new(ControllerButton.X, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, "item.0", ActiveInputScopeId: view.ActiveInputScopeId, SnapshotSequence: view.Sequence)), "X was not routed from a song");
        await Until(() => service.Commands.Contains("toggle"));
        view = widget.RenderSnapshot("controller", 11);
        Check(await widget.OnControllerInputAsync(new(ControllerButton.Y, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, "item.0", ActiveInputScopeId: view.ActiveInputScopeId, SnapshotSequence: view.Sequence)), "Y did not open settings");
        await Until(() => Nodes(widget.Render().CreateSnapshot("controller", 12).Root).Any(n => n.Id == "setup.primary"));
        view = widget.RenderSnapshot("controller", 13);
        Check(await widget.OnControllerInputAsync(new(ControllerButton.B, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, "setup.primary", ActiveInputScopeId: view.ActiveInputScopeId, SnapshotSequence: view.Sequence)), "B did not return from settings");
        await Until(() => Nodes(widget.Render().CreateSnapshot("controller", 14).Root).Any(n => n.Id == "item.0"));
        Check(widget.Render().InitialFocusId == "item.0", "Returning from settings lost the selected song");
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
        await Until(() => Nodes(widget.Render().CreateSnapshot("cursor", 1).Root).Any(n => n.Text == "Album 0"));
        for (var demand = 0; demand < 30; demand++)
        {
            var view = widget.Render().CreateSnapshot("cursor", demand + 2);
            var scroll = Nodes(view.Root).Single(n => n.Id.StartsWith("music.scroll.", StringComparison.Ordinal));
            var rowKeys = Nodes(scroll).Where(n => n.CollectionItemKey is not null).Select(n => n.CollectionItemKey!).ToArray();
            Check(rowKeys.Length <= 96, "Retained window exceeded its bound");
            Check(!Nodes(view.Root).Any(n => n.ActionId is "page.next" or "page.previous"), "Manual pages remain");
            if (scroll.ScrollNearEndActionId is null) break;
            await widget.OnActionAsync(new(scroll.ScrollNearEndActionId, scroll.Id) { VisibleCollectionKeys = rowKeys.TakeLast(4).ToArray() });
            await Until(() => Nodes(widget.Render().CreateSnapshot("cursor", 2).Root).Single(n => n.Id.StartsWith("music.scroll.", StringComparison.Ordinal)).CollectionGeneration != scroll.CollectionGeneration);
        }
        var last = widget.Render().CreateSnapshot("cursor", 40);
        Check(Nodes(last.Root).Any(n => n.ActionId == "item.499"), "Could not reach the final album");
        await widget.OnActionAsync(new("item.499", "item.499"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        await widget.OnActionAsync(new("back", "test"));
        await Until(() => widget.Render().InitialFocusId == "item.499");
        var returned = Nodes(widget.Render().CreateSnapshot("cursor", 41).Root).Single(n => n.Id.StartsWith("music.scroll.", StringComparison.Ordinal));
        Check(returned.ScrollNearStartActionId is not null, "Cannot scroll back through the earlier collection");
        await widget.OnActionAsync(new(returned.ScrollNearStartActionId!, returned.Id));
        await Until(() => Nodes(widget.Render().CreateSnapshot("cursor", 42).Root).Any(n => n.ActionId == "item.456"));
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
        for (var page = 0; page < 5; page++)
        {
            var scroll = BrowseScroll(widget);
            await widget.OnActionAsync(new(scroll.ScrollNearEndActionId!, scroll.Id)
            {
                VisibleCollectionKeys = Nodes(scroll).Where(n => n.CollectionItemKey is not null).Select(n => n.CollectionItemKey!).TakeLast(4).ToArray(),
            });
            await Until(() => BrowseScroll(widget).CollectionGeneration != scroll.CollectionGeneration);
        }
        var before = BrowseScroll(widget);
        Check(before.CollectionStartIndex > 0, "Test did not evict the initial cursor window");
        var focus = widget.Render().FocusGroupEntryRequest!;
        var calls = service.BrowseCalls.Count;
        foreach (var destination in new[] { "home", "search", "queue" })
        {
            await widget.OnActionAsync(new("tab." + destination, "test"));
            await Until(() => widget.Render().FocusGroupEntryRequest is not null);
            Check(BrowseScroll(widget).Id != before.Id, "Independent sections share a collection identity");
            await widget.OnActionAsync(new("tab.library", "test"));
            var after = BrowseScroll(widget);
            Check(after.CollectionResetGeneration == before.CollectionResetGeneration && after.CollectionGeneration == before.CollectionGeneration &&
                after.CollectionStartIndex == before.CollectionStartIndex && Nodes(after).Select(n => n.CollectionItemKey).SequenceEqual(Nodes(before).Select(n => n.CollectionItemKey)),
                "Section navigation replaced or reset its retained cursor window");
            Check(widget.Render().FocusGroupEntryRequest is { } request && request.GroupId == focus.GroupId && request.RequestId > focus.RequestId,
                "Returning did not request entry into the same remembered focus group");
        }
        Check(service.BrowseCalls.Count == calls, "Switching to loaded sections fetched their catalogue again");
        await widget.OnActionAsync(new("library.songs", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        var songs = BrowseScroll(widget);
        await widget.OnActionAsync(new("tab.home", "test"));
        await widget.OnActionAsync(new("tab.library", "test"));
        Check(BrowseScroll(widget).Id == songs.Id && BrowseScroll(widget).CollectionResetGeneration == songs.CollectionResetGeneration,
            "Returning to Library lost its last filter");
        await widget.OnActionAsync(new("library.playlists", "test"));
        Check(BrowseScroll(widget).CollectionGeneration == before.CollectionGeneration, "Switching filters discarded playlist scrolling");
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        Check(BrowseScroll(widget).CollectionStartIndex == 0 && BrowseScroll(widget).CollectionResetGeneration != before.CollectionResetGeneration,
            "Explicit refresh retained stale item focus");
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
        Check(group.Kind == ViewNodeKind.Scroll && group.InitialChildFocusId == "item.0" && loaded.InitialFocusId == "item.0",
            "Loaded Songs did not enter its first result");
        Check(!Nodes(group).Any(n => n.Id.StartsWith("library.", StringComparison.Ordinal)), "Filter controls remain in list focus memory");
        Check(Nodes(group).Single(n => n.Id == "item.0").Focus?.Up == "library.songs", "Up from Songs returns to the wrong filter");
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
        Check(service.SearchCalls == 1 && BrowseScroll(widget).CollectionResetGeneration == before.CollectionResetGeneration, "Returning to Search refreshed its cursor");
        await widget.OnActionAsync(new("search", "search") { CommittedText = "new query" });
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        Check(service.SearchCalls == 2 && BrowseScroll(widget).CollectionResetGeneration != before.CollectionResetGeneration, "New query did not reset old results");
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
        Check(BrowseScroll(widget).CollectionGeneration == home.CollectionGeneration &&
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
        await Until(() => Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Text == "New account playlist"));
        await widget.OnActionAsync(new("tab.library", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        Check(BrowseScroll(widget).CollectionResetGeneration != before.CollectionResetGeneration &&
            !Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Text == "Old account playlist"), "Reconnect reused old account results");
        service.PageOverride = new("Signed out", []);
        await widget.OnActionAsync(new("disconnect", "test"));
        await Until(() => !service.State.Connected && widget.Render().FocusGroupEntryRequest is not null);
        await widget.OnActionAsync(new("tab.home", "test"));
        await Until(() => widget.Render().FocusGroupEntryRequest is not null);
        Check(!Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Text == "New account playlist"), "Disconnect retained private Home results");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task BrowsePresentation()
{
    var (widget, service) = await Start();
    try
    {
        await widget.OnActionAsync(new("tab.library", "test"));
        await Until(() => Nodes(widget.Render().CreateSnapshot("ui", 1).Root).Any(n => n.Text == "Library result"));
        var view = widget.Render().CreateSnapshot("ui", 2);
        var toolbar = Nodes(view.Root).Single(n => n.Id == "library.toolbar");
        var scroll = Nodes(view.Root).Single(n => n.Id.StartsWith("music.scroll.", StringComparison.Ordinal));
        Check(!Nodes(scroll).Any(n => n.Id is "library.toolbar" or "page.heading"), "Library header moves with the list");
        Check(toolbar.Kind == ViewNodeKind.Row && toolbar.Children[0].Id == "library.filters" && toolbar.Children[1].Id == "refresh", "Refresh is not alongside the library filters");
        Check(Nodes(view.Root).Single(n => n.Id == "player.main.scroll").ShowScrollbar == false, "Player still reserves a scrollbar gutter");
        service.PageOverride = new("Home", [new("a", "song", "First", Section: "Quick picks"), new("b", "song", "Second", Section: "Quick picks"), new("c", "playlist", "Mix", Section: "For you")]);
        await widget.OnActionAsync(new("tab.home", "test"));
        await widget.OnActionAsync(new("refresh", "test"));
        await Until(() => Nodes(widget.Render().CreateSnapshot("ui", 3).Root).Any(n => n.Text == "For you"));
        var headings = Nodes(widget.Render().CreateSnapshot("ui", 4).Root).Where(n => n.Id.StartsWith("section.title.", StringComparison.Ordinal)).Select(n => n.Text).ToArray();
        Check(headings.SequenceEqual(new[] { "Quick picks", "For you" }), "Home lost section order or repeated headings within a section");
        var grids = Nodes(widget.Render().CreateSnapshot("ui", 5).Root).Where(n => n.Kind == ViewNodeKind.Grid).ToArray();
        Check(grids.Length == 2 && grids[0].Children.Count == 2 && grids[1].Children.Count == 1, "Home did not group posters by section");
        Check(grids.SelectMany(g => g.Children).All(n => n.ActionSurfacePresentation == ActionSurfacePresentation.Poster), "Home still uses list rows");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}

static async Task QueueRefresh()
{
    var (widget, service) = await Start();
    try
    {
        await widget.OnActionAsync(new("tab.queue", "test"));
        await Until(() => Nodes(widget.Render().CreateSnapshot("queue", 1).Root).Any(n => n.Id == "item.0"));
        var before = Nodes(widget.Render().CreateSnapshot("queue", 2).Root).Single(n => n.Id.StartsWith("music.scroll.", StringComparison.Ordinal));
        service.PlaybackTick();
        var after = Nodes(widget.Render().CreateSnapshot("queue", 3).Root).Single(n => n.Id.StartsWith("music.scroll.", StringComparison.Ordinal));
        Check(before.CollectionResetGeneration == after.CollectionResetGeneration, "Playback progress reset queue scrolling");
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        service.ReplaceQueue([new("new", "song", "New queue song")]);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        await Until(() => Nodes(widget.Render().CreateSnapshot("queue", 4).Root).Any(n => n.Text == "New queue song" && n.Id != "player.main.title"));
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
    public void ReplaceQueue(IReadOnlyList<MusicItem> queue) { State = State with { Queue = queue, Index = 0 }; Changed?.Invoke(); }
    public void SetPlayback(bool playing)
    {
        State = playing ? new(true, [new("test", "song", "A song title", "Artist name")], 0, false, "off", new("test", true, Position: 15, Duration: 196))
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
    public Task<PlayNextResult> PlayNextAsync(MusicItem item, CancellationToken token) { NextSongs.Add(item); return PendingNext?.Task.WaitAsync(token) ?? Task.FromResult(new PlayNextResult(1, false)); }
    public Task RadioAsync(MusicItem song, CancellationToken token) { RadioCalls++; return PendingRadio?.Task.WaitAsync(token) ?? Task.CompletedTask; }
    public Task CommandAsync(string command, double? value, CancellationToken token) { LastValue = value; Commands.Add(command); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
