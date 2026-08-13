using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameBarAlternative.AvaloniaPrototype.Diagnostics;
using GameBarAlternative.AvaloniaPrototype.Navigation;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed partial class PrototypeShellView : UserControl, IDisposable
{
    private readonly NavigationCoordinator<Control> navigation = new();
    private readonly PrototypePageFactory pageFactory = new();
    private CancellationTokenSource transitionLifetime = new();
    private bool contentOpen = true;
    private bool suspended;

    public PrototypeShellView()
    {
        InitializeComponent();
        ContentRegionControl.Transitions =
        [
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(120),
            },
        ];
    }

    public Border ContentRegionControl => this.FindControl<Border>("ContentRegion")!;

    public Border TrayRegionControl => this.FindControl<Border>("TrayRegion")!;

    public PageTransitionPresenter TransitionPresenterControl =>
        this.FindControl<PageTransitionPresenter>("TransitionPresenter")!;

    private Border LoadingStatusControl => this.FindControl<Border>("LoadingStatus")!;

    private TextBlock LoadingTextControl => this.FindControl<TextBlock>("LoadingText")!;

    private Button SettingsTrayButtonControl => this.FindControl<Button>("SettingsTrayButton")!;

    public NavigationCoordinator<Control> Navigation => navigation;

    public async Task<NavigationResult<Control>> NavigateAsync(
        PrototypeRoute route,
        CancellationToken cancellationToken = default)
    {
        if (suspended)
        {
            return new NavigationResult<Control>(NavigationOutcome.Cancelled, route, null, TimeSpan.Zero);
        }

        contentOpen = true;
        ContentRegionControl.IsVisible = true;
        ContentRegionControl.Opacity = 1;
        LoadingTextControl.Text = $"Preparing {RouteTitle(route)}…";
        AutomationProperties.SetName(LoadingStatusControl, $"Preparing {RouteTitle(route)}");
        LoadingStatusControl.IsVisible = true;

        var result = await navigation.NavigateAsync(
            route,
            pageFactory.CreateAsync,
            recordHistory: true,
            cancellationToken);

        if (result.Outcome != NavigationOutcome.Committed || result.Page is null)
        {
            if (!navigation.IsLoading)
            {
                LoadingStatusControl.IsVisible = false;
            }

            return result;
        }

        if (suspended)
        {
            return result with { Outcome = NavigationOutcome.Cancelled, Page = null };
        }

        transitionLifetime.Cancel();
        transitionLifetime.Dispose();
        var ownedTransition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        transitionLifetime = ownedTransition;
        await TransitionPresenterControl.PresentAsync(result.Page, ownedTransition.Token);
        if (suspended || ownedTransition.IsCancellationRequested)
        {
            return result with { Outcome = NavigationOutcome.Cancelled, Page = null };
        }

        LoadingStatusControl.IsVisible = false;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        FrameDiagnostics.Record(this, route, result.LoadDuration);
        FocusFirstElement(result.Page);
        return result;
    }

    public async Task BackAsync()
    {
        var previous = navigation.PopHistory();
        if (previous is { } route)
        {
            await NavigateWithoutHistoryAsync(route);
            return;
        }

        navigation.CancelPending();
        contentOpen = !contentOpen;
        ContentRegionControl.Opacity = contentOpen ? 1 : 0;
        if (!contentOpen)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(130));
            ContentRegionControl.IsVisible = false;
            SettingsTrayButtonControl.Focus();
        }
        else
        {
            ContentRegionControl.IsVisible = true;
        }
    }

    public FrameSnapshot CaptureFrame(PrototypeRoute route, TimeSpan transitionDuration) =>
        FrameDiagnostics.Capture(this, route, transitionDuration);

    public void Resume() => suspended = false;

    public void Suspend()
    {
        suspended = true;
        navigation.CancelPending();
        transitionLifetime.Cancel();
        TransitionPresenterControl.CancelTransition();
        LoadingStatusControl.IsVisible = false;
    }

    public void Dispose()
    {
        transitionLifetime.Cancel();
        transitionLifetime.Dispose();
        TransitionPresenterControl.Dispose();
        navigation.Dispose();
    }

    private async Task NavigateWithoutHistoryAsync(PrototypeRoute route)
    {
        LoadingTextControl.Text = $"Preparing {RouteTitle(route)}…";
        LoadingStatusControl.IsVisible = true;
        var result = await navigation.NavigateAsync(route, pageFactory.CreateAsync, recordHistory: false);
        if (result.Outcome == NavigationOutcome.Committed && result.Page is not null && !suspended)
        {
            transitionLifetime.Cancel();
            transitionLifetime.Dispose();
            var ownedTransition = new CancellationTokenSource();
            transitionLifetime = ownedTransition;
            await TransitionPresenterControl.PresentAsync(result.Page, ownedTransition.Token);
            if (suspended || ownedTransition.IsCancellationRequested)
            {
                return;
            }

            LoadingStatusControl.IsVisible = false;
            FrameDiagnostics.Record(this, route, result.LoadDuration);
            FocusFirstElement(result.Page);
        }
    }

    private static void FocusFirstElement(Control page)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var first = page.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => control.Focusable && control.IsVisible && control.IsEffectivelyEnabled);
            first?.Focus();
        }, DispatcherPriority.Input);
    }

    private static string RouteTitle(PrototypeRoute route) => route switch
    {
        PrototypeRoute.Settings => "Settings",
        PrototypeRoute.AudioMixer => "Audio Mixer",
        PrototypeRoute.SpotifyPlayer => "Spotify Player",
        PrototypeRoute.GameLauncher => "Game Launcher",
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private void SettingsClicked(object? sender, RoutedEventArgs e) => _ = NavigateAsync(PrototypeRoute.Settings);

    private void AudioClicked(object? sender, RoutedEventArgs e) => _ = NavigateAsync(PrototypeRoute.AudioMixer);

    private void SpotifyClicked(object? sender, RoutedEventArgs e) => _ = NavigateAsync(PrototypeRoute.SpotifyPlayer);

    private void LauncherClicked(object? sender, RoutedEventArgs e) => _ = NavigateAsync(PrototypeRoute.GameLauncher);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
