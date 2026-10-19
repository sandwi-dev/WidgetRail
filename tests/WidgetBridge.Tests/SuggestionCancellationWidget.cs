using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal sealed class SuggestionCancellationWidget : Widget
{
    private int pending;
    public override WidgetView Render() => new(UI.Stack("root",
        UI.TextEntry("", "Search", "search", "entry", 96).Suggest("suggestions"),
        UI.Text(Volatile.Read(ref pending).ToString(), "pending")), ActiveInputScopeId: "root");

    public override async ValueTask<TextSuggestionResult> OnSuggestTextAsync(TextSuggestionQuery query, CancellationToken cancellationToken)
    {
        if (query.Text == "delayed")
        {
            Interlocked.Increment(ref pending);
            Invalidate();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        return new([new("first", "First suggestion", "First suggestion")]);
    }
}
