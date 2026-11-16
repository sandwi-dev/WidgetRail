using Microsoft.UI.Xaml;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyPinAdjustmentVisualsAsync(PinnedSurface target,Action<bool,string> check,string path)
    {
        var toolbar=pinAdjustmentToolbar!;
        var priorAppearance=savedAppearance;var priorPalette=ShellPalette;
        var original=target.Window.Bounds;var alpha=target.Window.OpacityPercent;
        try
        {
            foreach(var variant in new[]{"Light","Dark","HighContrast"})
            foreach(var large in new[]{false,true})
            {
                var styles=(priorPalette??new Dictionary<string,BridgeNodeRenderStyles>()).ToDictionary(pair=>pair.Key,pair=>pair.Value);
                var light=variant=="Light";
                Override("canvas","background",light?"#fafafa":"#202020");
                Override("panel","background",light?"#f5f5f5":"#202020");
                Override("tray-item:selected:focused","outline-color",light?"#5137a1":"#d4c3ff");
                foreach(var role in new[]{"body","hint","tray-item","tray-item:selected"})Override(role,"color",light?"#161616":"#f5f5f5");
                ShellPalette=styles;
                savedAppearance=priorAppearance with{Contrast=variant=="HighContrast"?ContrastPreference.High:ContrastPreference.Standard,
                    TextScale=large?1.5:1,InterfaceScale=large?1.25:1};
                SetPinnedOpacity(large?30:100);
                RefreshPinnedAdjustmentToolbar();
                await WebAppUntil(()=>!toolbar.LayoutPending&&toolbar.Window.Child?.IsLoaded==true&&toolbar.DisplayedHintsForValidation.Count==3&&
                    Math.Abs(toolbar.Window.AutomationRoot.ActualWidth-toolbar.Window.ClientSize.Width/toolbar.Window.AutomationRoot.XamlRoot.RasterizationScale)<1&&
                    Math.Abs(toolbar.Window.AutomationRoot.ActualHeight-toolbar.Window.ClientSize.Height/toolbar.Window.AutomationRoot.XamlRoot.RasterizationScale)<1);
                toolbar.Window.AutomationRoot.UpdateLayout();
                var root=toolbar.Window.AutomationRoot;
                foreach(var control in new FrameworkElement[]{toolbar.Opacity,toolbar.Save,toolbar.Cancel,toolbar.Close})
                {
                    var bounds=control.TransformToVisual(root).TransformBounds(new(0,0,control.ActualWidth,control.ActualHeight));
                    check(bounds.Width>0&&bounds.Height>0&&bounds.Left>=0&&bounds.Top>=0&&bounds.Right<=root.ActualWidth+1&&bounds.Bottom<=root.ActualHeight+1,
                        $"{variant} {(large?"large text":"normal")} keeps {control.GetType().Name} inside the toolbar");
                }
                check(toolbar.Window.OpacityPercent==100,$"{variant} controls stay fully opaque at {target.Window.OpacityPercent}% pin opacity");
                await CapturePinToolbarAsync(toolbar,$"{path}.{variant}-{(large?"large":"normal")}.png");

                void Override(string role,string property,string value)
                {
                    var values=styles.GetValueOrDefault(role)?.Base.ToDictionary(pair=>pair.Key,pair=>pair.Value)??new Dictionary<string,BridgeComputedStyleValue>();
                    values[property]=new(){Kind=WrssValueKind.Color,Text=value};
                    styles[role]=new(){Base=values,Focused=values,Pressed=values};
                }
            }
            savedAppearance=priorAppearance;ShellPalette=priorPalette;SetPinnedOpacity(alpha);
            foreach(var monitor in PinnedDisplayAreas.Read())
            {
                var width=Math.Min(original.Width,monitor.WorkArea.Width/2);
                var height=Math.Min(original.Height,monitor.WorkArea.Height/2);
                foreach(var corner in new[]{0,1,2,3})
                {
                    var proposed=new PinnedBounds(corner%2==0?monitor.WorkArea.X:monitor.WorkArea.X+monitor.WorkArea.Width-width,
                        corner<2?monitor.WorkArea.Y:monitor.WorkArea.Y+monitor.WorkArea.Height-height,width,height);
                    target.Window.Place(PreviewPinnedPointerPlacement(target,proposed,0));
                    RefreshPinnedAdjustmentToolbar();await Task.Delay(80);
                    var bounds=toolbar.Window.Bounds;var area=target.Monitor.WorkArea;
                    check(bounds.X>=area.X&&bounds.Y>=area.Y&&bounds.X+bounds.Width<=area.X+area.Width&&bounds.Y+bounds.Height<=area.Y+area.Height,
                        $"Toolbar remains reachable at corner {corner} on monitor {monitor.Id}");
                    check(!NotificationPlacement.Overlaps(bounds,target.Window.Bounds),"Available outside space keeps the toolbar off the pin content");
                }
            }
        }
        finally
        {
            savedAppearance=priorAppearance;ShellPalette=priorPalette;SetPinnedOpacity(alpha);
            target.Window.Place(PreviewPinnedPointerPlacement(target,original,0));RefreshPinnedAdjustmentToolbar();
        }
    }
}
