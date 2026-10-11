using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Marketplace;

public sealed class MarketplaceWidget : Widget
{
    private sealed record State(MarketplaceSnapshot Snapshot, string? Selected = null, int Screenshot = 0,
        string? ReviewToken = null, bool UpdatesOnly = false, bool Busy = false, string? Error = null, string? LastSelected = null);
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
                await ReadAsync(new(state.Snapshot.CheckedAtUtc is null ? "refresh" : "inventory"),ct);
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
        model.Update(s => s with { Selected = null, ReviewToken = null, UpdatesOnly = false });
        return ValueTask.FromResult(WidgetIntentResult.Accepted);
    }
    public override ValueTask OnActionAsync(WidgetActionEvent action,CancellationToken cancellationToken = default)
    {
        var state = model.Value;
        var operation = state.Snapshot.Operation;
        if (action.ActionId == "marketplace.back") { model.Update(s => s with { Selected = null, ReviewToken = null }); return ValueTask.CompletedTask; }
        if (action.ActionId == "marketplace.review.cancel") { model.Update(s => s with { ReviewToken = null }); return ValueTask.CompletedTask; }
        if (action.ActionId == "marketplace.filter") { model.Update(s => s with { UpdatesOnly = !s.UpdatesOnly }); return ValueTask.CompletedTask; }
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
        switch (action.ActionId)
        {
            case "marketplace.refresh" when operation?.State is not ("running" or "review"): Work(ct => ReadAsync(new("refresh"),ct)); break;
            case "marketplace.status": Work(ct => ReadAsync(new("status"),ct)); break;
            case "marketplace.download" when state.Selected is not null && operation?.State is not ("running" or "review"):
                Work(ct => ReadAsync(new("download",PackageId: state.Selected),ct)); break;
            case "marketplace.review.open" when operation is { State: "review", Review: { } review }:
                model.Update(s => s with { Selected = review.PackageId, ReviewToken = review.Token }); break;
            case "marketplace.install" when operation is { State: "review", Review: { Compatible: true } approved } &&
                state.Selected == approved.PackageId && state.ReviewToken == approved.Token:
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
                _ => "Marketplace is unavailable. Check status before retrying. You can still manage installed widgets in Settings." }, ReviewToken = null }); }
            catch (OperationCanceledException) when(context.CancellationToken.IsCancellationRequested) { }
            finally { if(context.IsCurrent) model.Update(s => s with { Busy = false }); }
        });
    }
    private async Task ReadAsync(MarketplaceCommand command,CancellationToken token)
    {
        var snapshot = await HostServices.Marketplace.ExecuteAsync(command,token);
        token.ThrowIfCancellationRequested();
        model.Update(s => snapshot.Revision < s.Snapshot.Revision ? s : s with { Snapshot = snapshot, Error = null,
            ReviewToken = snapshot.Operation?.Review?.Token == s.ReviewToken ? s.ReviewToken : null });
    }
    private static string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    private static bool UpdateAvailable(MarketplaceItem item) => !item.AvailableVersionSaved && Version.TryParse(item.AvailableVersion,out var available) &&
        Version.TryParse(item.InstalledVersion,out var installed) && available > installed;
    private static string ItemStatus(MarketplaceItem item) => UpdateAvailable(item) ? $"Update available · {item.InstalledVersion} → {item.AvailableVersion}" :
        item.InstalledVersion is not null ? item.AvailableVersionSaved && item.AvailableVersion != item.InstalledVersion ? "Update already saved · manage in Settings" : "Installed " + item.InstalledVersion :
        item.AvailableVersion is not null ? "Available " + item.AvailableVersion : "Check releases for available versions";
    private static WidgetElement Manage(MarketplaceItem item,string id) => UI.Button("Manage in Settings","unused",id)
        .OpenIntent(WidgetIntentContracts.WidgetSettings,JsonSerializer.SerializeToElement(new { packageId = item.Id }),WidgetIntentPresentation.OpenWidget);

    public override WidgetView Render()
    {
        var state = model.Value; var operation = state.Snapshot.Operation; var running = operation?.State == "running";
        var busy = state.Busy || running || operation?.State == "review";
        var selected = state.Snapshot.Items.FirstOrDefault(i => i.Id == state.Selected);
        var items = new List<WidgetElement> { UI.Text(selected?.Name ?? "Marketplace","marketplace.title").Classes("page-heading") };
        string initial;
        if (selected is null)
        {
            var updates = state.Snapshot.Items.Count(UpdateAvailable);
            items.Add(UI.Text("Official WidgetRail add-ons. Download to review before installing.","marketplace.help").Classes("page-help"));
            items.Add(UI.Row("marketplace.actions", UI.Button("Check for updates","marketplace.refresh","marketplace.refresh").Busy(busy),
                UI.Button(state.UpdatesOnly ? $"Show all widgets · {updates} updates" : $"Show updates · {updates}","marketplace.filter","marketplace.filter")));
            if (state.Snapshot.CheckedAtUtc is { } checkedAt) items.Add(UI.Text("Last checked " + checkedAt.ToLocalTime().ToString("g",CultureInfo.CurrentCulture),"marketplace.checked").Classes("page-help"));
            var visible = state.Snapshot.Items.Where(i => !state.UpdatesOnly || UpdateAvailable(i)).ToArray();
            var cards = visible.Select(item => {
                var id = "marketplace.item." + Key(item.Id);
                return (WidgetElement)UI.ActionSurface("marketplace.select." + Key(item.Id),id,item.Name + ". " + ItemStatus(item),ActionSurfaceOrientation.Vertical,
                    UI.Text(item.Name,id + ".name").Classes("section-heading"),UI.Text(ItemStatus(item),id + ".status").Classes("page-help"),
                    UI.Text(item.Description,id + ".description").Classes("diagnostic-line")).Classes("marketplace-card"); }).ToArray();
            items.Add(UI.ResponsiveGrid("marketplace.items",260,2,cards));
            if (visible.Length == 0) items.Add(UI.Text(state.UpdatesOnly ? "No available updates. Check again to refresh release information." : state.Error is not null ? "Marketplace unavailable." : "Loading the official catalog…","marketplace.empty").Classes("page-help"));
            initial = visible.Any(i => i.Id == state.LastSelected) ? "marketplace.item." + Key(state.LastSelected!) : "marketplace.refresh";
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
            else if (selected.AvailableVersionSaved || selected.InstalledVersion is not null && !UpdateAvailable(selected)) items.Add(Manage(selected,"marketplace.primary"));
            else items.Add(UI.Button(selected.InstalledVersion is null ? "Download for review" : "Download update for review","marketplace.download","marketplace.primary").Busy(busy).Disabled(selected.AvailableVersion is null));
            if(selected.AvailableVersion is null) items.Add(UI.Button("Check available releases","marketplace.refresh","marketplace.refresh").Busy(busy));
            if(selected.InstalledVersion is not null && (UpdateAvailable(selected) || ready)) items.Add(Manage(selected,"marketplace.manage"));
            initial = selected.AvailableVersion is null ? "marketplace.refresh" : "marketplace.primary";
        }
        if (operation is { State: "review" } && operation.PackageId != selected?.Id) items.Add(UI.Button("Review downloaded widget","marketplace.review.open","marketplace.review.open").Busy(state.Busy));
        if ((state.Error ?? operation?.Message) is { } message) items.Add(UI.Text(message,"marketplace.status").Classes(state.Error is not null || operation?.State == "failed" ? "diagnostic-error" : "page-help"));
        if (operation is { State: "running", Kind: "download" })
        {
            items.Add(UI.Text($"Downloaded {operation.DownloadedBytes / 1048576d:0.0} MiB","marketplace.progress.label").Classes("page-help"));
            if(operation.TotalBytes is > 0) items.Add(UI.Progress(Math.Min(operation.DownloadedBytes,operation.TotalBytes.Value),operation.TotalBytes.Value,"marketplace.progress","Download progress"));
        }
        if(operation?.State is "running" or "review") items.Add(UI.Button(operation.State == "review" ? "Discard download" : "Cancel operation","marketplace.cancel","marketplace.cancel").Busy(state.Busy || operation.Message == "Cancelling…"));
        if(state.Error is not null) items.Add(UI.Button("Check status","marketplace.status","marketplace.status.retry").Busy(state.Busy));
        var root = UI.VerticalScroll("marketplace.page",items.ToArray()).InputScope("marketplace.page").Classes("marketplace-widget");
        if (selected is not null) root = root.Shortcut(ControllerButton.B,"marketplace.back",label: "Marketplace");
        else root = root.Shortcut(ControllerButton.X,"marketplace.filter",label: state.UpdatesOnly ? "All widgets" : "Updates");
        if (!busy) root = root.Shortcut(ControllerButton.Y,"marketplace.refresh",label: "Check updates");
        if (selected?.Screenshots.Count > 1) root = root.Shortcut(ControllerButton.LeftBumper,"marketplace.screenshot.previous",label: "Previous screenshot")
            .Shortcut(ControllerButton.RightBumper,"marketplace.screenshot.next",label: "Next screenshot");
        var view = new WidgetView(root,initial);
        if(state.ReviewToken is not null && operation?.Review is { } review && review.Token == state.ReviewToken && review.PackageId == selected?.Id)
            view = view.WithModal(WidgetRail.Internal.MarketplaceReviewPresentation.Create(review));
        return view;
    }
}
