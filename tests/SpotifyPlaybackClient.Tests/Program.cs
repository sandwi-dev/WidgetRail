using System.Text.Json;
using System.Threading.Channels;
using WidgetRail.SpotifyPlayback;
using WidgetRail.WindowsSpotifyProvider;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Hosted playback uses packaged resources and typed request correlation", Playback),
    ("Startup errors retain the safe provider code", StartupFailure),
    ("Canceled request replies cannot corrupt later playback", CanceledReply),
};
var failed = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static async Task Playback()
{
    var host = new FakeWebHost();
    await using var client = new SpotifyWebSessionClient(host.Service);
    var tokenRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.EventReceived += (_, message) => { if (message.Type == "token_requested") tokenRequest.TrySetResult(); };
    await client.StartAsync(default);
    Check(client.IsRunning && host.Definition?.Source.Kind == WebSessionSourceKind.Package, "Playback did not use a packaged web session.");
    Check(host.Definition!.Source.AllowAutoplay && host.Definition.Browser.Storage == BrowserStorage.Private, "Playback policy changed.");
    var response = await client.SendAsync("pause", null, default);
    Check(response.Type == "command_completed", "Command response was not correlated.");
    host.Publish(new { version = 1, type = "token_requested", requestId = (string?)null, payload = new { tokenRequestId = "token-1" } });
    await tokenRequest.Task.WaitAsync(TimeSpan.FromSeconds(3));
    await client.ProvideTokenAsync("token-1", new("synthetic-token", DateTimeOffset.UtcNow.AddMinutes(5), SpotifyPlaybackProtocol.RequiredScopes), default);
    Check(host.TokenMessages == 1, "Token was not delivered through the private message channel.");
    try { await client.ProvideTokenAsync("token-1", new("synthetic-token", DateTimeOffset.UtcNow.AddMinutes(5), SpotifyPlaybackProtocol.RequiredScopes), default); throw new InvalidOperationException("Token replay was accepted."); }
    catch (SpotifyPlaybackHostClientException error) when (error.Code == "unknown_token_request") { }
    await client.DisposeAsync();
    Check(host.Closed && !client.IsRunning, "Disposal did not close the host session.");
}
static async Task StartupFailure()
{
    var host = new FakeWebHost { StartupError = "authentication_error" };
    await using var client = new SpotifyWebSessionClient(host.Service);
    try { await client.StartAsync(default); throw new InvalidOperationException("Invalid startup succeeded."); }
    catch (SpotifyPlaybackHostClientException error) when (error.Code == "authentication_error") { }
}
static async Task CanceledReply()
{
    var host = new FakeWebHost { HoldReply = true };
    await using var client = new SpotifyWebSessionClient(host.Service);
    await client.StartAsync(default);
    using var canceled = new CancellationTokenSource();
    var pending = client.SendAsync("pause", null, canceled.Token);
    await host.RequestSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
    canceled.Cancel();
    try { await pending; throw new InvalidOperationException("Request did not cancel."); }
    catch (OperationCanceledException) { }
    host.HoldReply = false;
    host.ReleaseReply();
    Check((await client.SendAsync("resume", null, default)).Type == "command_completed" && client.IsRunning,
        "A late canceled reply corrupted the session.");
}
static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

sealed class FakeWebHost
{
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>();
    private readonly WebSessionReference reference = new(new string('a', 32));
    private object? held;
    internal bool HoldReply;
    internal bool Closed;
    internal int TokenMessages;
    internal string? StartupError;
    internal WebSessionDefinition? Definition;
    internal TaskCompletionSource RequestSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal WidgetWebSessionService Service => new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetWebSessionCapabilities.Command, Handle).Build().WebSessions;
    internal void Publish(object value) => queue.Writer.TryWrite(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)));
    internal void ReleaseReply() { if (held is { } value) Publish(value); held = null; }
    private async ValueTask<WebSessionReply> Handle(WebSessionCommand command, CancellationToken token)
    {
        switch (command.Operation)
        {
            case WebSessionOperation.Open:
                Definition = command.Definition;
                if (StartupError is null) Publish(new { version = 1, type = "sdk_loaded", requestId = (string?)null, payload = new { } });
                else Publish(new { version = 1, type = "sdk_error", requestId = (string?)null, payload = new { code = StartupError, message = "Synthetic failure" } });
                return new() { Session = reference };
            case WebSessionOperation.ReadMessages:
                return new() { MessagesBase64 = [await queue.Reader.ReadAsync(token)] };
            case WebSessionOperation.PostMessage:
                using (var document = JsonDocument.Parse(WebSessionContract.DecodeMessage(command.MessageBase64!)))
                {
                    var request = document.RootElement;
                    if (request.GetProperty("kind").GetString() == "cancel") return new();
                    if (request.GetProperty("method").GetString() == "provide_token") TokenMessages++;
                    object response = new { widgetRail = 1, kind = "response", id = request.GetProperty("id").GetString(),
                        payload = new { version = 1, type = "command_completed", requestId = "synthetic", payload = new { } } };
                    if (HoldReply) held = response; else Publish(response);
                    RequestSeen.TrySetResult();
                }
                return new();
            case WebSessionOperation.Close: Closed = true; return new();
            default: throw new InvalidOperationException("Unexpected test operation.");
        }
    }
}
