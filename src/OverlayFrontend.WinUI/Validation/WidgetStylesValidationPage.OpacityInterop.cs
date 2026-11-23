using System.Numerics;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task OpacityInteropAsync(string output)
    {
        var root = new Grid { Width = 400, Height = 280, Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
        var raw = new Grid();
        var facade = new Grid { Scale = new Vector3(1.02f), Opacity = .5 };
        var slot = new Grid { Width = 160, Height = 100 };
        root.Children.Add(raw); raw.Children.Add(facade);
        var nested = facade;
        for (var i = 0; i < 64; i++)
        {
            var child = new Grid(); nested.Children.Add(child); nested = child;
        }
        nested.Children.Add(slot);
        host.Children.Add(root);
        var layers = PresentationLayers.Attach(root);
        var fullscreen = new MediaFullscreenLayer(); fullscreen.Attach(root);
        MediaPresentationStage? stage = null;
        PresentationPortal? portal = null;
        var colors = new List<string>();
        var initialProducts = CompositionOpacityBinding.ActiveProductsForValidation;
        Exception? failure = null;
        try
        {
            await Wait(() => slot.IsLoaded && slot.ActualWidth > 0);
            var rawVisual = ElementCompositionPreview.GetElementVisual(raw);
            rawVisual.Opacity = .5f;
            stage = fullscreen.CreateMediaStage(1.6);
            stage.SurfaceHost.Background = new SolidColorBrush(Microsoft.UI.Colors.Red);
            stage.SetSlot(slot);
            Check(true, "Resident media accepts an ancestor using XAML Scale without taking its handout visual");
            var portalContent = new Grid();
            portal = new PresentationPortal(layers, slot, portalContent, passive: true, attachInput: false);
            Check(true, "Presentation portals accept the same mixed XAML/compositor ancestry");
            await Task.Delay(180);
            colors.Add(await CenterColor(root));
            rawVisual.Opacity = .25f;
            await Task.Delay(180);
            colors.Add(await CenterColor(root));
            facade.Opacity = .25;
            await Task.Delay(180);
            colors.Add(await CenterColor(root));
            // Pixel observations are retained separately from API acceptance.
            // They must show both facade and handout opacity, not only avoid an exception.
            Check(Math.Abs(Convert.ToInt32(colors[0].Substring(4, 2), 16) - 64) <= 4 &&
                Math.Abs(Convert.ToInt32(colors[1].Substring(4, 2), 16) - 32) <= 4 &&
                Math.Abs(Convert.ToInt32(colors[2].Substring(4, 2), 16) - 16) <= 4,
                "Resident media follows both handout Visual opacity and XAML facade opacity changes");
            stage.SetCovered(true);
            portalContent.Background = new SolidColorBrush(Microsoft.UI.Colors.Lime);
            rawVisual.Opacity = .5f; facade.Opacity = .5;
            await Task.Delay(180);
            colors.Add(await CenterColor(root));
            rawVisual.Opacity = .25f; facade.Opacity = .25;
            await Task.Delay(180);
            colors.Add(await CenterColor(root));
            Check(Math.Abs(Convert.ToInt32(colors[3].Substring(2, 2), 16) - 64) <= 4 &&
                Math.Abs(Convert.ToInt32(colors[4].Substring(2, 2), 16) - 16) <= 4,
                "Relocated portal chrome follows both opacity owners too");
            ElementCompositionPreview.GetElementVisual(root).Opacity = .5f;
            await Task.Delay(180);
            var portalWithRootOpacity = await CenterColor(root);
            portalContent.Background = null; stage.SetCovered(false);
            await Task.Delay(180);
            var mediaWithRootOpacity = await CenterColor(root);
            Check(Convert.ToInt32(portalWithRootOpacity.Substring(2, 2), 16) ==
                Convert.ToInt32(mediaWithRootOpacity.Substring(4, 2), 16),
                "Common root opacity applies exactly once to both relocated surfaces");
            portal.Dispose(); portal = null;
            stage.EnterFullscreen();
            Check(CompositionOpacityBinding.ActiveProductsForValidation == initialProducts,
                "Fullscreen releases inline opacity expressions and disposed portal references");
            stage.SetSlot(slot);
            Check(CompositionOpacityBinding.ActiveProductsForValidation > initialProducts,
                "Returning from fullscreen restores the complete opacity ancestry");
            stage.SetSlot(null);
            Check(CompositionOpacityBinding.ActiveProductsForValidation == initialProducts,
                "Detaching the media slot releases every intermediate compositor product");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            portal?.Dispose(); stage?.Dispose(); host.Children.Remove(root);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = failure is null, checks, colors, error = failure?.ToString() }));
        }
        if (failure is not null) throw failure;
    }
}
