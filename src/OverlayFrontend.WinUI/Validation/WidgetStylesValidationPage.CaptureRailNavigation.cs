using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task CheckCaptureRailNavigation(WidgetViewPresenter page, WidgetIndexedCollectionView list)
    {
        string? Action() => list.FocusedReadingAction?.ActionId;
        string Open(int index) => "open." + (index + 1).ToString("x32");
        async Task Move(FocusNavigationDirection dir)
        {
            page.MoveFocus(dir);
            await Task.Delay(45);
        }
        async Task Visible(string step)
        {
            // Check the focused tile against every clipping viewport after
            // native scrolling has settled, including the outer details body.
            await Task.Delay(90);
            if (FocusManager.GetFocusedElement(page.XamlRoot) is not FrameworkElement focus || Action() is null)
                throw new InvalidOperationException(step + ": lost thumbnail focus: " + page.FocusDiagnostics());
            for (DependencyObject? p = focus; p is not null && !ReferenceEquals(p, page); p = VisualTreeHelper.GetParent(p))
            {
                if (p is not ScrollViewer viewport) continue;
                var bounds = focus.TransformToVisual(viewport).TransformBounds(new(0, 0, focus.ActualWidth, focus.ActualHeight));
                var visible = WidgetScrollViewport.ContentBounds(viewport);
                var clipped = bounds;
                clipped.Intersect(visible);
                if (clipped.Width + 3 < Math.Min(bounds.Width, visible.Width) ||
                    clipped.Height + 3 < Math.Min(bounds.Height, visible.Height))
                    throw new InvalidOperationException($"{step}: {Action()} bounds={bounds} viewport={visible} clipped={clipped}");
            }
        }

        Check(page.FocusHostTarget("tab"), "Enter the Game Details tab bar");
        await Move(FocusNavigationDirection.Down);
        await Wait(() => Action() == Open(0));
        for (var pass = 0; pass < 3; pass++)
        {
            var dateStart = pass * MediaPlayerDefinition.MaximumGalleryItems;
            var dateEnd = dateStart + MediaPlayerDefinition.MaximumGalleryItems - 1;
            for (var index = dateStart + 1; index <= dateEnd; index++)
            {
                await Move(FocusNavigationDirection.Right);
                await Wait(() => Action() == Open(index));
            }
            await Visible("last thumbnail");
            await page.HandleControllerButtonAsync(ControllerButton.A);
            await page.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            await Wait(() => Action() == "close");
            await Visible("expanded last thumbnail");
            var selected = dateEnd;
            for (var index = dateEnd - 1; index >= dateStart; index--)
            {
                await Move(FocusNavigationDirection.Left);
                await Wait(() => Action() == Open(index));
                if ((index - dateStart) % 4 == 0) await Visible("traversing beneath preview at " + index);
                if (index == dateStart + 8)
                {
                    await page.HandleControllerButtonAsync(ControllerButton.A);
                    await page.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                    await Wait(() => Action() == "close" && !list.IsEntryPending);
                    selected = index;
                    await Visible("replaced preview");
                }
            }
            await Visible("first thumbnail beneath preview");
            for (var attempt = 0; attempt < 10 && !ReferenceEquals(FocusManager.GetFocusedElement(page.XamlRoot),
                     Find<Control>("Widget.play", page)); attempt++)
                await Move(FocusNavigationDirection.Up);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(page.XamlRoot), Find<Control>("Widget.play", page)), "Up reaches Play");
            await Move(FocusNavigationDirection.Down);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(page.XamlRoot), Find<Control>("Widget.tab", page)), "Down reaches navigation bar");
            await Move(FocusNavigationDirection.Down);
            await Wait(() => Action() is not null);
            await Visible("return from Play");
            for (var day = 0; day < pass && list.CaptureFocusedItem()?.ItemKey != "day." + pass; day++)
            {
                var before = list.CaptureFocusedItem()?.ItemKey;
                await Move(FocusNavigationDirection.Down);
                await Wait(() => list.CaptureFocusedItem()?.ItemKey != before && Action() is not null);
                await Visible("return to the expanded date");
            }
            await page.HandleControllerButtonAsync(ControllerButton.B);
            await page.HandleControllerButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
            await Wait(() => Action() == Open(selected) && !list.IsEntryPending);
            await Visible("closed preview");
            for (var index = selected - 1; index >= dateStart; index--)
            {
                await Move(FocusNavigationDirection.Left);
                await Wait(() => Action() == Open(index), $"after close: pass {pass}, expected tile {index}, current {Action()}");
            }
            if (pass < 2)
            {
                page.Height = 350;
                page.UpdateLayout();
                await Move(FocusNavigationDirection.Down);
                await Wait(() => Action() == Open(dateEnd + 1));
                await Visible("second date in a shorter viewport");
            }
        }
        Check(true, "Traversed overflow tiles, expanded, returned through Play and tabs, collapsed and repeated");
    }
}
