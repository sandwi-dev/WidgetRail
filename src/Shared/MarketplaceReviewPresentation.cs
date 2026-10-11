using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;
using WidgetRail.PlatformBroker;

namespace WidgetRail.Internal;

internal static class MarketplaceReviewPresentation
{
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
        content.Add(UI.Text("Installation leaves the widget off. Review permissions in Widgets before enabling it. Older versions and saved data are retained.", "marketplace.review.help").Classes("page-help"));
        return new("marketplace.review", "Review installation", UI.Stack("marketplace.review.content", content.ToArray()),
            "marketplace.review.cancel", "marketplace.review.cancel");
    }

}
