using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayFrontend.WinUI.Notifications;
using WidgetRail.WidgetBridge;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class DesktopAcrylicValidationPage : Page, IAsyncDisposable
{
    private readonly List<PinnedWidgetWindow> pins=[];
    private readonly List<string> checks=[];
    private NotificationBannerChrome? banner;
    internal DesktopAcrylicValidationPage(Window owner,string output)
    {
        var background=new Grid();
        for(var i=0;i<12;i++)
        {
            background.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
            var stripe=new Border {Background=new SolidColorBrush(i%2==0?Microsoft.UI.Colors.SteelBlue:Microsoft.UI.Colors.Goldenrod)};
            Grid.SetColumn(stripe,i);background.Children.Add(stripe);
        }
        Content=background;
        Loaded+=async(_,_)=>
        {
            string? error=null;
            try
            {
                var settings=AppearanceSettings.Default;
                var policy=OverlayAppearancePolicy.Resolve(settings,"fixture",WidgetSurfaceAppearance.DesktopAcrylic,false);
                var origin=owner.AppWindow.Position;
                var scale=XamlRoot.RasterizationScale;
                for(var i=0;i<6;i++)
                {
                    var pin=new PinnedWidgetWindow("Acrylic "+(i+1),WinRT.Interop.WindowNative.GetWindowHandle(owner));
                    pins.Add(pin);
                    pin.SetContent(new TextBlock {Text="Desktop acrylic "+(i+1),FontSize=24,Foreground=new SolidColorBrush(Microsoft.UI.Colors.White),Margin=new(24)});
                    pin.Place(new(origin.X+(int)((30+i%3*350)*scale),origin.Y+(int)((80+i/3*250)*scale),(int)(330*scale),(int)(220*scale)));
                    OverlaySurfacePaint.Apply(pin.SurfaceBackground,policy,null);
                    pin.Show();
                }
                await Task.Delay(500);
                Check(pins.All(pin=>pin.WindowlessMaterial().HasDesktopAcrylic&&!pin.Interactive),"Six nonactivating pin islands attach bounded desktop acrylic");
                await Task.Run(()=>{GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();});
                Check(pins.All(pin=>pin.IsVisible),"Backdrop targets survive forced collection while all six pins remain visible");
                var first=pins[0];var layer=first.WindowlessMaterial();var original=layer.Backdrop.SystemBackdrop;
                OverlaySurfacePaint.Apply(first.SurfaceBackground,OverlayAppearancePolicy.Resolve(settings with {AcrylicEffects=false},"fixture",WidgetSurfaceAppearance.DesktopAcrylic,false),null);
                Check(!layer.HasDesktopAcrylic && first.SurfaceBackground.Background is SolidColorBrush {Color.A:255},
                    "Acrylic off detaches the desktop effect and leaves an opaque fallback");
                OverlaySurfacePaint.Apply(first.SurfaceBackground,OverlayAppearancePolicy.Resolve(settings with {AcrylicEffects=false},"fixture",WidgetSurfaceAppearance.Transparent,false),null);
                Check(!layer.HasDesktopAcrylic && first.SurfaceBackground.Background is SolidColorBrush {Color.A:0},
                    "Acrylic off preserves intentionally transparent pins");
                OverlaySurfacePaint.Apply(first.SurfaceBackground,policy,null);
                original=layer.Backdrop.SystemBackdrop;
                var bounds=first.Bounds;
                first.Place(bounds with {Width=bounds.Width+20});
                await Task.Delay(100);
                first.Place(bounds);
                Check(ReferenceEquals(original,layer.Backdrop.SystemBackdrop),"Resize reuses the existing backdrop controller");
                Check(layer.Backdrop.CornerRadius==first.SurfaceBackground.CornerRadius,"Backdrop corners match the content frame");
                first.SetOpacity(50);
                Check(first.OpacityPercent==50&&first.SurfaceBackground.Child.Opacity==1,"Pin opacity preserves the authored content opacity");
                first.SetOpacity(100);
                OverlaySurfacePaint.Apply(first.SurfaceBackground,OverlayAppearancePolicy.Resolve(settings with {Transparency=TransparencyPreference.Reduced},"fixture",WidgetSurfaceAppearance.DesktopAcrylic,false),null);
                Check(!layer.HasDesktopAcrylic&&first.SurfaceBackground.Background is SolidColorBrush {Color.A:255},"Reduced transparency detaches desktop material and paints solid");
                OverlaySurfacePaint.Apply(first.SurfaceBackground,OverlayAppearancePolicy.Resolve(settings,"fixture",WidgetSurfaceAppearance.DesktopAcrylic,true),null);
                Check(!layer.HasDesktopAcrylic,"High contrast suppresses the desktop material");
                OverlaySurfacePaint.Apply(first.SurfaceBackground,policy,null);
                await Task.Delay(100);
                Check(layer.HasDesktopAcrylic,"Restoring appearance reconnects the material");
                OverlaySurfacePaint.Apply(first.SurfaceBackground,OverlayAppearancePolicy.Resolve(settings,"fixture",WidgetSurfaceAppearance.Transparent,false),null);
                Check(!layer.HasDesktopAcrylic&&first.SurfaceBackground.Background is SolidColorBrush {Color.A:0},"Transparent overlays retain no desktop blur");
                OverlaySurfacePaint.Apply(first.SurfaceBackground,policy,null);
                var card=new NotificationCard((_,_,_)=>throw new InvalidOperationException("No package artwork in this fixture"));
                var palette=ShellChromePalette.Resolve(null,settings);
                card.ApplyBannerPalette(palette);
                var entry=new NotificationEntry(1,"Desktop acrylic","fixture","","","Notification banner",
                    "Only the background fades. Text stays clear.",this,
                    new(()=>true,()=>{},()=>{},()=>{},()=>{}));
                card.Update(entry,null,1);
                banner=new(card);
                banner.Apply(palette,55);
                var bannerPin=new PinnedWidgetWindow("Acrylic banner",WinRT.Interop.WindowNative.GetWindowHandle(owner));
                pins.Add(bannerPin);
                bannerPin.SetContent(banner);
                bannerPin.Place(new(origin.X+(int)(380*scale),origin.Y+(int)(600*scale),(int)(480*scale),(int)(210*scale)));
                bannerPin.Show();
                await Task.Delay(150);
                Check(!bannerPin.Interactive&&banner.CardSurface.Opacity==1,"Acrylic banner is passive and background opacity does not fade foreground content");
                var bannerMaterial=(DesktopAcrylicSurface)banner.CardSurface.Parent;
                Check(Math.Abs(bannerMaterial.Backdrop.Opacity-.55)<.001,"A banner's saved background opacity is applied when its material first loads");
                var region=bannerMaterial.Backdrop.TransformToVisual(banner).TransformBounds(new(0,0,bannerMaterial.Backdrop.ActualWidth,bannerMaterial.Backdrop.ActualHeight));
                Check(Math.Abs(region.X-NotificationBannerChrome.ShadowGutter)<1&&Math.Abs(region.Y-NotificationBannerChrome.ShadowGutter)<1&&
                    Math.Abs(region.Width-banner.CardSurface.ActualWidth)<1&&Math.Abs(region.Height-banner.CardSurface.ActualHeight)<1,
                    "Desktop blur is confined to the banner card and excludes its shadow gutter");
                banner.Apply(palette,55,acrylicEffects:false);
                Check(!bannerMaterial.HasDesktopAcrylic && ((Border)banner.CardSurface).Background is LinearGradientBrush plain &&
                    plain.GradientStops.All(stop=>stop.Color.A==140) && banner.CardSurface.Opacity==1,
                    "Acrylic off detaches banner blur while preserving category opacity and foreground text");
                banner.Apply(palette,55,reducedTransparency:true);
                Check(((Border)banner.CardSurface).Background is LinearGradientBrush solid&&solid.GradientStops.All(stop=>stop.Color.A==255),"Reduced-transparency banners use a solid fallback");
                banner.Apply(palette,55);
                await Task.Delay(150);
                Check(pins.Take(6).All(pin=>pin.IsVisible),"Banner creation preserves all six pin windows");
                for(var cycle=0;cycle<4;cycle++)
                {
                    OverlaySurfacePaint.Apply(first.SurfaceBackground,OverlayAppearancePolicy.Resolve(settings,"fixture",WidgetSurfaceAppearance.Transparent,false),null);
                    OverlaySurfacePaint.Apply(first.SurfaceBackground,policy,null);
                    await Task.Run(()=>{GC.Collect();GC.WaitForPendingFinalizers();});
                }
                Check(layer.HasDesktopAcrylic,"Repeated detach and reconnect survives forced collection");
            }
            catch(Exception failure){error=failure.ToString();}
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=error is null,checks,error,
                windows=pins.Select(pin=>new{hwnd=(long)pin.Handle,bounds=pin.Bounds}).ToArray(),
                scope="Synthetic background and actual nonactivating native pin islands. Pixel inspection and sustained performance are separate checks."}));
        };
    }
    private void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
    public ValueTask DisposeAsync(){banner?.Dispose();foreach(var pin in pins)pin.Dispose();pins.Clear();return ValueTask.CompletedTask;}
}

internal static class AcrylicValidationAccess
{
    internal static DesktopAcrylicSurface WindowlessMaterial(this PinnedWidgetWindow pin)=>
        (DesktopAcrylicSurface)pin.SurfaceBackground.Parent;
}
