using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private BrowserSlot? expandedBrowser;
    private readonly RightStickScroll expandedScroll = new();

    private async Task<bool> ExpandBrowserAsync(BrowserSurface browser, CancellationToken token)
    {
        if (retired || !visible || browserOwner is null || !browser.CanCompleteSiteDecision) return false;
        if (expandedBrowser is { } existing)
            return !fullscreenLayer.IsTransitioning && existing.Surface?.Owns(browser) == true;
        if (fullscreenLayer.IsOpen) return false;
        var originPin = pinned is { } pin && ReferenceEquals(browser.XamlRoot, pin.Window.AutomationRoot.XamlRoot) ? pin : null;
        var previous = FocusManager.GetFocusedElement(browser.XamlRoot) as Control;
        var slot = new BrowserSlot { Active = true, AcceptsInput = false, Expanded = true, Margin = new(12), IsTabStop = true };
        AutomationProperties.SetAutomationId(slot, "Browser.Expanded");
        AutomationProperties.SetName(slot, AutomationProperties.GetName(browser.CompositionRoot));
        var layout = new Grid { RowDefinitions = { new() { Height = new(1, GridUnitType.Star) }, new() { Height = GridLength.Auto } } };
        layout.Children.Add(slot);
        TrayHelp.Content = null;
        Grid.SetRow(trayGuide, 1); layout.Children.Add(trayGuide);
        expandedBrowser = slot;
        slot.InteractionChanged = PublishControllerGuide;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = false;
        try
        {
            _ = fullscreenLayer.ShowAsync(new(slot, layout, slot.HandleButton,
                direction => slot.MoveFocus(direction), () => { slot.Focus(FocusState.Keyboard); slot.Enter(); },
                () =>
                {
                    browserOwner?.Collapse(slot); slot.Retire();
                    if (ReferenceEquals(expandedBrowser, slot)) expandedBrowser = null;
                    layout.Children.Remove(trayGuide); Grid.SetRow(trayGuide, 0); TrayHelp.Content = trayGuide;
                    PublishControllerGuide();
                    settled.TrySetResult();
                },
                () =>
                {
                    if (originPin is not null && ReferenceEquals(pinned, originPin)) _ = EnterPinnedAsync();
                    else if (previous is { IsLoaded: true, IsEnabled: true }) previous.Focus(FocusState.Keyboard);
                    else QueueEntryFocus();
                },
                AnalogMove: ScrollExpandedBrowser,
                Settled: () => { RefreshExpandedBrowser(); settled.TrySetResult(); },
                PointerMove: slot.MovePointer,
                BeforeDetach: async () =>
                {
                    if (browserOwner is not { } current) return;
                    current.Collapse(slot);
                    await current.ReleaseSlotsAsync((BrowserSlot[])[slot]);
                }));
            // The destination must be loaded and arranged before BrowserOwner
            // can admit it as a display slot or transfer a native controller.
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            if (!await browserOwner.ExpandAsync(browser, slot)) return false;
            // Transfer through the normal shell activation path only after the
            // placement owner detached the browser from the pinned presenter.
            if (originPin is not null) ExitPinnedInteraction(restoreMain: true);
            token.ThrowIfCancellationRequested();
            RefreshExpandedBrowser();
            slot.Focus(FocusState.Keyboard); slot.Enter();
            return ready = ReferenceEquals(expandedBrowser, slot) && browser.CanCompleteSiteDecision;
        }
        finally
        {
            if (ReferenceEquals(expandedBrowser, slot) && !ready)
                fullscreenLayer.Close(restoreFocus: false);
        }
    }

    private void RefreshExpandedBrowser()
    {
        if (expandedBrowser is not { } slot) return;
        if (!visible || retired) { if (!fullscreenLayer.IsClosing) fullscreenLayer.Close(restoreFocus: false); return; }
        slot.AcceptsInput = foreground && !fullscreenLayer.IsTransitioning && !HostChoiceActive && !SystemFilePickerOpen && !LocalInstallActive;
        slot.Surface?.SetPresentation(true, slot.AcceptsInput);
        slot.RefreshFocusPresentation();
    }

    private bool ScrollExpandedBrowser(short x, short y)
    {
        if (expandedBrowser is not { AcceptsInput: true } slot) { expandedScroll.Reset(); return false; }
        var delta = expandedScroll.Sample(x, y, Environment.TickCount64);
        return slot.Scroll(delta.X, delta.Y);
    }
}
