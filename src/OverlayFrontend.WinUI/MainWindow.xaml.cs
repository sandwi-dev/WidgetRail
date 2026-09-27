using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new TransparentWindowBackdrop();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
        }
        // Initial validation window uses physical pixels. Production placement
        // will come from the existing platform adapter's monitor/DPI policy.
        AppWindow.ResizeClient(new SizeInt32(960, 640));
        RootFrame.Navigate(typeof(MainPage));
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
