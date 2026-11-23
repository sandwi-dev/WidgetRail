using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Notifications;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>A self-contained theme sample. It has no shell actions, inbox or settings writer.</summary>
internal sealed partial class CatalogueThemeScene : Grid, IDisposable
{
    private static readonly BridgeWidgetDescriptor[] Widgets =
    [
        Widget("capture-library", "Capture Library", WidgetGlyph.Folder),
        Widget("now-playing", "Now Playing", WidgetGlyph.Music),
        Widget("audio", "Audio Mixer", WidgetGlyph.Volume),
        Widget("network", "Network Controls", WidgetGlyph.Wifi),
        Widget("settings", "Settings", WidgetGlyph.Settings),
        Widget("games", "Games & Apps", WidgetGlyph.Play),
        Widget("files", "Files", WidgetGlyph.Attach),
        Widget("catalogue", "WidgetRail Catalogue", WidgetGlyph.Favorite),
    ];
    private readonly NativePopupTheme popupTheme = new();
    private readonly RadialChooserView wheel = new(NoPackageIcon, appliesApplicationPolicy: false);
    private readonly NotificationCard card = new(NoPackageIcon);
    private readonly NotificationBannerChrome banner;
    private readonly NotificationEntry sample;
    private readonly StackPanel posters = new() { Orientation = Orientation.Horizontal, Spacing = 16,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly List<(Button Button, BitmapImage Image)> posterImages = [];
    private int selectedPoster;
    private string selected = Widgets[0].Id;
    private IReadOnlyDictionary<string, BridgeNodeRenderStyles>? roles;
    private AppearanceSettings appearance = AppearanceSettings.Default;
    private bool animations;

    internal CatalogueThemeScene()
    {
        popupTheme.Attach(this);
        wheel.HorizontalAlignment = HorizontalAlignment.Center;
        wheel.VerticalAlignment = VerticalAlignment.Center;
        wheel.Selected = item =>
        {
            if (selected == item.Id) return;
            selected = item.Id;
            RefreshWheel();
        };
        wheel.Activated = _ => ShowPosters(true);
        wheel.FavoriteInvoked = _ => ShowNotification(true);
        wheel.DirectionRequested = direction =>
        {
            var delta = direction is FocusNavigationDirection.Up or FocusNavigationDirection.Left ? -1 : 1;
            var index = RadialChooserPolicy.StepSelection(Widgets.Length, 0, Array.FindIndex(Widgets, item => item.Id == selected), delta);
            wheel.ButtonFor(Widgets[index].Id)?.Focus(FocusState.Keyboard);
        };
        banner = new(card) { MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new(12), Visibility = Visibility.Collapsed };
        sample = new(1, "Capture Library", "preview", "", "", "Screenshot saved", "View in Capture Library", this,
            new(() => true, () => ShowNotification(false), () => ShowNotification(false), () => { }, () => { }));
        Children.Add(wheel);
        Children.Add(posters);
        Children.Add(banner);
        SizeChanged += (_, _) => wheel.FitStage(true, false, ActualWidth, ActualHeight, false);
    }

    internal void Apply(IReadOnlyDictionary<string, BridgeNodeRenderStyles> styles, AppearanceSettings preferences, bool systemAnimations)
    {
        roles = styles;
        appearance = preferences;
        animations = systemAnimations;
        var palette = ShellChromePalette.Resolve(styles, preferences);
        Background = new SolidColorBrush(palette.HighContrast ? palette.Surface : Windows.UI.Color.FromArgb(255, 33, 36, 42));
        popupTheme.Update(styles, preferences);
        card.ApplyBannerPalette(palette);
        card.Update(sample, null, preferences.TextScale);
        banner.Apply(palette, 100);
        foreach (var (button, _) in posterImages) PaintPoster(button, palette);
        RefreshWheel();
        wheel.SetFavorite(TrayDirection.Left, "Take screenshot", true);
        wheel.SetFavorite(TrayDirection.Right, "Save replay", true);
    }

    private void RefreshWheel() => wheel.Update(Widgets, selected, 0, roles, appearance, animations);
    internal void ShowPosters(bool show)
    {
        if (show && posterImages.Count == 0)
        {
            foreach (var (title, asset) in new[] { ("Observatory", "observatory"), ("Crystal Valley", "crystal-valley"), ("Ember Peak", "ember-peak") })
            {
                var bitmap = new BitmapImage(new Uri($"ms-appx:///Assets/ThemePreview/{asset}.jpg"));
                var face = new StackPanel { Spacing = 6 };
                face.Children.Add(new Image { Source = bitmap, Width = 168, Height = 218, Stretch = Stretch.UniformToFill });
                face.Children.Add(new TextBlock { Text = title, Margin = new(8, 1, 8, 8), TextTrimming = TextTrimming.CharacterEllipsis });
                var button = new Button { Content = face, Padding = new(2), CornerRadius = new(5) };
                AutomationProperties.SetName(button, title);
                AutomationProperties.SetAutomationId(button, "ThemePreview.Poster." + asset);
                var index = posterImages.Count;
                button.GotFocus += (_, _) => selectedPoster = index;
                button.Click += (_, _) => ShowNotification(true);
                posterImages.Add((button, bitmap));
                posters.Children.Add(button);
                PaintPoster(button, ShellChromePalette.Resolve(roles, appearance));
            }
        }
        wheel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        posters.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ShowNotification(false);
        if (show) posterImages[selectedPoster].Button.Focus(FocusState.Keyboard);
        else wheel.FocusSelected();
    }

    private void PaintPoster(Button button, ShellChromePalette palette)
    {
        palette.PaintButton(button);
        button.FontSize = 18 * appearance.TextScale;
        button.Background = new SolidColorBrush(palette.Surface);
    }

    internal bool PostersReady => posterImages.Count > 0 && posterImages.All(p => p.Image.PixelWidth > 0 && p.Button.ActualWidth > 0);
    internal void ShowNotification(bool show) => banner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    internal Button? SelectedButton => wheel.ButtonFor(selected);
    internal Button? ButtonFor(string id) => wheel.ButtonFor(id);
    public void Dispose() { banner.Dispose(); wheel.Dispose(); }

    private static BridgeWidgetDescriptor Widget(string id, string name, WidgetGlyph icon) => new()
    {
        Id = id, Name = name, Icon = icon, InstanceId = id, RuntimeGeneration = "preview",
        PresentationGeneration = "preview", PackageContentDigest = "",
    };
    private static Task<WidgetPresentationPackageIcon> NoPackageIcon(BridgeWidgetDescriptor descriptor, string id, CancellationToken token) =>
        Task.FromException<WidgetPresentationPackageIcon>(new InvalidOperationException("The preview uses bundled glyphs."));
}
