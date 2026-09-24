using WidgetRail.Samples.YtMusicWidget.Standalone;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

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
};
if (args.Length == 2 && args[0] == "--package-root")
    tests.Add(("Packaged provider and player complete a muted live radio playback", () => LivePackage(args[1])));
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
    public TaskCompletionSource? PendingRadio;
    public TaskCompletionSource<MusicPage>? PendingSearch;
    public TaskCompletionSource<string>? PendingAuth;
    public int RadioCalls, SearchCalls, AuthCalls;
    public CancellationToken AuthToken;
    public bool Disposed;
    public MusicPage? PageOverride;
    public readonly System.Collections.Concurrent.ConcurrentBag<string> Commands = [];
    public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
    public Task<string> SignInAsync(CancellationToken token) { AuthCalls++; AuthToken = token; return PendingAuth?.Task.WaitAsync(token) ?? Task.FromResult("Connected"); }
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
    public Task CommandAsync(string command, double? value, CancellationToken token) { Commands.Add(command); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
