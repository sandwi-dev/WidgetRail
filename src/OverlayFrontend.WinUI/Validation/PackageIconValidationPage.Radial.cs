using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PackageIconValidationPage
{
    private async Task RadialIconContinuityAsync()
    {
        var host=(StackPanel)Content;
        var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls=0;
        using var radial=new RadialChooserView(async(_,asset,_)=>
        {calls++;await pending.Task;if(asset=="missing")throw new InvalidDataException();return Bytes(asset);});
        host.Children.Add(radial);
        var catalog=Enumerable.Range(0,32).Select(i=>new BridgeWidgetDescriptor{Id="radial-"+i,
            Name="Widget "+i,InstanceId="instance-"+i,RuntimeGeneration="runtime",PresentationGeneration="presentation",Icon=WidgetGlyph.Music,PackageContentDigest="first",
            PackageIcon=new("red",WidgetPackageIconColorMode.OriginalColor)}).ToArray();
        var shownPage=0;
        WidgetPackageIconView[] Icons()=>catalog.Skip(shownPage*8).Take(8)
            .Select(item=>((Grid)radial.ButtonFor(item.Id)!.Content).Children.OfType<WidgetPackageIconView>().Single()).ToArray();
        void Page(int index){shownPage=index;radial.Update(catalog,catalog[0].Id,index,null,AppearanceSettings.Default,false);radial.UpdateLayout();}
        try
        {
            Page(0);await Until(()=>Icons().Length==8);
            Check(Icons().All(i=>i.Content is FontIcon{Opacity:0}),"Radial slots reserve space without flashing fallback glyphs during loading");
            pending.SetResult();await Until(()=>Icons().All(i=>i.Content is Image));
            var first=Icons().Select(i=>i.Content).ToArray();
            Page(1);await Until(()=>Icons().All(i=>i.Content is Image));
            var before=calls;Page(0);
            Check(Icons().Select(i=>i.Content).SequenceEqual(first)&&calls==before,
                $"Returning to a radial page reuses its ready native icons synchronously without new demand (calls={calls}/{before}, icons={Icons().Length})");
            (catalog[0],catalog[1])=(catalog[1],catalog[0]);Page(0);
            Check(Icons().Select(i=>i.Content).SequenceEqual(new[]{first[1],first[0]}.Concat(first.Skip(2))),
                "Catalog reordering moves the correct retained icon to its new radial sector");
            catalog[0]=catalog[0] with{PackageContentDigest="replacement",PackageIcon=new("missing",WidgetPackageIconColorMode.OriginalColor)};
            Page(0);await Until(()=>Icons()[0].Content is FontIcon{Opacity:1});
            Check(true,"Changed package identity discards old art and real load failure reveals semantic fallback");
            Page(2);await Until(()=>Icons().All(i=>i.Content is Image));
            Page(3);await Until(()=>Icons().All(i=>i.Content is Image));
            Check(radial.RetainedIconCount==24,"Paging beyond three pages evicts old native views within a fixed bound");
            catalog=catalog.TakeLast(8).ToArray();Page(0);
            Check(radial.RetainedIconCount==8,"Removed catalog entries release retained radial icon views");
        }
        finally{host.Children.Remove(radial);}
    }
}
