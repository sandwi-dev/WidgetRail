using System.Diagnostics;
using System.Text.Json;
using WidgetRail.SpotifyPlayback;
using WidgetRail.WidgetSdk;
using WidgetRail.WindowsSpotifyProvider;

internal static class HostedSpotifyFixture
{
    internal static bool Enabled => File.Exists(Path.Combine(AppContext.BaseDirectory,"spotify-session-fixture.marker"));
    internal static string Page()
    {
        var html=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"SpotifySessionFixture.html"));
        const string replacement="""
            window.Spotify={Player:class {
                constructor(options){this.options=options;this.listeners={};this.volume=options.volume;this.paused=true;this.position=0;}
                addListener(name,callback){this.listeners[name]=callback;}
                connect(){this.listeners.ready?.({device_id:'synthetic-device'});return Promise.resolve(true);}
                snapshot(){return {paused:this.paused,position:this.position,duration:30000,repeat_mode:0,shuffle:false,disallows:{}};}
                changed(){this.listeners.player_state_changed?.(this.snapshot());return Promise.resolve();}
                getCurrentState(){return Promise.resolve(this.snapshot());}
                getVolume(){return Promise.resolve(this.volume);}
                setVolume(value){this.volume=value;return Promise.resolve();}
                pause(){this.paused=true;return this.changed();}
                resume(){this.paused=false;return this.changed();}
                seek(value){this.position=value;return this.changed();}
                activateElement(){return Promise.resolve();}
                disconnect(){}
            }};
            window.onSpotifyWebPlaybackSDKReady();
            """;
        const string original="document.head.appendChild(sdkScript);";
        if(!html.Contains(original,StringComparison.Ordinal))throw new InvalidOperationException("Spotify fixture injection point changed.");
        return html.Replace(original,replacement,StringComparison.Ordinal);
    }
    internal static async Task<object> RunAsync(WidgetWebSessionService service,CancellationToken token)
    {
        await using var client=new SpotifyWebSessionClient(service);
        var received=0;
        client.EventReceived+=(_,e)=>{if(e.Type=="player_state_changed")Interlocked.Increment(ref received);};
        await client.StartAsync(token);
        await client.ConnectAsync(new("Synthetic device",.2,new(true,SpotifyPlaybackProtocol.RequiredScopes)),token);
        await Task.Delay(18000,token);
        var samples=new List<double>();
        for(var i=0;i<100;i++)
        {
            var paused=i%2==0;var started=Stopwatch.GetTimestamp();
            await client.SendAsync(paused?"pause":"resume",new{},token);
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            await client.SendAsync("seek",new{positionMilliseconds=i*100},token);
            var response=await client.SendAsync("get_current_state",new{},token);
            var state=SpotifyPlaybackProtocolCodec.DecodePayload<SpotifyLocalPlaybackState>(response.Payload);
            if(state.Paused!=paused||state.PositionMilliseconds!=i*100)throw new InvalidOperationException("Spotify command did not reach the synthetic SDK or state was lost.");
            await Task.Delay(20,token);
        }
        samples.Sort();
        if(received!=200)throw new InvalidOperationException($"Expected 200 playback state events; received {received}.");
        return new{checks=new[]{"Actual Spotify client and page deliver 100 alternating pause/resume commands and 100 seeks with matching state replies", "200 unsolicited playback events arrive through the production path"},count=samples.Count,p50=samples[49],p95=samples[94],p99=samples[98],max=samples[^1],over50=samples.Count(v=>v>50),received};
    }
}
