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
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
    return 0;
}

var tests = new List<(string, Func<Task>)>
{
    ("Queue ends, repeats one only on automatic end, and wraps only with repeat all", QueuePolicy),
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
    await Until(() => Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.ActionId == "item.0"));
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
            Check(Nodes(snapshot.Root).Count() < 200, "Unbounded page projection");
        }
        await Until(() => Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.ActionId == "item.0"));
        await widget.OnActionAsync(new("page.next", "test"));
        var page = widget.Render().CreateSnapshot("test", 2);
        Check(Nodes(page.Root).Any(n => n.ActionId == "item.12") && !Nodes(page.Root).Any(n => n.ActionId == "item.0"), "Paging did not replace displayed items");
        service.PageOverride = new("Album list", [new("MPRE.test", "album", "Test album")]);
        await widget.OnActionAsync(new("refresh", "refresh"));
        await Until(() => Nodes(widget.Render().CreateSnapshot("test", 3).Root).Any(n => n.Text == "Test album"));
        await widget.OnActionAsync(new("item.0", "item.0"));
        var detail = widget.Render().CreateSnapshot("test", 4);
        Check(Nodes(detail.Root).Any(n => n.ActionId == "back"), "Nested collection lost its Back action");
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
        Check(rows.Length == 12, "The visible song window changed unexpectedly");
        Check(rows.All(n => n.ContextMenuButton == ControllerButton.Menu && n.ContextActions.Any(a => a.Label == "Start radio")), "Song radio is missing from the controller context menu");
        Check(!Nodes(view.Root).Any(n => n.Kind == ViewNodeKind.Button && n.ActionId?.StartsWith("radio.", StringComparison.Ordinal) == true), "Radio adds a focus stop beside each song");
        var panes = Nodes(view.Root).Single(n => n.Id == "music.panes");
        Check(panes.Kind == ViewNodeKind.Row && panes.Children.Count == 2, "Player and browsing are not adjacent panes");
        Check(Nodes(panes.Children[0]).Any(n => n.Id == "player.main.toggle") &&
            Nodes(panes.Children[0]).Any(n => n.Kind == ViewNodeKind.Slider), "Persistent transport or seeking is missing");
        Check(!Nodes(panes.Children[1]).Any(n => n.Kind == ViewNodeKind.Slider), "Playback sliders entered the browsing focus path");
        Check(!Nodes(view.Root).Any(n => n.ActionId == "player.open"), "Rejected mini-player route is still exposed");
        Check(rows.All(n => n.Focus?.Left == "player.main.toggle"), "Songs lack a direct route to transport");
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
    public void SetPlayback(bool playing)
    {
        State = playing ? new(true, [new("test", "song", "A song title", "Artist name")], 0, false, "off", new("test", true, Position: 15, Duration: 196))
            : new(true, [], -1, false, "off", new());
        Changed?.Invoke();
    }
    public TaskCompletionSource? PendingRadio;
    public TaskCompletionSource<MusicPage>? PendingSearch;
    public TaskCompletionSource<string>? PendingAuth;
    public int RadioCalls, SearchCalls, AuthCalls, CancelCalls;
    public CancellationToken AuthToken;
    public bool Disposed;
    public double? LastValue;
    public MusicPage? PageOverride;
    public readonly System.Collections.Concurrent.ConcurrentBag<string> Commands = [];
    public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
    public Task<string> SignInAsync(CancellationToken token) { AuthCalls++; AuthToken = token; return PendingAuth?.Task.WaitAsync(token) ?? Task.FromResult("Connected"); }
    public Task CancelSignInAsync(CancellationToken token) { CancelCalls++; return Task.CompletedTask; }
    public Task DisconnectAsync(CancellationToken token) { State = MusicState.Empty; Changed?.Invoke(); return Task.CompletedTask; }
    public Task<MusicPage> BrowseAsync(string kind, string value, CancellationToken token)
    {
        if (PageOverride is not null) return Task.FromResult(PageOverride);
        if (kind == "search") { SearchCalls++; if (PendingSearch is not null) return PendingSearch.Task; }
        if (kind == "library") return Task.FromResult(new MusicPage("Library result", []));
        return Task.FromResult(new MusicPage("Home", Enumerable.Range(0, 30).Select(i => new MusicItem(i.ToString(), "song", "Song " + i)).ToArray()));
    }
    public Task PlayAsync(IReadOnlyList<MusicItem> tracks, int index, CancellationToken token) => Task.CompletedTask;
    public Task RadioAsync(MusicItem song, CancellationToken token) { RadioCalls++; return PendingRadio?.Task.WaitAsync(token) ?? Task.CompletedTask; }
    public Task CommandAsync(string command, double? value, CancellationToken token) { LastValue = value; Commands.Add(command); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
