using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteLibraryDetailsPresentation
{
    private const string Prefix = "playnite-library.details.";
    internal const string FavoriteId = Prefix + "favorite";
    internal const string PlayId = Prefix + "play";
    internal const string ContentId = Prefix + "content";
    internal const string OptionsId = Prefix + "options";
    internal const int PageSize = 20;

    internal static WidgetModal Create(PlayniteLibraryItem item, bool current,
        string? launchingSavedId, string status, PlayniteLibraryLaunchState? launchState,
        bool loading = false, string? error = null, PlayniteDetailsExtras? extras = null,
        IReadOnlyList<PlayniteLibraryCategory>? categories = null, string? openingId = null,
        bool favorite = false, bool organizationBusy = false, string? completionStatus = null, long generation = 0,
        WidgetElement? achievementList = null, PlayniteAchievementBrowse? achievementQuery = null)
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
        var buttons = UI.Row(Prefix + "actions", play,
            UI.IconButton(PlayniteLibraryPresentation.FavoriteIcon(favorite), PlayniteLibraryActions.Favorite,
                FavoriteId, favorite ? "Remove favorite" : "Add favorite", size: IconButtonSize.Medium)
                .Selected(favorite).Busy(organizationBusy).Disabled(!current))
            .Classes("playnite-library-details-buttons");
        var artwork = presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile);
        WidgetElement poster = artwork is { Handle.Length: > 0 }
            ? UI.Artwork(new WidgetArtworkHandle(artwork.Handle), Prefix + "poster", presentation.DisplayName, ImageFit.Cover)
            : UI.Icon(WidgetGlyph.Play, Prefix + "poster", presentation.DisplayName);
        poster = poster.Classes("playnite-library-details-poster");
        var completion = completionStatus ?? game?.CompletionStatus;
        var controlItems = new List<WidgetElement>
        {
            UI.Text(presentation.Source.DisplayName, Prefix + "source").Classes("playnite-library-details-source"),
        };
        if (!current || launching || launchState is not null)
            controlItems.Add(UI.Text(!current ? "Game no longer in the current results" : status,
                Prefix + "status").Classes("playnite-library-details-meta"));
        controlItems.Add(extras.Tab == PlayniteDetailsTab.Achievements ? buttons.AddClasses("playnite-library-achievement-game-actions") : buttons);
        controlItems.Add(extras.Tab == PlayniteDetailsTab.Achievements
            ? CompletionSelect(extras, completion, generation, !current).AddClasses("playnite-library-achievement-completion")
            : CompletionSelect(extras, completion, generation, !current));
        var controls = UI.Stack(Prefix + "controls", controlItems.ToArray())
            .Classes("playnite-library-details-controls");
        var tabName = extras.Tab.ToString().ToLowerInvariant();
        var tabs = Enum.GetValues<PlayniteDetailsTab>().Select(tab =>
            new NavigationShellDestination(tab.ToString().ToLowerInvariant(), tab == PlayniteDetailsTab.Description ? "Overview" : tab.ToString(),
                Prefix + "tab." + tab.ToString().ToLowerInvariant(), tab switch
                {
                    PlayniteDetailsTab.Achievements => WidgetGlyph.Check,
                    PlayniteDetailsTab.Activity => WidgetGlyph.Rewind,
                    _ => WidgetGlyph.Play,
                })).ToArray();
        var navigation = UI.NavigationShellParts(Prefix + "navigation", tabName, NavigationShellContentEntry.Unavailable,
            UI.Stack(Prefix + "navigation.unused"), tabs,
            compactLeadingAdornment: UI.ControllerGlyph(ControllerButton.LeftBumper, Prefix + "previous.key").Classes("playnite-library-tab-key"),
            compactTrailingAdornment: UI.ControllerGlyph(ControllerButton.RightBumper, Prefix + "next.key").Classes("playnite-library-tab-key")).WithTransitions();
        var navigationRail = navigation.CompactNavigation.VisibleWhen(ResponsiveVisibility.Always)
            .AddClasses("playnite-library-details-tabs");
        var content = new List<WidgetElement>();
        if (extras.OperationMessage is { } message) content.Add(Copy(message, "operation.message"));
        if (extras.CompletionStatusesError is { } completionError) content.Add(Copy(completionError, "completion.error"));
        var tabContent = new List<WidgetElement>();
        if (extras.Tab is PlayniteDetailsTab.Description or PlayniteDetailsTab.Information or PlayniteDetailsTab.Links)
        {
            if (loading) tabContent.Add(Loading("Loading game details..."));
            if (error is not null) tabContent.Add(Copy(error, "load.error"));
            if (extras.Tab == PlayniteDetailsTab.Description)
            {
                var facts = new List<WidgetElement>();
                var metadata = item.Presentation.Metadata;
                if (metadata?.PlaytimeMinutes is > 0) facts.Add(Value("Time played", Duration(metadata.PlaytimeMinutes.Value * 60), Prefix + "overview.playtime"));
                if (metadata?.LastPlayedAtUnixMilliseconds is { } last && last is >= -62135596800000 and <= 253402300799999)
                    facts.Add(Value("Last played", DateTimeOffset.FromUnixTimeMilliseconds(last).ToLocalTime().ToString("d"), Prefix + "overview.last"));
                if (facts.Count > 0) tabContent.Add(UI.ResponsiveGrid(Prefix + "overview.facts", 240, 2, facts.ToArray()).Classes("playnite-library-details-facts"));
                AddDescription(tabContent, item, game, loading, error);
            }
            else if (extras.Tab == PlayniteDetailsTab.Information)
            {
                var facts = new List<WidgetElement>();
                AddInformation(facts, item, game);
                tabContent.Add(UI.ResponsiveGrid(Prefix + "information.facts", 240, 2, facts.ToArray()).Classes("playnite-library-details-facts"));
            }
            else if (!loading && error is null) AddLinks(tabContent, game, busy);
        }
        else if (extras.Tab == PlayniteDetailsTab.Achievements) tabContent.Add(PlayniteAchievementPresentation.Page(extras, achievementQuery, achievementList));
        else AddActivity(tabContent, extras);
        content.Add(UI.Stack(Prefix + "section.content", tabContent.ToArray()).Classes("playnite-library-details-content"));
        var tabBody = extras.Tab == PlayniteDetailsTab.Achievements
            ? UI.Grid(Prefix + "achievements.viewport", [GridTrack.Star()], [GridTrack.Star()], tabContent.ToArray()).Classes("playnite-library-achievement-page")
            : (WidgetElement)UI.VerticalScroll(modalId + ".scroll", content.ToArray()).Classes("playnite-library-details-scroll");
        var header = extras.Tab == PlayniteDetailsTab.Achievements
            ? UI.Row(Prefix + "overview-header", controls.AddClasses("playnite-library-achievement-game-controls"))
                .Classes("playnite-library-achievement-game-header")
            : UI.Row(Prefix + "overview-header", poster, controls).Classes("playnite-library-details-header");
        // Keep the rail outside the changing section: the compact achievements header
        // must not move navigation or bypass its content transition. A star row keeps
        // the indexed achievements viewport bounded without adding an outer scroll.
        var section = UI.Grid(Prefix + "tab.content", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
                header, tabBody.InGrid(row: 1))
            .Spacing(16, 0).Classes("playnite-library-details-layout")
            .TransitionContent(Prefix + "navigation", tabName, (int)extras.Tab);
        var body = UI.Grid(ContentId, [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
                navigationRail,
                section.InGrid(row: 1))
            .Spacing(16, 0).Classes("playnite-library-details-layout")
            .Shortcut(ControllerButton.LeftBumper, Prefix + "tab.previous", "Previous details tab")
            .Shortcut(ControllerButton.RightBumper, Prefix + "tab.next", "Next details tab")
            .Shortcut(ControllerButton.Y, Prefix + "refresh", "Refresh details");
        var menu = PlayniteLibraryGameOptions.Create(item, favorite, categories, busy || !current)
            .Take(ProtocolConstants.MaximumContextActionCount - 2)
            .Concat(new WidgetContextAction[] {
                new(Prefix + "refresh", "Refresh details", IsDisabled: busy || !current),
                new(Prefix + "uninstall", "Uninstall…", IsDisabled: busy || !current || !installed)
            }).ToArray();
        var options = UI.Row(OptionsId,
                UI.ControllerHint(ControllerButton.X, "Game options", Prefix + "options.hint"))
            .ContextMenu(ControllerButton.X,
                menu);
        return new(modalId, presentation.DisplayName, body, PlayId, PlayniteLibraryActions.DetailsClose)
        {
            ScrollContent = false,
            HeaderActions = UI.Row(Prefix + "header-hints", options,
                UI.ControllerHint(ControllerButton.Y, "Refresh", Prefix + "refresh.hint"),
                UI.ControllerHint(ControllerButton.B, "Close", Prefix + "close.hint"))
                .Classes("playnite-library-details-header-hints"),
        };
    }

    internal static string CompletionAction(long generation, string status) =>
        Prefix + "completion." + generation.ToString(CultureInfo.InvariantCulture) + "." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(status))).ToLowerInvariant();

    private static WidgetElement CompletionSelect(PlayniteDetailsExtras extras, string? current,
        long generation, bool disabled)
    {
        var statuses = extras.CompletionStatuses ?? [];
        var options = statuses.Select(value => new SelectOption(
            CompletionAction(generation, value), value, CompletionAction(generation, value),
            IsSelected: string.Equals(value, current, StringComparison.OrdinalIgnoreCase))).ToList();
        if (!options.Any(option => option.IsSelected))
        {
            var label = extras.CompletionStatusesLoading ? "Loading..."
                : extras.CompletionStatusesError is not null ? "Unavailable"
                : !string.IsNullOrWhiteSpace(current) ? current : "Not set";
            options.Insert(0, new(Prefix + "completion.current", label, Prefix + "completion.current",
                IsSelected: true, IsDisabled: true));
        }
        return UI.Select("Completion", options, Prefix + "completion.action", "Completion status")
            .Disabled(disabled || statuses.Count == 0)
            .AddClasses("playnite-library-details-completion-select");
    }

    private static void AddInformation(List<WidgetElement> content, PlayniteLibraryItem item, PlayniteBridgeGame? game)
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
        if (content.Count == 0) content.Add(Copy("No additional information is available for this game.", "information.empty"));
        void AddValue(string label, string? value, string id)
        {
            if (!string.IsNullOrWhiteSpace(value)) content.Add(Value(label, value, Prefix + id));
        }
    }

    private static void AddDescription(List<WidgetElement> content, PlayniteLibraryItem item, PlayniteBridgeGame? game,
        bool loading, string? error)
    {
        var metadata = item.Presentation.Metadata;
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
    }

    private static void AddLinks(List<WidgetElement> content, PlayniteBridgeGame? game, bool busy)
    {
        if (game?.Links.Count > 0)
        {
            content.Add(UI.Text("Links", Prefix + "links.title").Classes("playnite-library-details-section-title"));
            for (var index = 0; index < game.Links.Count; index++)
            {
                var link = game.Links[index];
                if (link.Url.Length > 2048 || link.Url != link.Url.Trim() || link.Url.Any(char.IsControl) ||
                    !Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
                    !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host)) continue;
                content.Add(UI.Button(link.Name, Prefix + "link." + index, Prefix + "link." + index)
                    .OpenIntent(WidgetIntentContracts.Web, JsonSerializer.SerializeToElement(new { url = link.Url })).Disabled(busy));
            }
        }
        else content.Add(Copy("No links are available for this game.", "links.empty"));
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
        UI.Stack(id,
            UI.Text(label, id + ".label").Classes("playnite-library-details-label"),
            UI.Text(value, id + ".value").Classes("playnite-library-details-field"))
            .Classes("playnite-library-details-value");
    private static WidgetElement Copy(string text, string id) => UI.Text(text, Prefix + id).Classes("playnite-library-details-description");
    private static WidgetElement Loading(string text) => UI.Row(Prefix + "loading",
        UI.LoadingIndicator(Prefix + "spinner", size: LoadingIndicatorSize.Compact), UI.Text(text, Prefix + "loading.text"));
    private static string Duration(long seconds) => seconds >= 3600 ? $"{seconds / 3600}h {seconds % 3600 / 60}m" : $"{seconds / 60}m";
    private static void AddParagraphs(List<WidgetElement> content, string text, string id)
    {
        var parts = text.Split("\n\n", 64, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        content.Add(UI.Stack(Prefix + id + ".paragraphs", parts.Select((part, index) =>
            Copy(part, index == 0 ? id : id + "." + index)).ToArray()).Classes("playnite-library-details-paragraphs"));
    }
}
