using System.Text;

namespace WidgetRail.Samples.DiscordWebView.Domain;

[Flags]
internal enum DiscordTextStyle { Plain=0, Bold=1, Italic=2, Underline=4, Strike=8, Code=16, Spoiler=32, Quote=64 }
internal sealed record DiscordTextPart(string Text,DiscordTextStyle Style=DiscordTextStyle.Plain,string? Url=null,string? EmojiId=null);

/// <summary>Bounded Discord display parsing. Produces native presentation data, never HTML or executable links.</summary>
internal static class DiscordMessageText
{
    private static readonly (string Marker,DiscordTextStyle Flag)[] Markers =
        [("***",DiscordTextStyle.Bold|DiscordTextStyle.Italic),("___",DiscordTextStyle.Underline|DiscordTextStyle.Italic),
            ("**",DiscordTextStyle.Bold),("__",DiscordTextStyle.Underline),("~~",DiscordTextStyle.Strike),("*",DiscordTextStyle.Italic),("_",DiscordTextStyle.Italic)];
    internal static IReadOnlyList<DiscordTextPart> Parse(string content,IReadOnlyList<DiscordMessageMention>? mentions=null,bool revealSpoilers=false,bool contentTruncated=false)
    {
        if(content.Length>4000)throw new ArgumentOutOfRangeException(nameof(content));
        var parts=new List<DiscordTextPart>();
        void Add(string text,DiscordTextStyle style,string? url=null,string? emoji=null)
        {
            if(text.Length==0)return;
            if(parts.Count>0 && parts[^1] is {Url:null,EmojiId:null} previous && url is null && emoji is null && previous.Style==style)
                parts[^1]=previous with{Text=previous.Text+text};
            else parts.Add(new(text,style,url,emoji));
        }
        void Read(string value,DiscordTextStyle style,int depth)
        {
            var plain=new StringBuilder();
            void Flush(){Add(plain.ToString(),style);plain.Clear();}
            for(var i=0;i<value.Length;)
            {
                if(parts.Count>=120 || depth>4){Flush();Add(revealSpoilers?value[i..]:HideSpoilers(value[i..],contentTruncated),style);return;}
                if(value[i]=='\\' && i+1<value.Length && "\\*_~|`[]<>".Contains(value[i+1])){plain.Append(value[i+1]);i+=2;continue;}
                if(value[i]=='`')
                {
                    var delimiter=value.AsSpan(i).StartsWith("```",StringComparison.Ordinal)?"```":"`";
                    var close=value.IndexOf(delimiter,i+delimiter.Length,StringComparison.Ordinal);
                    if(close>=0)
                    {
                        Flush();var code=value[(i+delimiter.Length)..close];
                        if(delimiter.Length==3 && code.IndexOf('\n') is var newline && newline>=0 && newline<=32 &&
                            code[..newline].All(c=>char.IsAsciiLetterOrDigit(c)||c is '+' or '-' or '#' or '\r'))code=code[(newline+1)..];
                        Add(code,style|DiscordTextStyle.Code);i=close+delimiter.Length;continue;
                    }
                }
                if(value.AsSpan(i).StartsWith("||",StringComparison.Ordinal))
                {
                    var close=value.IndexOf("||",i+2,StringComparison.Ordinal);
                    if(close>=0){Flush();if(revealSpoilers)Read(value[(i+2)..close],style,depth+1);else Add("[Spoiler]",style|DiscordTextStyle.Spoiler);i=close+2;continue;}
                    if(contentTruncated && !revealSpoilers){Flush();Add("[Spoiler]",style|DiscordTextStyle.Spoiler);return;}
                }
                if(value[i]=='<' && value.IndexOf('>',i+1) is var bracket && bracket>i && bracket-i<=160)
                {
                    var token=value[(i+1)..bracket];var bits=token.Split(':');
                    if(bits.Length==3 && bits[0] is "" or "a" && bits[1].Length is >0 and <=32 && bits[1].All(c=>char.IsAsciiLetterOrDigit(c)||c=='_') && DiscordMessageValues.Id(bits[2]))
                    {Flush();Add(":"+bits[1]+":",style,emoji:bits[2]);i=bracket+1;continue;}
                    if(token.StartsWith('@') && DiscordMessageValues.Id(token.TrimStart('@','!','&')))
                    {
                        Flush();var key=token.TrimStart('@','!','&');
                        Add("@"+(mentions?.FirstOrDefault(mention=>mention.Id==key)?.Name ?? (token.StartsWith("@&",StringComparison.Ordinal)?"role":"user")),style|DiscordTextStyle.Bold);
                        i=bracket+1;continue;
                    }
                    if(WebUrl(token)){Flush();Add(token,style,token);i=bracket+1;continue;}
                }
                if(value[i]=='[' && value.IndexOf("](",i+1,StringComparison.Ordinal) is var middle && middle>i && middle-i<256 &&
                    value.IndexOf(')',middle+2) is var linkEnd && linkEnd>middle+2 && WebUrl(value[(middle+2)..linkEnd]))
                {Flush();Add(value[(i+1)..middle],style,value[(middle+2)..linkEnd]);i=linkEnd+1;continue;}
                if(value.AsSpan(i).StartsWith("https://",StringComparison.OrdinalIgnoreCase) || value.AsSpan(i).StartsWith("http://",StringComparison.OrdinalIgnoreCase))
                {
                    var end=i;while(end<value.Length && !char.IsWhiteSpace(value[end]) && value[end] is not ('<' or '>'))end++;
                    var url=value[i..end].TrimEnd('.',',','!',';',':');
                    while(url.EndsWith(')') && url.Count(c=>c==')')>url.Count(c=>c=='('))url=url[..^1];
                    if(WebUrl(url)){Flush();Add(url,style,url);i+=url.Length;continue;}
                }
                if((i==0 || value[i-1]=='\n') && value[i]=='>' && i+1<value.Length && value[i+1]==' ')
                {Flush();var end=value.IndexOf('\n',i+2);if(end<0)end=value.Length;Add("│ ",style|DiscordTextStyle.Quote);Read(value[(i+2)..end],style|DiscordTextStyle.Quote,depth+1);i=end;continue;}
                var formatted=false;
                foreach(var (marker,flag) in Markers)
                {
                    if(!value.AsSpan(i).StartsWith(marker,StringComparison.Ordinal))continue;
                    if(marker=="_" && i>0 && char.IsLetterOrDigit(value[i-1]))continue;
                    var close=value.IndexOf(marker,i+marker.Length,StringComparison.Ordinal);
                    // In **bold *italic*** the first closing star belongs to
                    // the nested span; the final two close the outer bold run.
                    if(marker.Length==2&&marker[0] is '*' or '_'&&close>=0&&close+2<value.Length&&value[close+2]==marker[0])close++;
                    if(close<=i+marker.Length)continue;
                    Flush();Read(value[(i+marker.Length)..close],style|flag,depth+1);i=close+marker.Length;formatted=true;break;
                }
                if(!formatted)plain.Append(value[i++]);
            }
            Flush();
        }
        Read(content,DiscordTextStyle.Plain,0);
        return parts;
    }
    internal static bool WebUrl(string text)=>text.Length<=2048 && Uri.TryCreate(text,UriKind.Absolute,out var uri) &&
        uri.Scheme is "https" or "http" && uri.UserInfo.Length==0 && !text.Any(char.IsControl);
    private static string HideSpoilers(string value,bool truncated)
    {
        var result=new StringBuilder();var start=0;
        while(value.IndexOf("||",start,StringComparison.Ordinal) is var open && open>=0 &&
            value.IndexOf("||",open+2,StringComparison.Ordinal) is var close && close>=0)
        {result.Append(value.AsSpan(start,open-start));result.Append("[Spoiler]");start=close+2;}
        if(truncated && value.IndexOf("||",start,StringComparison.Ordinal) is var remainder && remainder>=0)
            return result.Append(value.AsSpan(start,remainder-start)).Append("[Spoiler]").ToString();
        return result.Append(value.AsSpan(start)).ToString();
    }
}
