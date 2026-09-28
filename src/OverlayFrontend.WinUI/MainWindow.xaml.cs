using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.System.ThemeSettings themeSettings;
    private readonly Input.PlatformInputPump? input;
    private readonly Validation.ControllerReplayScenario? replay;
    private bool closingAfterCleanup;
    private bool cleanupStarted;

    public MainWindow(bool validateExternalSurface = false, bool validateController = false, bool replayController = false,
        bool validateCollection = false, string? widgetConfiguration = null, bool validateGridView = false, bool validateControls = false, bool validateIndexed = false, bool validateFocusPolicy = false,
        string? indexedValidationPipe = null, bool validateGrouped = false, bool validateGroupedFlat = false, bool validateGroupedAdapted = false, bool validateSurfaces = false, bool validateSelect = false, bool validateMotion = false, bool validateModals = false, bool validateGlyphs = false, bool validateStyles = false, bool validateTextEntry = false, bool validateContextMenu = false,
        string? shellConfiguration = null, bool shellNoController = false, bool validateSlider = false, bool validatePackageIcons = false, bool validateShellSizing = false, bool validateEmbeddedMedia = false, bool validateWindowPreview = false)
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
        if (validateEmbeddedMedia)
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
            AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(880 * scale), (int)Math.Ceiling(680 * scale)));
        }
        if (validateController || replayController)
        {
            var page = new Validation.ControllerValidationPage();
            RootFrame.Content = page;
            try
            {
                var backend = replayController ? new Validation.ReplayNativePlatform() : null;
                input = new(DispatcherQueue, WinRT.Interop.WindowNative.GetWindowHandle(this), backend);
                if (backend is not null) replay = new(this, page, backend);
                input.FrameReceived += page.Receive;
                input.Failed += page.ReportFailure;
                input.ToggleRequested += () =>
                {
                    if (AppWindow.IsVisible && input.IsForeground) { page.ResetInputPresentation(); input.SetVisible(false); AppWindow.Hide(); }
                    else
                    {
                        input.PrepareShow();
                        AppWindow.Show();
                        Activate();
                        input.AcquireForeground();
                        StartInput();
                    }
                };
                input.PrepareShow();
            }
            catch (Exception error) { input?.Dispose(); page.ReportFailure(error); }
        }
        else if (validateWindowPreview) RootFrame.Content = new Validation.WindowPreviewValidationPage();
        else if (validateEmbeddedMedia) RootFrame.Content = new Validation.EmbeddedMediaValidationPage();
        else if (validatePackageIcons) RootFrame.Content = new Validation.PackageIconValidationPage();
        else if (validateShellSizing) RootFrame.Content = new Validation.ShellSizingValidationPage();
        else if (Environment.GetCommandLineArgs().Contains("--validate-shell-appearance")) RootFrame.Content = new Validation.ShellAppearanceValidationPage(this);
        else if (Environment.GetCommandLineArgs().Contains("--validate-pinned-window")) RootFrame.Content = new Validation.PinnedWindowValidationPage(this);
        else if (validateSlider) RootFrame.Content = new Validation.SliderControlValidationPage();
        else if (validateContextMenu) RootFrame.Content = new Validation.ContextMenuControlValidationPage();
        else if (shellConfiguration is not null)
        {
            var page = new Shell.OverlayShellPage(Shell.OverlayShellOptions.Load(shellConfiguration),
                unchecked((ulong)WinRT.Interop.WindowNative.GetWindowHandle(this)));
            RootFrame.Content = page;
            ShellCard.Style = (Style)ShellRoot.Resources["ProductionShellCardStyle"];
            ShellCard.Margin = new Thickness(16);
            ShellCard.Padding = new Thickness(16);
            ShellLayout.RowSpacing = 12;
            InitializeShellAppearance(page);
            page.HideRequested += HideOverlay;
            page.ReturnFromPinnedRequested += () => { Activate(); input?.AcquireForeground(); page.QueueEntryFocus(); };
            var taskActivation = new WidgetRail.OverlayPlatformClient.TaskWindowActivation(
                new WidgetRail.OverlayPlatformClient.WindowsTaskWindowActivation());
            page.TaskWindowActivationRequested += effect => page.ActivateTaskWindow(effect, taskActivation, HideOverlay);
            page.AppearanceLoaded += ApplyOverlayPlacement;
            page.MediaPresentationChanged += () =>
            {
                ShellHeader.Visibility = page.IsMediaFullscreen ? Visibility.Collapsed : Visibility.Visible;
                ShellLayout.RowSpacing = page.IsMediaFullscreen ? 0 : 12;
                ShellCard.Padding = new Thickness(page.IsMediaFullscreen ? 0 : 16);
                QueueOverlayPlacement();
            };
            if (!shellNoController)
            {
                try
                {
                    input = new(DispatcherQueue, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    input.FrameReceived += page.Receive;
                    input.Failed += page.ReportFailure;
                    input.ToggleRequested += ToggleOverlay;
                    input.PrepareShow();
                }
                catch (Exception error) { input?.Dispose(); page.ReportFailure(error); }
            }
            InitializeOverlaySizing(page);
        }
        else if (validateTextEntry) RootFrame.Content = new Validation.TextEntryValidationPage();
        else if (validateStyles) RootFrame.Content = new Validation.WidgetStylesValidationPage();
        else if (validateGlyphs) RootFrame.Content = new Validation.GlyphValidationPage();
        else if (validateModals) RootFrame.Content = new Validation.ModalValidationPage();
        else if (validateMotion) RootFrame.Content = new Validation.WidgetMotionValidationPage();
        else if (validateSelect) RootFrame.Content = new Validation.SelectControlValidationPage();
        else if (validateSurfaces) RootFrame.Content = new Validation.PresentationSurfaceValidationPage();
        else if (validateGrouped || validateGroupedFlat || validateGroupedAdapted)
            RootFrame.Content = new Validation.GroupedCollectionValidationPage(flatBaseline: validateGroupedFlat, useRangeAdapter: validateGroupedAdapted);
        else if (indexedValidationPipe is not null)
        {
            if (indexedValidationPipe.StartsWith("pinned-validation-", StringComparison.Ordinal))
                RootFrame.Content = new Validation.PinnedWidgetValidationPage(indexedValidationPipe);
            else if (indexedValidationPipe.StartsWith("discovered-validation-", StringComparison.Ordinal))
            {
                var discovery = new Validation.DiscoveredCollectionValidationPage(indexedValidationPipe);
                discovery.Initialize(); RootFrame.Content = discovery;
            }
            else RootFrame.Content = new Validation.IndexedWidgetValidationPage(indexedValidationPipe);
        }
        else if (widgetConfiguration is not null)
            RootFrame.Content = new Validation.BridgeWidgetValidationPage(widgetConfiguration);
        else if (validateControls)
            RootFrame.Content = new Validation.WidgetControlsValidationPage();
        else if (validateFocusPolicy)
            RootFrame.Content = new Validation.FocusPolicyValidationPage();
        else if (validateIndexed)
            RootFrame.Content = new Validation.IndexedCollectionValidationPage();
        else RootFrame.Navigate(validateExternalSurface ? typeof(Validation.ExternalSurfacePage) :
            validateCollection ? typeof(Validation.CollectionValidationPage) :
            validateGridView ? typeof(Validation.GridViewValidationPage) : typeof(MainPage));
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
                (RootFrame.Content as Validation.ControllerValidationPage)?.QueueEntryFocus();
                (RootFrame.Content as Shell.OverlayShellPage)?.SetForeground(true);
                (RootFrame.Content as Shell.OverlayShellPage)?.QueueEntryFocus();
            }
            else
            {
                (RootFrame.Content as Validation.ControllerValidationPage)?.ResetInputPresentation();
                (RootFrame.Content as Shell.OverlayShellPage)?.SetForeground(input?.IsForeground == true);
                (RootFrame.Content as Shell.OverlayShellPage)?.ResetInputPresentation();
            }
        };
        Closed += (_, _) =>
        {
            themeSettings.Changed -= SystemThemeChanged;
            RetireOverlaySizing();
            input?.Dispose();
            replay?.Dispose();
            (RootFrame.Content as Validation.ExternalSurfacePage)?.Retire();
        };
    }

    public void StartInput()
    {
        input?.SetVisible(true);
        (RootFrame.Content as Validation.ControllerValidationPage)?.QueueEntryFocus();
        (RootFrame.Content as Shell.OverlayShellPage)?.QueueEntryFocus();
    }

    public void StartReplay() => replay?.Start();

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
        finally { closingAfterCleanup = true; Close(); }
    }
}
