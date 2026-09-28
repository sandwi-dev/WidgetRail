using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly Input.PlatformInputPump? input;
    private readonly Validation.ControllerReplayScenario? replay;
    private bool closingAfterCleanup;
    private bool cleanupStarted;

    public MainWindow(bool validateExternalSurface = false, bool validateController = false, bool replayController = false,
        bool validateCollection = false, string? widgetConfiguration = null, bool validateGridView = false, bool validateControls = false, bool validateIndexed = false)
    {
        InitializeComponent();
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
                    if (AppWindow.IsVisible) { input.SetVisible(false); AppWindow.Hide(); }
                    else { input.PrepareShow(); AppWindow.Show(); Activate(); StartInput(); }
                };
                input.PrepareShow();
            }
            catch (Exception error) { input?.Dispose(); page.ReportFailure(error); }
        }
        else if (widgetConfiguration is not null)
            RootFrame.Content = new Validation.BridgeWidgetValidationPage(widgetConfiguration);
        else if (validateControls)
            RootFrame.Content = new Validation.WidgetControlsValidationPage();
        else if (validateIndexed)
            RootFrame.Content = new Validation.IndexedCollectionValidationPage();
        else RootFrame.Navigate(validateExternalSurface ? typeof(Validation.ExternalSurfacePage) :
            validateCollection ? typeof(Validation.CollectionValidationPage) :
            validateGridView ? typeof(Validation.GridViewValidationPage) : typeof(MainPage));
        AppWindow.Closing += async (_, args) =>
        {
            if (closingAfterCleanup || RootFrame.Content is not IAsyncDisposable resource) return;
            args.Cancel = true;
            if (cleanupStarted) return;
            cleanupStarted = true;
            try { await resource.DisposeAsync(); }
            catch (Exception error) { System.Diagnostics.Trace.TraceError("WinUI page shutdown failed: {0}", error); }
            finally { closingAfterCleanup = true; Close(); }
        };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
                (RootFrame.Content as Validation.ControllerValidationPage)?.QueueEntryFocus();
        };
        Closed += (_, _) =>
        {
            input?.Dispose();
            replay?.Dispose();
            (RootFrame.Content as Validation.ExternalSurfacePage)?.Retire();
        };
    }

    public void StartInput()
    {
        input?.SetVisible(true);
        (RootFrame.Content as Validation.ControllerValidationPage)?.QueueEntryFocus();
    }

    public void StartReplay() => replay?.Start();

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
