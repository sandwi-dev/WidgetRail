using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task RenderCatalogueThemeAsync(string directory)
    {
        Exception? failure = null;
        try
        {
            using var job = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory, "job.json")));
            var identity = job.RootElement;
            var catalog = new ThemeCatalog(new PlatformSettingsPaths(Path.Combine(directory, "profile")));
            var styles = await MarketplaceThemePreview.ReadAsync(await File.ReadAllBytesAsync(Path.Combine(directory, "package.wrtheme")),
                identity.GetProperty("id").GetString()!, identity.GetProperty("version").GetString()!,
                identity.GetProperty("sha256").GetString()!, catalog, default);
            App.Window.AppWindow.ResizeClient(new(1440, 900));
            using var scene = new CatalogueThemeScene { Width = 960, Height = 540,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Content = scene;
            scene.Apply(styles, AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
            await Wait(() => scene.SelectedButton is { IsLoaded: true, ActualWidth: > 0 });
            if (!scene.SelectedButton!.Focus(FocusState.Keyboard)) throw new InvalidOperationException("Preview focus was unavailable.");
            await Frame();
            await Save("tray.png", 1920);
            scene.ShowPosters(true);
            await Wait(() => scene.PostersReady);
            await Frame();
            await Save("posters.png", 1920);
            scene.ShowNotification(true);
            await Frame();
            await Save("notification.png", 1920);

            // These two paths are the v1 feed's thumbnail slots. Keep them readable
            // by older clients while publishing each composition under a new preview key.
            var mixer = MixerPreview(styles);
            Content = mixer;
            await Wait(() => mixer.IsLoaded && mixer.ActualWidth > 0);
            await Frame();
            foreach (int width in new[] { 480, 960 })
                await CaptureEditorBitmapAsync(mixer, Path.Combine(directory, $"tray-{width}.png"), width, width * 9 / 16);

            async Task Save(string file, int width)
            {
                await CaptureEditorBitmapAsync(scene, Path.Combine(directory, file), width, width * 9 / 16);
            }
            async Task Frame()
            {
                scene.UpdateLayout();
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Painted(object? sender, object args) => done.TrySetResult();
                CompositionTarget.Rendering += Painted;
                try { await done.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
                finally { CompositionTarget.Rendering -= Painted; }
            }
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"),
            JsonSerializer.Serialize(new { passed = failure is null, error = failure?.ToString() }));
        if (failure is not null) throw new InvalidOperationException("Theme preview rendering failed.", failure);
    }

    private static Grid MixerPreview(IReadOnlyDictionary<string, BridgeNodeRenderStyles> styles)
    {
        var palette = ShellChromePalette.Resolve(styles, AppearanceSettings.Default);
        SolidColorBrush Brush(Windows.UI.Color color) => new(color);
        TextBlock Label(string text, double size, Windows.UI.Color color) => new()
        {
            Text = text, FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center,
        };

        var root = new Grid { Width = 960, Height = 540, Background = Brush(palette.Surface) };
        var panel = new StackPanel { Margin = new(48, 0, 48, 0), Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        root.Children.Add(panel);
        panel.Children.Add(Label("Audio Mixer", 48, palette.Text));
        panel.Children.Add(new TextBlock { Text = "Application volume", Foreground = Brush(palette.Muted), FontSize = 23,
            Margin = new(0, 0, 0, 8) });
        foreach (var (name, level, role, selected) in new[]
            { ("Game", 84, "data-blue", true), ("Voice chat", 68, "data-green", false), ("Music", 32, "data-gold", false) })
        {
            var fill = styles.TryGetValue(role, out var style) && style.Base.GetValueOrDefault("color") is { } value &&
                NativeComputedStyleAdapter.TryColor(value.Text, out var color) ? color : palette.Focus;
            var ink = selected ? palette.SelectedText : palette.Text;
            var row = new Grid { ColumnSpacing = 18 };
            row.ColumnDefinitions.Add(new() { Width = new(170) });
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new(75) });
            row.Children.Add(Label(name, 27, ink));
            var meter = new Grid { Height = 14, CornerRadius = new(7), Background = Brush(ShellChromePalette.Blend(palette.Surface, ink, .18)) };
            meter.ColumnDefinitions.Add(new() { Width = new(level, GridUnitType.Star) });
            meter.ColumnDefinitions.Add(new() { Width = new(100 - level, GridUnitType.Star) });
            meter.Children.Add(new Border { Background = Brush(fill), CornerRadius = new(7) });
            Grid.SetColumn(meter, 1);
            row.Children.Add(meter);
            var amount = Label(level + "%", 27, ink);
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(amount, 2);
            row.Children.Add(amount);
            panel.Children.Add(new Border { Child = row, Padding = new(22, 12, 22, 12), BorderThickness = new(3),
                CornerRadius = new(12), Background = Brush(selected ? palette.Selected : ShellChromePalette.Blend(palette.Surface, palette.Text, .045)),
                BorderBrush = Brush(selected ? palette.Focus : ShellChromePalette.Blend(palette.Surface, palette.Text, .13)) });
        }
        return root;
    }

    private async Task CatalogueThemePreviewAsync(string directory)
    {
        Exception? failure = null;
        var menu = new MenuFlyout();
        try
        {
            var profile = new PlatformSettingsPaths(Path.Combine(directory, "profile"));
            var catalog = new ThemeCatalog(profile);
            const string id = "local.widgetrail.afterglow-fm";
            Check(!catalog.Discover().Themes.Any(entry => entry.CatalogId == id), "Candidate is not installed before preview");
            IReadOnlyDictionary<string, BridgeNodeRenderStyles> Roles(ThemeLoadResult theme)
            {
                var compiled = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new([], []), theme.Package);
                if (!compiled.IsValid) throw new InvalidDataException("Invalid theme in preview fixture.");
                return PlatformAppearanceService.ResolveShellStyles(compiled.Theme!).ToDictionary(p => p.Key,
                    p => new BridgeNodeRenderStyles { Base = p.Value, Focused = p.Value, Pressed = p.Value });
            }
            var outerStyles = Roles(catalog.Load(ThemeIdentity.BuiltInMonoInk, "1.0.0"));
            var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "afterglow.wrtheme"));
            var digest = (await File.ReadAllTextAsync(Path.Combine(directory, "afterglow.sha256"))).Trim();
            var previewStyles = await MarketplaceThemePreview.ReadAsync(bytes, id, "1.0.2", digest, catalog, default);
            var preferences = AppearanceSettings.Default with { Motion = MotionPreference.Reduced };
            var outerPalette = ShellChromePalette.Resolve(outerStyles, preferences);
            var candidatePalette = ShellChromePalette.Resolve(previewStyles, preferences);
            Check(outerPalette.Focus != candidatePalette.Focus, "Fixture uses distinct focus colors");
            var root = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var outerTheme = new NativePopupTheme();
            outerTheme.Attach(root);
            outerTheme.Update(outerStyles, preferences);
            var compare = new Button { Content = "Compare with current theme" };
            outerPalette.PaintButton(compare);
            root.Children.Add(compare);
            using var scene = new CatalogueThemeScene { Width = 960, Height = 540 };
            root.Children.Add(scene);
            App.Window.AppWindow.ResizeClient(new(1400, 890));
            Content = root;
            scene.Apply(previewStyles, preferences, false);
            await Wait(() => scene.SelectedButton is { IsLoaded: true, ActualWidth: > 0 });
            var button = scene.SelectedButton!;
            Check(button.Focus(FocusState.Keyboard), "The selected radial sector is focusable");
            await Task.Delay(250);
            await CaptureEditorBitmapAsync(scene, Path.Combine(directory, "candidate-tray.png"));
            menu.Items.Add(new MenuFlyoutItem { Text = "Show sample notification" });
            using var lease = NativePopupTheme.Menu(menu, button);
            menu.ShowAt(button);
            await Wait(() => menu.IsOpen && menu.MenuFlyoutPresenterStyle is not null);
            Windows.UI.Color? MenuSurface() => menu.MenuFlyoutPresenterStyle?.Setters.OfType<Setter>()
                .FirstOrDefault(setter => setter.Property == Control.BackgroundProperty)?.Value switch
                {
                    SolidColorBrush brush => brush.Color,
                    LinearGradientBrush brush => brush.GradientStops.First(stop => stop.Offset == .5).Color,
                    _ => null,
                };
            Check(MenuSurface() == candidatePalette.Surface, "Open sample menu uses the candidate surface");
            scene.Apply(outerStyles, preferences, false);
            Check(MenuSurface() == outerPalette.Surface, "Comparing updates the already-open sample menu");
            Check((compare.FocusVisualPrimaryBrush as SolidColorBrush)?.Color == outerPalette.Focus, "The surrounding focus brush retains the current theme");
            menu.Hide();
            await Wait(() => !menu.IsOpen);
            await CaptureEditorBitmapAsync(scene, Path.Combine(directory, "current-tray.png"));
            scene.Apply(previewStyles, preferences, false);
            scene.ShowNotification(true);
            await Task.Delay(180);
            await CaptureEditorBitmapAsync(scene, Path.Combine(directory, "candidate-notification.png"));
            root.Children.Remove(scene);
            Check(compare.Focus(FocusState.Keyboard) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), compare),
                "Closing the preview returns keyboard focus to its origin");
            Check(!File.Exists(profile.SettingsFile) && !Directory.Exists(profile.ThemesDirectory),
                "Preview and comparison create no installed theme or appearance settings");
        }
        catch (Exception error) { failure = error; }
        finally { menu.Hide(); }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"),
            JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw new InvalidOperationException("Theme preview validation failed.", failure);
    }
}
