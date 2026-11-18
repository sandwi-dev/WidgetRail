using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class FocusVisualTargetTests
{
    internal static Task Run()
    {
        var surface=UI.ReadingSurface("message","Message",ActionSurfaceOrientation.Vertical,
            UI.Text("Today","date"),UI.Row("content",UI.Stack("bubble",UI.Text("Hello","text"))))
            .FocusVisualTarget("bubble");
        var snapshot=new WidgetView(UI.Stack("root",surface)).CreateSnapshot("focus-visual",1);
        Check(snapshot.ProtocolVersion==ProtocolConstants.TargetedCollectionPresentationVersion,"Target decoration requires its actual host contract");
        var row=snapshot.Root.Children.Single();
        Check(row.IsFocusable && row.FocusVisualTargetId=="bubble" && row.Children[1].Children[0].IsFocusable==false,
            "Decoration does not create a second focus or activation owner");
        foreach(var invalid in new[]{"missing","root","date","text","message"})
        {
            var errors=ViewSnapshotValidator.Validate(snapshot with{Root=snapshot.Root with{Children=[row with{FocusVisualTargetId=invalid}]}});
            Check(errors.Any(error=>error.Code=="invalid_focus_visual_target"),"Rejects unrelated or non-layout target: "+invalid);
        }
        var hidden=row with{Children=[row.Children[0],row.Children[1] with{VisibleWhen=ResponsiveVisibility.CompactOnly}]};
        Check(ViewSnapshotValidator.Validate(snapshot with{Root=snapshot.Root with{Children=[hidden]}})
            .Any(error=>error.Code=="invalid_focus_visual_target"),"Hidden ancestors cannot withdraw the sole visible focus outline");
        var ordinary=new WidgetView(UI.Stack("root",surface with{FocusVisualTargetId=null})).CreateSnapshot("focus-visual",1);
        Check(!System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(ordinary)).Contains("focusVisualTargetId",StringComparison.Ordinal),
            "Default action surfaces retain their old wire shape");
        var update=WidgetPresentationDiff.Create(snapshot,ordinary with{Sequence=2,ProtocolVersion=snapshot.ProtocolVersion},
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",1,PresentationUpdateCapabilities.Current,WidgetPresentationTransactionKind.IncrementalUpdate);
        Check(update.Update?.Operations.Any(operation=>operation.Kind==PresentationUpdateOperationKind.ReplaceSubtree &&
            operation.TargetId=="message" && operation.Subtree?.FocusVisualTargetId is null)==true,
            "Removing a visual target reaches the host through the existing structural publication path");
        return Task.CompletedTask;
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
