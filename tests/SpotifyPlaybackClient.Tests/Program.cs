using System.Text;
using System.Threading.Channels;
using System.Diagnostics;
using WidgetRail.SpotifyPlayback;
using WidgetRail.WindowsSpotifyProvider;

if (args is ["--private-runtime", var runtimeRoot, "--playback-host", var playbackHost])
{
    await PrivateRuntimeBootstrap(runtimeRoot, playbackHost);
    return;
}
if (args is ["--cleanup-host", var cleanupHost])
{
    await RealProfileCleanup(Path.GetFullPath(cleanupHost));
    return;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Process launch is redirected and parent bounded", PlaybackHostStartInfo),
    ("Client correlates commands without leaking tokens", PlaybackHostClientProtocol),
    ("Startup SDK errors fail immediately with their exact code", StartupSdkError),
    ("Exited playback helper fails promptly without forwarding private diagnostics", StartupHostExit),
    ("Parent-owned profile cleanup preserves neighboring data", ProfileCleanup),
    ("Stale recovery preserves live owners and malformed directory names", StaleProfiles),
    ("A failed helper launch removes its allocated profile", FailedStartProfile),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}
if (failures != 0) Environment.Exit(1);
Console.WriteLine($"SpotifyPlaybackClient.Tests passed ({tests.Length} tests)");

static async Task PrivateRuntimeBootstrap(string runtimeRoot, string playbackHost)
{
    using var root = new ProfileTestRoot();
    using var profile = new SpotifyPlaybackProfileDirectory(root.Path);
    var start = SpotifyPlaybackHostProcessFactory.CreateStartInfo(
        Path.GetFullPath(playbackHost), Environment.ProcessId, profile.RootPath);
    start.Environment["DOTNET_ROOT"] = Path.GetFullPath(runtimeRoot);
    start.Environment["DOTNET_ROOT_X64"] = Path.GetFullPath(runtimeRoot);
    using var process = System.Diagnostics.Process.Start(start)!;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var errorDrain = process.StandardError.ReadToEndAsync(timeout.Token);
    try
    {
        var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
        if (line is null)
            throw new InvalidOperationException("Playback helper exited before initializing under the private runtime.");
        var initialized = SpotifyPlaybackProtocolCodec.DecodeEvent(line);
        Assert.Equal("host_initialized", initialized.Type);
        // No connect or token command is sent: this probes runtime and WebView2 startup only.
        process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
        await errorDrain;
        Console.WriteLine("PASS private Desktop runtime bootstraps the playback helper and closes cleanly without a Spotify account.");
    }
    finally
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }
}

static Task PlaybackHostStartInfo()
{
    var executable = Path.GetFullPath("SpotifyPlaybackHost.exe");
    var profilePath = Path.GetFullPath("spotify-test-profile");
    var start = SpotifyPlaybackHostProcessFactory.CreateStartInfo(executable, 321, profilePath);
    Assert.Equal(executable, start.FileName);
    Assert.True(!start.UseShellExecute && start.CreateNoWindow && !start.ErrorDialog);
    Assert.True(start.RedirectStandardInput && start.RedirectStandardOutput &&
        start.RedirectStandardError);
    Assert.Equal("1", start.Environment["DOTNET_DISABLE_GUI_ERRORS"]);
    Assert.SequenceEqual(["--parent-pid", "321", "--profile", profilePath], start.ArgumentList);
    return Task.CompletedTask;
}

static Task ProfileCleanup()
{
    using var root = new ProfileTestRoot();
    var neighbor = Path.Combine(root.Path, "unrelated.txt");
    File.WriteAllText(neighbor, "keep");
    string path;
    using (var profile = new SpotifyPlaybackProfileDirectory(root.Path))
    {
        path = profile.RootPath;
        Directory.CreateDirectory(Path.Combine(path, "EBWebView"));
        File.WriteAllText(Path.Combine(path, "EBWebView", "probe"), "temporary");
    }
    Assert.True(!Directory.Exists(path));
    Assert.Equal("keep", File.ReadAllText(neighbor));
    return Task.CompletedTask;
}

static Task StaleProfiles()
{
    using var root = new ProfileTestRoot();
    var active = Path.Combine(root.Path, $"WidgetRail.SpotifyPlayback.{Environment.ProcessId}.{Guid.NewGuid():N}");
    var malformed = Path.Combine(root.Path, "WidgetRail.SpotifyPlayback.unrelated");
    var dead = Path.Combine(root.Path, $"WidgetRail.SpotifyPlayback.2147483647.{Guid.NewGuid():N}");
    var recent = Path.Combine(root.Path, $"WidgetRail.SpotifyPlayback.2147483647.{Guid.NewGuid():N}");
    foreach (var path in new[] { active, malformed, dead, recent })
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "marker"), "preserve unless stale and unowned");
        if (path != recent) Directory.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-2));
    }
    using var allocated = new SpotifyPlaybackProfileDirectory(root.Path);
    Assert.True(Directory.Exists(active) && Directory.Exists(malformed) && Directory.Exists(recent));
    Assert.True(!Directory.Exists(dead));
    return Task.CompletedTask;
}

static Task FailedStartProfile()
{
    using var root = new ProfileTestRoot();
    var factory = new SpotifyPlaybackHostProcessFactory(root.Path);
    try { factory.Start(Path.Combine(root.Path, "missing.exe"), Environment.ProcessId); throw new Exception("Missing helper started."); }
    catch (System.ComponentModel.Win32Exception) { }
    Assert.True(!Directory.EnumerateDirectories(root.Path).Any());
    return Task.CompletedTask;
}

static async Task RealProfileCleanup(string executable)
{
    using var root = new ProfileTestRoot();
    var factory = new SpotifyPlaybackHostProcessFactory(root.Path);
    foreach (var forced in new[] { false, true })
    {
        await using (var process = factory.Start(executable, Environment.ProcessId))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                var line = await process.Output.ReadLineAsync(timeout.Token);
                Assert.True(line is not null);
                var message = SpotifyPlaybackProtocolCodec.DecodeEvent(line!);
                if (message.Type == "sdk_error") throw new Exception("Player initialization failed.");
                if (message.Type == "host_initialized") break;
            }
            Assert.Equal(1, Directory.EnumerateDirectories(root.Path).Count());
            if (forced) process.Terminate();
            else process.Input.Close();
            while (!process.HasExited) await Task.Delay(20, timeout.Token);
        }
        Assert.True(!Directory.EnumerateDirectories(root.Path).Any());
        Console.WriteLine($"PASS real WebView2 profile removed after {(forced ? "forced" : "normal")} helper exit; no account or playback used.");
    }
}

static async Task PlaybackHostClientProtocol()
{
    const string accessToken = "short-lived-secret-token";
    var process = new FakePlaybackHostProcess();
    var factory = new FakePlaybackHostProcessFactory(process);
    var options = new SpotifyPlaybackHostClientOptions(
        Environment.ProcessPath!, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    await using var client = new SpotifyPlaybackHostClient(options, factory);

    var observed = new List<string>();
    client.EventReceived += (_, value) => observed.Add(value.Type);
    process.OutputChannel.WriteLine(SpotifyPlaybackProtocolCodec.EncodeEvent(new(
        SpotifyPlaybackProtocol.Version, "host_initialized", null, new { })));
    process.OutputChannel.WriteLine(SpotifyPlaybackProtocolCodec.EncodeEvent(new(
        SpotifyPlaybackProtocol.Version, "sdk_loaded", null, new { })));
    await client.StartAsync(default);
    Assert.True(client.IsRunning);
    Assert.SequenceEqual(["host_initialized", "sdk_loaded"], observed);

    var pause = client.SendAsync("pause", new { }, default);
    var pauseRequest = SpotifyPlaybackProtocolCodec.DecodeRequest(
        await process.InputChannel.ReadLineAsync(default));
    Assert.Equal("pause", pauseRequest.Type);
    process.OutputChannel.WriteLine(SpotifyPlaybackProtocolCodec.EncodeEvent(new(
        SpotifyPlaybackProtocol.Version, "command_completed", pauseRequest.RequestId,
        new { })));
    Assert.Equal("command_completed", (await pause).Type);

    var tokenCommand = client.ProvideTokenAsync(
        "token-1",
        new TrustedHostSpotifyAccessToken(
            accessToken, DateTimeOffset.UtcNow.AddMinutes(5),
            SpotifyPlaybackProtocol.RequiredScopes),
        default);
    var tokenWire = await process.InputChannel.ReadLineAsync(default);
    var tokenRequest = SpotifyPlaybackProtocolCodec.DecodeRequest(tokenWire);
    Assert.Equal("provide_token", tokenRequest.Type);
    Assert.Equal(accessToken,
        tokenRequest.Payload.GetProperty("accessToken").GetString());
    process.OutputChannel.WriteLine(SpotifyPlaybackProtocolCodec.EncodeEvent(new(
        SpotifyPlaybackProtocol.Version, "command_failed", tokenRequest.RequestId,
        new { code = "authentication_error", message = accessToken })));
    try
    {
        await tokenCommand;
        throw new InvalidOperationException("Expected the token command to fail.");
    }
    catch (SpotifyPlaybackHostClientException exception)
    {
        Assert.Equal("authentication_error", exception.Code);
        Assert.True(!exception.ToString().Contains(accessToken, StringComparison.Ordinal));
    }
}

static async Task StartupHostExit()
{
    var process = new FakePlaybackHostProcess();
    process.OutputChannel.Complete();
    var options = new SpotifyPlaybackHostClientOptions(
        Environment.ProcessPath!, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
    await using var client = new SpotifyPlaybackHostClient(options, new FakePlaybackHostProcessFactory(process));
    try
    {
        await client.StartAsync(default);
        throw new InvalidOperationException("Expected exited playback host to fail startup.");
    }
    catch (SpotifyPlaybackHostClientException exception)
    {
        Assert.Equal("host_exited", exception.Code);
        Assert.True(!client.IsRunning);
    }
}

static async Task StartupSdkError()
{
    var process = new FakePlaybackHostProcess();
    var factory = new FakePlaybackHostProcessFactory(process);
    var options = new SpotifyPlaybackHostClientOptions(
        Environment.ProcessPath!, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
    await using var client = new SpotifyPlaybackHostClient(options, factory);
    process.OutputChannel.WriteLine(SpotifyPlaybackProtocolCodec.EncodeEvent(new(
        SpotifyPlaybackProtocol.Version, "sdk_error", null,
        new { code = "host_initialization_error", message = "private detail" })));

    var started = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await client.StartAsync(default);
        throw new InvalidOperationException("Expected playback-host startup to fail.");
    }
    catch (SpotifyPlaybackHostClientException exception)
    {
        Assert.Equal("host_initialization_error", exception.Code);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2));
        Assert.True(!exception.ToString().Contains("private detail", StringComparison.Ordinal));
    }
}

internal sealed class FakePlaybackHostProcessFactory(FakePlaybackHostProcess process) :
    ISpotifyPlaybackHostProcessFactory
{
    public ISpotifyPlaybackHostProcess Start(string executablePath, int parentProcessId)
    {
        Assert.Equal(Environment.ProcessPath, executablePath);
        Assert.Equal(Environment.ProcessId, parentProcessId);
        return process;
    }
}

internal sealed class FakePlaybackHostProcess : ISpotifyPlaybackHostProcess
{
    internal ChannelTextWriter InputChannel { get; } = new();
    internal ChannelTextReader OutputChannel { get; } = new();
    private ChannelTextReader ErrorChannel { get; } = new();

    public TextWriter Input => InputChannel;
    public TextReader Output => OutputChannel;
    public TextReader Error => ErrorChannel;
    public bool HasExited { get; private set; }

    public void Terminate()
    {
        HasExited = true;
        OutputChannel.Complete();
        ErrorChannel.Complete();
    }

    public ValueTask DisposeAsync()
    {
        Terminate();
        InputChannel.Complete();
        return ValueTask.CompletedTask;
    }
}

internal sealed class ChannelTextWriter : TextWriter
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
    public override Encoding Encoding => Encoding.UTF8;

    public override Task WriteLineAsync(
        ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        _lines.Writer.WriteAsync(buffer.ToString(), cancellationToken).AsTask();

    internal ValueTask<string> ReadLineAsync(CancellationToken cancellationToken) =>
        _lines.Reader.ReadAsync(cancellationToken);

    internal void Complete() => _lines.Writer.TryComplete();
}

internal sealed class ChannelTextReader : TextReader
{
    private readonly Channel<char> _characters = Channel.CreateUnbounded<char>();

    internal void WriteLine(string value)
    {
        foreach (var character in value) _characters.Writer.TryWrite(character);
        _characters.Writer.TryWrite('\n');
    }

    internal void Complete() => _characters.Writer.TryComplete();

    public override async ValueTask<int> ReadAsync(
        Memory<char> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        char first;
        try { first = await _characters.Reader.ReadAsync(cancellationToken); }
        catch (ChannelClosedException) { return 0; }
        buffer.Span[0] = first;
        var count = 1;
        while (count < buffer.Length && _characters.Reader.TryRead(out var next))
            buffer.Span[count++] = next;
        return count;
    }
}

internal sealed class ProfileTestRoot : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WidgetRail.SpotifyCleanupTest." + Guid.NewGuid().ToString("N"));
    internal ProfileTestRoot() => Directory.CreateDirectory(Path);
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
}

internal static class Assert
{
    internal static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed.");
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected <{expected}>; actual <{actual}>.");
    }

    internal static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Sequences differ.");
    }
}
