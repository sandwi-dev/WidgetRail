using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Hardware validation only; production scope/action routing is separate.</summary>
internal sealed class ControllerValidationPage : Page
{
    private readonly TextBlock status = new() { Text = "Waiting for native input" };
    private readonly TextBlock action = new() { Text = "No action" };
    private readonly StackPanel scope = new() { Spacing = 16 };
    private long lastStatus;
    private long frames;
    private int actions;
    private Button? entry;
    private string focusEntry = "pending";

    public ControllerValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Controller.Status");
        AutomationProperties.SetAutomationId(action, "Controller.Action");
        scope.Children.Add(status);
        scope.Children.Add(action);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var name in new[] { "First", "Second", "Third" })
        {
            var button = new Button { Content = name, Command = new RelayCommand(() => action.Text = $"{name}: {++actions}") };
            AutomationProperties.SetAutomationId(button, $"Controller.{name}");
            entry ??= button;
            row.Children.Add(button);
        }
        scope.Children.Add(row);
        scope.XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;
        Content = scope;
        Loaded += (_, _) => QueueEntryFocus();
    }

    public void QueueEntryFocus() => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
    {
        if (!IsLoaded || XamlRoot is null || entry is null) return;
        // Loaded can precede activation of the native island. Check the result
        // after activation; never assume an early Focus call established input.
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        for (var current = focused; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, scope)) { focusEntry = "retained"; return; }
        focusEntry = entry.Focus(FocusState.Keyboard) ? "focused" : "failed";
    });

    public void Receive(ControllerFrame frame)
    {
        ++frames;
        if (Environment.TickCount64 - lastStatus >= 250)
        {
            lastStatus = Environment.TickCount64;
            status.Text = $"Native frames: {frames}; connected: {frame.Connected != 0}; path: {frame.ReadPath}; entry: {focusEntry}";
        }
        if (frame.Connected == 0) return;
        var direction = frame.DpadNavigation.Phase != NavigationPhase.None ? frame.DpadNavigation : frame.StickNavigation;
        var next = direction.Direction switch
        {
            NavigationDirection.Left => FocusNavigationDirection.Left,
            NavigationDirection.Right => FocusNavigationDirection.Right,
            NavigationDirection.Up => FocusNavigationDirection.Up,
            NavigationDirection.Down => FocusNavigationDirection.Down,
            _ => FocusNavigationDirection.None,
        };
        if (direction.Phase != NavigationPhase.None && next != FocusNavigationDirection.None)
            FocusManager.TryMoveFocus(next, new FindNextElementOptions { SearchRoot = scope });
        if ((frame.PressedButtons & 0x1000) != 0 &&
            FocusManager.GetFocusedElement(XamlRoot) is Button { Command: { } command } button &&
            command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter);
    }

    public void ReportFailure(Exception error) => status.Text = $"Native input failed: {error.Message}";
}
