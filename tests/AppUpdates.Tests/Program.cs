using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Version ordering handles previews numerically and stable promotion", Versions),
    ("Release check selects matching edition and preserves notes", Check),
    ("Bare updates select their own installer and preserve bundle identity", Bare),
    ("Stable installations do not select preview releases", Stable),
    ("Candidate cannot install or prepare downloads", Candidate),
    ("Preparation verifies one installer and waits for helper readiness before closing", Prepare),
    ("Corrupt download cannot offer installation", Corrupt),
    ("Missing previous release does not block installation", MissingPrevious),
    ("Cancellation leaves installation untouched", Cancel),
    ("Automatic checks default on, run once, honor opt-out and persist", Automatic),
    ("Failed helper launch keeps the app open and permits retry", LaunchFailure),
    ("Prerequisite fallback requires a fresh reviewed interactive action", InteractiveFallback),
    ("Installer recovery is visible and survives automatic check until manual retry", Recovery),
};
var errors = 0;
foreach (var test in tests) try { await test.Run(); Console.WriteLine("PASS " + test.Name); } catch (Exception error) { errors++; Console.WriteLine("FAIL " + test.Name + ": " + error); }
Console.WriteLine($"{tests.Length - errors}/{tests.Length} passed"); return errors;

static Task Versions()
{
    Assert(BridgeAppUpdates.VersionCompare("0.1.0-preview.30", "0.1.0-preview.9") > 0);
    Assert(BridgeAppUpdates.VersionCompare("0.1.0", "0.1.0-preview.30") > 0);
    Assert(BridgeAppUpdates.VersionCompare("0.1.0-preview.30", "0.1.0") < 0);
    Assert(BridgeAppUpdates.VersionCompare("0.1.0", "0.1.0") == 0); return Task.CompletedTask;
}
static async Task Check()
{
    await using var fixture = new Fixture();
    await fixture.Service.HandleAsync(new("check"), default);
    var result = await Wait(fixture.Service);
    Assert(!result.InstallerOutcomePending && result.State == "available" && result.AvailableVersion == "0.1.0-preview.30" && result.Notes == "Release notes\nFixed things");
}
static async Task Bare()
{
    await using var fixture = new Fixture(edition: "bare");
    await fixture.Service.HandleAsync(new("check"), default); await Wait(fixture.Service);
    await fixture.Service.HandleAsync(new("download"), default);
    var ready = await Wait(fixture.Service);
    Assert(ready.State == "ready" && fixture.Api.LastDownload!.EndsWith("/WidgetRail-Bare-0.1.0-preview.30-win-x64-setup.exe", StringComparison.Ordinal));
    fixture.LaunchReady.SetResult();
    await fixture.Service.HandleAsync(new("install", ready.ReviewToken), default);
    await fixture.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
    using var plan = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(fixture.Root, "plan.json", SearchOption.AllDirectories).Single()));
    Assert(plan.RootElement.GetProperty("edition").GetString() == "bare");
}
static async Task Stable()
{
    await using var fixture = new Fixture(version: "0.0.9");
    await fixture.Service.HandleAsync(new("check"), default);
    Assert((await Wait(fixture.Service)).State == "current");
}
static async Task Candidate()
{
    await using var fixture = new Fixture(canInstall: false);
    await fixture.Service.HandleAsync(new("check"), default); await Wait(fixture.Service);
    await fixture.Service.HandleAsync(new("download"), default);
    Assert((await Wait(fixture.Service)).State == "available" && fixture.Api.Downloads == 0);
}
static async Task Prepare()
{
    await using var fixture = new Fixture();
    await fixture.Service.HandleAsync(new("check"), default); await Wait(fixture.Service);
    await fixture.Service.HandleAsync(new("download"), default);
    var ready = await Wait(fixture.Service);
    Assert(ready.State == "ready", ready.Message); Assert(fixture.Api.Downloads == 1 && fixture.Exits == 0 && fixture.Launches == 0);
    await fixture.Service.HandleAsync(new("install", new string('A', 64)), default); Assert(fixture.Launches == 0);
    await fixture.Service.HandleAsync(new("install", ready.ReviewToken), default);
    await fixture.Launched.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(fixture.Exits == 0);
    await fixture.Service.HandleAsync(new("install", ready.ReviewToken), default); Assert(fixture.Launches == 1);
    fixture.LaunchReady.SetResult();
    await fixture.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(fixture.Exits == 1);
    Assert(Directory.EnumerateFiles(fixture.Root, "plan.json", SearchOption.AllDirectories).Count() == 1);
    Assert(!Directory.EnumerateFiles(fixture.Root, "previous.exe", SearchOption.AllDirectories).Any());
    Assert(!Directory.EnumerateDirectories(fixture.Root, "profile-before", SearchOption.AllDirectories).Any());
}
static async Task Corrupt()
{
    await using var fixture = new Fixture(); fixture.Api.Corrupt = true;
    await fixture.Service.HandleAsync(new("check"), default); await Wait(fixture.Service);
    await fixture.Service.HandleAsync(new("download"), default);
    Assert((await Wait(fixture.Service)).State == "failed" && fixture.Launches == 0 && fixture.Exits == 0);
}
static async Task MissingPrevious()
{
    await using var fixture = new Fixture(); fixture.Api.MissingPrevious = true;
    await fixture.Service.HandleAsync(new("check"), default); await Wait(fixture.Service);
    await fixture.Service.HandleAsync(new("download"), default);
    Assert((await Wait(fixture.Service)).State == "ready" && fixture.Api.Downloads == 1);
}
static async Task Cancel()
{
    await using var fixture = new Fixture(); fixture.Api.Delay = true;
    await fixture.Service.HandleAsync(new("check"), default);
    await fixture.Service.HandleAsync(new("cancel"), default);
    Assert((await Wait(fixture.Service)).State == "cancelled" && fixture.Launches == 0);
}
static async Task Automatic()
{
    await using var fixture = new Fixture();
    Assert((await fixture.Service.HandleAsync(new("status"), default)).AutomaticallyCheck);
    await fixture.Service.CheckAtStartupAsync(default); await Wait(fixture.Service);
    await fixture.Service.CheckAtStartupAsync(default); Assert(fixture.Api.Checks == 1);
    await fixture.Service.HandleAsync(new("auto-disable"), default);
    Assert(!new AppUpdatePreference(Path.Combine(fixture.Root, ".preferences")).Read());
    await using var restarted = new BridgeAppUpdates(new("0.1.0-preview.29", "production", "", false), fixture.Root, fixture.Api);
    await restarted.CheckAtStartupAsync(default); Assert(fixture.Api.Checks == 1);
    await restarted.HandleAsync(new("check"), default); await Wait(restarted); Assert(fixture.Api.Checks == 2);
    await restarted.HandleAsync(new("auto-enable"), default);
    Assert(new AppUpdatePreference(Path.Combine(fixture.Root, ".preferences")).Read());
}
static async Task LaunchFailure()
{
    await using var fixture = new Fixture();
    await fixture.Service.HandleAsync(new("check"), default); await Wait(fixture.Service);
    await fixture.Service.HandleAsync(new("download"), default);
    var ready = await Wait(fixture.Service);
    fixture.LaunchReady.SetException(new IOException("Synthetic helper failure"));
    await fixture.Service.HandleAsync(new("install", ready.ReviewToken), default);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    AppUpdateSnapshot state;
    do { await Task.Delay(10, deadline.Token); state = await fixture.Service.HandleAsync(new("status"), default); } while (state.State == "handoff");
    Assert(state.State == "ready" && state.ReviewToken != ready.ReviewToken && fixture.Exits == 0);
    await fixture.Service.HandleAsync(new("install", ready.ReviewToken), default);
    Assert(fixture.Launches == 1);
    fixture.LaunchReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.LaunchReady.SetResult();
    await fixture.Service.HandleAsync(new("install", state.ReviewToken), default);
    await fixture.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(fixture.Launches == 2 && fixture.Exits == 1);
}
static async Task InteractiveFallback()
{
    await using var fixture = new Fixture();
    await fixture.Service.HandleAsync(new("check"), default); await Wait(fixture.Service);
    await fixture.Service.HandleAsync(new("download"), default);
    var ready = await Wait(fixture.Service);
    fixture.LaunchReady.SetException(new InteractiveAppUpdateRequiredException());
    await fixture.Service.HandleAsync(new("install", ready.ReviewToken), default);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    AppUpdateSnapshot state;
    do { await Task.Delay(10, deadline.Token); state = await fixture.Service.HandleAsync(new("status"), default); } while (state.State == "handoff");
    Assert(state.State == "ready" && state.RequiresInteractiveSetup && state.ReviewToken != ready.ReviewToken && fixture.Exits == 0);
    await fixture.Service.HandleAsync(new("install", ready.ReviewToken), default);
    Assert(fixture.Launches == 1);
    fixture.LaunchReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.LaunchReady.SetResult();
    await fixture.Service.HandleAsync(new("install", state.ReviewToken), default);
    await fixture.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
    using var plan = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(fixture.Root, "plan.json", SearchOption.AllDirectories).Single()));
    Assert(plan.RootElement.GetProperty("interactive").GetBoolean() && fixture.Exits == 1);
}
static async Task Recovery()
{
    await using var fixture = new Fixture();
    Directory.CreateDirectory(fixture.Root);
    foreach (var outcome in new[] { "failed", "cancelled", "restart-required", "startup-warning" })
    {
        File.WriteAllText(Path.Combine(fixture.Root, "result.json"), JsonSerializer.Serialize(new { outcome }));
        await using var restarted = new BridgeAppUpdates(new("0.1.0-preview.29", "production", "", false), fixture.Root, fixture.Api);
        var checksBefore = fixture.Api.Checks;
        var state = await restarted.CheckAtStartupAsync(default);
        Assert(state.InstallerOutcomePending && state.State == "failed" && state.Message.Length != 0 && fixture.Api.Checks == checksBefore);
        await restarted.HandleAsync(new("check"), default);
        Assert(!(await Wait(restarted)).InstallerOutcomePending);

        Assert(!File.Exists(Path.Combine(fixture.Root, "result.json")));
    }
}
static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
static async Task<AppUpdateSnapshot> Wait(BridgeAppUpdates service)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    while (true)
    {
        var state = await service.HandleAsync(new("status"), deadline.Token); AppUpdateContract.ValidateSnapshot(state);
        if (state.State is not ("checking" or "downloading")) { await Task.Delay(20, deadline.Token); return state; }
        await Task.Delay(10, deadline.Token);
    }
}
sealed class Fixture : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "wrail-updates-test-" + Guid.NewGuid().ToString("N"));
    public FakeApi Api { get; } = new();
    public BridgeAppUpdates Service { get; }
    public int Launches; public int Exits;
    public TaskCompletionSource Launched = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource LaunchReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Fixture(bool canInstall = true, string version = "0.1.0-preview.29", string edition = "production") => Service = new(new(version, edition, "", canInstall), Root, Api,
        async (_, token) => { Interlocked.Increment(ref Launches); Launched.TrySetResult(); await LaunchReady.Task.WaitAsync(token); },
        () => { Interlocked.Increment(ref Exits); Exited.TrySetResult(); });
    public async ValueTask DisposeAsync() { await Service.DisposeAsync(); Api.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
sealed class FakeApi : HttpMessageHandler
{
    public bool Corrupt, MissingPrevious, Delay; public int Downloads; public int Checks; public string? LastDownload;
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("fixture executable is never launched");
    private static object Release(string version) => new { tag_name = "v" + version, draft = false, prerelease = true, body = "Release notes\nFixed things", assets = new[] { "production", "bare" }.Select(edition =>
        new { name = BridgeAppUpdates.InstallerName(version, edition), size = Bytes.Length, digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Bytes)) }).ToArray() };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (Delay) await Task.Delay(Timeout.Infinite, token);
        var path = request.RequestUri!.AbsolutePath;
        if (request.RequestUri.Host == "api.github.com")
        {
            if (!path.Contains("/tags/")) Checks++;
            if (MissingPrevious && path.Contains("/tags/")) return new(HttpStatusCode.NotFound);
            var json = path.Contains("/tags/") ? JsonSerializer.Serialize(Release("0.1.0-preview.29")) : JsonSerializer.Serialize(new[] { Release("0.1.0-preview.30") });
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
        LastDownload = request.RequestUri.AbsoluteUri; Downloads++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Corrupt ? [0, 1] : Bytes) };
    }
}
