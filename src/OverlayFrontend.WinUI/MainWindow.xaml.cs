using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.System.ThemeSettings themeSettings;
    private Input.PlatformInputPump? input;
    private Input.OverlayKeyboardShortcut? keyboardShortcut;
    private readonly Input.ControllerInputTrace? controllerTrace;
    private readonly Input.GamepadKeyBoundary? gamepadKeys;
    private bool closingAfterCleanup;
    private bool cleanupStarted;
    private bool startHidden;

    internal string? CaptureFailureLayout() => (RootFrame.Content as Shell.OverlayShellPage)?.CaptureFailureLayout();

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
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            // IsResizable=false installs WS_DLGFRAME; remove chrome last.
            presenter.SetBorderAndTitleBar(false, false);
        }
        Shell.OverlayWindowFrame.SuppressBorder(WinRT.Interop.WindowNative.GetWindowHandle(this));
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
            if (RootFrame.Content is Shell.OverlayShellPage)
                input.ExternalForegroundObserved += DismissForExternalForeground;
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
                Shell.OverlayWindowFrame.SuppressBorder(WinRT.Interop.WindowNative.GetWindowHandle(this));
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
            keyboardShortcut?.Dispose(); keyboardShortcut = null;
            input?.Dispose();
            RetireValidation();
        };
    }

    private void ConfigureProduction(IReadOnlyList<string> arguments, Shell.OverlayShellOptions? launchOptions, Exception? configurationError)
    {
        // The overlay is summoned by its controller/keyboard shortcut, like
        // the original tool window. Use AppWindow policy rather than native
        // extended-style mutations that can conflict with WinUI ownership.
        AppWindow.IsShownInSwitchers = false;
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
        // Confirmed launch effects must hand foreground off without waiting for
        // decorative exit motion. Guide, keyboard and gap dismissal animate.
        page.HideRequested += HideOverlay;
        page.ImmediateHideRequested += HideOverlayImmediately;
        page.MotionPolicyChanged += RefreshOverlayMotionPolicy;
        page.WindowActivationDiagnostic = message =>
        {
            if (input is not null) input.TraceForegroundState(message);
            else Diagnostics.FrontendFailureLog.Current.Write("window-activation", null, message);
        };
        page.ApplicationControlRequested += action =>
        {
            if (cleanupStarted) return;
            App.RestartRequested = action == WidgetRail.WidgetPresentationSession.WidgetApplicationControl.Restart;
            _ = CloseWithCleanupAsync();
        };
        page.ReturnFromPinnedRequested += () => { Activate(); input?.AcquireForeground(); page.QueueEntryFocus(); };
        var taskActivation = new WidgetRail.WindowsWindowActivation.TaskWindowActivation(
            new WidgetRail.WindowsWindowActivation.WindowsTaskWindowActivation());
        page.TaskWindowActivationRequested += effect => _ = RunTaskHandoffAsync(page, effect, taskActivation);
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
                page.ControllerSettingsLoaded += input.ApplyControllerSettings;
                page.ControllerControlStatusRequested = input.ReadControllerControlAsync;
                page.ControllerControlPreferenceReceived += input.ApplyControllerControlPreference;
                input.FrameReceived += page.Receive;
                input.Failed += error => ReportInputFailure(page, error);
                input.ToggleRequested += ToggleOverlay;
                input.InitializeControllerSettings(async () =>
                    (await new WidgetRail.PlatformSettings.PlatformSettingsStore(new(options.SettingsRoot)).LoadAsync()).Controllers);
                if (!startHidden) input.PrepareShow();
            }
            catch (Exception error) { ReportInputFailure(page, error); }
        }
        InitializeOverlaySizing(page);
        InitializeKeyboardShortcut();
    }

    private void InitializeKeyboardShortcut()
    {
        keyboardShortcut = new(WinRT.Interop.WindowNative.GetWindowHandle(this), () =>
        {
            if (cleanupStarted) return;
            input?.TraceInput("F1 fallback toggle received");
            ToggleOverlay();
        });
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
        if (RootFrame.Content is Shell.OverlayShellPage) { ShowOverlay(); return; }
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
        CancelTaskHandoff();
        keyboardShortcut?.Dispose(); keyboardShortcut = null;
        input?.SetVisible(false);
        AppWindow.Hide();
        RetireOverlayMotion();
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
