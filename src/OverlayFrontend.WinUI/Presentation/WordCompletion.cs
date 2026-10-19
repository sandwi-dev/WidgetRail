using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

// A bounded vocabulary lookup, not a spell checker or a learned language model.
// Words arrive in frequency order; grouping keeps each keypress lookup small.
internal sealed class WordCompletion
{
    private readonly IReadOnlyDictionary<string,string[]> prefixes;
    internal WordCompletion(IEnumerable<string> words) => prefixes=words.Take(10000)
        .Select(word=>word.Trim()).Where(word=>word.Length is >=2 and <=24&&word.All(char.IsAsciiLetterLower))
        .Distinct(StringComparer.Ordinal).GroupBy(word=>word[..2],StringComparer.Ordinal)
        .ToDictionary(group=>group.Key,group=>group.ToArray(),StringComparer.Ordinal);

    internal TextSuggestionResult Complete(string prefix,int maximum,CancellationToken cancellation)
    {
        if(prefix.Length is <2 or >24||!prefix.All(char.IsAsciiLetter))return TextSuggestionResult.Empty;
        var candidates=prefixes.GetValueOrDefault(prefix[..2].ToLowerInvariant())??[];
        var result=new List<TextSuggestion>();
        foreach(var word in candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            if(word.Length<=prefix.Length||!word.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))continue;
            var value=prefix.All(char.IsUpper)?word.ToUpperInvariant():char.IsUpper(prefix[0])?char.ToUpperInvariant(word[0])+word[1..]:word;
            result.Add(new("word."+word,value,value));
            if(result.Count==maximum)break;
        }
        return new(result);
    }
}

internal sealed record WordCompletionContext(string Text,int Start,int End,int Caret)
{
    internal string Prefix=>Text[Start..Caret];
    internal static WordCompletionContext? Capture(string text,int caret,int selection)
    {
        if(selection!=0||caret<0||caret>text.Length)return null;
        var start=caret;var end=caret;
        while(start>0&&caret-start<=24&&char.IsAsciiLetter(text[start-1]))start--;
        if(caret-start>24)return null;
        while(end<text.Length&&end-start<=24&&char.IsAsciiLetter(text[end]))end++;
        if(caret-start is <2 or >24||end-start>24)return null;
        static bool Joined(char value)=>char.IsLetterOrDigit(value)||value is '_' or '\'';
        if(start>0&&Joined(text[start-1])||end<text.Length&&Joined(text[end]))return null;
        return new(text,start,end,caret);
    }
    internal TextEntryEditing.Edit? Accept(string word,int maximum)
    {
        if(word.Length<=Prefix.Length||!word.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)||!word.All(char.IsAsciiLetter))return null;
        return TextEntryEditing.Insert(Text,Start,End-Start,word,maximum);
    }
}
