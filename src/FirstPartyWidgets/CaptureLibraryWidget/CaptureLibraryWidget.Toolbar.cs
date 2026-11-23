using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private static WidgetIcon CaptureIcon(string name, WidgetGlyph fallback) =>
        WidgetIcon.PackageSvg("captures."+name, WidgetPackageIconColorMode.ThemeTint, fallback);
    private WidgetElement DetailHeader(State state)
    {
        var header=new List<WidgetElement>();
        header.Add(HintedAction("Back to library","capture-library.back",ControllerButton.B,state.Busy));
        header.Add(UI.Text("/  " + DetailName(state),"capture-library.title").Classes("capture-breadcrumb","capture-detail-header-title"));
        if(state.SelectedId is not null)
            header.Add(UI.Row("capture-library.viewer.navigation",
                UI.Button("Previous","capture-library.viewer.previous","capture-library.viewer.previous").Busy(state.Busy),
                UI.Button("Next","capture-library.viewer.next","capture-library.viewer.next").Busy(state.Busy))
                .Classes("capture-detail-navigation"));
        return UI.Row("capture-library.detail.header",header.ToArray()).Classes("capture-detail-header");
    }

    private WidgetElement HomeCaptureActions(State state)
    {
        var shortcuts=gallery.Selection?.IsActive != true;
        var actions=new List<WidgetElement>{
            CaptureToolbarAction("capture-library.photo", "Take screenshot", CaptureIcon("mark",WidgetGlyph.Fullscreen),
                shortcuts ? ControllerButton.Y : null, state.Busy, false),
            CaptureToolbarAction("capture-library.video", "Record clip", CaptureIcon("record",WidgetGlyph.Play),
                shortcuts ? ControllerButton.X : null, state.Busy, true)};
        if(state.Replay is null or {Settings.Enabled:false,Phase:WidgetRail.WidgetProtocol.CaptureReplayPhase.Off,Saving:false})
            actions.Add(UI.IconButton(CaptureIcon("replay",WidgetGlyph.Rewind),"capture-library.replay.start","capture-library.replay.start","Start replay",IconButtonVariant.Quiet)
                .Disabled(state.Busy||Operations.IsBusy(ReplayOperation)));
        return UI.Row("capture-library.capture-actions",actions.ToArray()).Classes("capture-header-actions");
    }

    private static ActionSurfaceElement CaptureToolbarAction(string id, string label, WidgetIcon icon,
        ControllerButton? shortcut, bool busy, bool recording)
    {
        var content = new List<WidgetElement>
        {
            UI.Icon(icon,id+".icon",label).Classes(recording ? "capture-toolbar-record-icon" : "capture-toolbar-icon"),
        };
        if(shortcut is {} button)
            content.Add(UI.ControllerGlyph(button,id+".hint").Classes("capture-toolbar-badge"));
        return UI.ActionSurface(id,id,label,ActionSurfaceOrientation.Horizontal,content.ToArray())
            .Busy(busy).Classes("capture-toolbar-command");
    }

    private SelectElement MoreCommands(bool busy)
    {
        var commands = new List<SelectOption> {
            new("refresh", "Refresh library", "capture-library.refresh"),
            new("settings", "Capture settings", "capture-library.settings"),
            new("folders", "Capture folders", "capture-library.sources.open"),
            new("open-folder", "Open library folder", "capture-library.folder"),
        };
        if (gallery.Selection is { IsActive: true } selected)
        {
            var limit = IndexedCollectionSelection.MaximumItems;
            commands.Add(gallery.Descriptor.Count > limit
                ? new("select-first", $"Select first {limit} matches", "capture-library.selection.first")
                : new("select-all", "Select all matches", "capture-library.selection.all", IsDisabled: gallery.Descriptor.Count == 0));
            commands.Add(new("clear-selection", "Clear selection", "capture-library.selection.clear", IsDisabled: selected.Count == 0));
            commands.Add(new("unfavorite", "Remove favorite", "capture-library.batch.unfavorite", IsDisabled: selected.Count == 0));
        }
        return UI.CommandMenu("Menu", commands, "capture-library.more", "Capture commands").Busy(busy) with { UseMenuShortcut = true };
    }
}
