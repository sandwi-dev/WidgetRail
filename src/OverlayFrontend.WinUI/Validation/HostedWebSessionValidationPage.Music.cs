using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class HostedWebSessionValidationPage
{
    private async Task VerifyMusicAdaptersAsync()
    {
        var runtimeScript = Resource("Runtime.js");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var serving = ServeWaveAsync(listener, stop.Token);
        var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/fixture.wav";
        try
        {
            await using var youtube = MusicSurface("youtube", Resource("YouTube.html"), runtimeScript);
            parking.Children.Add(youtube);
            try
            {
                await youtube.StartHostedSessionAsync(lifetime.Token);
                await Message(youtube, m => m.TryGetProperty("event", out var e) && e.GetString() == "ready");
                youtube.AuthorizeHostedResource(url);
                Send(youtube, "load", new { generation=1,trackId="fixture",title="Synthetic silence",artist="Fixture",volume=.1,url });
                await Message(youtube, m => m.TryGetProperty("state", out var state) && state.GetProperty("playing").GetBoolean() && state.GetProperty("duration").GetDouble()>0);
                Check(true, "The actual YouTube Music page plays an exactly authorized loopback stream while hidden");
                Send(youtube, "pause", new {});
                await Message(youtube, m => m.TryGetProperty("state", out var state) && !state.GetProperty("playing").GetBoolean());
                Check(true, "The actual YouTube Music page reports pause through the shared messaging runtime");
            }
            finally { parking.Children.Remove(youtube); }
        }
        finally { stop.Cancel(); listener.Stop(); await serving; }

        // Replace only the provider script URL in this fixture copy. The actual
        // package adapter and shared messaging library otherwise run unchanged.
        var spotifyHtml = Resource("Spotify.html").Replace("sdkScript.src = 'https://sdk.scdn.co/spotify-player.js';", "sdkScript.src = './fake-sdk.js';", StringComparison.Ordinal);
        Check(!spotifyHtml.Contains("sdkScript.src = 'https://sdk.scdn.co/spotify-player.js';", StringComparison.Ordinal), "Spotify fixture replaces the remote SDK without contacting an account");
        const string fakeSdk = """
            window.Spotify={Player:class {
                constructor(options){this.options=options;this.listeners={};this.volume=options.volume;}
                addListener(name,callback){this.listeners[name]=callback;}
                connect(){this.listeners.ready?.({device_id:'synthetic-device'});return Promise.resolve(true);}
                getVolume(){return Promise.resolve(this.volume);}
                setVolume(value){this.volume=value;return Promise.resolve();}
                pause(){return Promise.resolve();}
                disconnect(){}
            }};
            window.onSpotifyWebPlaybackSDKReady();
            """;
        await using var spotify = MusicSurface("spotify", spotifyHtml, runtimeScript, fakeSdk);
        parking.Children.Add(spotify);
        try
        {
            await spotify.StartHostedSessionAsync(lifetime.Token);
            await Message(spotify, m => m.TryGetProperty("type", out var e) && e.GetString()=="sdk_loaded");
            var connected = Send(spotify, "connect", new { deviceName="Synthetic device", initialVolume=.25 });
            var connection = await Message(spotify, m => m.TryGetProperty("id", out var id) && id.GetString()==connected);
            Check(connection.TryGetProperty("payload", out var outcome) && outcome.GetProperty("type").GetString()=="command_completed", "Synthetic Spotify connect reply: " + connection.GetRawText());
            var request = Send(spotify, "get_volume", new {});
            var reply = await Message(spotify, m => m.TryGetProperty("id",out var id) && id.GetString()==request);
            Check(reply.TryGetProperty("payload", out var envelope) && envelope.TryGetProperty("payload", out var payload) && payload.TryGetProperty("volume", out var volume) && volume.GetDouble()==.25,
                "The actual Spotify page connects and returns typed SDK responses with a synthetic provider: " + reply.GetRawText());
        }
        finally { parking.Children.Remove(spotify); }

        BrowserSurface MusicSurface(string name, string html, string script, string? fake = null)
        {
            var resources = new List<BridgeWebSessionResource> { Asset("web/player.html","text/html",html), Asset("web/web-session.js","text/javascript",script) };
            if (fake is not null) resources.Add(Asset("web/fake-sdk.js","text/javascript",fake));
            var source = WebSessionSource.Package("web/player.html", resources.Select(r=>new EmbeddedMediaResource{Path=r.Path,ContentType=r.ContentType}).ToArray()) with {AllowAutoplay=true};
            var definition = new WebSessionDefinition("music", "Synthetic music adapter", source);
            var surface = new BrowserSurface(name, new(name,1,"https://music-fixture.widgetrail.internal/web/player.html","Synthetic music adapter") {Options=definition.Browser, InteractionMode=BrowserInteractionMode.ActivateToInteract}, runtime.GetAsync);
            surface.ConfigureHostedSession(definition, resources);return surface;
        }
        string Send(BrowserSurface surface, string method, object payload)
        {
            var id=Guid.NewGuid().ToString("N");
            surface.PostHostedMessage(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new {widgetRail=1,kind="request",id,method,payload})));return id;
        }
        async Task<JsonElement> Message(BrowserSurface surface, Func<JsonElement,bool> accepts)
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(8));
            while(true)
            {
                using var json=JsonDocument.Parse(WebSessionContract.DecodeMessage(await surface.HostedMessages.ReadAsync(timeout.Token)));
                var value=json.RootElement;
                if(value.TryGetProperty("event",out var name) && name.GetString()=="playback-failed") throw new InvalidOperationException("Synthetic music playback failed: "+value.GetRawText());
                if(accepts(value))return value.Clone();
            }
        }
    }
    private static string Resource(string name)
    {
        using var stream=typeof(HostedWebSessionValidationPage).Assembly.GetManifestResourceStream("WidgetRail.SessionFixture."+name) ?? throw new InvalidOperationException("Missing fixture resource");
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    private static BridgeWebSessionResource Asset(string path,string type,string text)
    {
        var bytes=Encoding.UTF8.GetBytes(text);return new(path,type,Convert.ToHexString(SHA256.HashData(bytes)),Convert.ToBase64String(bytes));
    }
    private static async Task ServeWaveAsync(TcpListener listener,CancellationToken token)
    {
        var data = new byte[44+160000];
        using(var writer=new BinaryWriter(new MemoryStream(data)))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(data.Length-8);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(8000);writer.Write(16000);
            writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(data.Length-44);
        }
        try
        {
            while(!token.IsCancellationRequested)
            {
                using var client=await listener.AcceptTcpClientAsync(token);using var stream=client.GetStream();
                var header=new byte[8192];var count=0;
                while(count<header.Length)
                {
                    var read=await stream.ReadAsync(header.AsMemory(count),token);if(read==0)break;count+=read;
                    if(Encoding.ASCII.GetString(header,0,count).Contains("\r\n\r\n",StringComparison.Ordinal))break;
                }
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: audio/wav\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n"),token);
                await stream.WriteAsync(data,token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(SocketException) when(token.IsCancellationRequested){}
        catch(IOException) when(token.IsCancellationRequested){}
    }
}
