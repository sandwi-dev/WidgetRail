using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Hardware validation only; production scope/action routing is separate.</summary>
internal sealed partial class ControllerValidationPage : Page
{
    private readonly TextBlock status = new() { Text = "Waiting for native input" };
    private readonly TextBlock action = new() { Text = "No action" };
    private readonly TextBlock replay = new() { Text = "Physical input mode" };
    private readonly WidgetViewPresenter presenter = new();
    private long lastStatus;
    private long frames;
    private int actions;

    public ControllerValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Controller.Status");
        AutomationProperties.SetAutomationId(action, "Controller.Action");
        AutomationProperties.SetAutomationId(replay, "Controller.Replay");
        Content = new StackPanel { Spacing = 16, Children = { status, action, replay, presenter } };
        presenter.DispatchActionAsync = request =>
        {
            action.Text = $"{request.Action.ActionId}: {++actions}";
            return Task.CompletedTask;
        };
        var snapshot = new ViewSnapshot { WidgetInstanceId = "controller.instance", Sequence = 1,
            ActiveInputScopeId = "controller", InitialFocusId = "Controller.First",
            Root = new() { Id = "controller", Kind = ViewNodeKind.Row,
                Children = new[] { "First", "Second", "Third" }.Select(name => new ViewNode
                    { Id = $"Controller.{name}", Kind = ViewNodeKind.Button, Text = name, ActionId = name }).ToArray() } };
        var descriptor = new BridgeWidgetDescriptor { Id = "controller", Name = "Controller", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = "controller", PresentationGeneration = "controller", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
            descriptor.InstanceId, 1, snapshot.ActiveInputScopeId), descriptor,
            SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), new Dictionary<string, BridgeNodeRenderStyles>()));
        Loaded += (_, _) => QueueEntryFocus();
    }

    public int ActionCount => actions;
    public string FocusedId => XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused
        ? AutomationProperties.GetAutomationId(focused).Replace("Widget.", "", StringComparison.Ordinal) : string.Empty;
    public void SetReplayStatus(string value) => replay.Text = value;

    public void QueueEntryFocus() => presenter.Enter(restoreNativeFocus: true);
    public void ResetInputPresentation()
    {
        presenter.ResetPressedStyles();
        presenter.DismissTransientControl();
    }

    public void Receive(ControllerFrame frame)
    {
        ++frames;
        if (Environment.TickCount64 - lastStatus >= 250)
        {
            lastStatus = Environment.TickCount64;
            status.Text = $"Native frames: {frames}; connected: {frame.Connected != 0}; path: {frame.ReadPath}; focus: {FocusedId}";
        }
        if (frame.Connected == 0) return;
        presenter.SetControllerFamily(frame.LastInputFamily);
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
            presenter.MoveFocus(next);
        if ((frame.PressedButtons & 0x1000) != 0)
            _ = presenter.HandleControllerButtonAsync(ControllerButton.A);
        if ((frame.ReleasedButtons & 0x1000) != 0)
            _ = presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
    }

    public void ReportFailure(Exception error) => status.Text = $"Native input failed: {error.Message}";
}
