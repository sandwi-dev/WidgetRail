using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    private BrowserSlot? expandedSlot;
    private (string Widget, string Document)? expandedKey;
    internal Action? ExpansionInvalidated { get; set; }

    internal async Task<bool> ExpandAsync(BrowserSurface source, BrowserSlot destination)
    {
        if (retired || expandedSlot is not null || expandedPopupSlot is not null || destination.Retired) return false;
        if (popupPresentation is { IsCurrent: true } popup && ReferenceEquals(popup.Child, source))
            return await ExpandPopupAsync(popup, destination);
        var pair = entries.FirstOrDefault(pair => pair.Value.Surface.Owns(source));
        if (pair.Value?.Slot is not { } origin || !slots.TryGetValue(origin, out var binding) ||
            !session.IsWebBrowserSessionCurrent(binding.Authority, binding.Document.Id)) return false;
        expandedSlot = destination;
        expandedKey = pair.Key;
        destination.Expanded = true;
        destination.PlacementChanged = Refresh;
        slots[destination] = binding with { Slot = destination };
        Refresh();
        if (reconciliation is { } pending) await pending;
        return ReferenceEquals(expandedSlot, destination) && destination.Surface?.Owns(source) == true;
    }

    internal void Collapse(BrowserSlot destination)
    {
        if (ReferenceEquals(expandedPopupSlot, destination))
        {
            if (popupReconciliation is not { IsCompleted: false }) popupReconciliation = CollapsePopupAsync(destination);
            return;
        }
        if (!ReferenceEquals(expandedSlot, destination)) return;
        expandedSlot = null; expandedKey = null;
        Unbind(destination);
    }

    private void SynchronizeExpandedBinding()
    {
        if (expandedSlot is not { } expanded || expandedKey is not { } key) return;
        // The synthetic placement borrows authority from the real declaration.
        // Renders, account changes and widget retirement remain authoritative.
        var source = slots.Values.Where(binding => binding.Slot != expanded && binding.Slot.Active &&
            !binding.Slot.Retired && binding.Authority.WidgetId == key.Widget && binding.Document.Id == key.Document &&
            session.IsWebBrowserSessionCurrent(binding.Authority, binding.Document.Id))
            .OrderByDescending(binding => binding.Slot.Pinned).ThenByDescending(binding => binding.Document.NavigationId).FirstOrDefault();
        if (source is null)
        {
            expandedSlot = null; expandedKey = null;
            slots.Remove(expanded); expanded.SetSurface(null); expanded.PlacementChanged = null;
            ExpansionInvalidated?.Invoke();
            return;
        }
        slots[expanded] = source with { Slot = expanded };
    }
}
