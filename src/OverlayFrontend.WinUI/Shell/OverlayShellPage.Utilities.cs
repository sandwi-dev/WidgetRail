using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WindowsAppLibraryProvider;
using WidgetRail.WindowsWindowActivation;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed record HostTaskActivationRequest(TaskWindowTarget Target, long InitiatedAt, long VisibleSince, Func<bool> IsCurrent);

internal sealed partial class OverlayShellPage
{
    private const string SystemTaskViewId = "system-task-view";
    internal Func<Task<WindowsTaskViewResult>>? SystemTaskViewRequested { get; set; }
    private bool SystemTaskViewFocused => shownUtility == ShellUtility.Tasks && utilityPanel?.FocusedId == SystemTaskViewId;
    private static ShellUtilityEntry SystemTaskViewEntry() => new(SystemTaskViewId, "Windows Task View. Open Windows' app switcher.", SystemTaskViewId,
        () => new ShellUtilityActionRow("Windows Task View", "Open Windows’ app switcher", "\uE7C4"));
    private enum ShellUtility { Notifications, Tasks }
    private bool HasPendingNotifications => browserOwner?.Notifications.Entries.Count > 0;
    private readonly Button taskSwitcherButton = new() { Width = 48, Height = 48, Padding = new(4),
        CornerRadius = new(12), VerticalAlignment = VerticalAlignment.Center };
    private WindowsTaskWindowCatalog? taskWindowCatalog;
    private WindowsTaskWindowCatalog TaskWindows => taskWindowCatalog ??= new();
    private readonly SemaphoreSlim utilityRefreshGate = new(1, 1);
    private ShellUtilityPanel? utilityPanel;
    private ShellUtility? shownUtility;
    private CancellationTokenSource? utilityCancellation;
    private Task utilityRefresh = Task.CompletedTask;
    private IReadOnlyList<TaskWindowSummary> utilityWindows = [];
    private readonly Dictionary<string, (string Name, ImageSource? Image)> utilityIcons = new(StringComparer.Ordinal);
    private object? pendingHostTask;
    private string? taskStatus;
    private bool utilityClosing;
    private bool utilityEntered;
    private (ShellUtilityPanel Panel, double X, double Y, double Width, double Height, AppearanceSettings Appearance, object? Styles)? utilityPlacement;
    internal Func<HostTaskActivationRequest, Task<TaskWindowActivationResult>>? HostTaskActivationRequested { get; set; }
    internal void CancelPendingHostTaskActivation() => pendingHostTask = null;
    internal void CompleteControllerHandoffRelease() => shellGestures.Reset();
    internal void RefreshHostTaskActivationPermission() => owner?.RefreshForegroundPermissionForHandoff();
    internal Task<WidgetRail.WidgetPresentationSession.WidgetTaskActivationResult> CompleteHostTaskActivationAsync(HostTaskActivationRequest request, CancellationToken token)
    {
        if (owner is null || !request.IsCurrent()) throw new OperationCanceledException(token);
        var target = request.Target;
        return owner.Session.ActivateHostTaskWindowAsync(new("host-task", target.Handle, target.ProcessId, target.ProcessCreated, target.ClassName), request.InitiatedAt, token);
    }
    private bool UtilityIconFocused => notificationBell.FocusState != FocusState.Unfocused || taskSwitcherButton.FocusState != FocusState.Unfocused;
    private bool UtilityListFocused => utilityPanel is { } panel && (panel.ContainsFocus || panel.HasPendingFocus);
    private Button UtilityIcon => shownUtility == ShellUtility.Tasks ? taskSwitcherButton : notificationBell;

    private void InitializeUtilities()
    {
        taskSwitcherButton.Content = new FontIcon { Glyph = "\uE7C4", FontFamily = new("Segoe Fluent Icons"), FontSize = 22 }; // TaskView
        AutomationProperties.SetAutomationId(taskSwitcherButton, "Overlay.TaskSwitcher");
        AutomationProperties.SetName(taskSwitcherButton, "Task Switcher");
        InitializeUtilityIcon(notificationBell, ShellUtility.Notifications);
        InitializeUtilityIcon(taskSwitcherButton, ShellUtility.Tasks);
        LayoutUpdated += (_, _) => PositionUtilityPanel();
    }
    private void InitializeUtilityIcon(Button icon, ShellUtility kind)
    {
        var directionalFocus = false;
        icon.GettingFocus += (_, args) =>
        {
            directionalFocus = args.Direction != FocusNavigationDirection.None;
            if (!retired && !AllowsNativeShellFocusTransfer(args)) { args.TryCancel(); return; }
        };
        icon.GotFocus += (_, _) =>
        {
            WindowActivationDiagnostic?.Invoke($"Utility icon focus kind={kind} visible={visible} switching={switching} radial={RadialOpen} entered={utilityEntered} directional={directionalFocus} state={icon.FocusState}");
            if (retired || !visible || !foreground || HostChoiceActive || switching || utilityClosing || icon.FocusState == FocusState.Unfocused) return;
            // Native reactivation can restore the old icon after Up has already
            // entered the list. Only a user transfer or Back may leave that scope.
            if (utilityEntered && shownUtility == kind && !directionalFocus && icon.FocusState != FocusState.Pointer)
            { utilityPanel?.RestoreEntryFocus(); return; }
            utilityEntered = false;
            // Removing a panel can schedule fallback focus after Back has already
            // opened the wheel. That is not a fresh request to reopen the panel.
            if (RadialOpen && !directionalFocus) return;
            if (RadialOpen) { radialRequested = false; RefreshRadialChooser(); }
            ClearTrayFocus(); SetInteractive(false); ShowUtility(kind); PublishControllerGuide();
        };
        icon.Click += (_, _) => FocusUtility(kind);
        icon.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Up) { EnterUtility(); e.Handled = true; }
            else if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)
            { NavigateNotificationBell(e.Key == Windows.System.VirtualKey.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            { CloseUtilityPanel(); _ = BackFromTrayAsync(); e.Handled = true; }
        };
    }
    private void FocusUtility(ShellUtility kind)
    {
        if (retired || !visible || !foreground || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen) return;
        utilityEntered = false;
        radialRequested = false; ClearTrayFocus(); SetInteractive(false); RefreshRadialChooser();
        ShowUtility(kind);
        (kind == ShellUtility.Tasks ? taskSwitcherButton : notificationBell).Focus(FocusState.Keyboard);
        PublishControllerGuide();
    }
    private void ShowUtility(ShellUtility kind)
    {
        if (utilityPanel is not null && shownUtility == kind) return;
        CloseUtilityPanel(); shownUtility = kind;
        var panel = new ShellUtilityPanel(); utilityPanel = panel;
        panel.Configure(kind == ShellUtility.Tasks ? "Task Switcher" : "Notifications", kind == ShellUtility.Tasks ? "Refresh" : "Clear all", iconCommand: kind == ShellUtility.Tasks);
        panel.Back += ReturnToUtilityIcon;
        panel.FocusChanged += () =>
        {
            if (ReferenceEquals(panel, utilityPanel) && panel.ContainsFocus) utilityEntered = true;
            WindowActivationDiagnostic?.Invoke($"Utility panel focus row={panel.FocusedId is not null} pending={panel.HasPendingFocus}");
            PublishControllerGuide();
        };
        panel.EntryShown += id => { if (kind == ShellUtility.Notifications && long.TryParse(id, out var notification)) browserOwner?.Notifications.MarkShown(notification); };
        panel.Activated += id => _ = ActivateUtilityItemAsync(panel, kind, id);
        panel.Command += () =>
        {
            if (kind == ShellUtility.Notifications) { browserOwner?.Notifications.Clear(); HideNotificationBanner(); RefreshUtilityNotifications(); }
            else if (utilityCancellation is { } cancellation) { taskStatus = null; _ = RefreshUtilityTasksAsync(panel, cancellation.Token); }
        };
        ShellOverlayLayer.Children.Add(panel);
        utilityCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        if (kind == ShellUtility.Notifications) RefreshUtilityNotifications();
        else
        {
            utilityWindows = [];
            panel.Update((ShellUtilityEntry[])[SystemTaskViewEntry()]);
            panel.SetStatus(taskStatus ?? "Finding open windows…");
            utilityRefresh = RunUtilityTasksAsync(panel, utilityCancellation.Token);
        }
        PositionUtilityPanel();
    }
    private void ReturnToUtilityIcon()
    {
        WindowActivationDiagnostic?.Invoke("Utility return to icon requested");
        if (!visible || retired || utilityPanel is null) return;
        utilityEntered = false;
        utilityPanel.CancelPendingFocus();
        UtilityIcon.Focus(FocusState.Keyboard); PublishControllerGuide();
    }
    private void CloseUtilityPanel(bool cancelSwitch = true, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (utilityPanel is not null) WindowActivationDiagnostic?.Invoke($"Utility close caller={caller} entered={utilityEntered}");
        utilityEntered = false;
        if (cancelSwitch) pendingHostTask = null;
        utilityCancellation?.Cancel(); utilityCancellation?.Dispose(); utilityCancellation = null;
        var panel = utilityPanel; utilityPanel = null; shownUtility = null;
        utilityPlacement = null;
        if (panel is not null)
        {
            utilityClosing = true;
            try { ShellOverlayLayer.Children.Remove(panel); panel.Dispose(); }
            finally { utilityClosing = false; }
        }
    }
    private void EnterUtility()
    {
        if (utilityPanel is null) return;
        utilityEntered = true;
        utilityPanel.Enter();
    }
    private void SuspendUtilityPanel()
    {
        utilityPanel?.CancelPendingFocus();
        utilityCancellation?.Cancel();
    }
    private void ResumeUtilityPanel()
    {
        if (utilityPanel is not { } panel || utilityCancellation?.IsCancellationRequested != true) return;
        utilityCancellation.Dispose();
        utilityCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        if (shownUtility == ShellUtility.Tasks) utilityRefresh = RunUtilityTasksAsync(panel, utilityCancellation.Token);
        else RefreshUtilityNotifications();
    }
    private void PositionUtilityPanel()
    {
        if (utilityPanel is not { } panel || XamlRoot is null || UtilityIcon.ActualWidth <= 0) return;
        var anchor = UtilityIcon.TransformToVisual(ShellOverlayLayer).TransformPoint(new(0, 0));
        var placement = (panel, anchor.X, anchor.Y, ShellOverlayLayer.ActualWidth, ShellOverlayLayer.ActualHeight, Appearance, (object?)ShellPalette);
        if (utilityPlacement == placement) return;
        utilityPlacement = placement;
        // This layer is already inside OverlayScaleRoot; do not apply interface zoom twice.
        var width = Math.Min(440, Math.Max(1, ShellOverlayLayer.ActualWidth - 32));
        var bottom = Math.Min(anchor.Y - 12, ShellOverlayLayer.ActualHeight);
        var height = Math.Max(160, bottom - 16);
        var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
        panel.Apply(palette, PreviewFrameStyle.Resolve(ShellPalette, palette, Appearance), Appearance.TextScale, height);
        panel.Width = width;
        panel.MaxHeight = height;
        var left = Appearance.OverlayPosition == OverlayPosition.BottomLeft;
        var x = Math.Clamp(left ? anchor.X : anchor.X + UtilityIcon.ActualWidth - width, 16, Math.Max(16, ShellOverlayLayer.ActualWidth - width - 16));
        // Native bottom alignment measures content once. Position must not depend
        // on DesiredSize (which includes Margin), or layout feeds back on itself.
        var margin = new Thickness(x, 16, 0, Math.Max(0, ShellOverlayLayer.ActualHeight - bottom));
        if (panel.Margin != margin) panel.Margin = margin;
    }
    private void RefreshUtilityNotifications()
    {
        if (shownUtility != ShellUtility.Notifications || utilityPanel is not { } panel || browserOwner is not { } browsers) return;
        var inbox = browsers.Notifications;
        panel.SetCount(inbox.Entries.Count);
        panel.Update(inbox.Entries.Select(entry => new ShellUtilityEntry(entry.Id.ToString(CultureInfo.InvariantCulture),
            NotificationChoice(entry).Label, (entry.ContentRevision, entry.Available), () =>
            {
                var card = new NotificationCard(ResolveTrayIconAsync);
                card.Update(entry, catalogItems.FirstOrDefault(widget => widget.Id == entry.Widget), Appearance.TextScale);
                card.ApplyBannerPalette(ShellChromePalette.Resolve(ShellPalette, Appearance));
                return card;
            }, entry.Interactive)).ToArray());
        panel.SetStatus(inbox.Entries.Count == 0 ? "You're all caught up" : inbox.OverflowCount > 0 ?
            $"{inbox.OverflowCount} additional notifications. Open the webapp for its full history." : "");
        panel.SetCommandEnabled(inbox.Entries.Count > 0 || inbox.OverflowCount > 0);
        PublishControllerGuide();
    }
    private async Task RunUtilityTasksAsync(ShellUtilityPanel panel, CancellationToken token)
    {
        try
        {
            do { await RefreshUtilityTasksAsync(panel, token); await Task.Delay(2000, token); }
            while (!token.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private async Task RefreshUtilityTasksAsync(ShellUtilityPanel panel, CancellationToken token)
    {
        try
        {
            await utilityRefreshGate.WaitAsync(token);
            try
            {
                var windows = await TaskWindows.GetWindowsAsync(token);
                if (!ReferenceEquals(utilityPanel, panel) || token.IsCancellationRequested) return;
                utilityWindows = windows;
                panel.SetCount(windows.Count);
                panel.Update((ShellUtilityEntry[])[SystemTaskViewEntry(), .. windows.Select(window => new ShellUtilityEntry(window.WindowId,
                    $"{window.ApplicationName}. {window.Title}. {(window.IsMinimized ? "Minimized" : "Open")}", window,
                    () => TaskWindowRow(window)))]);
                panel.SetStatus(taskStatus ?? (windows.Count == 0 ? "No other application windows are open." : ""));
            }
            finally { utilityRefreshGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("host-task-list", error);
            if (ReferenceEquals(utilityPanel, panel)) panel.SetStatus("Couldn't refresh windows. Try Refresh.");
        }
    }
    private UIElement TaskWindowRow(TaskWindowSummary window)
    {
        var row = new ShellTaskWindowRow(window);
        var identity = window.WindowId[..window.WindowId.LastIndexOf(':')];
        if (utilityIcons.TryGetValue(identity, out var ready)) row.Update(ready.Name, ready.Image);
        var panel = utilityPanel;
        var token = utilityCancellation?.Token ?? lifetime.Token;
        var started = false;
        row.Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            try
            {
                var appearance = await TaskWindows.GetAppearanceAsync(window.WindowId, token);
                if (appearance is null || token.IsCancellationRequested || !ReferenceEquals(panel, utilityPanel)) return;
                var cached = utilityIcons.TryGetValue(appearance.Identity, out var existing);
                var image = existing.Image;
                if (!cached && appearance.PngBase64 is { } png)
                {
                    using var bytes = new MemoryStream(Convert.FromBase64String(png), writable: false);
                    using var stream = bytes.AsRandomAccessStream();
                    var bitmap = new BitmapImage { DecodePixelWidth = 48, DecodePixelHeight = 48 };
                    await bitmap.SetSourceAsync(stream);
                    if (token.IsCancellationRequested || !ReferenceEquals(panel, utilityPanel)) return;
                    image = bitmap;
                }
                if (!cached && utilityIcons.Count >= 128) utilityIcons.Remove(utilityIcons.Keys.First());
                utilityIcons[appearance.Identity] = (appearance.ApplicationName, image);
                row.Update(appearance.ApplicationName, image);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { Diagnostics.FrontendFailureLog.Current.Write("task-row-icon", error); }
        };
        return row;
    }
    private async Task ActivateUtilityItemAsync(ShellUtilityPanel panel, ShellUtility kind, string id)
    {
        if (!ReferenceEquals(panel, utilityPanel) || panel.Busy || !visible || !foreground) return;
        if (kind == ShellUtility.Notifications)
        {
            if (!long.TryParse(id, out var notificationId) || browserOwner is not { } browsers) return;
            var entry = browsers.Notifications.Find(notificationId);
            if (entry is not null && !entry.Interactive) return;
            if (entry is null || !catalogItems.Any(widget => widget.Id == entry.Widget))
            { browsers.Notifications.Dismiss(notificationId); panel.SetStatus("This notification is no longer available."); return; }
            CloseUtilityPanel(); await OpenUtilityNotificationAsync(id); return;
        }
        if (id == SystemTaskViewId)
        {
            if (SystemTaskViewRequested is null) return;
            panel.Busy = true; taskStatus = null;
            try
            {
                var result = await SystemTaskViewRequested();
                if (result is WindowsTaskViewResult.KeyboardBusy or WindowsTaskViewResult.InputRejected)
                    taskStatus = result == WindowsTaskViewResult.KeyboardBusy ? "Release keyboard modifiers and try again." : "Couldn't open Windows Task View. Try again.";
            }
            catch (Exception error)
            { Diagnostics.FrontendFailureLog.Current.Write("system-task-view", error); taskStatus = "Couldn't open Windows Task View. Try again."; }
            finally
            {
                panel.Busy = false;
                if (visible && taskStatus is not null) utilityPanel?.SetStatus(taskStatus);
            }
            return;
        }
        if (utilityWindows.FirstOrDefault(window => window.WindowId == id)?.PreviewTarget is not { } target || HostTaskActivationRequested is null) return;
        if (!ulong.TryParse(target.Handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var handle) ||
            !ulong.TryParse(target.ProcessCreated, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var created)) return;
        var request = new object(); pendingHostTask = request; panel.Busy = true; taskStatus = null;
        try
        {
            var result = await HostTaskActivationRequested(new(new(handle, target.ProcessId, created, target.ClassName),
                Environment.TickCount64, visibleSince, () => !retired && ReferenceEquals(pendingHostTask, request)));
            if (result != TaskWindowActivationResult.Requested && ReferenceEquals(pendingHostTask, request))
            {
                taskStatus = "Windows couldn't switch to that window. Refresh the list or use the taskbar.";
                if (visible)
                {
                    if (!ReferenceEquals(panel, utilityPanel)) FocusUtility(ShellUtility.Tasks);
                    utilityPanel?.SetStatus(taskStatus);
                }
            }
        }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("host-task-switch", error);
            if (ReferenceEquals(pendingHostTask, request))
            {
                taskStatus = "Couldn't switch windows. Try again.";
                if (visible && ReferenceEquals(utilityPanel, panel)) panel.SetStatus(taskStatus);
            }
        }
        finally { if (ReferenceEquals(pendingHostTask, request)) pendingHostTask = null; panel.Busy = false; }
    }
    private async Task DismissUtilityItemAsync(ShellUtilityPanel panel, ShellUtility kind, string id)
    {
        if (!ReferenceEquals(panel, utilityPanel) || panel.Busy || !visible || !foreground) return;
        if (kind == ShellUtility.Tasks && id == SystemTaskViewId) return;
        if (kind == ShellUtility.Notifications)
        { if (long.TryParse(id, out var notification)) browserOwner?.Notifications.Dismiss(notification); return; }
        panel.Busy = true;
        panel.SetStatus("Requesting close…");
        try
        {
            await TaskWindows.CloseAsync(id, lifetime.Token);
            if (ReferenceEquals(panel, utilityPanel)) { panel.SetStatus("Close requested. The app may ask you to save your work."); if (utilityCancellation is { } c) await RefreshUtilityTasksAsync(panel, c.Token); }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("host-task-close", error);
            if (ReferenceEquals(panel, utilityPanel)) panel.SetStatus("Couldn't close that window. Close it from the app itself.");
        }
        finally { panel.Busy = false; }
    }
}
