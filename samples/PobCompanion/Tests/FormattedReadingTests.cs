using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class FormattedReadingTests
{
    internal static int Run()
    {
        var assertions=0;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);assertions++;}
        foreach(var separator in new[]{" ","\n\n"})
        {
            var text=string.Join(separator,Enumerable.Range(1,100).Select(i=>$"Paragraph {i}: keep covering these lines without splitting words across presentation chunks."));
            var rows=new List<WidgetElement>();
            PobWidget.AddFormatted(rows,JsonSerializer.SerializeToElement(new{text}),"text","runs","reading");
            var snapshot=new WidgetView(UI.Stack("root",rows.ToArray())).CreateSnapshot("reading",1);
            Check(ViewSnapshotValidator.Validate(snapshot).Count==0,"Bounded reading snapshot is invalid");
            var chunks=snapshot.Root.Children.Select(row=>string.Concat(row.Children.Select(span=>span.Text))).ToArray();
            Check(string.Concat(chunks)==text,"Reading chunks lost or rewrote text");
            Check(chunks.All(chunk=>chunk.Length<=2400),"Reading chunks exceed the protocol budget");
            Check(chunks.SkipLast(1).All(chunk=>char.IsWhiteSpace(chunk[^1])),"Reading chunk breaks a word despite an available natural boundary");
            if(separator.Contains('\n'))Check(chunks.SkipLast(1).All(chunk=>chunk.EndsWith('\n')),"Paragraph boundaries were not preferred");
        }
        var unicode=string.Concat(Enumerable.Repeat("😀",3000));
        var longRows=new List<WidgetElement>();
        PobWidget.AddFormatted(longRows,JsonSerializer.SerializeToElement(new{text=unicode}),"text","runs","unicode");
        var result=new WidgetView(UI.Stack("root",longRows.ToArray())).CreateSnapshot("reading",2);
        var parts=result.Root.Children.SelectMany(row=>row.Children).Select(span=>span.Text!).ToArray();
        Check(string.Concat(parts)==unicode&&parts.All(part=>!char.IsHighSurrogate(part[^1])&&!char.IsLowSurrogate(part[0])),"Unbroken Unicode text split a surrogate pair");
        Check(ViewSnapshotValidator.Validate(result).Count==0,"Unbroken Unicode text exceeds protocol bounds");
        return assertions;
    }
}
