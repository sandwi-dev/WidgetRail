using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

internal static class PobTradeSearch
{
    internal static async Task<string[]> ReadLeaguesAsync(int game,CancellationToken token)
    {
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(15)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WidgetRail-PoB/0.1");
        using var response=await client.GetAsync($"https://www.pathofexile.com/api/trade{(game==2?"2":"")}/data/leagues",HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(token);
        using var buffer=new MemoryStream();var bytes=new byte[8192];int count;
        while((count=await stream.ReadAsync(bytes,token))!=0){if(buffer.Length+count>1024*1024)throw new IOException("League response is too large.");buffer.Write(bytes,0,count);}
        using var json=JsonDocument.Parse(buffer.ToArray());
        return json.RootElement.GetProperty("result").EnumerateArray().Select(l=>l.GetProperty("id").GetString()!)
            .Where(l=>l.Length is >0 and <=160&&!l.Any(char.IsControl)).Distinct(StringComparer.Ordinal).Take(128).ToArray();
    }
    internal static string SearchUrl(int game,string league,JsonElement query,bool weighted=false)
    {
        if(string.IsNullOrWhiteSpace(league)||league.Length>160||league.Any(char.IsControl))throw new IOException("Choose a league.");
        var root=$"https://www.pathofexile.com/trade{(game==2?"2":"")}/search/{Uri.EscapeDataString(league)}";
        var text=query.GetRawText();
        string url;
        if(game==2)
        {
            using var output=new MemoryStream();
            using(var zip=new GZipStream(output,CompressionLevel.Optimal,true))zip.Write(Encoding.UTF8.GetBytes(text));
            url=root+"/"+Convert.ToBase64String(output.ToArray()).Replace('+','-').Replace('/','_');
        }
        else url=root+"?q="+Uri.EscapeDataString(text);
        if(game==2&&weighted&&query.TryGetProperty("stats",out var stats))
        {
            var index=Array.FindIndex(stats.EnumerateArray().ToArray(),g=>g.TryGetProperty("type",out var type)&&type.GetString()=="weight");
            if(index<0)throw new IOException("PoB did not provide a weighted search group.");
            url+="#widgetrail-weighted="+index;
        }
        if(url.Length>8192)throw new IOException("This weighted search is too large to open. Choose a more specific priority.");
        return url;
    }
}
