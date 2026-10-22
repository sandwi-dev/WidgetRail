namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private bool hasExpandedDisclosures;

    private WidgetElementIdentity? CollapsedDisclosureHeader(
        IReadOnlyDictionary<string, Declaration> next, Binding? focused)
    {
        if (focused is null) return null;
        WidgetElementIdentity? target = null;
        var closest = int.MaxValue;
        foreach (var before in declarations.Values)
        {
            if (before.Node.Disclosure is not { IsExpanded: true } disclosure ||
                !next.TryGetValue(before.Node.Id, out var after) || before.Identity != after.Identity ||
                after.Node.Disclosure is not { IsExpanded: false } collapsed || collapsed.ContentId != disclosure.ContentId ||
                next.ContainsKey(disclosure.ContentId) || before.Identity.Scope != activeScope) continue;
            // Only collapsed disclosures need an ancestry walk. Ordinary frames
            // scan declarations once, without allocations or repeated full scans.
            var distance = 0;
            for (var cursor = declarations.GetValueOrDefault(focused.Identity.Id); cursor is not null;
                 cursor = cursor.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null, ++distance)
            {
                if (cursor.Node.Id != disclosure.ContentId || distance >= closest) continue;
                target = before.Identity;
                closest = distance;
                break;
            }
        }
        return target;
    }
}
