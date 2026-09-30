using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private int recoveryAnnouncementCount;
    partial void RecoveryAnnounced() => ++recoveryAnnouncementCount;
    internal void ApplyLayoutFixture(WidgetPresentationFrame frame, AppearanceSettings appearance,
        IReadOnlyList<BridgeWidgetDescriptor> catalog)
    {
        if (startup is not null || owner is not null) throw new InvalidOperationException("Layout fixture cannot alter a running bridge shell.");
        Appearance = appearance;
        authoredSurfaceHints = frame.Snapshot.Surface;
        if (!catalogItems.SequenceEqual(catalog))
        {
            catalogItems.Clear();
            foreach (var descriptor in catalog) catalogItems.Add(descriptor);
        }
        if (surface is null)
        {
            surface = new WidgetViewPresenter();
            surface.SetAutomaticFocusEnabled(false);
            WidgetSurfaces.Children.Add(surface);
        }
        surface.Apply(frame);
        activeWidget = requestedWidget = frame.Descriptor.Id;
        retainedSurfaces[activeWidget] = new(frame.Descriptor, surface, null)
        { Frame = frame, PresentedLifecycle = DesiredLifecycle, RestoreMemoryOnFirstApply = false };
        ShowPresentationStatus(frame.Descriptor.Name);
    }
    internal async Task<IReadOnlyList<string>> ValidateRecoveryGuideFixtureAsync()
    {
        if (startup is not null || owner is not null) throw new InvalidOperationException("Recovery fixture cannot alter a running bridge shell.");
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        SetInteractive(false);
        var initialAnnouncements = recoveryAnnouncementCount;
        var previousFocus = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot);
        ShowRecovery("Fixture recovery", true);
        await CommitFixtureMode();
        Check(FrameworkElementAutomationPeer.CreatePeerForElement(Status).GetLiveSetting() == AutomationLiveSetting.Polite &&
            recoveryAnnouncementCount == initialAnnouncements + 1,
            "native recovery status requests one polite live-region announcement");
        Check(ReferenceEquals(previousFocus, Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot)),
            "recovery announcement preserves rail/radial focus");
        ShowRecovery("Fixture recovery", true);
        await CommitFixtureMode();
        Check(recoveryAnnouncementCount == initialAnnouncements + 1, "unchanged recovery status does not repeat announcements");
        ShowRecovery("Superseded recovery", true);
        ShowRecovery("Current recovery", true);
        await CommitFixtureMode();
        Check(recoveryAnnouncementCount == initialAnnouncements + 2 && Status.Text == "Current recovery",
            "queued recovery announcements coalesce to the current message");
        ShowRecovery("Already recovered", true);
        ShowPresentationStatus("Fixture ready");
        await CommitFixtureMode();
        Check(recoveryAnnouncementCount == initialAnnouncements + 2, "successful recovery cancels an obsolete queued announcement");
        ShowRecovery("Fixture recovery", true);
        await CommitFixtureMode();
        Check(trayGuide.HelpText.Contains("Open widget") && !trayGuide.HelpText.Contains("Retry"),
            "background recovery preserves normal tray input hints");
        SetInteractive(true);
        await CommitFixtureMode();
        Check(trayGuide.DisplayedHints.Any(hint => hint.Button == ControllerButton.A && hint.Label == "Retry") &&
            !trayGuide.DisplayedHints.Any(hint => hint.Button == ControllerButton.Y),
            "interactive recovery replaces underlying widget shortcuts with Retry");
        ShowRecovery("Fixture cannot retry", false);
        await CommitFixtureMode();
        Check(trayGuide.DisplayedHints.All(hint => hint.Button != ControllerButton.A) &&
            trayGuide.DisplayedHints.Any(hint => hint.Button == ControllerButton.B),
            "nonretryable recovery retains Back without advertising A");
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
        await CommitFixtureMode();
        Check(!interactive && trayGuide.HelpText.Contains("Open widget"), "recovery Back uses existing routing and restores tray guide");
        SetInteractive(true);
        ShowPresentationStatus("Fixture ready");
        await CommitFixtureMode();
        Check(!RecoveryVisible && trayGuide.DisplayedHints.Any(hint => hint.Button == ControllerButton.Y && hint.Label == "Fixture refresh") &&
            !trayGuide.HelpText.Contains("Retry"), "successful recovery restores current widget declarations");
        return checks;

        async Task CommitFixtureMode()
        {
            // This layout-only widget has no worker and its static declaration
            // does not depend on lifecycle. Mark that declaration committed,
            // as the production establishment transaction normally would.
            retainedSurfaces[activeWidget!].PresentedLifecycle = DesiredLifecycle;
            UpdateTrayHelp(); UpdateLayout();
            var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => dispatched.SetResult()))
                throw new InvalidOperationException("Recovery fixture dispatcher is unavailable.");
            await dispatched.Task;
            var deadline = Environment.TickCount64 + 5000;
            while (trayGuide.HasPendingHints)
            {
                if (Environment.TickCount64 >= deadline) throw new TimeoutException("Fixture guide did not settle.");
                await Task.Delay(16);
            }
            UpdateLayout();
        }
    }

    internal async Task DisposeLayoutFixtureAsync()
    {
        DisposeShellChrome();
        await DisposeWidgetSurfacesAsync();
        await DisposeAsync();
    }
}
