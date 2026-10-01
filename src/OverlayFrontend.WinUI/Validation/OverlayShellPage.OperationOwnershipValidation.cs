namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Fixture-only coverage: barriers hold host lifecycle admission; no widget
    // action, playback command, or machine setting is invoked.
    private async Task ValidatePinnedOperationOwnershipAsync(PinnedSurface current, Action<bool, string> check)
    {
        var originalPinFailure = CapturePinnedFailureGuard(current);
        check(originalPinFailure(), "current pin owns its operation failure presentation");
        Task<bool> lifecycle;
        await transitions.WaitAsync(lifetime.Token);
        try
        {
            lifecycle = SetPinnedLifecycleAsync(current);
            check(!lifecycle.IsCompleted, "lifecycle admission waits at the host transition barrier");
            ++pinIntent;
            check(!originalPinFailure(), "a newer pin intent retires the previous operation failure owner");
        }
        finally { transitions.Release(); }
        check(!await lifecycle, "superseded pinned lifecycle is dropped before worker admission");
        check(ReferenceEquals(pinned, current) && current.IsCurrent && !current.Window.Interactive,
            "discarding old lifecycle work preserves the current passive pin");

        Task unpin;
        await transitions.WaitAsync(lifetime.Token);
        try
        {
            unpin = UnpinAsync(save: false, current);
            check(!unpin.IsCompleted, "unpin waits at the host transition barrier");
            ++pinIntent;
        }
        finally { transitions.Release(); }
        await unpin;
        check(ReferenceEquals(pinned, current) && current.Window.IsVisible,
            "a retired unpin request cannot remove the later pin intent's surface");

        var widget = FocusedTrayWidget() ?? throw new InvalidOperationException("Ownership validation requires tray focus.");
        await ShowTrayMenuAsync(widget);
        var oldMenu = trayMenu ?? throw new InvalidOperationException("First validation menu did not open.");
        await WaitForMenuFocus(oldMenu);
        var oldClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldMenu.Flyout.Closed += (_, _) => oldClosed.TrySetResult();
        var oldRequest = trayMenuRequest!;
        var oldTrayGuard = CaptureTrayOperationGuard(widget, selectionVersion);
        await ShowTrayMenuAsync(widget);
        var currentMenu = trayMenu ?? throw new InvalidOperationException("Replacement validation menu did not open.");
        await oldClosed.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
        await WaitForMenuFocus(currentMenu);
        check(!ReferenceEquals(oldMenu, currentMenu), "replacement menu has its own opening identity");
        OnTrayMenuOpened(oldMenu);
        check(ReferenceEquals(trayMenu, currentMenu), "late Opened notification cannot close a replacement flyout");
        var recovering = RecoveryVisible;
        ReportTrayMenuFailure(new InvalidOperationException("Expected fixture failure from a retired tray opening."),
            oldRequest, oldMenu, () => ReferenceEquals(trayMenuRequest, oldRequest) && oldTrayGuard());
        check(ReferenceEquals(trayMenu, currentMenu) && RecoveryVisible == recovering,
            "late menu failure is diagnosed without closing the replacement menu or showing widget recovery");
        var currentClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        currentMenu.Flyout.Closed += (_, _) => currentClosed.TrySetResult();
        CloseTrayMenu(restoreFocus: false);
        // Hide starts native popup dismissal asynchronously. Let Closed and its
        // focus restoration finish before the next test activates another HWND.
        await currentClosed.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
        FocusTray();

        async Task WaitForMenuFocus(TrayMenu menu)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!menu.Items.Any(item => item.FocusState != Microsoft.UI.Xaml.FocusState.Unfocused))
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Ownership validation menu did not acquire native focus.");
                await Task.Delay(10, lifetime.Token);
            }
        }
    }
}
