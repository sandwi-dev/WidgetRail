using System.Globalization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetTextEntryDialog
{
    private static readonly Lazy<Task<WordCompletion>> EnglishWords=new(()=>Task.Run(()=>
    {
        using var stream=typeof(WidgetTextEntryDialog).Assembly.GetManifestResourceStream("WidgetRail.EnglishWords")
            ??throw new IOException("Completion vocabulary is unavailable.");
        using var reader=new StreamReader(stream);
        var words=new List<string>();
        while(words.Count<10000&&reader.ReadLine() is {} line)words.Add(line);
        return new WordCompletion(words);
    }));
    private bool wordCompletion;
    private WordCompletionContext? completionContext;

    private void EnableWordCompletion(ViewNode node)
    {
        if(node.TextEntryInputKind!=TextEntryInputKind.Prose||node.TextEntrySuggestions is not null||IsLive||protectedInput||text is null||
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName!="en")return;
        wordCompletion=true;
        completionContext=WordCompletionContext.Capture(text.Text,text.SelectionStart,text.SelectionLength);
        EnableSuggestions(node with{TextEntrySuggestions=new("host.english-words",2,5)},async(query,token)=>
        {
            var dictionary=await EnglishWords.Value.WaitAsync(token).ConfigureAwait(false);
            return dictionary.Complete(query.Text,query.MaximumResults,token);
        });
        text.SelectionChanged+=(_,_)=>UpdateWordCompletion();
    }

    private void UpdateWordCompletion()
    {
        if(!wordCompletion||text is null||completed||acceptingSuggestion)return;
        var context=WordCompletionContext.Capture(text.Text,text.SelectionStart,text.SelectionLength);
        if(context==completionContext)return;
        completionContext=context;
        // Moving the caret can change the target without changing the prefix.
        suggestions?.Update(context?.Prefix??"",force:true);
    }
}
