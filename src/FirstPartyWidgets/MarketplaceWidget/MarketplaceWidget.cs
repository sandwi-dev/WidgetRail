using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Marketplace;

public sealed class MarketplaceWidget : Widget
{
    private enum Section { Browse, Installed, Updates }
    private sealed record State(MarketplaceSnapshot Snapshot, string? Selected = null, int Screenshot = 0,
        string? ReviewToken = null, Section Section = Section.Browse, bool Busy = false, string? Error = null, string? LastSelected = null);
    private readonly WidgetModel<State> model;
    private static readonly CompiledWidgetIntentContract open = CompiledWidgetIntentContract.Create(WidgetIntentContracts.Marketplace);
    public MarketplaceWidget() => model = CreateModel(new State(MarketplaceSnapshot.Unavailable));
    protected override ValueTask OnActivatedAsync(CancellationToken token)
    {
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
                if (Operations.IsBusy("marketplace.work") || model.Value.Snapshot.Operation?.State != "running") continue;
                try { await ReadAsync(new("status"),context.CancellationToken); }
                catch (WidgetCapabilityException) { model.Update(s => s with { Error = "Status is unavailable. Check again before retrying an installation." }); }
            }
        });
        return ValueTask.CompletedTask;
    }
    public override ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request,CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsActive || !request.IsWellFormed() || request.ContractId != open.Id || request.Version != open.Version ||
            request.SchemaDigest != open.SchemaDigest || !open.Accepts(request.Payload)) return ValueTask.FromResult(WidgetIntentResult.Rejected);
        model.Update(s => s with { Selected = null, ReviewToken = null, Section = Section.Browse });
        return ValueTask.FromResult(WidgetIntentResult.Accepted);
    }
    public override ValueTask OnActionAsync(WidgetActionEvent action,CancellationToken cancellationToken = default)
    {
        var state = model.Value;
        var operation = state.Snapshot.Operation;
        if (action.ActionId == "marketplace.back") { model.Update(s => s with { Selected = null, ReviewToken = null }); return ValueTask.CompletedTask; }
        if (action.ActionId == "marketplace.review.cancel") { model.Update(s => s with { ReviewToken = null }); return ValueTask.CompletedTask; }
        if (action.ActionId.StartsWith("marketplace.tab.", StringComparison.Ordinal))
        {
            var section = action.ActionId switch { "marketplace.tab.browse" => Section.Browse, "marketplace.tab.installed" => Section.Installed,
                "marketplace.tab.updates" => Section.Updates, _ => (Section?)null };
            if (section is not null) model.Update(s => s with { Section = section.Value, Selected = null, ReviewToken = null });
            return ValueTask.CompletedTask;
        }
        if (action.ActionId.StartsWith("marketplace.select.",StringComparison.Ordinal))
        {
            var selected = state.Snapshot.Items.FirstOrDefault(i => Key(i.Id) == action.ActionId["marketplace.select.".Length..]);
            if (selected is not null) model.Update(s => s with { Selected = selected.Id, LastSelected = selected.Id, Screenshot = 0, ReviewToken = null });
            return ValueTask.CompletedTask;
        }
        if (action.ActionId is "marketplace.screenshot.next" or "marketplace.screenshot.previous")
        {
            var count = state.Snapshot.Items.FirstOrDefault(i => i.Id == state.Selected)?.Screenshots.Count ?? 0;
            if (count > 0) model.Update(s => s with { Screenshot = (s.Screenshot + count + (action.ActionId.EndsWith("next",StringComparison.Ordinal) ? 1 : -1)) % count });
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
                    (action.ActionId == "marketplace.update-all" || retry?.Contains(item.Id) == true)).Select(item => item.Id).ToArray();
                if (ids.Length > 0) Work(ct => ReadAsync(new("download-updates", PackageIds: ids), ct));
                break;
            case "marketplace.batch.review" when operation is { Kind: "download-updates", State: "review" } && state.Snapshot.UpdateBatch is { ReviewToken: { } batchToken }:
                model.Update(s => s with { ReviewToken = batchToken }); break;
            case "marketplace.batch.install" when operation is { Kind: "download-updates", State: "review" } &&
                state.Snapshot.UpdateBatch is { ReviewToken: { } acceptedToken } batch && state.ReviewToken == acceptedToken:
                model.Update(s => s with { ReviewToken = null });
                Work(ct => ReadAsync(new("install-updates", OperationId: operation.Id, ReviewToken: acceptedToken,
                    ApproveFullTrust: batch.Items.Any(item => item.Review?.FullTrust == true)), ct)); break;
            case "marketplace.refresh" when operation?.State is not ("running" or "review"): Work(ct => ReadAsync(new("refresh"),ct)); break;
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
                _ => "WidgetRail Catalogue is unavailable. Check status before retrying. You can still manage installed widgets in Settings." }, ReviewToken = null }); }
            catch (OperationCanceledException) when(context.CancellationToken.IsCancellationRequested) { }
            finally { if(context.IsCurrent) model.Update(s => s with { Busy = false }); }
        });
    }
    private async Task ReadAsync(MarketplaceCommand command,CancellationToken token)
    {
        var snapshot = await HostServices.Marketplace.ExecuteAsync(command,token);
        token.ThrowIfCancellationRequested();
        model.Update(s => snapshot.Revision < s.Snapshot.Revision ? s : s with { Snapshot = snapshot, Error = null,
            ReviewToken = snapshot.Operation?.Review?.Token == s.ReviewToken || snapshot.UpdateBatch?.ReviewToken == s.ReviewToken ? s.ReviewToken : null });
    }
    private static string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    private static bool UpdateAvailable(MarketplaceItem item) => Version.TryParse(item.AvailableVersion,out var available) &&
        Version.TryParse(item.InstalledVersion,out var installed) && available > installed;
    private static string ItemStatus(MarketplaceItem item) => UpdateAvailable(item) ? $"Update available · {item.InstalledVersion} → {item.AvailableVersion}" :
        item.InstalledVersion is not null ? "Installed " + item.InstalledVersion :
        item.AvailableVersion is not null ? "Available " + item.AvailableVersion : "Check releases for available versions";
    private static WidgetElement Manage(MarketplaceItem item,string id) => UI.Button("Manage in Settings","unused",id)
        .OpenIntent(WidgetIntentContracts.WidgetSettings,JsonSerializer.SerializeToElement(new { packageId = item.Id }),WidgetIntentPresentation.OpenWidget);

    public override WidgetView Render()
    {
        var state = model.Value; var operation = state.Snapshot.Operation; var running = operation?.State == "running";
        var busy = state.Busy || running || operation?.State == "review";
        var selected = state.Snapshot.Items.FirstOrDefault(i => i.Id == state.Selected);
        var items = new List<WidgetElement> { UI.Text(selected?.Name ?? "WidgetRail Catalogue","marketplace.title").Classes("page-heading") };
        string initial;
        if (selected is null)
        {
            var updates = state.Snapshot.Items.Count(UpdateAvailable);
            items.Add(UI.SegmentedTabs("marketplace.tabs", "marketplace.tab." + state.Section.ToString().ToLowerInvariant(),
                new SegmentedTab("marketplace.tab.browse", "Browse", "marketplace.tab.browse"),
                new SegmentedTab("marketplace.tab.installed", "Installed", "marketplace.tab.installed"),
                new SegmentedTab("marketplace.tab.updates", $"Updates ({updates})", "marketplace.tab.updates")));
            items.Add(UI.Text(state.Section switch
            {
                Section.Installed => "Your widgets from the catalogue. Manage other installed widgets in Settings → Widgets.",
                Section.Updates => "Review and update your widgets here. Your preferences and data are preserved.",
                _ => "Discover widgets for WidgetRail. Download to review before installing."
            },"marketplace.help").Classes("page-help"));
            items.Add(UI.Button("Check for updates","marketplace.refresh","marketplace.refresh").Busy(busy));
            if (state.Section == Section.Updates && updates > 0)
                items.Add(UI.Button($"Update all ({updates})", "marketplace.update-all", "marketplace.update-all").Busy(busy).Classes("primary-button"));
            if (state.Snapshot.CheckedAtUtc is { } checkedAt) items.Add(UI.Text("Last checked " + checkedAt.ToLocalTime().ToString("g",CultureInfo.CurrentCulture),"marketplace.checked").Classes("page-help"));
            var visible = state.Snapshot.Items.Where(i => state.Section switch
                { Section.Installed => i.InstalledVersion is not null,
                    Section.Updates => UpdateAvailable(i) || state.Snapshot.UpdateBatch?.Items.Any(result => result.PackageId == i.Id) == true, _ => true }).ToArray();
            var cards = visible.Select(item => {
                var id = "marketplace.item." + Key(item.Id);
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
                        UI.Text(status, id + ".status").Classes(result?.State == "failed" ? "diagnostic-error" : "page-help"),
                        UI.Row(id + ".actions", actions.ToArray())).Classes("marketplace-card");
                }
                return (WidgetElement)UI.ActionSurface("marketplace.select." + Key(item.Id),id,item.Name + ". " + ItemStatus(item),ActionSurfaceOrientation.Vertical,
                    UI.Text(item.Name,id + ".name").Classes("section-heading"),UI.Text(ItemStatus(item),id + ".status").Classes("page-help"),
                    UI.Text(item.Description,id + ".description").Classes("diagnostic-line")).Classes("marketplace-card"); }).ToArray();
            items.Add(UI.ResponsiveGrid("marketplace.items",260,2,cards));
            if (visible.Length == 0) items.Add(UI.Text(state.Section switch
            {
                Section.Installed => "No marketplace widgets installed yet. Find one in Browse.",
                Section.Updates => state.Error is not null || operation?.State == "failed" ? "Updates could not be checked. Try again when ready." :
                    running && operation?.Kind == "refresh" ? "Checking available updates…" :
                    state.Snapshot.CheckedAtUtc is null ? "Check for updates to see available releases." : "No updates were found at the last check.",
                _ => state.Error is not null ? "Catalogue unavailable." : "Loading the catalogue…"
            },"marketplace.empty").Classes("page-help"));
            initial = state.Section != Section.Updates && visible.Any(i => i.Id == state.LastSelected)
                ? "marketplace.item." + Key(state.LastSelected!) : "marketplace.tab." + state.Section.ToString().ToLowerInvariant();
        }
        else
        {
            items.Add(UI.Button("Back to marketplace","marketplace.back","marketplace.back"));
            items.Add(UI.Text(selected.Description,"marketplace.description").Classes("diagnostic-line"));
            items.Add(UI.Text(ItemStatus(selected),"marketplace.version").Classes("page-help"));
            if (selected.DownloadBytes is { } size) items.Add(UI.Text($"{size / 1048576d:0.0} MiB download" + (selected.IsPrerelease ? " · Preview release" : ""),"marketplace.size").Classes("page-help"));
            if (selected.Screenshots.Count > 0)
            {
                var index = Math.Clamp(state.Screenshot,0,selected.Screenshots.Count - 1);
                items.Add(UI.Image(selected.Screenshots[index],"marketplace.screenshot",$"{selected.Name}, screenshot {index + 1}",ImageFit.Contain).Classes("marketplace-preview"));
                if(selected.Screenshots.Count > 1) items.Add(UI.Row("marketplace.screenshots",UI.Button("Previous screenshot","marketplace.screenshot.previous","marketplace.screenshot.previous"),
                    UI.Text($"{index + 1} / {selected.Screenshots.Count}","marketplace.screenshot.count"),UI.Button("Next screenshot","marketplace.screenshot.next","marketplace.screenshot.next")));
            }
            var ready = operation is { State: "review" } && operation.PackageId == selected.Id;
            if (ready) items.Add(UI.Button("Review installation","marketplace.review.open","marketplace.primary").Busy(state.Busy));
            else if (selected.InstalledVersion is not null && !UpdateAvailable(selected)) items.Add(Manage(selected,"marketplace.primary"));
            else items.Add(UI.Button(selected.InstalledVersion is null ? "Download for review" : "Download update for review","marketplace.download","marketplace.primary").Busy(busy).Disabled(selected.AvailableVersion is null));
            if(selected.AvailableVersion is null) items.Add(UI.Button("Check available releases","marketplace.refresh","marketplace.refresh").Busy(busy));
            if(selected.InstalledVersion is not null && (UpdateAvailable(selected) || ready)) items.Add(Manage(selected,"marketplace.manage"));
            initial = selected.AvailableVersion is null ? "marketplace.refresh" : "marketplace.primary";
        }
        if (operation is { Kind: "download-updates", State: "review" } && state.Snapshot.UpdateBatch is { } readyBatch)
            items.Add(UI.Button($"Review {readyBatch.Items.Count(item => item.State == "review")} updates", "marketplace.batch.review", "marketplace.batch.review.open").Busy(state.Busy));
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
        var root = UI.VerticalScroll("marketplace.page",items.ToArray()).InputScope("marketplace.page").Classes("marketplace-widget");
        if (selected is not null) root = root.Shortcut(ControllerButton.B,"marketplace.back",label: "Catalogue");
        else root = root.Shortcut(ControllerButton.X,"marketplace.tab.updates",label: "Updates");
        if (!busy) root = root.Shortcut(ControllerButton.Y,"marketplace.refresh",label: "Check updates");
        if (selected?.Screenshots.Count > 1) root = root.Shortcut(ControllerButton.LeftBumper,"marketplace.screenshot.previous",label: "Previous screenshot")
            .Shortcut(ControllerButton.RightBumper,"marketplace.screenshot.next",label: "Next screenshot");
        var view = new WidgetView(root,initial);
        if(state.ReviewToken is not null && operation?.Review is { } review && review.Token == state.ReviewToken)
            view = view.WithModal(WidgetRail.Internal.MarketplaceReviewPresentation.Create(review));
        else if (state.ReviewToken is not null && operation is { Kind: "download-updates", State: "review" } &&
            state.Snapshot.UpdateBatch is { } reviewedBatch && reviewedBatch.ReviewToken == state.ReviewToken)
            view = view.WithModal(WidgetRail.Internal.MarketplaceReviewPresentation.CreateBatch(reviewedBatch));
        return view;
    }

    private static string BatchItemStatus(string state) => state switch
    {
        "queued" => "Queued", "downloading" => "Downloading", "review" => "Ready to review", "installing" => "Installing",
        "completed" => "Updated", "cancelled" => "Cancelled", _ => "Needs attention",
    };
}
