using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private IntroductionProgressStore? introductionStore;
    private IntroductionProgress introduction = IntroductionProgress.SafeFallback;
    private IntroductionCard? introductionCard;
    private readonly Canvas introductionHighlights = new() { IsHitTestVisible = false };
    private ControllerSettings introductionControllers = new();
    private Task introductionWrite = Task.CompletedTask;
    private bool introductionLoaded, introductionQueued, introductionBusy, introductionWasShown, introductionSaveFailed;
    private (string Step, bool Notifications, ControllerOpenShortcut Shortcut, bool F1)? introductionContent;
    private (FrameworkElement Target, Windows.Foundation.Rect Bounds)[] introductionBounds = [];
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
        introductionCard.Leave += LeaveIntroduction;
        introductionCard.GotFocus += (_, _) => { surface?.SetAutomaticFocusEnabled(false); PublishControllerGuide(); };
        introductionCard.LostFocus += (_, _) => ScheduleIntroduction();
        ProductionRoot.LayoutUpdated += (_, _) => PositionIntroduction();
    }

    private async Task LoadIntroductionAsync()
    {
        if (introductionLoaded || introductionStore is null || options.Development is not null || options.InitialWidgetId is not null) return;
        introduction = await introductionStore.LoadAsync(lifetime.Token);
        if (retired) return;
        introductionLoaded = true;
        RefreshIntroduction();
        if (introduction.Step == "welcome" && !introduction.Paused && visible && foreground)
            introductionCard?.FocusPrimary();
    }

    private void ReplayIntroduction()
    {
        introductionLoaded = true;
        CloseTrayMenu(false);
        CloseUtilityPanel();
        radialRequested = false;
        SetInteractive(false); RefreshRadialChooser();
        SetIntroduction(IntroductionProgress.New);
        RefreshIntroduction();
        introductionCard?.FocusPrimary();
    }

    private void ContinueIntroduction()
    {
        SetIntroduction(introduction with { Paused = false });
        RefreshIntroduction();
        introductionCard?.FocusPrimary();
    }

    private void SetIntroduction(IntroductionProgress progress)
    {
        if (introduction == progress) return;
        introduction = progress;
        introductionWasShown = false;
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
        if (introductionLoaded && !introduction.Paused && !introductionBusy)
        {
            if (introduction.Step == "welcome" && introductionWasShown && !card.Focused &&
                (interactive || trayMenu is not null || utilityPanel is not null || RadialOpen))
                SetIntroduction(introduction with { Paused = true });
            var observed = introduction.Step switch
            {
                "guide" => introductionWasShown && interactive && !card.Focused,
                "commands" => trayMenu is not null,
                "utilities" => UtilityListFocused,
                "catalogue" => activeWidget == "marketplace" && interactive,
                "appearance" => IntroductionAppearanceVisible,
                "appearance-details" => introductionWasShown && !IntroductionAppearanceVisible && !switching,
                _ => false,
            };
            if (observed) SetIntroduction(introduction.Next());
        }
        var blocked = !visible || !foreground || retired || startupPresentationPending || switching ||
            IsMediaFullscreen || PinnedInputActive || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen ||
            nativeBrowserPickerActive || trayMenu is not null || RadialOpen || utilityPanel is not null || surface?.HasTransientControl == true;
        var show = introductionLoaded && !introduction.Finished && !introduction.Paused && !blocked &&
            (introduction.Step != "appearance-details" || IntroductionAppearanceVisible) &&
            (introduction.Step == "welcome" || card.Focused || !interactive ||
                introduction.Step == "appearance-details" && IntroductionAppearanceVisible);
        card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { card.CancelPendingFocus(); introductionHighlights.Children.Clear(); introductionBounds = []; return; }
        var content = (introduction.Step, HasPendingNotifications, introductionControllers.OpenShortcut, introductionControllers.F1ShortcutEnabled);
        if (introductionContent != content)
        {
            introductionContent = content;
            card.Present(introduction.Step, HasPendingNotifications, "Show or hide the overlay",
                introductionControllers.OpenShortcut == ControllerOpenShortcut.Guide ? ControllerPrompt.Guide : ControllerPrompt.View,
                introductionControllers.F1ShortcutEnabled);
        }
        introductionWasShown = true;
        PositionIntroduction();
    }

    private IEnumerable<FrameworkElement> IntroductionTargets()
    {
        if (introduction.Step is "guide" or "commands") yield return TrayHelp;
        else if (introduction.Step == "utilities") { yield return taskSwitcherButton; yield return notificationBell; }
        else if (introduction.Step == "catalogue" && catalogItems.FirstOrDefault(item => item.Id == "marketplace") is { } catalogue &&
            Tray.ContainerFromItem(catalogue) is FrameworkElement tile) yield return tile;
        else if (introduction.Step == "appearance-details" && surface is { } presenter)
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

    private void PositionIntroduction()
    {
        if (retired || introductionCard is not { Visibility: Visibility.Visible } card || ShellOverlayLayer.ActualWidth <= 0 || ShellOverlayLayer.ActualHeight <= 0) return;
        var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
        var availableWidth = Math.Max(1, ShellOverlayLayer.ActualWidth - 32);
        card.Width = Math.Min(introduction.Step == "welcome" ? 620 : 560, availableWidth);
        var availableHeight = Math.Max(100, ShellOverlayLayer.ActualHeight - 32);
        // Stay above the guide. Appearance tips prefer the free side of Settings;
        // narrow viewports place them above it instead of covering its controls.
        var x = Math.Max(16, (ShellOverlayLayer.ActualWidth - card.Width) / 2);
        var margin = new Thickness(x, 16, 0, 12);
        if (introduction.Step == "appearance-details" && surface is { IsLoaded: true } settings)
        {
            card.VerticalAlignment = VerticalAlignment.Top;
            var origin = settings.TransformToVisual(ShellOverlayLayer).TransformPoint(new(0, 0));
            var right = origin.X + settings.ActualWidth + 20;
            var top = Math.Max(16, origin.Y);
            if (right + card.Width <= ShellOverlayLayer.ActualWidth - 16) x = right;
            else if (origin.X - card.Width - 20 >= 16) x = origin.X - card.Width - 20;
            else
            {
                // In a narrow viewport keep the two taught controls uncovered.
                // The remaining copy scrolls independently if space is limited.
                var protectedBottom = IntroductionTargets().Where(item => item.IsLoaded).Select(item =>
                    item.TransformToVisual(ShellOverlayLayer).TransformPoint(new(0, item.ActualHeight)).Y).DefaultIfEmpty(origin.Y).Max();
                top = Math.Max(16, protectedBottom + 16);
                x = Math.Clamp(origin.X, 16, Math.Max(16, ShellOverlayLayer.ActualWidth - card.Width - 16));
            }
            availableHeight = Math.Max(100, ShellOverlayLayer.ActualHeight - top - 16);
            margin = new(x, top, 0, 0);
        }
        else card.VerticalAlignment = VerticalAlignment.Bottom;
        card.Apply(palette, PreviewFrameStyle.Resolve(ShellPalette, palette, Appearance), Appearance.TextScale, availableHeight);
        if (card.Margin != margin) card.Margin = margin;
        var bounds = new List<(FrameworkElement Target, Windows.Foundation.Rect Bounds)>();
        foreach (var target in IntroductionTargets())
        {
            if (!target.IsLoaded || target.Visibility != Visibility.Visible || target.ActualWidth <= 0 || target.ActualHeight <= 0) continue;
            var point = target.TransformToVisual(ProductionRoot).TransformPoint(new(0, 0));
            bounds.Add((target, new(point.X - 4, point.Y - 4, target.ActualWidth + 8, target.ActualHeight + 8)));
        }
        if (introductionOutline == palette.Focus && introductionBounds.SequenceEqual(bounds)) return;
        introductionBounds = bounds.ToArray(); introductionOutline = palette.Focus;
        introductionHighlights.Children.Clear();
        foreach (var (_, rect) in introductionBounds)
        {
            var outline = new Border { Width = rect.Width, Height = rect.Height,
                BorderThickness = new(2), CornerRadius = new(10), BorderBrush = new SolidColorBrush(palette.Focus) };
            Canvas.SetLeft(outline, rect.X); Canvas.SetTop(outline, rect.Y);
            introductionHighlights.Children.Add(outline);
        }
    }

    private async Task IntroductionActionAsync(string action)
    {
        if (retired || !visible || introductionBusy) return;
        if (action == "skip") { SetIntroduction(introduction with { Step = "complete", Paused = false }); LeaveIntroduction(); return; }
        if (action == "next")
        {
            var fromWelcome = introduction.Step == "welcome";
            SetIntroduction(introduction.Step == "appearance" ? introduction with { Step = "complete" } : introduction.Next());
            RefreshIntroduction();
            if (introduction.Finished || fromWelcome) LeaveIntroduction(); else introductionCard?.FocusPrimary();
            PublishControllerGuide();
            return;
        }
        if (action != "open" || owner is null) return;
        introductionBusy = true;
        try
        {
            if (introduction.Step == "catalogue")
            {
                if (!catalogItems.Any(item => item.Id == "marketplace")) { introductionCard?.ShowError("Catalogue is not available yet. Try again when widgets have finished loading."); return; }
                await SelectAsync("marketplace");
                if (activeWidget == "marketplace") SetIntroduction(introduction.Next());
            }
            else if (introduction.Step == "appearance")
            {
                if (await InvokeIntentCoreAsync(prepareNotification: owner.Session.PrepareAppearanceIntentAsync))
                    SetIntroduction(introduction with { Step = "appearance-details" });
            }
        }
        catch (OperationCanceledException) when (retired || !visible) { }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("introduction-destination", error);
            introductionCard?.ShowError("This page could not be opened. You can still open it from the tray.");
        }
        finally { introductionBusy = false; ScheduleIntroduction(); }
    }

    private void LeaveIntroduction()
    {
        introductionCard?.CancelPendingFocus();
        if (introduction.Step == "welcome") SetIntroduction(introduction with { Paused = true });
        FocusDirectionalTray();
        ScheduleIntroduction();
    }

    private bool RouteIntroductionButton(ControllerButton button, ControllerEventPhase phase)
    {
        if (introductionCard is not { Visibility: Visibility.Visible, Focused: true } card) return false;
        if (phase != ControllerEventPhase.Pressed) return true;
        if (button is ControllerButton.A or ControllerButton.B)
        {
            shellGestures.Claim(button);
            if (button == ControllerButton.A) card.Activate(); else LeaveIntroduction();
        }
        else if (button is ControllerButton.Menu or ControllerButton.Y)
        {
            LeaveIntroduction();
            _ = RouteTrayButtonAsync(button, phase, ControllerInputOrigin.PhysicalController);
        }
        return true;
    }

    private bool ReceiveIntroduction(WidgetRail.OverlayPlatformClient.ControllerFrame frame)
    {
        if (introductionCard is not { Visibility: Visibility.Visible, Focused: true } card) return false;
        WidgetControllerPrompts.Set(frame.LastInputFamily);
        var navigation = frame.DpadNavigation.Phase != WidgetRail.OverlayPlatformClient.NavigationPhase.None ? frame.DpadNavigation : frame.StickNavigation;
        var direction = navigation.Direction switch
        {
            WidgetRail.OverlayPlatformClient.NavigationDirection.Up => FocusNavigationDirection.Up,
            WidgetRail.OverlayPlatformClient.NavigationDirection.Down => FocusNavigationDirection.Down,
            WidgetRail.OverlayPlatformClient.NavigationDirection.Left => FocusNavigationDirection.Left,
            WidgetRail.OverlayPlatformClient.NavigationDirection.Right => FocusNavigationDirection.Right,
            _ => FocusNavigationDirection.None,
        };
        if (navigation.Phase != WidgetRail.OverlayPlatformClient.NavigationPhase.None && direction != FocusNavigationDirection.None &&
            !card.Move(direction) && direction == FocusNavigationDirection.Down) LeaveIntroduction();
        var delta = rightStick.Sample(frame.State.RightThumbX, frame.State.RightThumbY, Environment.TickCount64);
        if (delta.Y != 0) card.Scroll(delta.Y);
        foreach (var (mask, button) in Buttons)
        {
            if ((frame.PressedButtons & mask) != 0) _ = RouteButtonAsync(button, ControllerEventPhase.Pressed);
            if ((frame.ReleasedButtons & mask) != 0) _ = RouteButtonAsync(button, ControllerEventPhase.Released);
        }
        return true;
    }
}
