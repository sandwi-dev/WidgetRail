using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.System.ThemeSettings themeSettings;
    private Input.PlatformInputPump? input;
    private readonly Input.ControllerInputTrace? controllerTrace;
    private readonly Input.GamepadKeyBoundary? gamepadKeys;
    private bool closingAfterCleanup;
    private bool cleanupStarted;
    private bool startHidden;

    internal MainWindow(IReadOnlyList<string> arguments, Shell.OverlayShellOptions? launchOptions, Exception? configurationError)
    {
        InitializeComponent();
        themeSettings = Microsoft.UI.System.ThemeSettings.CreateForWindowId(AppWindow.Id);
        Presentation.WidgetViewPresenter.SetSystemHighContrast(themeSettings.HighContrast);
        themeSettings.Changed += SystemThemeChanged;
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new WinUIEx.TransparentTintBackdrop();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        // Initial validation window uses physical pixels. Production placement
        // will come from the existing platform adapter's monitor/DPI policy.
        AppWindow.ResizeClient(new SizeInt32(960, 640));
        var configured = false;
        ConfigureValidation(arguments, ref configured);
        if (!configured) ConfigureProduction(arguments, launchOptions, configurationError);
        if (input is not null && Environment.GetCommandLineArgs().Contains("--trace-controller-input"))
            controllerTrace = new(ShellRoot, input.TraceInput);
        if (input is not null)
        {
            gamepadKeys = new(ShellRoot, () => !cleanupStarted && input is { IsActive: true, IsForeground: true } && AppWindow.IsVisible);
            input.Failed += _ => gamepadKeys.Dispose();
        }
        AppWindow.Closing += (sender, args) =>
        {
            if (closingAfterCleanup || RootFrame.Content is not IAsyncDisposable resource) return;
            args.Cancel = true;
            _ = CloseWithCleanupAsync();
        };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                QueueValidationEntryFocus();
                (RootFrame.Content as Shell.OverlayShellPage)?.SetForeground(true);
                (RootFrame.Content as Shell.OverlayShellPage)?.QueueEntryFocus();
            }
            else
            {
                ResetValidationInput();
                (RootFrame.Content as Shell.OverlayShellPage)?.SetForeground(input?.IsForeground == true);
                (RootFrame.Content as Shell.OverlayShellPage)?.ResetInputPresentation();
            }
        };
        Closed += (_, _) =>
        {
            themeSettings.Changed -= SystemThemeChanged;
            RetireOverlaySizing();
            controllerTrace?.Dispose();
            gamepadKeys?.Dispose();
            input?.Dispose();
            RetireValidation();
        };
    }

    private void ConfigureProduction(IReadOnlyList<string> arguments, Shell.OverlayShellOptions? launchOptions, Exception? configurationError)
    {
        Shell.OverlayShellOptions options;
        try
        {
            if (configurationError is not null) throw configurationError;
            options = launchOptions ?? throw new InvalidDataException("The frontend launch configuration is missing.");
            if (!Shell.OverlayLaunchConfiguration.HasInstallationFiles(options))
                throw new InvalidDataException("The frontend installation is incomplete.");
        }
        catch (Exception error) when (Shell.OverlayLaunchConfiguration.IsConfigurationError(error))
        {
            var recovery = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = "WidgetRail could not open its installation. Repair or reinstall WidgetRail, then try again.",
                TextWrapping = TextWrapping.Wrap,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(recovery, "Overlay.InstallationError");
            RootFrame.Content = recovery;
            System.Diagnostics.Trace.TraceError("WinUI launch configuration: {0}", error);
            return;
        }
        var shellNoController = arguments.Contains("--shell-no-controller");
        startHidden = arguments.Contains("--hidden");
        var page = new Shell.OverlayShellPage(options,
            unchecked((ulong)WinRT.Interop.WindowNative.GetWindowHandle(this)), initiallyVisible: !startHidden);
        RootFrame.Content = page;
        ConfigureProductionValidation(page, arguments);
        // Production has no validation title/card/Close row. Keep one native
        // scale root, while fixtures retain their independent test wrapper.
        ShellLayout.Children.Remove(RootFrame);
        ScaleRoot.Children.Clear();
        ScaleRoot.Children.Add(RootFrame);
        Microsoft.UI.Xaml.Controls.Grid.SetRow(RootFrame, 0);
        InitializeShellAppearance(page);
        page.HideRequested += HideOverlay;
        page.ReturnFromPinnedRequested += () => { Activate(); input?.AcquireForeground(); page.QueueEntryFocus(); };
        var taskActivation = new WidgetRail.OverlayPlatformClient.TaskWindowActivation(
            new WidgetRail.OverlayPlatformClient.WindowsTaskWindowActivation());
        page.TaskWindowActivationRequested += effect => page.ActivateTaskWindow(effect, taskActivation, HideOverlay);
        page.AppearanceLoaded += ApplyOverlayPlacement;
        page.MediaPresentationChanged += () =>
        {
            QueueOverlayPlacement();
        };
        if (!shellNoController)
        {
            try
            {
                input = new(DispatcherQueue, WinRT.Interop.WindowNative.GetWindowHandle(this));
                input.FrameReceived += page.Receive;
                input.Failed += error => ReportInputFailure(page, error);
                input.ToggleRequested += ToggleOverlay;
                if (!startHidden) input.PrepareShow();
            }
            catch (Exception error) { ReportInputFailure(page, error); }
        }
        InitializeOverlaySizing(page);
    }

    partial void ConfigureValidation(IReadOnlyList<string> arguments, ref bool handled);
    partial void ConfigureProductionValidation(Shell.OverlayShellPage page, IReadOnlyList<string> arguments);
    partial void QueueValidationEntryFocus();
    partial void ResetValidationInput();
    partial void StartValidationReplay();
    partial void RetireValidation();

    private void ReportInputFailure(Shell.OverlayShellPage page, Exception error)
    {
        input?.Dispose();
        input = null;
        page.ReportFailure(error);
        // A resident with no Guide listener must not remain unreachable.
        startHidden = false;
        DispatcherQueue.TryEnqueue(() => { if (!cleanupStarted && !AppWindow.IsVisible) ShowOverlay(); });
    }

    internal void StartPresentation()
    {
        // Do not activate and then hide: that flashes a frame and steals focus
        // during sign-in. The platform adapter still listens for Guide while
        // XAML/Bridge initialization waits for the first real show.
        if (startHidden && RootFrame.Content is Shell.OverlayShellPage)
        {
            input?.SetVisible(false);
            return;
        }
        Activate();
        StartInput();
    }

    public void StartInput()
    {
        input?.SetVisible(true);
        QueueValidationEntryFocus();
        (RootFrame.Content as Shell.OverlayShellPage)?.QueueEntryFocus();
    }

    public void StartReplay() => StartValidationReplay();

    private void SystemThemeChanged(Microsoft.UI.System.ThemeSettings sender, object args)
    {
        Presentation.WidgetViewPresenter.SetSystemHighContrast(sender.HighContrast);
        DispatcherQueue.TryEnqueue(RefreshShellAppearance);
    }

    private async void CloseClicked(object sender, RoutedEventArgs e) => await CloseWithCleanupAsync();

    private async Task CloseWithCleanupAsync()
    {
        if (cleanupStarted) return;
        cleanupStarted = true;
        desktopBackdrop?.Dispose();
        try { if (RootFrame.Content is IAsyncDisposable resource) await resource.DisposeAsync(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("WinUI page shutdown failed: {0}", error); }
        finally
        {
            await Presentation.NativePackageIconTintCache.ShutdownAsync(DispatcherQueue);
            closingAfterCleanup = true;
            Close();
        }
    }
}
