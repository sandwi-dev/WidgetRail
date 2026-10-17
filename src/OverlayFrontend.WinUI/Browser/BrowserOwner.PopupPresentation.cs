using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>A borrowed live child; its parent remains the authority and lifetime owner.</summary>
internal sealed class BrowserPopupPresentation(string widgetId, BrowserSurface parent, BrowserSurface child, BrowserSlot slot, Grid origin)
{
    internal string WidgetId { get; } = widgetId;
    internal BrowserSurface Parent { get; } = parent;
    internal BrowserSurface Child { get; } = child;
    internal BrowserSlot Slot { get; } = slot;
    internal Grid Origin { get; } = origin;
    internal bool Released { get; set; }
    internal bool IsCurrent => !Released && Parent.IsDetachedPopup(Child) && Child.IsReady;
}

internal sealed partial class BrowserOwner
{
    private BrowserPopupPresentation? popupPresentation;
    private readonly SemaphoreSlim popupPlacement = new(1, 1);
    private BrowserSlot? expandedPopupSlot;
    private Task? popupReconciliation;
    internal Action<BrowserPopupPresentation>? PopupPresentationInvalidated { get; set; }

    private async Task<CoreWebView2> PreparePopupAsync(BrowserSurface child, Grid destination, bool deferPlacement, CancellationToken token)
    {
        if (retired || !entries.Values.Select(entry => entry.Surface).Any(root => root.Owns(child))) throw new OperationCanceledException();
        if (child.Parent is null) parking.Children.Add(child);
        var core = await child.PrepareCoreAsync(token);
        token.ThrowIfCancellationRequested();
        if (!deferPlacement) await MoveAsync(child, destination, () => !token.IsCancellationRequested && child.IsReady);
        token.ThrowIfCancellationRequested();
        return core;
    }

    internal BrowserSurface[] PinnablePopups(string widgetId) => retired ? [] : entries
        .Where(pair => pair.Key.Widget == widgetId && session.IsWebBrowserSessionCurrent(pair.Value.Authority, pair.Key.Document))
        .Select(pair => pair.Value.Surface.PinnablePopup).OfType<BrowserSurface>().ToArray();
    internal BrowserSurface? PinnablePopup(string widgetId) => PinnablePopups(widgetId).FirstOrDefault();
    internal bool CanPinPopup(string widgetId, BrowserSurface child) => PinnablePopups(widgetId).Contains(child);

    internal async Task<BrowserPopupPresentation?> PinPopupAsync(BrowserSurface child, BrowserSlot destination)
    {
        await popupPlacement.WaitAsync();
        BrowserPopupPresentation? lease = null;
        var attached = false;
        try
        {
            while (reconciliation is { IsCompleted: false } pending) await pending;
            var pair = entries.FirstOrDefault(pair => ReferenceEquals(pair.Value.Surface.PinnablePopup, child));
            if (retired || popupPresentation is not null || destination.Retired || !destination.CanDisplay ||
                pair.Value is not { } entry || !session.IsWebBrowserSessionCurrent(entry.Authority, pair.Key.Document)) return null;
            var origin = entry.Surface.DetachPopup(child);
            if (origin is null) return null;
            lease = new(entry.Authority.WidgetId, entry.Surface, child, destination, origin);
            popupPresentation = lease;
            destination.Pinned = true;
            destination.PlacementChanged = RefreshPopupPresentation;
            await MoveAsync(child, destination.Host, () => lease.IsCurrent && destination.CanDisplay);
            if (retired || !lease.IsCurrent || !destination.CanDisplay) return null;
            destination.SetSurface(child);
            RefreshPopupPresentation();
            attached = true;
            return lease;
        }
        finally
        {
            try
            {
                if (lease is not null && !attached) await ReturnPopupCoreAsync(lease);
            }
            finally { popupPlacement.Release(); }
        }
    }

    internal async Task ReleasePopupPresentationAsync(BrowserPopupPresentation lease, bool closePage = false)
    {
        await popupPlacement.WaitAsync();
        try { await ReturnPopupCoreAsync(lease, closePage); }
        finally { popupPlacement.Release(); }
    }

    private async Task ReturnPopupCoreAsync(BrowserPopupPresentation lease, bool closePage = false)
    {
        if (lease.Released) return;
        lease.Released = true;
        if (ReferenceEquals(popupPresentation, lease)) popupPresentation = null;
        InvalidatePopupExpansion();
        lease.Slot.PlacementChanged = null;
        lease.Slot.SetSurface(null);
        lease.Child.SetPresentation(false, false);
        if (retired || !lease.Parent.IsDetachedPopup(lease.Child)) return;
        if (closePage) { await lease.Parent.ClosePopupAsync(); return; }
        if (!lease.Child.IsReady) return;
        try
        {
            await MoveAsync(lease.Child, lease.Origin, () => lease.Parent.IsDetachedPopup(lease.Child) && lease.Child.IsReady);
            if (!lease.Parent.RestorePopup(lease.Child)) await lease.Parent.ClosePopupAsync();
        }
        catch
        {
            // Close before the borrowed peer disappears if native transfer fails.
            await lease.Parent.ClosePopupAsync();
            throw;
        }
    }

    private void RefreshPopupPresentation()
    {
        if (retired || popupPresentation is not { IsCurrent: true } lease) return;
        var slot = expandedPopupSlot ?? lease.Slot;
        lease.Child.SetPresentation(slot.CanDisplay, slot.AcceptsInput,
            passivePin: slot.Pinned && !slot.Expanded && !slot.AcceptsInput);
    }

    private void PopupClosed(BrowserSurface child)
    {
        if (popupPresentation is not { } lease || !ReferenceEquals(lease.Child, child)) return;
        lease.Released = true;
        popupPresentation = null;
        lease.Slot.PlacementChanged = null;
        lease.Slot.SetSurface(null);
        InvalidatePopupExpansion();
        // Notification only: shell teardown is queued, never awaited while the
        // parent/profile's lifetime operation owns its own gate.
        PopupPresentationInvalidated?.Invoke(lease);
    }

    private void InvalidatePopupExpansion()
    {
        if (expandedPopupSlot is not { } slot) return;
        expandedPopupSlot = null;
        slot.SetSurface(null); slot.PlacementChanged = null;
        ExpansionInvalidated?.Invoke();
    }

    private async Task<bool> ExpandPopupAsync(BrowserPopupPresentation lease, BrowserSlot destination)
    {
        await popupPlacement.WaitAsync();
        try
        {
            if (retired || !lease.IsCurrent || expandedPopupSlot is not null || !destination.CanDisplay) return false;
            expandedPopupSlot = destination;
            destination.Expanded = true;
            destination.PlacementChanged = RefreshPopupPresentation;
            lease.Slot.SetSurface(null);
            lease.Child.SetPresentation(lease.Child.CanCompleteSiteDecision, false);
            await MoveAsync(lease.Child, destination.Host, () => lease.IsCurrent && ReferenceEquals(expandedPopupSlot, destination));
            if (retired || !lease.IsCurrent || !ReferenceEquals(expandedPopupSlot, destination)) return false;
            destination.SetSurface(lease.Child);
            RefreshPopupPresentation();
            return true;
        }
        finally { popupPlacement.Release(); }
    }

    private async Task CollapsePopupAsync(BrowserSlot destination)
    {
        await popupPlacement.WaitAsync();
        BrowserPopupPresentation? returning = null;
        try
        {
            if (!ReferenceEquals(expandedPopupSlot, destination)) return;
            expandedPopupSlot = null;
            destination.SetSurface(null); destination.PlacementChanged = null;
            if (retired || popupPresentation is not { IsCurrent: true } lease) return;
            returning = lease;
            lease.Child.SetPresentation(false, false);
            await MoveAsync(lease.Child, lease.Slot.Host, () => lease.IsCurrent && !lease.Slot.Retired);
            if (!lease.IsCurrent || retired) return;
            lease.Slot.SetSurface(lease.Child);
            RefreshPopupPresentation();
        }
        catch
        {
            if (returning is not null) await returning.Parent.ClosePopupAsync();
            throw;
        }
        finally { popupPlacement.Release(); }
    }
}
