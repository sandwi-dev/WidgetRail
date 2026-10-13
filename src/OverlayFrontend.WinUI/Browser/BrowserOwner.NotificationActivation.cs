using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    internal async Task<bool> FocusNotificationSourceAsync(BrowserSurface source)
    {
        while (reconciliation is { IsCompleted: false } pending) await pending;
        if (retired) return false;
        if (popupPresentation is { IsCurrent: true, Slot: { CanDisplay: true, AcceptsInput: true } popupSlot } popup &&
            ReferenceEquals(popup.Child, source))
        {
            if (!popupSlot.Focus(FocusState.Keyboard)) return false;
            source.Enter();
            return source.NotificationPresentationActive;
        }
        var entry = entries.Values.FirstOrDefault(entry => entry.Surface.Owns(source));
        if (entry?.Slot is not { CanDisplay: true, AcceptsInput: true } slot || !ReferenceEquals(entry.Surface.ActiveSurface, source)) return false;
        if (!slot.Focus(FocusState.Keyboard)) return false;
        source.Enter();
        return source.NotificationPresentationActive;
    }
}
