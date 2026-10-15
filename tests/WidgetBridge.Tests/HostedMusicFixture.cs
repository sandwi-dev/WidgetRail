using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class HostedMusicFixture
{
    internal static async Task<object> RunAsync(WidgetWebSessionService service,CancellationToken token,Action ready)
    {
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(token);
        var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
        var serving=ServeAsync(listener,stop.Token);
        var states=Channel.CreateUnbounded<JsonElement>();
        var checks=new List<string>();
        var receivedStates=new List<string>();
        try
        {
            var source=WebSessionSource.Package("web/player.html",
                [new(){Path="web/player.html",ContentType="text/html"},new(){Path="web/web-session.js",ContentType="text/javascript"}]) with {AllowAutoplay=true};
            await using var page=await service.OpenAsync(new("music","Synthetic music",source),token);
            var reading=ReadAsync();
            try
            {
                await page.RequestAsync<object,JsonElement>("ready",new{},token);
                ready();
                await Task.Delay(18000,token);
                var url=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/tone.wav";
                await page.AuthorizeLocalResourceAsync(new(url),token);
                var clock=Stopwatch.StartNew();
                await page.RequestAsync<object,JsonElement>("load",new {generation=1,trackId="fixture",title="Synthetic tone",artist="Fixture",volume=.05,url},token);
                var loadReplyMs=clock.Elapsed.TotalMilliseconds;
                await State(s=>s.GetProperty("playing").GetBoolean()&&s.GetProperty("position").GetDouble()>.5);
                var playingMs=clock.Elapsed.TotalMilliseconds;
                checks.Add("Playback begins after 18 seconds hidden idle without script execution or a gesture");
                await page.RequestAsync<object,JsonElement>("pause",new{},token);
                var paused=await State(s=>!s.GetProperty("playing").GetBoolean()&&!s.GetProperty("buffering").GetBoolean());
                var position=paused.GetProperty("position").GetDouble();
                await Task.Delay(2000,token);
                while(states.Reader.TryRead(out var prior)) ValidatePaused(prior);
                await page.RequestAsync<object,JsonElement>("volume",new{value=.05},token);
                var settled=await State(s=>true);
                ValidatePaused(settled);
                checks.Add("Pause remains paused with stable progress for two seconds");
                await page.RequestAsync<object,JsonElement>("seek",new{value=10},token);
                await State(s=>Math.Abs(s.GetProperty("position").GetDouble()-10)<.25&&!s.GetProperty("playing").GetBoolean());
                await page.RequestAsync<object,JsonElement>("play",new{},token);
                await State(s=>s.GetProperty("playing").GetBoolean()&&s.GetProperty("position").GetDouble()>10.5);
                checks.Add("Paused seek settles and resumed playback advances from the requested position");
                await page.RequestAsync<object,JsonElement>("stop",new{},token);
                return new{checks,loadReplyMs,playingMs};
                void ValidatePaused(JsonElement state)
                {
                    if(state.GetProperty("playing").GetBoolean()||Math.Abs(state.GetProperty("position").GetDouble()-position)>.25)
                        throw new InvalidOperationException("Playback resumed or progress moved after pause.");
                }
            }
            finally{stop.Cancel();await reading;}
            async Task ReadAsync()
            {
                try
                {
                    await foreach(var message in page.ReadMessagesAsync(stop.Token))
                    {
                        if(message.TryGetProperty("event",out var e)&&e.GetString()=="playback-failed")
                            throw new InvalidOperationException("Synthetic playback failed: "+message.GetProperty("reason").GetString());
                        if(message.TryGetProperty("state",out var state))
                        { if(receivedStates.Count<10)receivedStates.Add(state.GetRawText());await states.Writer.WriteAsync(state.Clone(),stop.Token); }
                    }
                }
                catch(OperationCanceledException)when(stop.IsCancellationRequested){}
                catch(Exception error){states.Writer.TryComplete(error);}
            }
            async Task<JsonElement> State(Func<JsonElement,bool> accepts)
            {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(10000);
                try { while(true){var state=await states.Reader.ReadAsync(deadline.Token);if(accepts(state))return state;} }
                catch(OperationCanceledException)when(!token.IsCancellationRequested)
                {throw new TimeoutException("Synthetic playback state deadline. States="+string.Join(";",receivedStates)+" server="+serving.Status);}
            }
        }
        finally{stop.Cancel();listener.Stop();await serving;}
    }
    private static async Task ServeAsync(TcpListener listener,CancellationToken token)
    {
        var data=new byte[44+30*16000];
        using(var writer=new BinaryWriter(new MemoryStream(data)))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(data.Length-8);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(8000);writer.Write(16000);
            writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(data.Length-44);
            for(var i=0;i<30*8000;i++)writer.Write((short)(Math.Sin(i*2*Math.PI*440/8000)*3000));
        }
        try
        {
            while(!token.IsCancellationRequested)
            {
                using var client=await listener.AcceptTcpClientAsync(token);using var stream=client.GetStream();
                using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                var start=0;var end=data.Length-1;var ranged=false;
                while(await reader.ReadLineAsync(token) is {Length:>0} line)
                {
                    if(!line.StartsWith("Range: bytes=",StringComparison.OrdinalIgnoreCase))continue;
                    var parts=line[13..].Split('-');
                    if(int.TryParse(parts[0],out start)){ranged=true;if(parts.Length>1&&int.TryParse(parts[1],out var requestedEnd))end=Math.Min(end,requestedEnd);}
                }
                if(start<0||start>end)throw new InvalidOperationException("Invalid synthetic stream range.");
                await Task.Delay(100,token);
                var headers=$"HTTP/1.1 {(ranged?"206 Partial Content":"200 OK")}\r\nContent-Type: audio/wav\r\nAccept-Ranges: bytes\r\nContent-Length: {end-start+1}\r\n"+
                    (ranged?$"Content-Range: bytes {start}-{end}/{data.Length}\r\n":"")+"Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers),token);
                await stream.WriteAsync(data.AsMemory(start,end-start+1),token);
            }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(SocketException)when(token.IsCancellationRequested){}
        catch(IOException)when(token.IsCancellationRequested){}
    }
}
