using System.Text;
using System.Threading.Channels;
using WidgetRail.SpotifyPlayback;
using WidgetRail.WindowsSpotifyProvider;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Process launch is redirected and parent bounded", PlaybackHostStartInfo),
    ("Client correlates commands without leaking tokens", PlaybackHostClientProtocol),
    ("Startup SDK errors fail immediately with their exact code", StartupSdkError),
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

static Task PlaybackHostStartInfo()
{
    var executable = Path.GetFullPath("SpotifyPlaybackHost.exe");
    var start = SpotifyPlaybackHostProcessFactory.CreateStartInfo(executable, 321);
    Assert.Equal(executable, start.FileName);
    Assert.True(!start.UseShellExecute && start.CreateNoWindow && !start.ErrorDialog);
    Assert.True(start.RedirectStandardInput && start.RedirectStandardOutput &&
        start.RedirectStandardError);
    Assert.SequenceEqual(["--parent-pid", "321"], start.ArgumentList);
    return Task.CompletedTask;
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
            accessToken, DateTimeOffset.UtcNow.AddMinutes(5), ["streaming"]),
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
