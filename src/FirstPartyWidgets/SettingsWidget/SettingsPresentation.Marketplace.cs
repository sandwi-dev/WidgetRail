using System.Globalization;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal static partial class SettingsPresentation
{
    private static WidgetView RenderMarketplace(StackElement header, SettingsPresentationState presentation)
    {
        var state = presentation.Marketplace ?? SettingsMarketplaceState.Empty;
        var operation = state.Snapshot.Operation;
        var running = operation?.State == "running";
        var detail = presentation.Page == SettingsPage.MarketplaceDetails;
        var selected = state.Snapshot.Items.FirstOrDefault(i => i.Id == state.SelectedId);
        var items = new List<WidgetElement>
        {
            UI.Text(detail ? selected?.Name ?? "Widget details" : "Widget marketplace", "marketplace.heading").Classes("page-heading"),
        };
        if (!detail || selected is null)
        {
            items.Add(UI.Text("Curated add-ons from WidgetRail releases. Download to review compatibility and permissions before installing.",
                "marketplace.help").Classes("page-help"));
            items.Add(UI.Button("Check for updates", "marketplace.refresh", "marketplace.refresh").Busy(running || operation?.State == "review").Classes("setting-row"));
            if (state.Snapshot.CheckedAtUtc is { } checkedAt)
                items.Add(UI.Text("Last checked " + checkedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), "marketplace.checked").Classes("page-help"));
            foreach (var item in state.Snapshot.Items)
            {
                var key = SettingsMarketplaceState.Key(item.Id);
                var id = "marketplace.item." + key;
                var status = operation is { State: "review" } && operation.PackageId == item.Id ? "Download ready to review" : ItemStatus(item);
                items.Add(UI.ActionSurface("marketplace.select." + key, id, item.Name + ". " + status,
                    ActionSurfaceOrientation.Horizontal,
                    UI.Stack(id + ".copy", UI.Text(item.Name, id + ".name").Classes("installed-entry-name"),
                        UI.Text(status, id + ".status").Classes("installed-entry-status")).Classes("installed-entry-copy"),
                    UI.Text("›", id + ".chevron").Classes("installed-entry-chevron")).Classes("installed-entry"));
            }
            if (state.Snapshot.Items.Count == 0)
                items.Add(UI.Text("The marketplace is unavailable. You can still install a local .wrwidget file from Widgets.", "marketplace.empty").Classes("page-help"));
        }
        else
        {
            items.Add(UI.Text(selected.Description, "marketplace.description").Classes("page-help"));
            items.Add(UI.Text(ItemStatus(selected), "marketplace.version").Classes("diagnostic-line"));
            if (selected.DownloadBytes is { } size)
                items.Add(UI.Text($"Download: {size / 1048576d:0.0} MiB" + (selected.IsPrerelease ? " · Preview release" : ""), "marketplace.size").Classes("page-help"));
            if (selected.Screenshots.Count != 0)
            {
                var index = Math.Clamp(state.ScreenshotIndex, 0, selected.Screenshots.Count - 1);
                items.Add(UI.Image(selected.Screenshots[index], "marketplace.screenshot", $"{selected.Name} screenshot {index + 1}", ImageFit.Contain)
                    .Classes("marketplace-preview"));
                if (selected.Screenshots.Count > 1)
                    items.Add(UI.Row("marketplace.screenshots", UI.Button("Previous screenshot", "marketplace.screenshot.previous", "marketplace.screenshot.previous"),
                        UI.Text($"{index + 1} / {selected.Screenshots.Count}", "marketplace.screenshot.count"),
                        UI.Button("Next screenshot", "marketplace.screenshot.next", "marketplace.screenshot.next")));
            }
            var ready = operation is { State: "review" } && operation.PackageId == selected.Id;
            var upToDate = selected.AvailableVersionSaved ||
                Version.TryParse(selected.AvailableVersion, out var available) && Version.TryParse(selected.InstalledVersion, out var installed) && available <= installed;
            items.Add(UI.Button(ready ? "Review installation" : upToDate ? "Manage in Widgets" : selected.InstalledVersion is null ? "Download for review" : "Download update for review",
                ready ? "marketplace.review.open" : upToDate ? "marketplace.manage" : "marketplace.download", "marketplace.primary")
                .Busy((running || operation?.State == "review" && !ready) && !upToDate).Disabled(!ready && selected.AvailableVersion is null).Classes("primary-button"));
            if (selected.AvailableVersion is null)
                items.Add(UI.Button("Check available releases", "marketplace.refresh", "marketplace.refresh").Busy(running || operation?.State == "review").Classes("setting-row"));
            if (selected.InstalledVersion is not null && (!upToDate || ready))
                items.Add(UI.Button("Manage versions and permissions in Widgets", "marketplace.manage", "marketplace.manage").Classes("setting-row"));
        }
        if ((state.Error ?? operation?.Message) is { } message)
        {
            var ownerName = state.Snapshot.Items.FirstOrDefault(i => i.Id == operation?.PackageId)?.Name;
            if (ownerName is not null && (!detail || selected?.Id != operation?.PackageId)) message = ownerName + ": " + message;
            items.Add(UI.Text(message, "marketplace.status").Classes(state.Error is not null || operation?.State == "failed" ? "diagnostic-error" : "page-help"));
        }
        if (operation is { State: "running", Kind: "download" })
        {
            items.Add(UI.Text($"Downloaded {operation.DownloadedBytes / 1048576d:0.0} MiB", "marketplace.progress.label").Classes("page-help"));
            if (operation.TotalBytes is > 0)
                items.Add(UI.Progress(Math.Min(operation.DownloadedBytes, operation.TotalBytes.Value), operation.TotalBytes.Value, "marketplace.progress", "Download progress"));
        }
        var cancellable = operation?.State is "running" or "review";
        items.Add(UI.Button(cancellable ? operation!.State == "review" ? "Discard download" : "Cancel operation" : "Back",
            cancellable ? "marketplace.cancel" : "back", "marketplace.back").Busy(operation?.Message == "Cancelling…").Classes("secondary-button"));
        var scope = detail ? "marketplace.details" : "marketplace.page";
        var initial = detail ? selected?.AvailableVersion is null ? "marketplace.refresh" : "marketplace.primary"
            : selected is null ? "marketplace.refresh" : "marketplace.item." + SettingsMarketplaceState.Key(selected.Id);
        var page = PageScope(scope, items.ToArray());
        if (detail && selected?.Screenshots.Count > 1)
            page = page.Shortcut(ControllerButton.LeftBumper, "marketplace.screenshot.previous", label: "Previous screenshot")
                .Shortcut(ControllerButton.RightBumper, "marketplace.screenshot.next", label: "Next screenshot");
        var view = View(header, page, initial, scope);
        if (state.ReviewOpen && operation is { State: "review", Review: { } review } && review.PackageId == selected?.Id)
            view = view.WithModal(ReviewDialog(review));
        return view;
    }

    private static WidgetModal ReviewDialog(MarketplaceReview review)
    {
        var content = new List<WidgetElement>
        {
            UI.Text(review.InstalledVersion is null ? $"Install {review.Name} {review.Version}" : $"Update {review.Name}: {review.InstalledVersion} → {review.Version}",
                "marketplace.review.version").Classes("section-heading"),
            UI.Text(review.Compatibility, "marketplace.review.compatibility").Classes(review.Compatible ? "diagnostic-ok" : "diagnostic-error"),
            UI.Text(review.FullTrust ? "Full access: this widget runs with the same Windows-account access as other desktop software. Only install it if you trust its source."
                : "This widget runs in WidgetRail's sandbox with the permissions listed below.", "marketplace.review.trust").Classes(review.FullTrust ? "diagnostic-error" : "page-help"),
        };
        if (review.InstalledVersion is not null && review.AddedPermissions.Count != 0)
            content.Add(UI.Text($"This update requests {review.AddedPermissions.Count} new permissions, marked below.",
                "marketplace.review.changes").Classes("page-help"));
        // Keep the default Cancel inside the scroll viewport near the warning,
        // rather than revealing the end of a long permission list on opening.
        content.Add(UI.Row("marketplace.review.actions",
            UI.Button("Cancel", "marketplace.review.cancel", "marketplace.review.cancel").Classes("secondary-button"),
            UI.Button(review.FullTrust ? "Install full-access widget" : "Install widget", "marketplace.install", "marketplace.review.install")
                .Disabled(!review.Compatible).Classes("primary-button")).Classes("marketplace-review-actions"));
        foreach (var group in new[] { ("Required", review.RequiredPermissions), ("Optional", review.OptionalPermissions) })
        {
            content.Add(UI.Text(group.Item1 + " permissions", "marketplace.review." + group.Item1).Classes("section-heading"));
            if (group.Item2.Count == 0) content.Add(UI.Text("None", "marketplace.review." + group.Item1 + ".none").Classes("page-help"));
            for (var index = 0; index < group.Item2.Count; index++)
            {
                var permission = group.Item2[index];
                content.Add(UI.Text(SettingsPermissionPresentation.CapabilityName(permission) +
                    (review.InstalledVersion is not null && review.AddedPermissions.Contains(permission) ? " · new" : ""),
                    $"marketplace.review.{group.Item1}.{index}").Classes("diagnostic-line"));
            }
        }
        content.Add(UI.Text("Installation leaves the widget off. Review permissions in Widgets before enabling it. Older versions and saved data are retained.", "marketplace.review.help").Classes("page-help"));
        return new("marketplace.review", "Review installation", UI.Stack("marketplace.review.content", content.ToArray()),
            "marketplace.review.cancel", "marketplace.review.cancel");
    }

    private static string ItemStatus(MarketplaceItem item)
    {
        if (item.AvailableVersion is null) return item.InstalledVersion is null ? "Check releases to see available versions" : "Installed " + item.InstalledVersion;
        if (item.InstalledVersion is null) return "Available " + item.AvailableVersion;
        if (item.AvailableVersionSaved && item.AvailableVersion != item.InstalledVersion)
            return $"Selected {item.InstalledVersion} · {item.AvailableVersion} already saved in Versions";
        return Version.TryParse(item.AvailableVersion, out var available) && Version.TryParse(item.InstalledVersion, out var installed) && available > installed
            ? $"Update available · {item.InstalledVersion} → {item.AvailableVersion}" : "Installed " + item.InstalledVersion + " · Up to date";
    }
}
