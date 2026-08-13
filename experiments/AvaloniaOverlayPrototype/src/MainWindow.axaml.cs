using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GameBarAlternative.AvaloniaPrototype.Input;
using GameBarAlternative.AvaloniaPrototype.Lifecycle;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using GameBarAlternative.AvaloniaPrototype.Views;

namespace GameBarAlternative.AvaloniaPrototype;

public sealed partial class MainWindow : Window
{
    private readonly PrototypeLifecycle lifecycle = new();
    private readonly IControllerInputAdapter controller;
    private readonly SemanticInputRouter inputRouter;

    public MainWindow() : this(new XInputControllerAdapter(new XInputStateSource()))
    {
    }

    internal MainWindow(IControllerInputAdapter controller)
    {
        this.controller = controller;
        InitializeComponent();
        inputRouter = new SemanticInputRouter(this);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        controller.InputReceived += ControllerInputReceived;
        Opened += (_, _) =>
        {
            lifecycle.Show();
            ShellView.Resume();
            controller.Start();
            controller.SetActive(IsActive);
        };
        Activated += (_, _) => controller.SetActive(IsVisible);
        Deactivated += (_, _) => controller.SetActive(false);
        Closed += (_, _) =>
        {
            lifecycle.Hide();
            ShellView.Suspend();
            ShellView.Dispose();
            controller.Dispose();
        };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty)
            {
                if (IsVisible)
                {
                    lifecycle.Show();
                    ShellView.Resume();
                    controller.SetActive(IsActive);
                }
                else
                {
                    lifecycle.Hide();
                    ShellView.Suspend();
                    controller.SetActive(false);
                }
            }
        };
        ShellView.RouteChanged += (_, _) =>
        {
            controller.ResetHeldState();
            inputRouter.ResetDuplicateState();
        };
    }

    public PrototypeShellView ShellView => this.FindControl<PrototypeShellView>("Shell")!;

    public PrototypeLifecycle Lifecycle => lifecycle;

    public Task<Navigation.NavigationResult<Control>> NavigateAsync(
        PrototypeRoute route,
        CancellationToken cancellationToken = default) =>
        ShellView.NavigateAsync(route, cancellationToken);

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var semantic = e.Key switch
        {
            Key.Up => SemanticInput.Up,
            Key.Down => SemanticInput.Down,
            Key.Left => SemanticInput.Left,
            Key.Right => SemanticInput.Right,
            Key.Enter => SemanticInput.Activate,
            Key.Escape or Key.B => SemanticInput.Back,
            _ => (SemanticInput?)null,
        };
        if (semantic is { } input && inputRouter.Route(input, SemanticInputSource.Keyboard))
        {
            e.Handled = true;
        }
    }

    private void ControllerInputReceived(object? sender, SemanticInputEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible && IsActive)
            {
                inputRouter.Route(e.Input, e.Source);
            }
        }, DispatcherPriority.Input);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
