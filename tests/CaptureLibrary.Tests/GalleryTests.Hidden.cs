using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task HiddenFilterRestoresWithoutDeletingOriginal()
    {
        await using var fixture = new GalleryFixture();
        fixture.Items[0] = fixture.Items[0] with { SourceId = new string('b', 32) };
        var id = fixture.Items[0].Id;
        await fixture.Start();
        using var host = await OpenEditingSource(fixture);
        Check(fixture.Node("capture-library.delete")?.Text == "Hide from library", "Linked removal must be labeled Hide");
        await fixture.Action("capture-library.delete");
        Check(fixture.Node("capture-library.delete.confirm")?.Text == "Hide", "Hide confirmation must not imply file deletion");
        await fixture.Action("capture-library.delete.confirm");
        await fixture.Until(() => !fixture.Busy && fixture.Collection?.IndexedCollection?.Count == 2);
        Check(fixture.HiddenItems.Single().Id == id && fixture.Recycled == 0, "Hide must retain an original and its hidden record");
        await fixture.Action("capture-library.filter.hidden");
        await fixture.Until(() => fixture.LastQuery?.HiddenOnly == true && fixture.Collection?.IndexedCollection?.Count == 1);
        Check(fixture.Node("capture-library.active.type")?.Text == "Hidden ×", "Hidden filter must be visible and clearable");
        using (var page = await host.AcquireAsync("capture-library.entries", 0, 1))
            Check(page.RouteAction(id, ControllerButton.A) is not null, "Hidden captures must remain openable");
        await fixture.Until(() => fixture.Node("capture-library.unhide") is not null && !fixture.Busy);
        Check(fixture.Node("capture-library.delete") is null && fixture.Node("capture-library.recycle-original") is not null,
            "Hidden detail must distinguish Unhide from recycling the original");
        await fixture.Action("capture-library.favorite");
        await fixture.Until(()=>!fixture.Busy&&fixture.HiddenItems.Single().IsFavorite);
        await fixture.Action("capture-library.unhide");
        await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.empty") is not null);
        Check(fixture.HiddenItems.Count == 0 && fixture.Items.Count == 3 && fixture.Recycled == 0 && fixture.LastQuery!.HiddenOnly,
            "Unhide must leave the original intact and retain the user's Hidden filter");
        Check(fixture.Items.Single(item=>item.Id==id).IsFavorite,"Unhide must retain metadata edited while hidden");
        await fixture.Action("capture-library.filter.all");
        await fixture.Until(() => fixture.Collection?.IndexedCollection?.Count == 3 && fixture.LastQuery?.HiddenOnly == false);
    }

    internal static async Task HiddenBatchRestoresAndKeepsSelectionExitAvailable()
    {
        await using var fixture = new GalleryFixture();
        fixture.HiddenItems.AddRange(fixture.Items.Take(2).Select((item,index)=>item with
            {Id=(index+4).ToString("x32"),SourceId=new string('b',32)}));
        await fixture.Start();
        await fixture.Action("capture-library.filter.hidden");
        await fixture.Until(()=>fixture.Collection?.IndexedCollection?.Count==2);
        await fixture.Action("capture-library.selection.toggle");
        await fixture.Action("capture-library.selection.all");
        await fixture.Until(()=>fixture.Collection?.IndexedSelection?.Count==2);
        Check(fixture.Node("capture-library.batch.hide") is {Text:"Unhide",ActionId:"capture-library.batch.unhide"},
            "Hidden selection must offer restoration instead of removal");
        await fixture.Action("capture-library.batch.unhide");
        await fixture.Until(()=>!fixture.Busy&&fixture.Node("capture-library.empty") is not null);
        Check(fixture.HiddenItems.Count==0&&fixture.Items.Count==5&&fixture.Recycled==0&&
            fixture.Node("capture-library.selection.toggle")?.IsDisabled!=true,
            "Unhide-all must preserve originals and leave Done selecting reachable on the empty result");
        await fixture.Action("capture-library.selection.toggle");
        Check(fixture.Node("capture-library.selection.toggle")?.IsSelected!=true,"Selection mode could not be exited after restoring the last hidden item");
    }
}
