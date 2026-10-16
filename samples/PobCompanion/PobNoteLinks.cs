using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

internal sealed record PobNoteLink(string Url,string Label,WidgetIntentRequest Intent);

internal static class PobNoteLinks
{
    private static readonly TimeSpan Timeout=TimeSpan.FromMilliseconds(100);
    private static readonly Regex Links=new("https?://[^\\s<>\"\\[\\]]+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,Timeout);
    private static readonly Regex Titles=new("\\[(?<title>[^\\]\\r\\n]{1,200})\\]\\(\\s*(?<url>https?://[^\\s<>\"\\[\\]]+)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,Timeout);
    private static readonly Regex Time=new("^(?:(?<h>[0-9]{1,2})h)?(?:(?<m>[0-9]{1,4})m)?(?:(?<s>[0-9]{1,5})s)?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,Timeout);

    internal static IReadOnlyList<PobNoteLink> Read(string notes)
    {
        var titles=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(Match match in Titles.Matches(notes))titles.TryAdd(Trim(match.Groups["url"].Value),Clean(match.Groups["title"].Value));
        var result=new List<PobNoteLink>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(Match match in Links.Matches(notes))
        {
            var url=Trim(match.Value);
            if(url.Length>2048||!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.UserInfo.Length>0||!seen.Add(url))continue;
            var web=WidgetIntentRequest.Create(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url}));
            var video=TryVideo(uri,out var id,out var seconds);
            var destination=video ? "youtu.be/"+id+(seconds.HasValue?" · "+seconds.Value.ToString("0.###",CultureInfo.InvariantCulture)+"s":"")
                : Clean(Uri.UnescapeDataString(uri.Authority+uri.PathAndQuery)).TrimEnd('/');
            var caption=titles.GetValueOrDefault(url) is {Length:>0} title?title:destination;
            var label=(video?"Watch video · ":"Open link · ")+Limit(caption,160);
            var intent=video?WidgetIntentRequest.Create(WidgetIntentContracts.Video,seconds is {} start
                ? JsonSerializer.SerializeToElement(new{provider="youtube",videoId=id,startTimeSeconds=start})
                : JsonSerializer.SerializeToElement(new{provider="youtube",videoId=id})) with {ReportsResult=true,Fallback=web}:web;
            // Repeated authored titles or truncated paths must remain distinguishable.
            if(result.Any(link=>link.Label==label))label+=" · "+(result.Count+1).ToString(CultureInfo.InvariantCulture);
            result.Add(new(url,label,intent));
            if(result.Count==12)break;
        }
        return result;
    }

    internal static bool UseFallback(WidgetIntentFeedback feedback)=>
        feedback.Request.ContractId==WidgetIntentContracts.OpenVideo &&
        feedback.Request.Fallback?.ContractId==WidgetIntentContracts.OpenWebPage &&
        feedback.Status is WidgetIntentStatus.Unavailable or WidgetIntentStatus.Rejected;

    private static bool TryVideo(Uri uri,out string id,out double? seconds)
    {
        id="";seconds=null;
        if(!uri.IsDefaultPort)return false;
        var host=uri.IdnHost.ToLowerInvariant();
        if(host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be" or "www.youtu.be"))return false;
        var query=uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(part=>part.Split('=',2))
            .GroupBy(part=>Uri.UnescapeDataString(part[0]),StringComparer.Ordinal)
            .ToDictionary(group=>group.Key,group=>group.Select(part=>part.Length==2?Uri.UnescapeDataString(part[1]):"").Distinct().ToArray(),StringComparer.Ordinal);
        if(new[]{"v","t","start","time_continue"}.Any(key=>query.TryGetValue(key,out var values)&&values.Length!=1))return false;
        string? Value(string key)=>query.TryGetValue(key,out var values)?values[0]:null;
        var path=uri.AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);
        var parsed=host is "youtu.be" or "www.youtu.be" ? path.Length==1?path[0]:null
            : path.Length==1&&path[0]=="watch"?Value("v")
            : path.Length==2&&path[0] is "shorts" or "live" or "embed"?Uri.UnescapeDataString(path[1]):null;
        if(parsed is not {Length:11}||!parsed.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_'))return false;
        id=parsed;
        var time=Value("t")??Value("start")??Value("time_continue")??(uri.Fragment.StartsWith("#t=",StringComparison.Ordinal)?Uri.UnescapeDataString(uri.Fragment[3..]):null);
        if(time is null)return true;
        if(time.Length is 0 or >32)return false;
        if(!double.TryParse(time,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number))
        {
            var match=Time.Match(time);if(!match.Success)return false;
            static int Part(Group group)=>group.Success?int.Parse(group.Value,CultureInfo.InvariantCulture):0;
            number=Part(match.Groups["h"])*3600+Part(match.Groups["m"])*60+Part(match.Groups["s"]);
        }
        // Match the YouTube receiver's accepted seek range, not just the generic schema.
        if(!double.IsFinite(number)||number is <0 or >86400)return false;
        seconds=number;return true;
    }

    private static string Trim(string url)
    {
        url=url.TrimEnd('.',',',';');
        while(url.EndsWith(')')&&url.Count(c=>c==')')>url.Count(c=>c=='('))url=url[..^1];
        while(url.EndsWith('}')&&url.Count(c=>c=='}')>url.Count(c=>c=='{'))url=url[..^1];
        return url;
    }
    private static string Clean(string value)=>Regex.Replace(value,"\\s+"," ",RegexOptions.None,Timeout).Trim();
    private static string Limit(string value,int length)=>value.Length<=length?value:value[..(char.IsHighSurrogate(value[length-1])?length-1:length)]+"…";
}
