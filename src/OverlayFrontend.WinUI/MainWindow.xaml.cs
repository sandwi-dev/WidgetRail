using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly Input.PlatformInputPump? input;

    public MainWindow(bool validateExternalSurface = false, bool validateController = false)
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
        if (validateController)
        {
            var page = new Validation.ControllerValidationPage();
            RootFrame.Content = page;
            try
            {
                input = new(DispatcherQueue, WinRT.Interop.WindowNative.GetWindowHandle(this));
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
        else RootFrame.Navigate(validateExternalSurface ? typeof(Validation.ExternalSurfacePage) : typeof(MainPage));
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
                (RootFrame.Content as Validation.ControllerValidationPage)?.QueueEntryFocus();
        };
        Closed += (_, _) =>
        {
            input?.Dispose();
            (RootFrame.Content as Validation.ExternalSurfacePage)?.Retire();
        };
    }

    public void StartInput()
    {
        input?.SetVisible(true);
        (RootFrame.Content as Validation.ControllerValidationPage)?.QueueEntryFocus();
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
