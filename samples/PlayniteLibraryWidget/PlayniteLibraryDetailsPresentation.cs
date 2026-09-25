using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteLibraryDetailsPresentation
{
    private const string Prefix = "playnite-library.details.";
    internal const string PlayId = Prefix + "play";
    internal const string ContentId = Prefix + "content";
    internal const int PageSize = 20;

    internal static WidgetModal Create(PlayniteLibraryItem item, bool current,
        string? launchingSavedId, string status, PlayniteLibraryLaunchState? launchState,
        bool loading = false, string? error = null, PlayniteDetailsExtras? extras = null,
        IReadOnlyList<PlayniteLibraryCategory>? categories = null, string? openingId = null,
        bool favorite = false, bool organizationBusy = false)
    {
        extras ??= new();
        var modalId = openingId ?? "playnite-library.details";
        if (extras.ConfirmUninstall)
            return new(modalId, "Uninstall " + item.Presentation.DisplayName + "?",
                UI.Stack(Prefix + "confirm",
                    UI.Text("Playnite will ask the game's launcher to uninstall it. Follow any prompts in Playnite or the launcher.",
                        Prefix + "confirm.copy").Classes("playnite-library-details-description"),
                    UI.Button("Cancel", Prefix + "uninstall.cancel", Prefix + "uninstall.cancel"),
                    UI.Button("Uninstall", Prefix + "uninstall.confirm", Prefix + "uninstall.confirm")),
                Prefix + "uninstall.cancel", Prefix + "uninstall.cancel")
            {
                HeaderActions = UI.ControllerHint(ControllerButton.B, "Cancel", Prefix + "cancel.hint"),
            };
        var game = extras.Full?.Game;
        var presentation = item.Presentation;
        var availability = PlayniteLibraryAvailabilityPresentation.Tile(item);
        var installed = presentation.Availability.State == WidgetAppLibraryAvailabilityState.Installed;
        var uninstalled = PlayniteLibraryAvailabilityPresentation.IsUninstalled(item);
        var launching = item.Value.SavedId == launchingSavedId;
        var busy = launching || extras.OperationBusy || organizationBusy;
        var play = UI.Button(busy ? "Working..." : uninstalled ? "Install" : "Play",
                uninstalled ? Prefix + "install" : PlayniteLibraryActions.Launch, PlayId)
            .Disabled(!current || (!availability.Launchable && !uninstalled))
            .Busy(busy).Classes("playnite-library-details-play");
        var buttons = UI.Stack(Prefix + "actions", play,
            UI.Button("Completion status", Prefix + "completion", Prefix + "completion.action").Disabled(!current || busy).Classes("playnite-library-details-button"),
            UI.Button("Uninstall", Prefix + "uninstall", Prefix + "uninstall").Disabled(!current || !installed || busy).Classes("playnite-library-details-button"))
            .Classes("playnite-library-details-buttons");
        var artwork = presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile);
        WidgetElement poster = artwork is { Handle.Length: > 0 }
            ? UI.Artwork(new WidgetArtworkHandle(artwork.Handle), Prefix + "poster", presentation.DisplayName, ImageFit.Contain)
            : UI.Icon(WidgetGlyph.Play, Prefix + "poster", presentation.DisplayName);
        poster = poster.Classes("playnite-library-details-poster");
        var controls = UI.Stack(Prefix + "controls",
            UI.Text(presentation.Source.DisplayName, Prefix + "source").Classes("playnite-library-details-source"),
            UI.Text(!current ? "Game no longer in the current results" : uninstalled ? "Not installed"
                : launching || launchState is not null ? status : availability.Status == "Play" ? "Installed" : availability.Status,
                Prefix + "status").Classes("playnite-library-details-meta"),
            buttons,
            UI.ControllerHint(ControllerButton.X, "Game options", Prefix + "options"))
            .Classes("playnite-library-details-controls");
        var tabName = extras.Tab.ToString().ToLowerInvariant();
        var tabs = Enum.GetValues<PlayniteDetailsTab>().Select(tab =>
            new NavigationShellDestination(tab.ToString().ToLowerInvariant(), tab.ToString(),
                Prefix + "tab." + tab.ToString().ToLowerInvariant(), tab switch
                {
                    PlayniteDetailsTab.Achievements => WidgetGlyph.Check,
                    PlayniteDetailsTab.Activity => WidgetGlyph.Rewind,
                    _ => WidgetGlyph.Play,
                })).ToArray();
        var navigation = UI.NavigationShellParts(Prefix + "navigation", tabName, NavigationShellContentEntry.Unavailable,
            UI.Stack(Prefix + "navigation.unused"), tabs,
            compactLeadingAdornment: UI.ControllerGlyph(ControllerButton.LeftBumper, Prefix + "previous.key").Classes("playnite-library-tab-key"),
            compactTrailingAdornment: UI.ControllerGlyph(ControllerButton.RightBumper, Prefix + "next.key").Classes("playnite-library-tab-key"));
        var navigationRail = navigation.CompactNavigation.VisibleWhen(ResponsiveVisibility.Always)
            .AddClasses("playnite-library-details-tabs");
        var content = new List<WidgetElement>
        {
            UI.Row(Prefix + "overview-header", poster, controls).Classes("playnite-library-details-header"),
            navigationRail,
        };
        if (extras.OperationMessage is { } message) content.Add(Copy(message, "operation.message"));
        if (extras.Tab == PlayniteDetailsTab.Overview)
        {
            if (loading) content.Add(Loading("Loading game details..."));
            if (error is not null) content.Add(Copy(error, "load.error"));
            AddOverview(content, item, game, loading, error, busy);
        }
        else if (extras.Tab == PlayniteDetailsTab.Achievements) AddAchievements(content, extras);
        else AddActivity(content, extras);
        var body = UI.Stack(ContentId, content.ToArray()).Classes("playnite-library-details-content")
            .Shortcut(ControllerButton.LeftBumper, Prefix + "tab.previous", "Previous details tab")
            .Shortcut(ControllerButton.RightBumper, Prefix + "tab.next", "Next details tab")
            .Shortcut(ControllerButton.Y, Prefix + "refresh", "Refresh details");
        var scopedBody = body.ContextMenu(ControllerButton.X,
            PlayniteLibraryGameOptions.Create(item, favorite, categories, busy || !current));
        return new(modalId, presentation.DisplayName, scopedBody, PlayId, PlayniteLibraryActions.DetailsClose)
        {
            HeaderActions = UI.Row(Prefix + "header-hints",
                UI.ControllerHint(ControllerButton.Y, "Refresh", Prefix + "refresh.hint"),
                UI.ControllerHint(ControllerButton.B, "Close", Prefix + "close.hint"))
                .Classes("playnite-library-details-header-hints"),
        };
    }

    private static void AddOverview(List<WidgetElement> content, PlayniteLibraryItem item, PlayniteBridgeGame? game,
        bool loading, string? error, bool busy)
    {
        var metadata = item.Presentation.Metadata;
        if (metadata?.PlaytimeMinutes is { } minutes) content.Add(Value("Time played", Duration(minutes * 60), Prefix + "playtime"));
        if (metadata?.LastPlayedAtUnixMilliseconds is { } played && played is >= -62135596800000 and <= 253402300799999)
            content.Add(Value("Last played", DateTimeOffset.FromUnixTimeMilliseconds(played).ToLocalTime().ToString("d"), Prefix + "last-played"));
        if (game is not null)
        {
            AddValue("Developer", string.Join(", ", game.Developers), "developers");
            AddValue("Publisher", string.Join(", ", game.Publishers), "publishers");
            AddValue("Released", game.ReleaseDate, "release");
            AddValue("Genres", string.Join(", ", game.Genres), "genres");
            AddValue("Platforms", string.Join(", ", game.Platforms), "platforms");
            AddValue("Features", string.Join(", ", game.Features), "features");
            AddValue("Series", string.Join(", ", game.Series), "series");
            AddValue("Age rating", string.Join(", ", game.AgeRatings), "age-rating");
            AddValue("Completion", game.CompletionStatus, "completion");
            if (game.PlayCount is > 0) AddValue("Times played", game.PlayCount.Value.ToString(CultureInfo.CurrentCulture), "play-count");
            if (game.InstallSize is > 0) AddValue("Installation size", $"{game.InstallSize.Value / (1024d * 1024 * 1024):0.##} GB", "size");
            var scores = new List<string>();
            if (game.CriticScore is { } critic) scores.Add($"Critics {critic}/100");
            if (game.CommunityScore is { } community) scores.Add($"Community {community}/100");
            if (game.UserScore is { } personal) scores.Add($"Your rating {personal}/100");
            AddValue("Ratings", string.Join(" | ", scores), "ratings");
            AddValue("Tags", string.Join(", ", game.Tags), "tags");
        }
        AddValue("Version", metadata?.Version, "version");
        if (error is null && (!loading || !string.IsNullOrWhiteSpace(metadata?.Description)))
        {
            content.Add(UI.Text("Description", Prefix + "description.title").Classes("playnite-library-details-section-title"));
            var description = metadata?.Description;
            if (string.IsNullOrWhiteSpace(description)) content.Add(Copy("No description is available for this game.", "description"));
            else AddParagraphs(content, description, "description");
        }
        if (!string.IsNullOrWhiteSpace(game?.Notes))
        {
            content.Add(UI.Text("Notes", Prefix + "notes.title").Classes("playnite-library-details-section-title"));
            AddParagraphs(content, game.Notes, "notes");
        }
        if (game?.Links.Count > 0)
        {
            content.Add(UI.Text("Links", Prefix + "links.title").Classes("playnite-library-details-section-title"));
            for (var index = 0; index < game.Links.Count; index++)
                content.Add(UI.Button(game.Links[index].Name + " (browser)", Prefix + "link." + index, Prefix + "link." + index).Disabled(busy));
        }
        void AddValue(string label, string? value, string id)
        {
            if (!string.IsNullOrWhiteSpace(value)) content.Add(Value(label, value, Prefix + id));
        }
    }

    private static void AddAchievements(List<WidgetElement> content, PlayniteDetailsExtras state)
    {
        if (state.AchievementsLoading) { content.Add(Loading("Loading achievements...")); return; }
        if (state.AchievementsError is { } error) { content.Add(Copy(error, "achievements.error")); return; }
        if (state.Achievements is not { Available: true } data)
        {
            content.Add(Copy("No SuccessStory data is available for this game. Install and enable SuccessStory in Playnite, then let it sync this game and refresh here.", "achievements.unavailable"));
            return;
        }
        if (data.Total == 0) { content.Add(Copy("SuccessStory reports no achievements for this game.", "achievements.empty")); return; }
        content.Add(UI.Text($"{data.Unlocked} of {data.Total} unlocked", Prefix + "achievements.summary").Classes("playnite-library-details-section-title"));
        content.Add(UI.Progress(data.Unlocked, data.Total, Prefix + "achievements.progress"));
        var page = Math.Clamp(state.Page, 0, Math.Max(0, (data.Items.Count - 1) / PageSize));
        for (var index = page * PageSize; index < Math.Min(data.Items.Count, (page + 1) * PageSize); index++)
        {
            var item = data.Items[index];
            var secret = item.Hidden && !item.Unlocked && !state.RevealedAchievements.Contains(index);
            var name = secret ? "Secret achievement" : item.Name;
            var description = secret ? "Press A to reveal" : item.Description;
            var status = item.Unlocked ? "Unlocked" : "Locked";
            if (item.UnlockedAt is { } date && item.Unlocked) status += " | " + date.ToLocalTime().ToString("d");
            if (item.Percent is { } percent) status += $" | {percent:0.#}% of players";
            if (item.GamerScore is { } points) status += $" | {points} points";
            var id = Prefix + "achievement." + index;
            content.Add(UI.ActionSurface(id, id, name + ". " + description + ". " + status,
                ActionSurfaceOrientation.Vertical,
                UI.Text(name, id + ".name").Classes("playnite-library-details-row-title"),
                UI.Text(description, id + ".description").Classes("playnite-library-details-meta"),
                UI.Text(status, id + ".status").Classes("playnite-library-details-meta"))
                .Classes("playnite-library-details-row"));
        }
        AddPaging(content, page, data.Items.Count);
    }
    private static void AddActivity(List<WidgetElement> content, PlayniteDetailsExtras state)
    {
        if (state.ActivityLoading) { content.Add(Loading("Loading activity...")); return; }
        if (state.ActivityError is { } error) { content.Add(Copy(error, "activity.error")); return; }
        if (state.Activity is not { Available: true } data)
        {
            content.Add(Copy("No GameActivity data is available for this game. Install and enable GameActivity in Playnite, play the game, then refresh here.", "activity.unavailable"));
            return;
        }
        if (data.Sessions.Count == 0) { content.Add(Copy("GameActivity has no recorded sessions for this game.", "activity.empty")); return; }
        content.Add(Value("Recorded playtime", Duration(data.TotalSeconds), Prefix + "activity.total"));
        content.Add(Value("Sessions", data.Sessions.Count.ToString(CultureInfo.CurrentCulture), Prefix + "activity.count"));
        content.Add(Value("Average session", Duration((long)data.Sessions.Average(item => (double)item.Seconds)), Prefix + "activity.average"));
        content.Add(Value("Longest session", Duration(data.Sessions.Max(item => item.Seconds)), Prefix + "activity.longest"));
        var page = Math.Clamp(state.Page, 0, (data.Sessions.Count - 1) / PageSize);
        for (var index = page * PageSize; index < Math.Min(data.Sessions.Count, (page + 1) * PageSize); index++)
        {
            var session = data.Sessions[index];
            var label = (session.Date?.ToLocalTime().ToString("g") ?? "Date unknown") + " | " + Duration(session.Seconds);
            content.Add(UI.Stack(Prefix + "session." + index,
                UI.Text(label, Prefix + "session." + index + ".summary").Classes("playnite-library-details-row-title"),
                UI.Text(session.Action ?? "Play session", Prefix + "session." + index + ".action").Classes("playnite-library-details-meta"))
                .Classes("playnite-library-details-row"));
        }
        AddPaging(content, page, data.Sessions.Count);
    }
    private static void AddPaging(List<WidgetElement> content, int page, int count)
    {
        if (count <= PageSize) return;
        content.Add(UI.Row(Prefix + "pages",
            UI.Button("Previous", Prefix + "page.previous", Prefix + "page.previous").Disabled(page == 0),
            UI.Text($"{page + 1} / {(count + PageSize - 1) / PageSize}", Prefix + "page.label"),
            UI.Button("Next", Prefix + "page.next", Prefix + "page.next").Disabled((page + 1) * PageSize >= count))
            .Classes("playnite-library-details-toolbar"));
    }
    private static WidgetElement Value(string label, string value, string id) =>
        UI.Row(id,
            UI.Text(label, id + ".label").Classes("playnite-library-details-label"),
            UI.Text(value, id + ".value").Classes("playnite-library-details-field"))
            .Classes("playnite-library-details-value");
    private static WidgetElement Copy(string text, string id) => UI.Text(text, Prefix + id).Classes("playnite-library-details-description");
    private static WidgetElement Loading(string text) => UI.Row(Prefix + "loading",
        UI.LoadingIndicator(Prefix + "spinner", size: LoadingIndicatorSize.Compact), UI.Text(text, Prefix + "loading.text"));
    private static string Duration(long seconds) => seconds >= 3600 ? $"{seconds / 3600}h {seconds % 3600 / 60}m" : $"{seconds / 60}m";
    private static void AddParagraphs(List<WidgetElement> content, string text, string id)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            if (current.Length + word.Length > 240 && current.Length != 0) { parts.Add(current); current = ""; }
            current = current.Length == 0 ? word : current + " " + word;
        }
        if (current.Length != 0) parts.Add(current);
        content.Add(UI.Stack(Prefix + id + ".paragraphs", parts.Select((part, index) =>
            Copy(part, index == 0 ? id : id + "." + index)).ToArray()).Classes("playnite-library-details-paragraphs"));
    }
}
