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

    internal static WidgetView Create(WidgetView parent, PlayniteLibraryItem item, bool current,
        string? launchingSavedId, string status, PlayniteLibraryLaunchState? launchState,
        bool loading = false, string? error = null, PlayniteDetailsExtras? extras = null,
        IReadOnlyList<PlayniteLibraryCategory>? categories = null, string? openingId = null,
        bool favorite = false, bool organizationBusy = false, string? completionStatus = null, long generation = 0,
        WidgetElement? achievementList = null, PlayniteAchievementBrowse? achievementQuery = null, Func<string,WidgetElement>? captures = null,
        string? captureEntry = null, string? captureReturn = null, WidgetElement? timeline = null, PlaynitePlayWeek? week = null, SessionTabView? sessions = null)
    {
        extras ??= new();
        var pageScope = openingId ?? "playnite-library.details";
        var game = extras.Full?.Game;
        var presentation = item.Presentation;
        var availability = PlayniteLibraryAvailabilityPresentation.Tile(item);
        var installed = presentation.Availability.State == WidgetAppLibraryAvailabilityState.Installed;
        var uninstalled = PlayniteLibraryAvailabilityPresentation.IsUninstalled(item);
        var launching = item.Value.SavedId == launchingSavedId;
        var busy = launching || extras.OperationBusy || organizationBusy;
        var play = UI.Button(launching ? "Launching…" : busy ? "Working..." : uninstalled ? "Install" : "Play",
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
            : UI.Icon(PlayniteIcons.Game, Prefix + "poster", presentation.DisplayName);
        poster = poster.Classes("playnite-library-details-poster");
        var completion = completionStatus ?? game?.CompletionStatus;
        var titleItems = new List<WidgetElement>
        {
            UI.Text(presentation.DisplayName, Prefix + "title").Classes("playnite-details-title"),
        };
        if (!current || launching || launchState is not null)
            titleItems.Add(UI.Text(!current ? "Game no longer in the current results" : launchState == PlayniteLibraryLaunchState.Running ? "Running" : status,
                Prefix + "status").Classes("playnite-library-details-meta"));

        var tabName = extras.Tab.ToString().ToLowerInvariant();
        var tabs = Enum.GetValues<PlayniteDetailsTab>().Select(tab =>
            new NavigationShellDestination(tab.ToString().ToLowerInvariant(), tab switch
                { PlayniteDetailsTab.Description => "Home", PlayniteDetailsTab.Activity => "Sessions", _ => tab.ToString() },
                Prefix + "tab." + tab.ToString().ToLowerInvariant(), tab switch
                {
                    PlayniteDetailsTab.Achievements => WidgetGlyph.Check,
                    PlayniteDetailsTab.Activity => WidgetGlyph.Rewind,
                    _ => WidgetGlyph.Play,
                })
            {
                CompactSeparatorBefore = tab != PlayniteDetailsTab.Description,
                CompactFocus = new FocusNeighbors(Up: play.IsDisabled == true ? FavoriteId : PlayId),
                Icon = tab switch
                {
                    PlayniteDetailsTab.Description => PlayniteIcons.Overview,
                    PlayniteDetailsTab.Information => PlayniteIcons.Information,
                    PlayniteDetailsTab.Achievements => PlayniteIcons.Achievements,
                    PlayniteDetailsTab.Captures => PlayniteIcons.Captures,
                    PlayniteDetailsTab.Activity => PlayniteIcons.Activity,
                    PlayniteDetailsTab.Links => PlayniteIcons.Links,
                    _ => throw new ArgumentOutOfRangeException(nameof(tab)),
                },
            }).ToArray();
        var addon = extras.Tab switch
        {
            PlayniteDetailsTab.Achievements when !extras.AchievementsLoading && extras.AchievementsError is null && extras.Achievements is not { Available: true } => PlayniteOptionalAddon.Achievements,
            PlayniteDetailsTab.Activity when !extras.ActivityLoading && extras.ActivityError is null && extras.Activity is not { Available: true } => PlayniteOptionalAddon.GameActivity,
            _ => PlayniteOptionalAddon.None,
        };
        var addonId = Prefix + tabName + ".addon";
        var install = addon == PlayniteOptionalAddon.None ? null : PlayniteLibraryWidget.MissingAddon(extras.AddonSetup, addon, addonId);
        var contentEntry = install is not null && !extras.AddonSetup.Busy ? addonId + ".install" :
            extras.Tab == PlayniteDetailsTab.Description && timeline is IndexedCollectionElement ? timeline.Id : sessions?.Entry ?? captureEntry;
        var navigation = UI.NavigationShellParts(Prefix + "navigation", tabName, contentEntry is null ? NavigationShellContentEntry.Unavailable : NavigationShellContentEntry.Available(contentEntry),
            UI.Stack(Prefix + "navigation.unused"), tabs,
            compactLeadingAdornment: UI.ControllerGlyph(ControllerButton.LeftBumper, Prefix + "previous.key").Classes("playnite-library-tab-key"),
            compactTrailingAdornment: UI.ControllerGlyph(ControllerButton.RightBumper, Prefix + "next.key").Classes("playnite-library-tab-key")).WithTransitions();
        var navigationRail = navigation.CompactNavigation.VisibleWhen(ResponsiveVisibility.Always)
            .AddClasses("playnite-library-details-tabs");
        var content = new List<WidgetElement>();
        if (extras.OperationMessage is { } message) content.Add(Copy(message, "operation.message"));
        var tabContent = new List<WidgetElement>();
        if (extras.Tab == PlayniteDetailsTab.Description)
        {
            if (timeline is IndexedCollectionElement days) timeline = days.FocusUp(navigation.CompactFocusId(tabName));
            var journal = UI.Grid(Prefix + "journal", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
                UI.Text("Activity", Prefix + "journal.title").Classes("playnite-journal-title"),
                (timeline ?? UI.Text("No recent activity for this game yet.", Prefix + "timeline.empty")
                    .Classes("playnite-library-details-meta")).InGrid(row: 1)).Spacing(8, 0);
            var summary = HomeSummary(extras, week);
            WidgetElement activity = summary is null ? journal : UI.Grid(Prefix + "home.columns", [GridTrack.Star()], [GridTrack.Star(2.5), GridTrack.Star()],
                journal, summary.InGrid(column: 1)).Spacing(0, 24);
            tabContent.Add(activity);
        }
        else if (extras.Tab is PlayniteDetailsTab.Information or PlayniteDetailsTab.Links)
        {
            if (loading) tabContent.Add(Loading("Loading game details..."));
            if (error is not null) tabContent.Add(Copy(error, "load.error"));
            if (extras.Tab == PlayniteDetailsTab.Information)
            {
                var facts = new List<WidgetElement>();
                tabContent.Add(CompletionSelect(extras, completion, generation, !current || busy));
                if (extras.CompletionStatusesError is { } completionError)
                    tabContent.Add(Copy(completionError, "completion.error"));
                AddInformation(facts, item, game);
                tabContent.Add(UI.ResponsiveGrid(Prefix + "information.facts", 240, 2, facts.ToArray()).Classes("playnite-library-details-facts"));
                tabContent.Add(UI.Divider(Prefix + "information.divider"));
                AddDescription(tabContent, item, game, loading, error);
            }
            else if (!loading && error is null) AddLinks(tabContent, game, busy);
        }
        else if (extras.Tab == PlayniteDetailsTab.Achievements) tabContent.Add(PlayniteAchievementPresentation.Page(extras, achievementQuery, achievementList, install));
        else if (extras.Tab == PlayniteDetailsTab.Captures) tabContent.Add(captures?.Invoke(navigation.CompactFocusId("captures")) ?? UI.Text("Loading captures…", Prefix + "captures.loading"));
        else if (sessions is not null) tabContent.Add(sessions.Body);
        else AddActivity(tabContent, extras, install);
        content.Add(UI.Stack(Prefix + "section.content", tabContent.ToArray()).Classes("playnite-library-details-content"));
        var tabBody = sessions is not null || extras.Tab is PlayniteDetailsTab.Achievements or PlayniteDetailsTab.Description
            ? UI.Grid(Prefix + tabName + ".viewport", [GridTrack.Star()], [GridTrack.Star()], tabContent.ToArray()).Classes("playnite-library-achievement-page")
            : (WidgetElement)UI.VerticalScroll(pageScope + ".scroll", content.ToArray()).Classes("playnite-library-details-scroll");
        var menu = PlayniteLibraryGameOptions.Create(item, favorite, categories, busy || !current)
            .Take(ProtocolConstants.MaximumContextActionCount - 3)
            .Concat(new WidgetContextAction[] {
                new(Prefix + "choose-status", "Change completion status\u2026", IsDisabled: busy || !current || extras.CompletionStatuses is not { Count: > 0 }),
                new(Prefix + "refresh", "Refresh details", IsDisabled: busy || extras.AchievementRefreshBusy || !current),
                new(Prefix + "uninstall", "Uninstall…", IsDisabled: busy || !current || !installed)
            }).ToArray();
        var options = UI.Row(OptionsId,
                UI.ControllerHint(ControllerButton.X, "Game options", Prefix + "options.hint"))
            .ContextMenu(ControllerButton.X,
                menu);
        var stats = new List<WidgetElement> { buttons };
        var metadata = presentation.Metadata;
        if (metadata?.PlaytimeMinutes is > 0)
            stats.Add(Value("Time played", Duration(metadata.PlaytimeMinutes.Value * 60), Prefix + "hero.playtime", "playnite-ink-time"));
        if (metadata?.LastPlayedAtUnixMilliseconds is >= -62135596800000 and <= 253402300799999)
            stats.Add(Value("Last played", DateTimeOffset.FromUnixTimeMilliseconds(metadata.LastPlayedAtUnixMilliseconds.Value)
                .ToLocalTime().ToString("MMM d, yyyy"), Prefix + "hero.last", "playnite-ink-time"));
        if (!string.IsNullOrWhiteSpace(presentation.Source.DisplayName))
            stats.Add(Value("Platform", presentation.Source.DisplayName, Prefix + "hero.platform"));
        var actions = UI.Row(Prefix + "identity", stats.ToArray()).Classes("playnite-hero-actions");
        var trailerId = extras.StoreMedia?.PublisherTrailerId;
        var trailer = trailerId is null || trailerId == extras.UnavailablePublisherTrailer || extras.StoreMediaLoading ? null :
            PlayniteTrailerPresentation.Session(trailerId, presentation.DisplayName, generation);
        WidgetElement? media = StoreGallery(extras.StoreMedia, presentation.DisplayName, trailer);
        if (media is null && extras.StoreMediaLoading)
            media = UI.ReadingSurface(Prefix + "store.gallery", "Loading game media", ActionSurfaceOrientation.Vertical,
                UI.Grid(Prefix + "store.loading-stage", [GridTrack.Star()], [GridTrack.Star()],
                    UI.LoadingIndicator(Prefix + "store.progress").AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center))
                    .Classes("playnite-store-media")).Classes("playnite-store-loading");
        var stage = media is null
            ? UI.Grid(Prefix + "hero.stage", [GridTrack.Auto()], [GridTrack.Auto(), GridTrack.Star()], poster, actions.InGrid(column: 1))
            : UI.Grid(Prefix + "hero.stage", [GridTrack.Auto(), GridTrack.Auto()], [GridTrack.Star()], media, actions.InGrid(row: 1));
        var header = UI.BackgroundSurface(stage, Prefix + "overview-header")
            .Classes("playnite-details-artwork");
        var section = UI.Grid(Prefix + "tab.content", [GridTrack.Star()], [GridTrack.Star()], tabBody)
            .Classes("playnite-library-details-layout")
            .TransitionContent(Prefix + "navigation", tabName, (int)extras.Tab);
        var body = UI.Grid(ContentId, [GridTrack.Auto(), GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
                header, navigationRail.InGrid(row: 1), section.InGrid(row: 2))
            .Spacing(16, 0).RevealBodyOnFocus().Classes("playnite-library-details-layout");
        var back = UI.IconButton(PlayniteIcons.Back, PlayniteLibraryActions.DetailsClose,
            Prefix + "back", "Back to games", size: IconButtonSize.Medium);
        var heading = UI.Grid(Prefix + "heading", [GridTrack.Auto()], [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto()],
            back, UI.Stack(Prefix + "title-block", titleItems.ToArray()).Classes("playnite-details-title-block").InGrid(column: 1),
            UI.Row(Prefix + "header-hints", options,
                UI.ControllerHint(ControllerButton.Y, extras.AchievementRefreshBusy ? "Refreshing\u2026" : "Refresh", Prefix + "refresh.hint"),
                UI.ControllerHint(ControllerButton.B, "Back", Prefix + "close.hint"))
                .Classes("playnite-library-details-header-hints").InGrid(column: 2)).Spacing(0, 16);
        var frame = UI.Grid(Prefix + "frame", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            heading, body.InGrid(row: 1)).Spacing(12, 0).Classes("playnite-library-details-layout")
            .RememberChildFocus(media?.Id ?? navigation.CompactFocusId(tabName))
            .Shortcut(ControllerButton.LeftBumper, Prefix + "tab.previous", "Previous details tab")
            .Shortcut(ControllerButton.RightBumper, Prefix + "tab.next", "Next details tab")
            .Shortcut(ControllerButton.Y, Prefix + "refresh", "Refresh details");
        if (extras.ChoosingCompletion && extras.CompletionStatuses is { Count: > 0 } statuses)
        {
            var choices = statuses.Select(value =>
            {
                var action = CompletionAction(generation, value);
                return UI.ChoiceRow(value, action, action,
                    isSelected: string.Equals(value, completion, StringComparison.OrdinalIgnoreCase));
            }).ToArray();
            var initial = choices.FirstOrDefault(choice => choice.IsSelected == true) ?? choices[0];
            return parent.WithModal(new WidgetModal(pageScope + ".status-dialog", "Completion status",
                UI.Stack(Prefix + "status-choices", choices), initial.Id!, Prefix + "cancel-status"));
        }
        if (extras.ConfirmUninstall)
            return parent.WithModal(new WidgetModal(pageScope + ".uninstall-dialog", "Uninstall " + item.Presentation.DisplayName + "?",
                UI.Stack(Prefix + "confirm",
                    UI.Text("Playnite will ask the game's launcher to uninstall it. Follow any prompts in Playnite or the launcher.",
                        Prefix + "confirm.copy").Classes("playnite-library-details-description"),
                    UI.Button("Cancel", Prefix + "uninstall.cancel", Prefix + "uninstall.cancel"),
                    UI.Button("Uninstall", Prefix + "uninstall.confirm", Prefix + "uninstall.confirm")),
                Prefix + "uninstall.cancel", Prefix + "uninstall.cancel")
            {
                HeaderActions = UI.ControllerHint(ControllerButton.B, "Cancel", Prefix + "cancel.hint"),
            });
        var details = parent.WithModal(new WidgetModal(pageScope, presentation.DisplayName, frame,
            captureReturn ?? media?.Id ?? navigation.CompactFocusId(tabName),
            sessions?.Back ?? PlayniteLibraryActions.DetailsClose)
            { ShowHeader = false, ScrollContent = false, StyleClass = "playnite-game-details-dialog" });
        if (trailer is not null) details = details with { EmbeddedMediaSession = trailer };
        if (extras.Tab == PlayniteDetailsTab.Captures && extras.CapturePageEntry is { } captureFocus &&
            captureReturn is not null && captureReturn == extras.CaptureReturnId)
            return details with { FocusGroupEntryRequest = captureFocus.FocusElement(Prefix + "frame", captureReturn) };
        if (sessions?.Focus is { } sessionFocus) return details with { FocusGroupEntryRequest = sessionFocus };
        return extras.TabFocus is { } focus
            ? details with { FocusGroupEntryRequest = focus.FocusElement(Prefix + "frame", navigation.CompactFocusId(tabName)) }
            : details;
    }

    internal static string CompletionAction(long generation, string status) =>
        Prefix + "completion." + generation.ToString(CultureInfo.InvariantCulture) + "." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(status))).ToLowerInvariant();

    private static MediaPlayerElement? StoreGallery(SteamStoreMedia? media, string gameName, EmbeddedMediaSession? embedded)
    {
        if (media is null && embedded is null) return null;
        media ??= SteamStoreMedia.Empty;
        var items = new List<MediaPlayerGalleryItem>();
        foreach (var trailer in media.Trailers)
        {
            var source = trailer.Dash is not null ? MediaPlayerSource.PublicDash(trailer.Dash) :
                trailer.Mp4 is not null ? MediaPlayerSource.PublicFile(trailer.Mp4) : null;
            if (source is null) continue;
            items.Add(new($"movie-{trailer.Id}", trailer.Name, source,
                new(AutoPlay: true, Muted: true)) { PublicThumbnail = trailer.Thumbnail });
        }
        if (embedded is not null && media.PublisherTrailerId is { } videoId)
            items.Add(new("publisher-trailer", "Game trailer on YouTube", MediaPlayerSource.EmbeddedSession(embedded.Id, videoId),
                new(AutoPlay: true, Muted: true)) { PublicThumbnail = $"https://i.ytimg.com/vi/{videoId}/mqdefault.jpg",
                    PublicPreviewImage = $"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg" });
        for (var i = 0; i < media.Screenshots.Count && items.Count < MediaPlayerDefinition.MaximumGalleryItems; i++)
        {
            var image = media.Screenshots[i];
            items.Add(new($"image-{image.Id}", $"Screenshot {i + 1} of {media.Screenshots.Count}",
                MediaPlayerSource.PublicImage(image.Image)) { PublicThumbnail = image.Thumbnail });
        }
        return items.Count == 0 ? null : UI.MediaGallery(items, Prefix + "store.gallery", inlineCarousel: true, gameName + " store media",
            new(Muted: true)).WithThumbnailAnchor(FocusAnchorPoint.Center, 50).Classes("playnite-store-media");
    }

    private static WidgetElement? HomeSummary(PlayniteDetailsExtras extras, PlaynitePlayWeek? week)
    {
        var cards = new List<WidgetElement>();
        if (extras.Achievements is { Available: true, Total: > 0 } achievements)
        {
            var unlocked = achievements.Unlocked;
            var progress = UI.Stack(Prefix + "home.progress",
                UI.Icon(PlayniteIcons.Achievements, Prefix + "home.trophy", "Achievements").Classes("playnite-journal-trophy"),
                UI.Text("Achievements", Prefix + "home.progress.title").Classes("playnite-journal-title"),
                UI.Text($"{unlocked} of {achievements.Total} unlocked", Prefix + "home.progress.count").Classes("playnite-library-details-meta"),
                UI.Progress(unlocked, achievements.Total, Prefix + "home.progress.bar").Classes("playnite-journal-progress"),
                UI.Button("View achievements", Prefix + "tab.achievements", Prefix + "home.progress.open"))
                .Classes("playnite-home-fact-card");
            cards.Add(progress);
        }
        if (week is not null) cards.Add(PlayWeekChart(week));
        return cards.Count == 0 ? null : UI.VerticalScroll(Prefix + "home.sidebar", UI.Stack(Prefix + "home.summary", cards.ToArray()).Classes("playnite-home-facts"));
    }

    private static WidgetElement PlayWeekChart(PlaynitePlayWeek week)
    {
        const string id = Prefix + "week";
        var cells = new List<WidgetElement>();
        var high = Math.Max(1, week.DailySeconds.Max());
        for (var i = 0; i < week.DailySeconds.Count; i++)
        {
            var date = week.FirstDay.AddDays(i);
            var seconds = week.DailySeconds[i];
            var share = (double)seconds / high;
            if (seconds > 0)
            {
                cells.Add(UI.Grid(id + ".column." + i, [GridTrack.Star(Math.Max(.001, 1 - share)), GridTrack.Star(share)], [],
                    UI.Stack(id + ".bar." + i).Classes("journal-band-played", "week-bar").InGrid(row: 1))
                    .Classes("week-column").InGrid(column: i));
            }
            cells.Add(UI.Text(date.ToString("ddd", CultureInfo.CurrentCulture)[..1], id + ".day." + i, $"{date:dddd}, {Duration(seconds)}")
                .Classes("story-credit-time").InGrid(row: 1, column: i).AlignInGrid(GridCellAlignment.Center));
        }
        var total = (long)Math.Min(long.MaxValue, week.DailySeconds.Sum(s => (decimal)s));
        var last = week.FirstDay.AddDays(week.DailySeconds.Count - 1);
        return UI.Stack(id, UI.Text("Playtime", id + ".title").Classes("playnite-journal-title"),
            UI.Text(Duration(total), id + ".total").Classes("story-duration"),
            UI.Text($"{week.FirstDay:MMM d} – {last:MMM d, yyyy}", id + ".range").Classes("story-credit-time"),
            UI.Grid(id + ".chart", [GridTrack.Pixel(56), GridTrack.Auto()], week.DailySeconds.Select(_ => GridTrack.Star()).ToArray(),
                cells.ToArray()).Spacing(4, 4).Classes("week-chart")).Classes("playnite-home-fact-card");
    }

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
        if (metadata?.PlaytimeMinutes is { } minutes) content.Add(Value("Time played", Duration(minutes * 60), Prefix + "playtime", "playnite-ink-time"));
        if (metadata?.LastPlayedAtUnixMilliseconds is { } played && played is >= -62135596800000 and <= 253402300799999)
            content.Add(Value("Last played", DateTimeOffset.FromUnixTimeMilliseconds(played).ToLocalTime().ToString("d"), Prefix + "last-played", "playnite-ink-time"));
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
            AddValue("Ratings", string.Join(" | ", scores), "ratings", "playnite-ink-reward");
            AddValue("Tags", string.Join(", ", game.Tags), "tags");
        }
        AddValue("Version", metadata?.Version, "version");
        if (content.Count == 0) content.Add(Copy("No additional information is available for this game.", "information.empty"));
        void AddValue(string label, string? value, string id, string ink = "playnite-ink-source")
        {
            if (!string.IsNullOrWhiteSpace(value)) content.Add(Value(label, value, Prefix + id, ink));
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
            if (content.Count > 0) content.Add(UI.Divider(Prefix + "notes-divider"));
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

    private static void AddActivity(List<WidgetElement> content, PlayniteDetailsExtras state, WidgetElement? install)
    {
        if (state.ActivityLoading) { content.Add(Loading("Loading activity...")); return; }
        if (state.ActivityError is { } error) { content.Add(Copy(error, "activity.error")); return; }
        if (state.Activity is not { Available: true } data)
        {
            if (install is not null)
            {
                content.Add(Copy("GameActivity records your play sessions.", "activity.unavailable"));
                content.Add(install);
            }
            else content.Add(Copy("No GameActivity sessions are available for this game. Check that GameActivity is enabled in Playnite.", "activity.unavailable"));
            return;
        }
        if (data.Sessions.Count == 0) content.Add(Copy("GameActivity has no recorded sessions for this game.", "activity.empty"));
    }
    private static WidgetElement Value(string label, string value, string id, string ink = "playnite-ink-source") =>
        UI.Stack(id,
            UI.Text(label, id + ".label").Classes("playnite-library-details-label"),
            UI.Text(value, id + ".value").Classes("playnite-library-details-field", ink))
            .Classes("playnite-library-details-value");
    private static WidgetElement Copy(string text, string id) => UI.Text(text, Prefix + id).Classes("playnite-library-details-description");
    private static WidgetElement Loading(string text) => UI.Row(Prefix + "loading",
        UI.LoadingIndicator(Prefix + "spinner", size: LoadingIndicatorSize.Compact), UI.Text(text, Prefix + "loading.text"));
    internal static string Duration(long seconds) => seconds >= 3600 ? $"{seconds / 3600}h {seconds % 3600 / 60}m"
        : seconds >= 60 ? $"{seconds / 60}m" : $"{seconds}s";
    private static void AddParagraphs(List<WidgetElement> content, string text, string id)
    {
        var parts = text.Split("\n\n", 64, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        content.Add(UI.Stack(Prefix + id + ".paragraphs", parts.Select((part, index) =>
            Copy(part, index == 0 ? id : id + "." + index)).ToArray()).Classes("playnite-library-details-paragraphs"));
    }
}
