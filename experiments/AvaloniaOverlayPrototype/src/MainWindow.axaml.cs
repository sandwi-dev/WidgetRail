using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GameBarAlternative.AvaloniaPrototype.Lifecycle;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using GameBarAlternative.AvaloniaPrototype.Views;

namespace GameBarAlternative.AvaloniaPrototype;

public sealed partial class MainWindow : Window
{
    private readonly PrototypeLifecycle lifecycle = new();

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            lifecycle.Show();
            ShellView.Resume();
        };
        Closed += (_, _) =>
        {
            lifecycle.Hide();
            ShellView.Suspend();
            ShellView.Dispose();
        };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty)
            {
                if (IsVisible)
                {
                    lifecycle.Show();
                    ShellView.Resume();
                }
                else
                {
                    lifecycle.Hide();
                    ShellView.Suspend();
                }
            }
        };
    }

    public PrototypeShellView ShellView => this.FindControl<PrototypeShellView>("Shell")!;

    public PrototypeLifecycle Lifecycle => lifecycle;

    public Task<Navigation.NavigationResult<Control>> NavigateAsync(
        PrototypeRoute route,
        CancellationToken cancellationToken = default) =>
        ShellView.NavigateAsync(route, cancellationToken);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.B)
        {
            _ = ShellView.BackAsync();
            e.Handled = true;
            return;
        }

        if (FocusNavigator.HandleDirectionalKey(this, e))
        {
            return;
        }

        base.OnKeyDown(e);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
