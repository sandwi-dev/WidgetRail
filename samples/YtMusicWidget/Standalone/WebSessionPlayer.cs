using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

internal sealed class WebSessionPlayer : IMusicProcess
{
    private readonly WidgetWebSession session;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task receiving;
    public event Action<JsonElement>? Event;
    private WebSessionPlayer(WidgetWebSession session) { this.session = session; receiving = ReceiveAsync(); }
    internal static async Task<IMusicProcess> OpenAsync(WidgetWebSessionService service, CancellationToken token)
    {
        var source = WebSessionSource.Package("web/player.html",
            [new() { Path = "web/player.html", ContentType = "text/html" },
             new() { Path = "web/web-session.js", ContentType = "text/javascript" }]) with { AllowAutoplay = true };
        return new WebSessionPlayer(await service.OpenAsync(new("playback", "YouTube Music playback", source)
            { Browser = new() { Chrome = BrowserChrome.OnDemand, Background = BrowserBackgroundMode.KeepAlive } }, token).ConfigureAwait(false));
    }
    public async Task<JsonElement> CallAsync(string method, object? args, CancellationToken token)
    {
        try
        {
            if (method == "load")
            {
                var value = JsonSerializer.SerializeToElement(args, JsonProcess.Json);
                await session.AuthorizeLocalResourceAsync(new(value.GetProperty("url").GetString()!), token).ConfigureAwait(false);
            }
            return await session.RequestAsync<object?, JsonElement>(method, args, token).ConfigureAwait(false);
        }
        catch (WidgetCapabilityException error) { throw new IOException("The music playback session is unavailable.", error); }
    }
    private async Task ReceiveAsync()
    {
        try { await foreach (var message in session.ReadMessagesAsync(lifetime.Token).ConfigureAwait(false)) Event?.Invoke(message); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!lifetime.IsCancellationRequested) Event?.Invoke(JsonSerializer.SerializeToElement(new { @event = "failed" })); }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        await receiving.ConfigureAwait(false);
        await session.DisposeAsync().ConfigureAwait(false);
        lifetime.Dispose();
    }
}
