using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

internal sealed record CurrencyRateSnapshot(string Realm,string League,DateTimeOffset FetchedAt,DateTimeOffset MarketHour,Dictionary<string,double> Rates)
{
    internal bool Valid(string realm,string league,DateTimeOffset now)=>Realm==realm&&League==league&&FetchedAt<=now.AddMinutes(1)&&
        MarketHour<=FetchedAt&&FetchedAt-MarketHour<TimeSpan.FromHours(3)&&Rates is{Count:>0 and <=1000}&&
        Rates.All(r=>r.Key.Length is >0 and <=100&&double.IsFinite(r.Value)&&r.Value>0);
}

internal static class PobCurrencyRates
{
    internal static async Task<CurrencyRateSnapshot> ReadAsync(int game,string realm,string league,string directory,PobEngine engine,string xml,CancellationToken token)
    {
        if(!PobGame.For(game).SupportsRealm(realm)||string.IsNullOrWhiteSpace(league)||league.Length>160)
            throw new IOException("The listing's game, realm or league is unavailable.");
        var now=DateTimeOffset.UtcNow;
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(realm+"\n"+league)));
        var path=Path.Combine(directory,"currency-"+key+".json");
        try
        {
            var file=new FileInfo(path);
            if(file.Exists&&file.Length<=128*1024&&JsonSerializer.Deserialize<CurrencyRateSnapshot>(File.ReadAllText(path)) is {} saved&&
                saved.Valid(realm,league,now)&&now-saved.FetchedAt<TimeSpan.FromMinutes(61))return saved;
        }
        catch(Exception error)when(error is IOException or JsonException or UnauthorizedAccessException or ArgumentException){}
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WidgetRail-PoB/0.1");
        var hour=DateTimeOffset.FromUnixTimeSeconds((now.ToUnixTimeSeconds()/3600-1)*3600);
        var staticTask=ReadJsonAsync(client,$"https://www.pathofexile.com{PobGame.For(game).TradeApiPath}data/static",4*1024*1024,token);
        var marketsTask=ReadJsonAsync(client,$"https://web.poecdn.com/api/currency-exchange{(realm=="pc"?"":"/"+realm)}/{hour.ToUnixTimeSeconds()}",32*1024*1024,token);
        try{await Task.WhenAll(staticTask,marketsTask);}
        catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new IOException("GGG currency data did not respond in time. Try price ordering again later.");}
        var markets=await marketsTask;var staticData=await staticTask;
        if(!markets.TryGetProperty("markets",out var all)||all.ValueKind!=JsonValueKind.Array)throw new IOException("GGG returned no currency markets. Try again later.");
        var filtered=all.EnumerateArray().Where(m=>m.TryGetProperty("league",out var l)&&l.GetString()==league).ToArray();
        var response=await engine.ExecuteAsync(new{op="currency-rates",realm,league,@static=staticData,markets=new{markets=filtered}},token,xml);
        var rates=JsonSerializer.Deserialize<Dictionary<string,double>>(response.State.GetProperty("rates"))!;
        var snapshot=new CurrencyRateSnapshot(realm,league,now,hour,rates);
        if(!snapshot.Valid(realm,league,now))throw new IOException("GGG returned invalid exchange rates for this league.");
        BuildDocument.SaveAtomic(path,JsonSerializer.Serialize(snapshot));
        return snapshot;
    }
    private static async Task<JsonElement> ReadJsonAsync(HttpClient client,string url,int maximum,CancellationToken token)
    {
        // ResponseHeadersRead finishes the HttpClient timeout at the headers.
        // Bound body reads too, so a stalled feed cannot hold ranking open.
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));token=deadline.Token;
        using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>maximum)throw new IOException("Currency response exceeds the size limit.");
        await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await input.ReadAsync(buffer,token))>0){if(output.Length+count>maximum)throw new IOException("Currency response exceeds the size limit.");output.Write(buffer,0,count);}
        using var json=JsonDocument.Parse(output.ToArray());return json.RootElement.Clone();
    }
}
