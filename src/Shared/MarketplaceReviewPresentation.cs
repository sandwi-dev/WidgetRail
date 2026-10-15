using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;
using WidgetRail.PlatformBroker;

namespace WidgetRail.Internal;

internal static class MarketplaceReviewPresentation
{
    internal static WidgetModal CreateBatch(MarketplaceUpdateBatch batch)
    {
        var reviews = batch.Items.Where(item => item.State == "review" && item.Review is not null).Select(item => item.Review!).ToArray();
        var fullTrust = reviews.Any(review => review.FullTrust);
        var content = new List<WidgetElement>
        {
            UI.Text($"Review {reviews.Length} verified updates", "marketplace.batch.review.heading").Classes("section-heading"),
            UI.Text(fullTrust
                ? "Includes full-access widgets, marked below. These run with your Windows account's access to files, network and other apps. Install only if you trust their source."
                : "These updates run in WidgetRail's sandbox. Permissions are listed below.", "marketplace.batch.review.trust")
                .Classes(fullTrust ? "diagnostic-error" : "page-help"),
            UI.Row("marketplace.batch.review.actions",
                UI.Button("Cancel", "marketplace.review.cancel", "marketplace.batch.review.cancel").Classes("secondary-button"),
                UI.Button(fullTrust ? "Install updates including full access" : "Install updates", "marketplace.batch.install", "marketplace.batch.review.install").Classes("primary-button"))
                .Classes("marketplace-review-actions"),
        };
        for (var index = 0; index < reviews.Length; index++)
        {
            var review = reviews[index];
            var id = "marketplace.batch.review.item." + index;
            content.Add(UI.Text($"{review.Name} · {review.InstalledVersion} → {review.Version}", id + ".version").Classes("section-heading"));
            content.Add(UI.Text(review.FullTrust ? "Full access" : "Sandboxed", id + ".trust").Classes(review.FullTrust ? "diagnostic-error" : "page-help"));
            // Chunk long permission lists so all reviewed authority is visible
            // without exceeding snapshot node/text bounds for a 16-item batch.
            foreach (var group in new[] { ("Required", review.RequiredPermissions), ("Optional", review.OptionalPermissions) })
            {
                var permissions = group.Item2.Select(permission => CapabilityPresentation.CapabilityName(permission) +
                    (review.AddedPermissions.Contains(permission) ? " · new" : "")).ToArray();
                content.Add(UI.Text(group.Item1 + " permissions" + (permissions.Length == 0 ? ": none" : ""), id + "." + group.Item1).Classes("page-help"));
                var chunk = 0;
                foreach (var names in permissions.Chunk(8))
                    content.Add(UI.Text(string.Join("\n", names), id + "." + group.Item1 + "." + chunk++).Classes("diagnostic-line"));
            }
        }
        content.Add(UI.Text("Your widget settings will be kept, along with enabled/disabled and stopped state. Permissions are requested again when needed. Failed downloads are excluded and can be retried separately.",
            "marketplace.batch.review.help").Classes("page-help"));
        return new("marketplace.batch.review", "Review updates", UI.Stack("marketplace.batch.review.content", content.ToArray()),
            "marketplace.batch.review.cancel", "marketplace.review.cancel");
    }

    internal static WidgetModal Create(MarketplaceReview review)
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
                content.Add(UI.Text(CapabilityPresentation.CapabilityName(permission) +
                    (review.InstalledVersion is not null && review.AddedPermissions.Contains(permission) ? " · new" : ""),
                    $"marketplace.review.{group.Item1}.{index}").Classes("diagnostic-line"));
            }
        }
        content.Add(UI.Text(review.InstalledVersion is null
            ? "The widget will be enabled after installation. Permissions are requested when a feature needs them."
            : "Your widget settings will be kept, along with enabled/disabled and stopped state. Permissions are requested again when a feature needs them. The previous version remains available.",
            "marketplace.review.help").Classes("page-help"));
        return new("marketplace.review", "Review installation", UI.Stack("marketplace.review.content", content.ToArray()),
            "marketplace.review.cancel", "marketplace.review.cancel");
    }

}
