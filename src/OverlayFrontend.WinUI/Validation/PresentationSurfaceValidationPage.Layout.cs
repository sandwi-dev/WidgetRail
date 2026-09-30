using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PresentationSurfaceValidationPage
{
    private async Task FocusSurfaceLayoutAsync()
    {
        var stage = (StackPanel)Content;
        var surface = new WidgetPresentationSurface(ViewNodeKind.FocusPresentationSurface) { Width = 500, Height = 300 };
        surface.Fragment!.Width = 180; surface.Fragment.Height = 50;
        surface.ContentPanel.Children.Add(new Border { Width = 380, Height = 100 });
        stage.Children.Add(surface);
        try
        {
            surface.ConfigureFocusLayout(false, "end", "end", 20, false);
            await Until(() => surface.IsLoaded);
            surface.UpdateLayout();
            var summary = surface.Fragment.TransformToVisual(surface).TransformPoint(new(0, 0));
            var rail = surface.ContentPanel.TransformToVisual(surface).TransformPoint(new(0, 0));
            Check(Math.Abs(summary.X - 320) < 1 && Math.Abs(summary.Y - 130) < 1,
                "focus summary follows right and bottom justification");
            Check(Math.Abs(rail.Y + surface.ContentPanel.ActualHeight - 300) < 1 && Math.Abs(rail.Y - summary.Y - 70) < 1,
                "bounded focus content sits at bottom with declared gap above");
            surface.ConfigureFocusLayout(false, "center", "start", 20, false);
            surface.UpdateLayout();
            summary = surface.Fragment.TransformToVisual(surface).TransformPoint(new(0, 0));
            Check(Math.Abs(summary.X) < 1 && Math.Abs(summary.Y - 65) < 1,
                "style change repositions focus layout without retained end tracks");
            surface.ConfigureFocusLayout(false, "end", "start", 20, true);
            surface.UpdateLayout();
            Check(Math.Abs(surface.ContentPanel.ActualHeight - 230) < 1,
                "unbounded collection content keeps a finite remaining viewport");
            surface.ConfigureFocusLayout(false, "space-around", "start", 20, false);
            surface.UpdateLayout();
            summary = surface.Fragment.TransformToVisual(surface).TransformPoint(new(0, 0));
            rail = surface.ContentPanel.TransformToVisual(surface).TransformPoint(new(0, 0));
            Check(Math.Abs(summary.Y - 32.5) < 1 && Math.Abs(rail.Y - 167.5) < 1,
                "focus fragment and content use half outer spacing plus additive authored gap");
            surface.ConfigureFocusLayout(false, "space-between", "start", 200, false);
            surface.UpdateLayout();
            summary = surface.Fragment.TransformToVisual(surface).TransformPoint(new(0, 0));
            rail = surface.ContentPanel.TransformToVisual(surface).TransformPoint(new(0, 0));
            Check(Math.Abs(rail.Y - summary.Y - surface.Fragment.ActualHeight - 200) < 1,
                "focus flow reserves its authored gap even when no distributed free space remains");
            surface.ContentPanel.Children.Clear();
            surface.ContentPanel.Children.Add(new Border { Width = 100, Height = 100 });
            surface.ConfigureFocusLayout(true, "space-around", "start", 20, false);
            surface.UpdateLayout();
            summary = surface.Fragment.TransformToVisual(surface).TransformPoint(new(0, 0));
            rail = surface.ContentPanel.TransformToVisual(surface).TransformPoint(new(0, 0));
            Check(Math.Abs(summary.X - 50) < 1 && Math.Abs(rail.X - 350) < 1,
                "focus flow switches axis with shared space-around track semantics");
            surface.Fragment.Visibility = Visibility.Collapsed;
            surface.ConfigureFocusLayout(true, "space-around", "start", 20, false);
            surface.UpdateLayout();
            rail = surface.ContentPanel.TransformToVisual(surface).TransformPoint(new(0, 0));
            Check(Math.Abs(rail.X - 200) < 1,
                "collapsed focus fragment contributes neither gap nor distributed spacing");
        }
        finally { stage.Children.Remove(surface); await surface.DisposeAsync(); }
    }
}
