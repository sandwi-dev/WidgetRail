using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string? savedTradeUrl;
    internal static string? SafeTradeReturnUrl(string? url,int game)
    {
        if(url is not{Length:>0 and <=48000}||!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.UserInfo.Length>0||
            uri.GetLeftPart(UriPartial.Authority)!="https://www.pathofexile.com"||!uri.AbsolutePath.StartsWith(PobGame.For(game).TradeSearchPath,StringComparison.Ordinal))return null;
        // Retain only the public search query. Never store sign-in redirects or action tokens.
        var query=uri.Query.TrimStart('?').Split('&').FirstOrDefault(p=>p.StartsWith("q=",StringComparison.Ordinal));
        var marker=System.Text.RegularExpressions.Regex.IsMatch(uri.Fragment,@"^#widgetrail-weighted=[0-9]{1,2}$")?uri.Fragment:"";
        return uri.GetLeftPart(UriPartial.Path)+(query is null?"":"?"+query)+marker;
    }
    private void SaveTradeReturn(string url)
    {
        var safe=SafeTradeReturnUrl(url,game);if(safe is null||documentId is null||safe==savedTradeUrl)return;
        BuildDocument.SaveAtomic(Path.Combine(dataDirectory,documentId+".trade-url.txt"),safe);savedTradeUrl=safe;
    }
    private void RestoreTradeReturn(string id)
    {
        savedTradeUrl=null;
        try{var file=new FileInfo(Path.Combine(dataDirectory,id+".trade-url.txt"));if(file.Exists&&file.Length<=192000)savedTradeUrl=SafeTradeReturnUrl(File.ReadAllText(file.FullName),game);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){ }
        savedTradeUrl??=SafeTradeReturnUrl(tradeListing?.Url,game);
    }
}
