using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameBarAlternative.AvaloniaPrototype.Composition;
using GameBarAlternative.AvaloniaPrototype.Diagnostics;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using GameBarAlternative.AvaloniaPrototype.ViewModels;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed partial class PrototypeShellView : UserControl, IDisposable
{
    private readonly NavigationCoordinator<Control> navigation = new();
    private readonly PrototypeComposition composition;
    private readonly PrototypePageFactory pageFactory;
    private readonly PrototypeShellViewModel viewModel;
    private readonly bool ownsComposition;
    private CancellationTokenSource transitionLifetime = new();
    private readonly Dictionary<PrototypeRoute, string> lastPageFocus = [];
    private bool contentOpen = true;
    private bool suspended;
    private PrototypeRoute selectedRoute = PrototypeRoute.Settings;

    public PrototypeShellView() : this(PrototypeComposition.Create(), ownsComposition: true)
    {
    }

    internal PrototypeShellView(PrototypeComposition composition, bool ownsComposition = false)
    {
        this.composition = composition;
        this.ownsComposition = ownsComposition;
        pageFactory = composition.PageFactory;
        viewModel = composition.Shell;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RouteRequested += RouteRequested;
        ContentRegionControl.Transitions =
        [
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(120),
            },
        ];
        AddHandler(GotFocusEvent, OnDescendantGotFocus, RoutingStrategies.Bubble);
    }

    public Border ContentRegionControl => this.FindControl<Border>("ContentRegion")!;

    public Border TrayRegionControl => this.FindControl<Border>("TrayRegion")!;

    public PageTransitionPresenter TransitionPresenterControl =>
        this.FindControl<PageTransitionPresenter>("TransitionPresenter")!;

    private Border LoadingStatusControl => this.FindControl<Border>("LoadingStatus")!;

    private Button SettingsTrayButtonControl => this.FindControl<Button>("SettingsTrayButton")!;

    public IReadOnlyList<Button> TrayButtons =>
    [
        this.FindControl<Button>("SettingsTrayButton")!,
        this.FindControl<Button>("AudioTrayButton")!,
        this.FindControl<Button>("SpotifyTrayButton")!,
        this.FindControl<Button>("LauncherTrayButton")!,
    ];

    public NavigationCoordinator<Control> Navigation => navigation;

    public Control? ActivePage => TransitionPresenterControl.AdmittedPage;

    public PrototypeRoute SelectedRoute => selectedRoute;

    public Button SelectedTrayButton => TrayButtons[(int)selectedRoute];

    public event EventHandler<PrototypeRoute>? RouteChanged;

    public async Task<NavigationResult<Control>> NavigateAsync(
        PrototypeRoute route,
        CancellationToken cancellationToken = default)
    {
        if (suspended)
        {
            return new NavigationResult<Control>(NavigationOutcome.Cancelled, route, null, TimeSpan.Zero);
        }

        selectedRoute = route;
        if (route != PrototypeRoute.GameLauncher)
        {
            composition.Launcher.Deactivate();
        }

        contentOpen = true;
        ContentRegionControl.IsVisible = true;
        ContentRegionControl.Opacity = 1;
        viewModel.BeginNavigation(route, RouteTitle(route));
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
                viewModel.CancelNavigation();
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
        await TransitionPresenterControl.PresentAsync(
            result.Page,
            phase => FrameDiagnostics.RecordTransition(this, route, phase),
            ownedTransition.Token);
        if (suspended || ownedTransition.IsCancellationRequested)
        {
            return result with { Outcome = NavigationOutcome.Cancelled, Page = null };
        }

        LoadingStatusControl.IsVisible = false;
        viewModel.CompleteNavigation(route);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        FrameDiagnostics.Record(this, route, result.LoadDuration);
        SelectedTrayButton.Focus(NavigationMethod.Directional);
        RouteChanged?.Invoke(this, route);
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
            SelectedTrayButton.Focus();
        }
        else
        {
            ContentRegionControl.IsVisible = true;
        }
    }

    public FrameSnapshot CaptureFrame(PrototypeRoute route, TimeSpan transitionDuration) =>
        FrameDiagnostics.Capture(this, route, transitionDuration);

    public bool TryCycleTray(Control? focused, int delta)
    {
        var buttons = TrayButtons;
        var current = focused is null ? -1 : Array.IndexOf(buttons.ToArray(), focused);
        if (current < 0)
        {
            return false;
        }

        var next = (current + delta + buttons.Count) % buttons.Count;
        selectedRoute = (PrototypeRoute)next;
        buttons[next].Focus(Avalonia.Input.NavigationMethod.Directional);
        _ = NavigateAsync(selectedRoute);
        return true;
    }

    public bool TryEnterContent(Control? focused)
    {
        if (!IsTrayFocus(focused))
        {
            return false;
        }

        if (ActivePage is not { } page || Navigation.CurrentRoute != selectedRoute)
        {
            _ = NavigateAndEnterContentAsync(selectedRoute);
            return true;
        }

        return FocusRememberedOrInitial(page);
    }

    public bool RestoreSelectedTrayFocus(Control? focused)
    {
        if (!IsPageFocus(focused))
        {
            return false;
        }

        RememberPageFocus(focused);
        return SelectedTrayButton.Focus(NavigationMethod.Directional);
    }

    public bool TryMoveSpatial(Control? focused, NavigationDirection direction)
    {
        if (focused is null || IsTrayFocus(focused))
        {
            return false;
        }

        RememberPageFocus(focused);
        if (ActivePage is IPrototypeSemanticPage semanticPage && semanticPage.TryMoveSemantic(focused, direction))
        {
            return true;
        }

        return FocusNavigator.Move(focused, direction, GetSpatialSearchRoots(focused));
    }

    public bool TryActivateFocused(Control? focused) =>
        focused is not null && ActivePage is IPrototypeSemanticPage semanticPage && semanticPage.TryActivateSemantic(focused);

    internal IReadOnlyList<Control> GetSpatialSearchRoots(Control focused)
    {
        if (IsTrayFocus(focused))
        {
            return [TrayRegionControl];
        }

        if (ActivePage is { } page && IsWithin(focused, page))
        {
            var roots = new List<Control>(2);
            var scroll = focused.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is not null)
            {
                roots.Add(scroll);
            }

            roots.Add(page);
            return roots;
        }

        return TopLevel.GetTopLevel(focused) is Control modalRoot ? [modalRoot] : [];
    }

    public void Resume() => suspended = false;

    public void Suspend()
    {
        suspended = true;
        navigation.CancelPending();
        transitionLifetime.Cancel();
        TransitionPresenterControl.CancelTransition();
        LoadingStatusControl.IsVisible = false;
        composition.Launcher.Deactivate();
        viewModel.CancelNavigation();
    }

    public void Dispose()
    {
        transitionLifetime.Cancel();
        transitionLifetime.Dispose();
        TransitionPresenterControl.Dispose();
        navigation.Dispose();
        viewModel.RouteRequested -= RouteRequested;
        if (ownsComposition)
        {
            composition.Dispose();
        }
    }

    private async Task NavigateWithoutHistoryAsync(PrototypeRoute route)
    {
        viewModel.BeginNavigation(route, RouteTitle(route));
        LoadingStatusControl.IsVisible = true;
        var result = await navigation.NavigateAsync(route, pageFactory.CreateAsync, recordHistory: false);
        if (result.Outcome == NavigationOutcome.Committed && result.Page is not null && !suspended)
        {
            transitionLifetime.Cancel();
            transitionLifetime.Dispose();
            var ownedTransition = new CancellationTokenSource();
            transitionLifetime = ownedTransition;
            await TransitionPresenterControl.PresentAsync(
                result.Page,
                phase => FrameDiagnostics.RecordTransition(this, route, phase),
                ownedTransition.Token);
            if (suspended || ownedTransition.IsCancellationRequested)
            {
                return;
            }

            LoadingStatusControl.IsVisible = false;
            viewModel.CompleteNavigation(route);
            FrameDiagnostics.Record(this, route, result.LoadDuration);
            selectedRoute = route;
            SelectedTrayButton.Focus(NavigationMethod.Directional);
            RouteChanged?.Invoke(this, route);
        }
    }

    private async Task NavigateAndEnterContentAsync(PrototypeRoute route)
    {
        var result = await NavigateAsync(route);
        if (result.Outcome == NavigationOutcome.Committed && result.Page is not null && !suspended)
        {
            FocusRememberedOrInitial(result.Page);
        }
    }

    private bool FocusRememberedOrInitial(Control page)
    {
        Control? target = null;
        if (lastPageFocus.TryGetValue(selectedRoute, out var rememberedId))
        {
            if (page is IPrototypeSemanticPage semanticPage && semanticPage.TryRestoreSemanticFocus(rememberedId))
            {
                return true;
            }

            target = page.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control =>
                    string.Equals(AutomationProperties.GetAutomationId(control), rememberedId, StringComparison.Ordinal) &&
                    IsValidFocus(control));
        }

        target ??= (page as IPrototypeFocusPage)?.InitialFocus;
        return target is not null && IsValidFocus(target) && target.Focus(NavigationMethod.Directional);
    }

    private void OnDescendantGotFocus(object? sender, FocusChangedEventArgs e)
    {
        RememberPageFocus(e.Source as Control);
    }

    private void RememberPageFocus(Control? focused)
    {
        if (focused is null || ActivePage is not { } page || !IsWithin(focused, page) || !IsValidFocus(focused))
        {
            return;
        }

        var id = AutomationProperties.GetAutomationId(focused);
        if (!string.IsNullOrWhiteSpace(id) && Navigation.CurrentRoute is { } route)
        {
            lastPageFocus[route] = id;
        }
    }

    private bool IsTrayFocus(Control? focused) => focused is not null && IsWithin(focused, TrayRegionControl);

    private bool IsPageFocus(Control? focused) => focused is not null && ActivePage is { } page && IsWithin(focused, page);

    private static bool IsWithin(Control child, Control root) =>
        ReferenceEquals(child, root) || child.GetVisualAncestors().Contains(root);

    private static bool IsValidFocus(Control control) =>
        control.Focusable && control.IsVisible && control.IsEffectivelyEnabled;

    private static string RouteTitle(PrototypeRoute route) => route switch
    {
        PrototypeRoute.Settings => "Settings",
        PrototypeRoute.AudioMixer => "Audio Mixer",
        PrototypeRoute.SpotifyPlayer => "Spotify Player",
        PrototypeRoute.GameLauncher => "Game Launcher",
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private void RouteRequested(object? sender, PrototypeRoute route) => _ = NavigateAsync(route);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
