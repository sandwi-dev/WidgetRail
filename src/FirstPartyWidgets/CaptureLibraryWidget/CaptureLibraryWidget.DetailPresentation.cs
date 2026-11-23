using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private FocusGroupEntryRequest? detailEntry;

    // One existing SDK action surface owns icon, label and trailing hint.
    // No nested button or extra focus target; page shortcuts retain input ownership.
    private static ActionSurfaceElement HintedAction(string label, string action, ControllerButton? button,
        bool busy, WidgetGlyph? icon = null, bool selected = false, string? id = null)
    {
        var content = new List<WidgetElement>();
        id ??= action;
        if (icon is { } glyph) content.Add(UI.Icon(glyph, id + ".icon", label).Classes("capture-action-icon"));
        content.Add(UI.Text(label, id + ".label").Classes("capture-action-label"));
        if (button is { } shortcut) content.Add(UI.ControllerGlyph(shortcut, id + ".hint").Classes("capture-action-badge"));
        return UI.ActionSurface(action, id, label, ActionSurfaceOrientation.Horizontal, content.ToArray())
            .Busy(busy).Selected(selected).Classes("capture-hint-button");
    }

    private string DetailName(State state) => selectedCapture?.Title ?? state.Preview?.SourceApplication?.ApplicationName ??
            selectedCapture?.Media.SourceApplication?.ApplicationName ?? selectedCapture?.FileName ?? "Capture details";

    private WidgetElement DetailBody(State state)
    {
        var media = state.Preview ?? selectedCapture?.Media;
        WidgetElement viewer = state.Preview is { } preview
            ? UI.MediaPlayer(MediaPlayerSource.FromCapture(preview), "capture-library.preview", "Capture preview",
                new(Muted: preview.Properties?.Audio is null or CaptureAudioMode.None)).Classes("capture-detail-media")
            : UI.Stack("capture-library.detail.unavailable", UI.Text(state.Busy ? "Opening capture…" :
                "Preview unavailable. You can return to the library or remove this entry.", "capture-library.unavailable").Classes("capture-help"))
                .Classes("capture-detail-media", "capture-detail-unavailable");

        var info = new List<WidgetElement>
        {
            UI.Text(DetailName(state), "capture-library.heading").Classes("capture-heading", "capture-detail-name"),
        };
        if (state.SelectedId is not null && selectedCapture is { } selected)
            info.Add(UI.Stack("capture-library.detail.title-field",
                UI.Text("Title", "capture-library.title.label").Classes("capture-field-label"),
                UI.TextEntry(selected.Title ?? "", "Add a title…", "capture-library.title", "capture-library.edit-title", 160)
                    .Disabled(state.Busy)).Classes("capture-detail-field"));
        if (media is not null)
        {
            var video = media.ContentType == "video/mp4";
            var metadata = new List<WidgetElement>
            {
                UI.Text(video ? "VIDEO CLIP" : "SCREENSHOT", "capture-library.media-kind")
                    .Classes("capture-media-kind", video ? "capture-card-video" : "capture-card-image"),
                UI.Text((video ? $"{media.DurationSeconds:0.#} seconds · " : "") +
                    $"{media.Width} × {media.Height} · {media.ByteLength / 1048576d:0.0} MiB", "capture-library.metadata").Classes("capture-detail-metadata"),
            };
            if (video && media.Properties is { } actual)
                metadata.Add(UI.Text($"{actual.RequestedFramesPerSecond} fps · " +
                    (actual.Audio == CaptureAudioMode.Application ? "Application audio" : "No audio") +
                    (actual.MissedFrames > 0 ? $" · {actual.MissedFrames} missed frames" : ""), "capture-library.recording-properties").Classes("capture-help"));
            if (state.SelectedId is not null && selectedCapture is { } saved)
                metadata.Add(UI.Text((saved.FileModifiedAtUnixMilliseconds is not null ? "Modified " : "") +
                    DateTimeOffset.FromUnixTimeMilliseconds(saved.FileModifiedAtUnixMilliseconds ?? saved.SavedAtUnixMilliseconds)
                        .ToLocalTime().ToString("g", CultureInfo.CurrentCulture), "capture-library.detail.date").Classes("capture-help"));
            info.Add(UI.Stack("capture-library.detail.metadata", metadata.ToArray()).Classes("capture-detail-metadata-group"));
        }

        var actions = new List<WidgetElement>();
        if (state.Preview is not null)
            actions.Add(ShareCaptureButton(state).AddClasses("capture-detail-command", "capture-share-action"));
        if (state.SelectedId is not null && selectedCapture is { } favorite)
            actions.Add(HintedAction(favorite.IsFavorite ? "Favorited" : "Favorite", "capture-library.favorite", ControllerButton.Y,
                state.Busy, favorite.IsFavorite ? WidgetGlyph.FavoriteFilled : WidgetGlyph.Favorite, favorite.IsFavorite)
                .AddClasses("capture-detail-command"));
        if (state.SelectedId is null ? state.Preview is not null : selectedCapture?.Available == true)
            actions.Add(UI.Button("Edit a copy", "capture-library.edit-copy", "capture-library.edit-copy")
                .Icon(WidgetGlyph.Edit).Busy(state.Busy).Classes("capture-detail-command"));
        if (state.SelectedId is not null && selectedCapture is { } item)
        {
            if (item.Available) actions.Add(UI.Button("Link to game…", "capture-library.detail.link", "capture-library.detail.link")
                .OpenIntent(WidgetIntentContracts.CaptureAssociation, AssociationPayload([item]), WidgetIntentPresentation.OpenWidget)
                .Busy(state.Busy).WithIntentFeedback().Classes("capture-detail-command"));
            actions.Add(UI.Button("Show in folder", "capture-library.reveal", "capture-library.reveal")
                .Icon(WidgetGlyph.Folder).Disabled(!item.Available).Busy(state.Busy).Classes("capture-detail-command"));
            actions.Add(UI.Button("Save a copy", "capture-library.copy", "capture-library.copy")
                .Icon(WidgetGlyph.Save).Disabled(!item.Available).Busy(state.Busy).Classes("capture-detail-command"));
            if (item.ContextCount > 0 || undoDestination is { } undo && undo.CaptureId == item.Id && undo.ContentRevision == item.Revision)
                actions.Add(UI.Button("Related details", "capture-library.context.open", "capture-library.context.open")
                    .Busy(state.Busy).Classes("capture-detail-command"));
            if (item.SourceId is not null)
            {
                var hidden = galleryQuery.Filter.HiddenOnly;
                var action = hidden ? "capture-library.unhide" : "capture-library.delete";
                actions.Add(UI.Button(hidden ? "Unhide" : "Hide from library", action, action)
                    .Icon(CaptureIcon(hidden ? "show" : "hide", hidden ? WidgetGlyph.Open : WidgetGlyph.Close)).Busy(state.Busy)
                    .Classes("capture-detail-command", "capture-detail-delete"));
            }
            else actions.Add(UI.Button(!item.Available ? "Remove unavailable entry" : "Move to Recycle Bin",
                "capture-library.delete", "capture-library.delete").Icon(WidgetGlyph.Delete).Busy(state.Busy)
                .Classes("capture-detail-command", "capture-delete", "capture-detail-delete"));
            if (item is {SourceId:not null,Available:true})
                actions.Add(UI.Button("Move to Recycle Bin", "capture-library.recycle-original", "capture-library.recycle-original")
                    .Icon(WidgetGlyph.Delete).Busy(state.Busy).Classes("capture-detail-command", "capture-delete"));
        }
        info.Add(UI.Stack("capture-library.actions", actions.ToArray()).RememberChildFocus(DetailActionEntry(state)).Classes("capture-detail-action-list"));

        return UI.Grid("capture-library.detail.layout", [GridTrack.Star()],
                [GridTrack.Star(7), GridTrack.Star(3, minimum: 280, maximum: 400)],
                UI.Grid("capture-library.detail.stage", [GridTrack.Star()], [GridTrack.Star()], viewer.InGrid())
                    .Classes("capture-detail-stage").InGrid().AlignInGrid(vertical: GridCellAlignment.Center),
                UI.VerticalScroll("capture-library.detail.scroll",
                    UI.Stack("capture-library.detail.info", info.ToArray()).Classes("capture-detail-info")).Classes("capture-detail-scroll")
                    .InGrid(column: 1).AlignInGrid(vertical: GridCellAlignment.Center))
            .Spacing(0, 16).Classes("capture-detail-layout");
    }

    private static string DetailActionEntry(State state) => state.Preview is not null ? "capture-library.share" : "capture-library.favorite";
}
