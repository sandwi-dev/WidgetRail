using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private IntroductionProgressStore? introductionStore;
    private IntroductionProgress introduction = IntroductionProgress.SafeFallback;
    private IntroductionCard? introductionCard;
    private readonly Canvas introductionHighlights = new() { IsHitTestVisible = false };
    private ControllerSettings introductionControllers = new();
    private Task introductionWrite = Task.CompletedTask;
    private bool introductionLoaded, introductionQueued, introductionBusy, introductionSaveFailed;
    private string? introductionPendingFocus, introductionError;
    private long introductionNavigation;
    private bool IntroductionRunning => introductionLoaded && !introduction.Paused && !introduction.Finished;
    private (string Step, bool Notifications, ControllerOpenShortcut Shortcut, bool F1)? introductionContent;
    private (FrameworkElement Target, Rect Bounds)[] introductionBounds = [];
    private Windows.UI.Color introductionOutline;

    private void InitializeIntroduction()
    {
        introductionStore = new(options.SettingsRoot);
        introductionCard = new() { Visibility = Visibility.Collapsed };
        ShellOverlayLayer.Children.Add(introductionCard);
        PresentationLayers.Order(introductionCard, PresentationLayer.ShellOverlay);
        ProductionRoot.Children.Add(introductionHighlights);
        PresentationLayers.Order(introductionHighlights, PresentationLayer.ShellOverlay);
        AutomationProperties.SetAccessibilityView(introductionHighlights, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        introductionCard.Action += action => _ = IntroductionActionAsync(action);
        ProductionRoot.LayoutUpdated += (_, _) => { TryFocusIntroductionTarget(); PositionIntroduction(); };
        ProductionRoot.PreviewKeyDown += (_, args) =>
        {
            if (!IntroductionRunning || Input.GamepadKeyBoundary.Owns(this, args)) return;
            if (args.Key == Windows.System.VirtualKey.F8) { args.Handled = true; _ = IntroductionActionAsync("next"); }
            else if (args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; LeaveIntroduction(); }
        };
    }

    private async Task LoadIntroductionAsync()
    {
        if (introductionLoaded || introductionStore is null || options.Development is not null || options.InitialWidgetId is not null) return;
        introduction = await introductionStore.LoadAsync(lifetime.Token);
        if (retired) return;
        introductionLoaded = true;
        RefreshIntroduction();
    }

    private void ReplayIntroduction()
    {
        ++introductionNavigation;
        introductionPendingFocus = null;
        introductionLoaded = true;
        CloseTrayMenu(false); CloseUtilityPanel();
        SetIntroduction(IntroductionProgress.New);
        FocusDirectionalTray();
        RefreshIntroduction(); PublishControllerGuide();
    }

    private void ContinueIntroduction()
    {
        SetIntroduction(introduction with { Paused = false });
        RefreshIntroduction(); PublishControllerGuide();
    }

    private void SetIntroduction(IntroductionProgress progress)
    {
        if (introduction == progress) return;
        introduction = progress;
        introductionError = null;
        introductionContent = null;
        introductionWrite = SaveIntroductionAsync(progress);
        ScheduleIntroduction();
    }

    private async Task SaveIntroductionAsync(IntroductionProgress progress)
    {
        if (introductionStore is not null && !await introductionStore.SaveAsync(progress) && !introductionSaveFailed)
        {
            introductionSaveFailed = true;
            System.Diagnostics.Trace.TraceWarning("Introduction progress could not be saved. The overlay remains available.");
        }
    }

    private void ScheduleIntroduction()
    {
        if (retired || introductionQueued) return;
        introductionQueued = true;
        DispatcherQueue.TryEnqueue(() => { introductionQueued = false; if (!retired) RefreshIntroduction(); });
    }

    private bool IntroductionAppearanceVisible => activeWidget == "settings" && surface?.CurrentBinding?.Scope == "appearance.page";

    private void RefreshIntroduction()
    {
        if (introductionCard is not { } card) return;
        // Ordinary widget input, menus, utility panels and loading publications
        // do not advance or dismiss an explicitly started tour.
        var show = IntroductionRunning && visible && foreground && !retired && !startupPresentationPending;
        card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { introductionHighlights.Children.Clear(); introductionBounds = []; return; }
        var content = (introduction.Step, HasPendingNotifications, introductionControllers.OpenShortcut, introductionControllers.F1ShortcutEnabled);
        if (introductionContent != content)
        {
            introductionContent = content;
            card.Present(introduction.Step, HasPendingNotifications, "Show or hide the overlay",
                introductionControllers.OpenShortcut == ControllerOpenShortcut.Guide ? ControllerPrompt.Guide : ControllerPrompt.View,
                introductionControllers.F1ShortcutEnabled);
        }
        card.ShowStatus(introductionError ?? (introductionBusy || introductionPendingFocus is not null
            ? introduction.Step == "appearance-details" ? "Opening Appearance…" : "Opening Catalogue…" : null));
        TryFocusIntroductionTarget();
        PositionIntroduction();
    }

    private IEnumerable<FrameworkElement> IntroductionTargets()
    {
        if (introduction.Step is "guide" or "commands") yield return TrayHelp;
        else if (introduction.Step == "utilities")
        {
            if (utilityPanel is { IsLoaded: true } panel) yield return panel;
            else { yield return taskSwitcherButton; yield return notificationBell; }
        }
        else if (introduction.Step == "catalogue")
        {
            if (activeWidget == "marketplace" && surface is { } catalogue)
                yield return FindIntroductionTarget(catalogue, "marketplace.title") ?? catalogue;
            else if (catalogItems.FirstOrDefault(item => item.Id == "marketplace") is { } item && Tray.ContainerFromItem(item) is FrameworkElement tile)
                yield return tile;
        }
        else if (introduction.Step is "appearance" or "appearance-details" && IntroductionAppearanceVisible && surface is { } presenter)
        {
            foreach (var id in new[] { "appearance.theme", "interface.stepper" })
                if (FindIntroductionTarget(presenter, id) is { } element) yield return element;
        }
    }

    private static FrameworkElement? FindIntroductionTarget(DependencyObject node, string id)
    {
        if (node is FrameworkElement element && AutomationProperties.GetAutomationId(element) == "Widget." + id) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindIntroductionTarget(VisualTreeHelper.GetChild(node, i), id) is { } found) return found;
        return null;
    }

    private void TryFocusIntroductionTarget()
    {
        if (!IntroductionRunning || !visible || !foreground || switching || introductionBusy || surface is null || introductionPendingFocus is not { } id) return;
        if (!IntroductionAppearanceVisible) return;
        var element = FindIntroductionTarget(surface, id);
        if (element is not Control { IsLoaded: true, IsEnabled: true, ActualWidth: > 0 } target) return;
        introductionPendingFocus = null;
        if (!surface.FocusHostTarget(AutomationProperties.GetAutomationId(target)["Widget.".Length..])) introductionPendingFocus = id;
        else ScheduleIntroduction();
    }

    private void PositionIntroduction()
    {
        if (retired || introductionCard is not { Visibility: Visibility.Visible } card || ShellOverlayLayer.ActualWidth <= 0 || ShellOverlayLayer.ActualHeight <= 0) return;
        var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
        var width = ShellOverlayLayer.ActualWidth;
        var height = ShellOverlayLayer.ActualHeight;
        card.Width = Math.Min(introduction.Step == "welcome" ? 600 : 480, Math.Max(1, width - 32));
        card.Apply(palette, PreviewFrameStyle.Resolve(ShellPalette, palette, Appearance), Appearance.TextScale, Math.Max(100, height - 32));
        var cardHeight = Math.Min(card.ActualHeight > 0 ? card.ActualHeight : introduction.Step == "welcome" ? 430 : 220, height - 32);
        var targets = IntroductionTargets().Where(target => target.IsLoaded && target.Visibility == Visibility.Visible && target.ActualWidth > 0 && target.ActualHeight > 0).ToArray();
        var x = (width - card.Width) / 2;
        var y = height - cardHeight - 16;
        if (targets.Length > 0)
        {
            var rectangles = targets.Select(target => target.TransformToVisual(ShellOverlayLayer).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight))).ToArray();
            var left = rectangles.Min(rect => rect.Left); var right = rectangles.Max(rect => rect.Right);
            var top = rectangles.Min(rect => rect.Top); var bottom = rectangles.Max(rect => rect.Bottom);
            x = (left + right - card.Width) / 2;
            if (top - cardHeight - 16 >= 16) y = top - cardHeight - 16;
            else if (right + card.Width + 16 <= width - 16) { x = right + 16; y = top; }
            else if (left - card.Width - 16 >= 16) { x = left - card.Width - 16; y = top; }
            else y = bottom + 16;
        }
        x = Math.Clamp(x, 16, Math.Max(16, width - card.Width - 16));
        y = Math.Clamp(y, 16, Math.Max(16, height - cardHeight - 16));
        card.VerticalAlignment = VerticalAlignment.Top;
        var margin = new Thickness(x, y, 0, 0);
        if (card.Margin != margin) card.Margin = margin;
        var bounds = targets.Select(target => (Target: target, Bounds: target.TransformToVisual(ProductionRoot)
            .TransformBounds(new Rect(-4, -4, target.ActualWidth + 8, target.ActualHeight + 8)))).ToArray();
        if (introductionOutline == palette.Focus && introductionBounds.SequenceEqual(bounds)) return;
        introductionBounds = bounds; introductionOutline = palette.Focus;
        introductionHighlights.Children.Clear();
        foreach (var (_, rect) in bounds)
        {
            var outline = new Border { Width = rect.Width, Height = rect.Height, BorderThickness = new(2), CornerRadius = new(10), BorderBrush = new SolidColorBrush(palette.Focus) };
            Canvas.SetLeft(outline, rect.X); Canvas.SetTop(outline, rect.Y); introductionHighlights.Children.Add(outline);
        }
    }

    private async Task IntroductionActionAsync(string action)
    {
        if (retired || !visible || !IntroductionRunning) return;
        if (action == "close") { LeaveIntroduction(); return; }
        if (action != "next" || introductionBusy || introductionPendingFocus is not null) return;
        var previous = introduction;
        var next = introduction.Step is "catalogue" or "appearance" ? "appearance-details" : introduction.Next().Step;
        CloseTrayMenu(false);
        if (next is not ("catalogue" or "appearance-details"))
        {
            SetIntroduction(introduction with { Step = next });
            if (next is "guide" or "commands" or "utilities") { CloseUtilityPanel(); FocusDirectionalTray(); }
            RefreshIntroduction(); PublishControllerGuide(); return;
        }
        if (owner is null) return;
        var navigation = ++introductionNavigation;
        introductionBusy = true;
        SetIntroduction(introduction with { Step = next });
        introductionPendingFocus = next == "appearance-details" ? "appearance.theme" : null;
        RefreshIntroduction();
        try
        {
            bool opened;
            if (next == "catalogue")
            {
                if (!catalogItems.Any(item => item.Id == "marketplace")) throw new InvalidOperationException("Catalogue is not available.");
                await SelectAsync("marketplace");
                opened = activeWidget == "marketplace";
            }
            else opened = await InvokeIntentCoreAsync(prepareNotification: owner.Session.PrepareAppearanceIntentAsync);
            if (!opened && navigation == introductionNavigation && IntroductionRunning)
            {
                introductionPendingFocus = null; SetIntroduction(previous);
                introductionError = "This page could not be opened. Continue to try again.";
            }
        }
        catch (OperationCanceledException) when (retired || !visible || navigation != introductionNavigation) { }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("introduction-destination", error);
            if (navigation == introductionNavigation && IntroductionRunning)
            {
                introductionPendingFocus = null; SetIntroduction(previous);
                introductionError = "This page could not be opened. Continue to try again.";
            }
        }
        finally { introductionBusy = false; RefreshIntroduction(); PublishControllerGuide(); }
    }

    private void LeaveIntroduction()
    {
        ++introductionNavigation;
        introductionPendingFocus = null;
        if (introductionBusy) { intentCancellation?.Cancel(); switchCancellation?.Cancel(); }
        SetIntroduction(introduction with { Paused = true });
        RefreshIntroduction(); PublishControllerGuide();
    }

    private bool RouteIntroductionButton(ControllerButton button, ControllerEventPhase phase)
    {
        if (!IntroductionRunning || !visible || !foreground || button is not (ControllerButton.X or ControllerButton.B)) return false;
        if (phase == ControllerEventPhase.Pressed)
        {
            shellGestures.Claim(button);
            if (button == ControllerButton.X) _ = IntroductionActionAsync("next"); else LeaveIntroduction();
        }
        return true;
    }

    private bool ReceiveIntroduction(WidgetRail.OverlayPlatformClient.ControllerFrame frame)
    {
        if (!IntroductionRunning || !visible || !foreground || frame.Connected == 0 || frame.Primed != 0) return false;
        const ushort buttons = 0x4000 | 0x2000;
        if (((frame.State.Buttons | frame.PressedButtons | frame.ReleasedButtons) & buttons) == 0) return false;
        WidgetControllerPrompts.Set(frame.LastInputFamily);
        foreach (var (mask, button) in Buttons)
        {
            if ((mask & buttons) == 0) continue;
            if ((frame.PressedButtons & mask) != 0) _ = RouteButtonAsync(button, ControllerEventPhase.Pressed);
            if ((frame.ReleasedButtons & mask) != 0) _ = RouteButtonAsync(button, ControllerEventPhase.Released);
        }
        return true;
    }

    private IReadOnlyList<ControllerGuideHint> IntroductionGuide(IReadOnlyList<ControllerGuideHint> hints) => !IntroductionRunning ? hints :
        (ControllerGuideHint[])[.. hints.Where(hint => hint.Button is not (ControllerButton.X or ControllerButton.B)),
            new(ControllerPrompt.X, introduction.Step == "appearance-details" ? "Finish tour" : "Continue tour", ControllerButton.X, Required: true),
            new(ControllerPrompt.B, "Close tour", ControllerButton.B, Required: true)];
}
