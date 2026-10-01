using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private readonly List<IDisposable> depthSpecimens = [];
    private async Task NativeCollectionDepthAsync()
    {
        var specimens = new StackPanel { Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray) };
        host.Children.Add(specimens);
        foreach (var grid in new[] { false, true })
        {
            ListViewBase list = grid ? new GridView() : new ListView();
            list.Width = 310; list.Height = 130;
            SelectorItem item = grid ? new GridViewItem() : new ListViewItem();
            item.Content = grid ? "Native grid poster" : "Native list row";
            AutomationProperties.SetAutomationId(list, grid ? "Styles.Depth.Grid" : "Styles.Depth.List");
            AutomationProperties.SetAutomationId(item, grid ? "Styles.Depth.Poster" : "Styles.Depth.Row");
            list.Items.Add(item); specimens.Children.Add(list);
            await Wait(() => item.IsLoaded && item.ActualWidth > 0);
            var nativeTemplate = VisualTreeHelper.GetChild(item, 0);
            var adapter = new NativeComputedStyleAdapter(item);
            depthSpecimens.Add(adapter);
            const string box = "button { width: 210px; height: 72px; margin: 20px; background: rgba(240,232,208,.8); color: #102030; corner-radius: 10px; border-width: 2px; border-color: #556677; border-top-color: #dd3311; shadow-color: rgba(0,0,0,.65); shadow-blur: 12px; shadow-offset-x: -5px; shadow-offset-y: 7px; transition-duration: 100ms; }";
            const string focus = "button:focused { scale: 1.04; outline-color: #228877; outline-width: 2px; }";
            var style = Compute(box + focus, "native-depth-item", "button");
            using (var foreign = CompositionTarget.GetCompositorForCurrentThread().CreateContainerVisual())
            {
                ElementCompositionPreview.SetElementChildVisual(item, foreign);
                adapter.Update(style);
                Check(adapter.Depth is null && adapter.FocusDecoration is null && ColorOf(item.BorderBrush).A > 0 &&
                    ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(item), foreign),
                    $"{list.GetType().Name} foreign child visual keeps ownership and native border fallback");
                adapter.Update(null);
                ElementCompositionPreview.SetElementChildVisual(item, null);
            }
            adapter.Update(style);
            await Wait(() => adapter.Depth?.IsRealized == true);
            var depth = adapter.Depth!;
            Check(nativeTemplate is ListViewItemPresenter && ReferenceEquals(nativeTemplate, VisualTreeHelper.GetChild(item, 0)),
                $"{list.GetType().Name} depth keeps optimized native item template identity");
            Check(depth.UsesOuterMask && depth.MaskPadding >= 31 && depth.NativeShadow is { Offset.X: -5, Offset.Y: 7 },
                $"{list.GetType().Name} shadow excludes content with an offset-aware native outer mask");
            var creates = depth.NativeResourceCreates;
            item.Focus(FocusState.Keyboard);
            await Wait(() => item.Scale.X > 1 && adapter.ScaleMotion?.IsAnimating == false);
            Check(adapter.FocusDecoration is not null && depth.IsRealized && ColorOf(item.Background).A == 204,
                $"{list.GetType().Name} native scale focus and translucent content coexist with depth");
            adapter.Update(Compute(box, "native-depth-item", "button"));
            Check(adapter.FocusDecoration is null && ReferenceEquals(depth, adapter.Depth) && depth.IsRealized,
                $"{list.GetType().Name} removing focus lease preserves depth lease");
            adapter.Update(style);
            var decoration = adapter.FocusDecoration;
            adapter.Update(Compute("button { background: #f0e8d0; }" + focus, "native-depth-item", "button"));
            Check(adapter.Depth is null && decoration is not null && ReferenceEquals(decoration, adapter.FocusDecoration),
                $"{list.GetType().Name} removing depth lease preserves focus lease");
            adapter.Update(style); depth = adapter.Depth!; creates = depth.NativeResourceCreates;
            adapter.Update(style);
            item.Width += 10;
            await Task.Delay(50);
            Check(depth.NativeResourceCreates == creates,
                $"{list.GetType().Name} snapshot and resize reuse depth resources");
            specimens.Children.Remove(list);
            await Wait(() => !item.IsLoaded && !depth.IsRealized);
            specimens.Children.Add(list);
            await Wait(() => depth.IsRealized);
            Check(depth.NativeResourceCreates == creates + 1,
                $"{list.GetType().Name} unload retires and reload rebuilds masked depth");
            WidgetViewPresenter.SetHighContrastStyleOverride(true);
            await Wait(() => adapter.Depth is null && adapter.FocusDecoration is null);
            Check(adapter.Depth is null && adapter.FocusDecoration is null && item.UseSystemFocusVisuals,
                $"{list.GetType().Name} contrast restores native focus and border without depth [systemFocus={item.UseSystemFocusVisuals}]");
            WidgetViewPresenter.SetHighContrastStyleOverride(false);
            adapter.Update(null);
            Check(ElementCompositionPreview.GetElementChildVisual(item) is null,
                $"{list.GetType().Name} final decoration lease releases the shared child slot");
            adapter.Update(style);
        }
        outside.Focus(FocusState.Keyboard);
    }
}
