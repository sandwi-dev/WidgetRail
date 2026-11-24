using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Marketplace;

public sealed partial class MarketplaceWidget : Widget
{
    private enum Section { Browse, Installed, Updates, Themes }
    private sealed record State(MarketplaceSnapshot Snapshot, string? Selected = null,
        string? ReviewToken = null, Section Section = Section.Browse, bool Busy = false, string? Error = null, string? LastSelected = null, string Search = "", long FocusRequest = 0, bool SearchFocusRequested = false,
        string ThemeSearch = "", int ThemePage = 0, CatalogueTheme? SelectedTheme = null, string? ThemeReturnId = null, int ThemeImage = 0,
        WidgetFocusRequest? ThemePageFocus = null, WidgetFocusRequest? ReturnFocus = null);
    private readonly WidgetModel<State> model;
    private static readonly CompiledWidgetIntentContract open = CompiledWidgetIntentContract.Create(WidgetIntentContracts.Marketplace);
    private readonly WidgetQueryResource<ThemeBrowseQuery, ThemeBrowsePage> themePages;
    private readonly WidgetQueryResource<CatalogueThemeSelection, CatalogueThemeStatus> themeStatus;
    public MarketplaceWidget()
    {
        model = CreateModel(new State(MarketplaceSnapshot.Unavailable));
        themeStatus = CreateQueryResource<CatalogueThemeSelection, CatalogueThemeStatus>("marketplace.theme.status", new()
        {
            Load = async (theme, token) => (await HostServices.Marketplace.ExecuteAsync(new("theme-status", Theme: theme), token)).ThemeStatus
                ?? throw new InvalidDataException("Missing theme status."),
            MapError = _ => new("theme_status_failed", "Installation status is unavailable. Reopen the listing to check again."),
        });
        themePages = CreateQueryResource<ThemeBrowseQuery, ThemeBrowsePage>("marketplace.themes.pages", new()
        {
            Load = async (query, token) => (await HostServices.Marketplace.ExecuteAsync(new("themes", ThemeQuery: query), token)).Themes
                ?? throw new InvalidDataException("Missing theme catalogue."),
            MapError = _ => new("catalogue_unavailable", "Themes could not be loaded. Try again."),
            CacheDuration = TimeSpan.FromSeconds(30),
        });
    }
    protected override ValueTask OnActivatedAsync(CancellationToken token)
    {
        if (model.Value.Section == Section.Themes) LoadThemePage(model.Value);
        if (model.Value.SelectedTheme is { } selectedTheme) themeStatus.EnsureLoaded(Selection(selectedTheme), forceRefresh: true);
        Work(async ct =>
        {
            await ReadAsync(new("status"),ct);
            var state = model.Value;
            if (state.Snapshot.Operation?.State is not ("running" or "review"))
                await ReadAsync(new("refresh"),ct);
        });
        Operations.RunLatest("marketplace.poll",async context =>
        {
            while (true)
            {
                await Task.Delay(750,context.CancellationToken);
                var current = model.Value;
                if (current.Section == Section.Themes &&
                    ThemePageFor(current) is { Value: { } page, Status: WidgetResourceStatus.Ready })
                {
                    // Observe an existing check promptly. While offline, let the
                    // resource cache space out checks until the bridge reconnects.
                    if (page.Refreshing) LoadThemePage(current, true);
                    else if (page.Offline && current.SelectedTheme is null) LoadThemePage(current);
                }
                if (Operations.IsBusy("marketplace.work") || model.Value.Snapshot.Operation?.State != "running") continue;
                try { await ReadAsync(new("status"),context.CancellationToken); }
                catch (WidgetCapabilityException) { model.Update(s => s with { Error = "Status is unavailable. Check again before retrying an installation." }); }
            }
        });
        return ValueTask.CompletedTask;
    }
    protected override ValueTask OnDeactivatedAsync(CancellationToken token)
    {
        model.Update(s => s.ThemePageFocus is null && s.ReturnFocus is null ? s : s with { ThemePageFocus = null, ReturnFocus = null });
        return ValueTask.CompletedTask;
    }
    public override ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request,CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsActive || !request.IsWellFormed() || request.ContractId != open.Id || request.Version != open.Version ||
            request.SchemaDigest != open.SchemaDigest || !open.Accepts(request.Payload)) return ValueTask.FromResult(WidgetIntentResult.Rejected);
        model.Update(s => s with { Selected = null, SelectedTheme = null, ReviewToken = null, Section = Section.Browse,
            SearchFocusRequested = false, ThemePageFocus = null, ReturnFocus = null });
        return ValueTask.FromResult(WidgetIntentResult.Accepted);
    }
    public override ValueTask OnActionAsync(WidgetActionEvent action,CancellationToken cancellationToken = default)
    {
        var state = model.Value;
        if (state.ReturnFocus is not null) model.Update(s => s with { ReturnFocus = null });
        var operation = state.Snapshot.Operation;
        if (state.Section == Section.Themes && HandleThemeAction(state, action)) return ValueTask.CompletedTask;
        if (action.ActionId == "marketplace.search.focus")
        {
            if (state.ReviewToken is null)
                model.Update(s => s with { Selected = null, SearchFocusRequested = true, FocusRequest = CreateFocusRequest().Id, ThemePageFocus = null });
            return ValueTask.CompletedTask;
        }
        if (state.SearchFocusRequested) model.Update(s => s with { SearchFocusRequested = false });
        if (action.ActionId is "marketplace.search" or "marketplace.search.clear")
        {
            if (state.Selected is null && state.ReviewToken is null &&
                (action.ActionId == "marketplace.search.clear" || action.CommittedText is not null))
                model.Update(s => s with { Search = action.ActionId == "marketplace.search.clear" ? "" : action.CommittedText!.Trim(), LastSelected = null });
            return ValueTask.CompletedTask;
        }
        if (action.ActionId == "marketplace.back") { model.Update(s => s with { Selected = null, ReviewToken = null,
            ReturnFocus = s.Selected is null ? null : CreateFocusRequest() }); return ValueTask.CompletedTask; }
        if (action.ActionId == "marketplace.review.cancel") { model.Update(s => s with { ReviewToken = null }); return ValueTask.CompletedTask; }
        if (action.ActionId.StartsWith("marketplace.tab.", StringComparison.Ordinal))
        {
            var section = action.ActionId switch { "marketplace.tab.browse" => Section.Browse, "marketplace.tab.installed" => Section.Installed,
                "marketplace.tab.updates" => Section.Updates, "marketplace.tab.themes" => Section.Themes, _ => (Section?)null };
            if (section is not null && state.Selected is null && state.SelectedTheme is null && state.ReviewToken is null)
            {
                model.Update(s => s with { Section = section.Value, Selected = null, ReviewToken = null, ThemePageFocus = null });
                if (section == Section.Themes) LoadThemePage(model.Value);
            }
            return ValueTask.CompletedTask;
        }
        if (action.ActionId.StartsWith("marketplace.select.",StringComparison.Ordinal))
        {
            var selected = state.Snapshot.Items.FirstOrDefault(i => Key(i.Id) == action.ActionId["marketplace.select.".Length..]);
            if (selected is not null) model.Update(s => s with { Selected = selected.Id, LastSelected = selected.Id, ReviewToken = null });
            return ValueTask.CompletedTask;
        }
        if (state.Busy) return ValueTask.CompletedTask;
        if (action.ActionId.StartsWith("marketplace.update.", StringComparison.Ordinal) && operation?.State is not ("running" or "review"))
        {
            var updating = state.Snapshot.Items.FirstOrDefault(item => UpdateAvailable(item) && Key(item.Id) == action.ActionId["marketplace.update.".Length..]);
            if (updating is not null) Work(ct => ReadAsync(new("download-updates", PackageIds: [updating.Id]), ct));
            return ValueTask.CompletedTask;
        }
        switch (action.ActionId)
        {
            case "marketplace.update-all" or "marketplace.retry-updates" when operation?.State is not ("running" or "review"):
                var retry = state.Snapshot.UpdateBatch?.Items.Where(item => item.State is "failed" or "cancelled").Select(item => item.PackageId).ToHashSet(StringComparer.Ordinal);
                var ids = state.Snapshot.Items.Where(item => UpdateAvailable(item) &&
                    (action.ActionId == "marketplace.update-all" || retry?.Contains(item.Id) == true)).Select(item => item.Id).Take(16).ToArray();
                if (ids.Length > 0) Work(ct => ReadAsync(new("download-updates", PackageIds: ids), ct));
                break;
            case "marketplace.batch.review" when operation is { Kind: "download-updates", State: "review" } && state.Snapshot.UpdateBatch is { ReviewToken: { } batchToken }:
                model.Update(s => s with { ReviewToken = batchToken }); break;
            case "marketplace.batch.install" when operation is { Kind: "download-updates", State: "review" } &&
                state.Snapshot.UpdateBatch is { ReviewToken: { } acceptedToken } batch && state.ReviewToken == acceptedToken:
                model.Update(s => s with { ReviewToken = null });
                Work(ct => ReadAsync(new("install-updates", OperationId: operation.Id, ReviewToken: acceptedToken,
                    ApproveFullTrust: batch.Items.Any(item => item.Review?.FullTrust == true)), ct)); break;
            case "marketplace.refresh" when operation?.State is not ("running" or "review"): Work(ct => ReadAsync(new("refresh", ForceRefresh: true),ct)); break;
            case "marketplace.status": Work(ct => ReadAsync(new("status"),ct)); break;
            case "marketplace.download" when state.Selected is not null && operation?.State is not ("running" or "review"):
                Work(ct => ReadAsync(new("download",PackageId: state.Selected),ct)); break;
            case "marketplace.review.open" when operation is { State: "review", Review: { } review }:
                model.Update(s => s with { ReviewToken = review.Token }); break;
            case "marketplace.install" when operation is { State: "review", Review: { Compatible: true } approved } &&
                state.ReviewToken == approved.Token:
                model.Update(s => s with { ReviewToken = null });
                Work(ct => ReadAsync(new("install",OperationId: operation.Id,ReviewToken: approved.Token,ApproveFullTrust: approved.FullTrust),ct)); break;
            case "marketplace.cancel" when operation?.State is "running" or "review":
                Work(ct => ReadAsync(new("cancel",OperationId: operation.Id),ct)); break;
        }
        return ValueTask.CompletedTask;
    }
    private void Work(Func<CancellationToken,Task> work)
    {
        model.Update(s => s with { Busy = true, Error = null });
        Operations.RunLatest("marketplace.work",async context =>
        {
            try { await work(context.CancellationToken); }
            catch (WidgetCapabilityException error) { if(context.IsCurrent) model.Update(s => s with { Error = error.ErrorCode switch {
                "marketplace_review_required" => "The reviewed package changed or expired. Check status, then download and review it again.",
                "marketplace_refresh_required" => "Check available releases before downloading.",
                "theme_listing_changed" => "This theme listing changed. Return to Themes and reopen it.",
                "marketplace_busy" => "Another catalogue operation is in progress.",
                _ => "WidgetRail Catalogue is unavailable. Check status before retrying. You can still manage installed widgets in Settings." }, ReviewToken = null }); }
            catch (OperationCanceledException) when(context.CancellationToken.IsCancellationRequested) { }
            finally { if(context.IsCurrent) model.Update(s => s with { Busy = false }); }
        });
    }
    private async Task ReadAsync(MarketplaceCommand command,CancellationToken token)
    {
        var previous = model.Value.Snapshot.Operation;
        var snapshot = await HostServices.Marketplace.ExecuteAsync(command,token);
        token.ThrowIfCancellationRequested();
        model.Update(s => snapshot.Revision < s.Snapshot.Revision ? s : s with { Snapshot = snapshot, Error = null,
            Selected = snapshot.Items.Any(item => item.Id == s.Selected) ? s.Selected : null,
            ReviewToken = snapshot.Operation?.Review?.Token == s.ReviewToken || snapshot.UpdateBatch?.ReviewToken == s.ReviewToken ? s.ReviewToken : null });
        if (snapshot.Operation is { Kind: "theme-install" or "theme-install-apply" or "theme-apply", State: not "running" } finished &&
            previous != finished && model.Value.SelectedTheme is { } theme && theme.Id == finished.PackageId)
            themeStatus.EnsureLoaded(Selection(theme), forceRefresh: true);
    }
    private static string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    private static bool UpdateAvailable(MarketplaceItem item) => Version.TryParse(item.AvailableVersion,out var available) &&
        Version.TryParse(item.InstalledVersion,out var installed) && available > installed;
    private static string ItemStatus(MarketplaceItem item) => (UpdateAvailable(item) ? $"Update available · {item.InstalledVersion} → {item.AvailableVersion}" :
        item.InstalledVersion is not null ? "Installed " + item.InstalledVersion :
        item.AvailableVersion is not null ? "Available " + item.AvailableVersion : "Check releases for available versions") + (item.RetainedAvailability ? " · Previously checked release" : "");
    private static WidgetElement Manage(MarketplaceItem item,string id) => UI.Button("Manage in Settings","unused",id)
        .OpenIntent(WidgetIntentContracts.WidgetSettings,JsonSerializer.SerializeToElement(new { packageId = item.Id }),WidgetIntentPresentation.OpenWidget);

    public override WidgetView Render()
    {
        var state = model.Value; var operation = state.Snapshot.Operation; var running = operation?.State == "running";
        var busy = state.Busy || running || operation?.State == "review";
        var selected = state.Snapshot.Items.FirstOrDefault(i => i.Id == state.Selected);
        var browse = state.Section == Section.Themes ? null : BrowseContextFor(state);
        var header = new List<WidgetElement> { UI.Text(selected?.Name ?? "WidgetRail Catalogue","marketplace.title").Classes("page-heading") };
        var items = new List<WidgetElement>();
        var commands = new List<WidgetElement>();
        string initial;
        if (selected is null)
        {
            var updates = state.Snapshot.Items.Count(UpdateAvailable);
            if (state.SelectedTheme is null) header.Add(UI.Row("marketplace.section.navigation",
                UI.ControllerGlyph(ControllerButton.LeftBumper, "marketplace.section.previous.hint", "Previous section").Classes("marketplace-bumper-key"),
                UI.SegmentedTabs("marketplace.tabs", "marketplace.tab." + state.Section.ToString().ToLowerInvariant(),
                new SegmentedTab("marketplace.tab.browse", "Browse", "marketplace.tab.browse"),
                new SegmentedTab("marketplace.tab.installed", "Installed", "marketplace.tab.installed"),
                new SegmentedTab("marketplace.tab.updates", $"Updates ({updates})", "marketplace.tab.updates"),
                new SegmentedTab("marketplace.tab.themes", "Themes", "marketplace.tab.themes")),
                UI.ControllerGlyph(ControllerButton.RightBumper, "marketplace.section.next.hint", "Next section").Classes("marketplace-bumper-key"))
                .Classes("marketplace-section-navigation"));
            if (state.Section == Section.Themes) return RenderThemes(state, header);
            if (state.Section == Section.Updates && updates > 0)
                commands.Add(UI.Button(updates > 16 ? "Update next 16" : $"Update all ({updates})", "marketplace.update-all", "marketplace.update-all").Busy(busy).Classes("primary-button"));
            var visible = browse!.Items;
            WidgetElement ListingCard(MarketplaceItem item)
            {
                var id = CardId(item);
                if (state.Section == Section.Updates)
                {
                    var result = state.Snapshot.UpdateBatch?.Items.FirstOrDefault(result => result.PackageId == item.Id && result.Version == item.AvailableVersion);
                    var batchReview = operation is { Kind: "download-updates", State: "review" } && result?.State == "review";
                    var reviewing = operation is { State: "review", Review: not null } && operation.PackageId == item.Id;
                    var updating = operation is { State: "running" } && operation.PackageId == item.Id;
                    var status = result is null ? ItemStatus(item) : $"{result.Version} · {BatchItemStatus(result.State)}" +
                        (result.Message is null ? "" : "\n" + result.Message);
                    var actions = new List<WidgetElement>();
                    if (UpdateAvailable(item)) actions.Add(UI.Button(batchReview || reviewing ? "Review update" : updating ? "Updating…" :
                            result?.State is "failed" or "cancelled" ? "Retry update" : "Download update",
                            batchReview ? "marketplace.batch.review" : reviewing ? "marketplace.review.open" : "marketplace.update." + Key(item.Id), id + ".update")
                            .Busy(batchReview || reviewing ? state.Busy : busy));
                    actions.Add(Manage(item, id + ".manage"));
                    return (WidgetElement)UI.Stack(id,
                        UI.Text(item.Name, id + ".name").Classes("section-heading"),
                        ItemBadges(item, id),
                        UI.Text(status, id + ".status").Classes(result?.State == "failed" ? "diagnostic-error" : "page-help"),
                        UI.Row(id + ".actions", actions.ToArray())).Classes("marketplace-card");
                }
                return BrowseCard(item, id);
            }
            string? firstHomeCard = null;
            var grid = state.Section == Section.Browse && state.Search.Length == 0 && visible.Length > 0
                ? CuratedHome(visible, out firstHomeCard)
                : UI.ResponsiveGrid("marketplace.items", 360, 2, visible.Select(ListingCard).ToArray());
            var search = UI.TextEntry(state.Search, "Search widgets", "marketplace.search", "marketplace.search", 128).Suggest(browse.SourceKey);
            if (firstHomeCard is not null) search = search.FocusDown(firstHomeCard);
            header.Add(UI.Grid("marketplace.search.row", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Auto(), GridTrack.Auto()],
                UI.Stack("marketplace.search.group", search).RememberChildFocus("marketplace.search").InGrid(column: 0),
                UI.Button("Clear", "marketplace.search.clear", "marketplace.search.clear").InGrid(column: 1),
                UI.Button("Check for updates", "marketplace.refresh", "marketplace.refresh").Busy(busy).InGrid(column: 2)).Spacing(0, 8));
            items.Add(state.Section == Section.Updates ? grid : WithBrowsePreviews(grid));
            if (visible.Length == 0) items.Add(UI.Text(state.Search.Length > 0 ? "No widgets match this search in " + state.Section.ToString().ToLowerInvariant() + "." : state.Section switch
            {
                Section.Installed => "No catalogue widgets installed yet. Find one in Browse.",
                Section.Updates => state.Error is not null || operation?.State == "failed" ? "Updates could not be checked. Try again when ready." :
                    running && operation?.Kind == "refresh" ? "Checking available updates…" :
                    state.Snapshot.CheckedAtUtc is null ? "Check for updates to see available releases." : "No updates were found at the last check.",
                _ => state.Error is not null ? "Catalogue unavailable." : "Loading the catalogue…"
            },"marketplace.empty").Classes("page-help"));
            initial = state.Section != Section.Updates && visible.Any(i => i.Id == state.LastSelected)
                ? "marketplace.item." + Key(state.LastSelected!) : firstHomeCard ?? "marketplace.tab." + state.Section.ToString().ToLowerInvariant();
        }
        else
        {
            header.Clear();
            header.Add(UI.Grid("marketplace.detail.heading", [GridTrack.Auto()], [GridTrack.Auto(), GridTrack.Star()],
                UI.Button("Back", "marketplace.back", "marketplace.back"),
                UI.Text(selected.Name, "marketplace.title").Classes("page-heading").InGrid(column: 1)).Spacing(0, 12));
            items.Add(ItemBadges(selected, "marketplace.detail"));
            items.Add(UI.Text(selected.Description,"marketplace.description").Classes("diagnostic-line"));
            items.Add(UI.Text(ItemStatus(selected),"marketplace.version").Classes("page-help"));
            if (selected.Screenshots.Count == 1)
                items.Add(UI.Image(selected.Screenshots[0], "marketplace.screenshot", selected.Name + ", screenshot 1", ImageFit.Contain)
                    .Classes("marketplace-preview"));
            else if (selected.Screenshots.Count > 1)
            {
                var media = selected.Screenshots.Select((url, index) => new MediaPlayerGalleryItem(
                    "image-" + index, $"{selected.Name}, screenshot {index + 1}", MediaPlayerSource.PublicImage(url)) { PublicThumbnail = url }).ToArray();
                items.Add(UI.MediaGallery(media, "marketplace.gallery", inlineCarousel: true, selected.Name + " screenshots")
                    .WithThumbnailAnchor(FocusAnchorPoint.Center, 50).Classes("catalogue-carousel"));
            }
            items.AddRange(DetailSections(selected.Details));
            if (selected.SetupRequirements is { Count: > 0 } requirements)
                items.Add(UI.Stack("marketplace.requirements",
                    new[] { UI.Text("Before you install", "marketplace.requirements.title").Classes("section-heading", "catalogue-gold") }
                    .Cast<WidgetElement>().Concat(requirements.Select((text, index) =>
                        UI.Text("• " + text, "marketplace.requirement." + index).Classes("diagnostic-line"))).ToArray())
                    .Classes("marketplace-requirements"));
            if (selected.DownloadBytes is { } size) items.Add(UI.Text($"{size / 1048576d:0.0} MiB download" + (selected.IsPrerelease ? " · Preview release" : ""),"marketplace.size").Classes("page-help"));
            var ready = operation is { State: "review" } && operation.PackageId == selected.Id;
            if (ready) commands.Add(UI.Button("Review installation","marketplace.review.open","marketplace.primary").Busy(state.Busy));
            else if (selected.InstalledVersion is not null && !UpdateAvailable(selected)) commands.Add(Manage(selected,"marketplace.primary"));
            else commands.Add(UI.Button(selected.InstalledVersion is null ? "Download for review" : "Download update for review","marketplace.download","marketplace.primary").Busy(busy).Disabled(selected.AvailableVersion is null));
            if(selected.AvailableVersion is null) commands.Add(UI.Button("Check available releases","marketplace.refresh","marketplace.refresh").Busy(busy));
            if(selected.InstalledVersion is not null && (UpdateAvailable(selected) || ready)) commands.Add(Manage(selected,"marketplace.manage"));
            initial = selected.AvailableVersion is null ? "marketplace.refresh" : "marketplace.primary";
        }
        if (operation is { Kind: "download-updates", State: "review" } && state.Snapshot.UpdateBatch is { } readyBatch)
        {
            var count = readyBatch.Items.Count(item => item.State == "review");
            items.Add(UI.Button($"Review {count} {(count == 1 ? "update" : "updates")}", "marketplace.batch.review", "marketplace.batch.review.open").Busy(state.Busy));
        }
        if (state.Section == Section.Updates && state.Snapshot.UpdateBatch?.Items.Any(result => result.State is "failed" or "cancelled" &&
            state.Snapshot.Items.Any(item => item.Id == result.PackageId && UpdateAvailable(item))) == true)
            items.Add(UI.Button("Retry remaining updates", "marketplace.retry-updates", "marketplace.retry-updates").Busy(busy));
        if (operation is { State: "review", Review: not null } && operation.PackageId != selected?.Id &&
            (state.Section != Section.Updates || !state.Snapshot.Items.Any(item => item.Id == operation.PackageId && UpdateAvailable(item))))
            items.Add(UI.Button("Review downloaded widget","marketplace.review.open","marketplace.review.open").Busy(state.Busy));
        if ((state.Error ?? operation?.Message) is { } message) items.Add(UI.Text(message,"marketplace.status").Classes(state.Error is not null || operation?.State == "failed" ? "diagnostic-error" : "page-help"));
        if (operation is { State: "running", Kind: "download" or "download-updates" })
        {
            items.Add(UI.Text($"Downloaded {operation.DownloadedBytes / 1048576d:0.0} MiB","marketplace.progress.label").Classes("page-help"));
            if(operation.TotalBytes is > 0) items.Add(UI.Progress(Math.Min(operation.DownloadedBytes,operation.TotalBytes.Value),operation.TotalBytes.Value,"marketplace.progress","Download progress"));
        }
        if(operation?.State is "running" or "review") items.Add(UI.Button(operation.Kind is "download-updates" or "install-updates"
            ? operation.State == "review" ? "Discard downloads" : "Cancel remaining updates"
            : operation.State == "review" ? "Discard download" : "Cancel operation", "marketplace.cancel", "marketplace.cancel").Busy(state.Busy || operation.Message == "Cancelling…"));
        if(state.Error is not null) items.Add(UI.Button("Check status","marketplace.status","marketplace.status.retry").Busy(state.Busy));
        var tools = UI.Row("marketplace.commands", commands.ToArray()).Classes("marketplace-commands");
        if (selected is null && state.Snapshot.CheckedAtUtc is { } checkedAt)
        {
            var checkedLabel = UI.Text("Last checked " + checkedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), "marketplace.checked").Classes("page-help");
            if (state.Section == Section.Browse && state.Error is null && operation?.State != "failed") items.Add(checkedLabel);
            else header.Add(commands.Count > 0 ? UI.ResponsiveGrid("marketplace.toolbar", 340, 2, tools, checkedLabel) : checkedLabel);
        }
        else if (commands.Count > 0) header.Add(tools);
        var root = UI.Grid("marketplace.page", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            UI.Stack("marketplace.header", header.ToArray()).Classes("marketplace-header"),
            UI.VerticalScroll("marketplace.content", items.ToArray()).Classes("marketplace-content").InGrid(row: 1))
            .Spacing(12, 0).InputScope("marketplace.page").Classes("marketplace-widget");
        if (selected is not null) root = root.Shortcut(ControllerButton.B,"marketplace.back",label: "Catalogue");
        else root = root.Shortcut(ControllerButton.X,"marketplace.tab.updates",label: "Updates");
        if (state.ReviewToken is null) root = root.Shortcut(ControllerButton.LeftStick, "marketplace.search.focus", label: "Search");
        // Keep the guide stable while checks run. OnActionAsync already rejects
        // refresh during work, downloads and reviews; the button shows busy.
        root = root.Shortcut(ControllerButton.Y,"marketplace.refresh",label: "Check updates");
        if (selected is null && state.ReviewToken is null)
        {
            var previous = state.Section switch { Section.Browse => "themes", Section.Installed => "browse", _ => "installed" };
            var next = state.Section switch { Section.Browse => "installed", Section.Installed => "updates", _ => "themes" };
            root = root.Shortcut(ControllerButton.LeftBumper, "marketplace.tab." + previous, label: "Previous section")
                .Shortcut(ControllerButton.RightBumper, "marketplace.tab." + next, label: "Next section");
        }
        var view = new WidgetView(root,initial, Surface: new WidgetSurfaceHints
        {
            PreferredWidth = 1000, PreferredHeight = 640, MinimumWidth = 520, MinimumHeight = 400,
            Appearance = WidgetSurfaceAppearance.DesktopAcrylic,
            HeightMode = WidgetSurfaceAxisMode.Preferred,
        })
        {
            FocusGroupEntryRequest = selected is null && state.ReviewToken is null && state.SearchFocusRequested
                ? new() { RequestId = state.FocusRequest, GroupId = "marketplace.search.group" }
                : selected is null && state.ReviewToken is null && initial.StartsWith("marketplace.item.", StringComparison.Ordinal)
                    ? state.ReturnFocus?.FocusElement("marketplace.content", initial) : null,
        };
        if(state.ReviewToken is not null && operation?.Review is { } review && review.Token == state.ReviewToken)
            view = view.WithModal(WidgetRail.Internal.MarketplaceReviewPresentation.Create(review));
        else if (state.ReviewToken is not null && operation is { Kind: "download-updates", State: "review" } &&
            state.Snapshot.UpdateBatch is { } reviewedBatch && reviewedBatch.ReviewToken == state.ReviewToken)
            view = view.WithModal(WidgetRail.Internal.MarketplaceReviewPresentation.CreateBatch(reviewedBatch));
        return view;
    }

    private static IEnumerable<WidgetElement> DetailSections(IReadOnlyList<CatalogueDetail>? details)
    {
        foreach (var group in (details ?? []).GroupBy(row => row.Section, StringComparer.Ordinal))
        {
            var sectionId = "listing." + Key(group.Key);
            yield return UI.Text(group.Key, sectionId).Classes("catalogue-shelf-title");
            var rows = group.Select((row, index) =>
            {
                var id = sectionId + "." + index;
                return UI.ReadingSurface(id, row.Label + ". " + row.Value, ActionSurfaceOrientation.Vertical,
                    UI.Text(row.Label, id + ".name").Classes("listing-detail-name"),
                    UI.Text(row.Value, id + ".value").Classes("diagnostic-line")).Classes("marketplace-card", "listing-detail");
            }).ToArray();
            yield return UI.ResponsiveGrid(sectionId + ".rows", 320, 2, rows);
        }
    }

    private static WidgetElement BrowseCard(MarketplaceItem item, string id, WidgetElement[]? content = null,
        string style = "marketplace-browse-card")
    {
        content ??= [
            UI.Text(item.Name, id + ".name").Classes("section-heading", "marketplace-card-title"),
            UI.Text(item.Description, id + ".description").Classes("diagnostic-line", "marketplace-card-description"),
            ItemBadges(item, id) ];
        var label = item.Name + ". " + ItemStatus(item) + ". " + item.Description;
        if (item.SetupRequirements is { Count: > 0 } setup) label += " Setup: " + setup[0];
        var card = UI.ActionSurface("marketplace.select." + Key(item.Id), id, label,
            ActionSurfaceOrientation.Vertical, content).Classes("marketplace-card", style);
        return item.SetupRequirements is { Count: > 0 } ? card.PresentOnFocus(Key(item.Id)) : card;
    }

    private static WidgetElement ItemBadges(MarketplaceItem item, string id)
    {
        var badges = new List<WidgetElement> { UI.StatusBadge(
            item.RetainedAvailability ? "Cached release" : UpdateAvailable(item) ? "Update available" : item.InstalledVersion is not null ? "Installed" : item.AvailableVersion is not null ? "Available" : "Not checked",
            UpdateAvailable(item) ? StatusTone.Info : item.InstalledVersion is not null ? StatusTone.Success : StatusTone.Neutral,
            id + ".badge.state") };
        if (item.IsPrerelease) badges.Add(UI.StatusBadge("Preview", StatusTone.Warning, id + ".badge.preview"));
        if (item.SetupRequirements is { Count: > 0 }) badges.Add(UI.StatusBadge("Setup required", StatusTone.Warning, id + ".badge.setup"));
        return UI.Row(id + ".badges", badges.ToArray()).Classes("marketplace-badges");
    }

    private static string BatchItemStatus(string state) => state switch
    {
        "queued" => "Queued", "downloading" => "Downloading", "review" => "Ready to review", "installing" => "Installing",
        "completed" => "Updated", "cancelled" => "Cancelled", _ => "Needs attention",
    };
}
