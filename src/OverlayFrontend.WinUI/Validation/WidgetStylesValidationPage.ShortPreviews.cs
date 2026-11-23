using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task ShortPreviewsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        Exception? failure=null;
        var measurements=new List<object>();
        var catalog=new ThemeCatalog(new PlatformSettingsPaths(directory));
        var authored=WrssParser.Parse(".links{width:100%;min-width:0px;min-height:0px;flex-grow:1;gap:16px;padding:4px;} "+
            ".link{min-width:0px;min-height:40px;flex-grow:0;padding:8px 10px;font-size:13px;font-weight:600;}","short-preview.wrss");
        var theme=ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,new((WrssDocument[])[authored.Document],[]),
            catalog.Load(ThemeIdentity.BuiltInMonoInk,"1.0.0").Package).Theme!;
        presenter.Visibility=Visibility.Collapsed;
        try
        {
            foreach(var width in new[]{1040d,420d})
            foreach(var textScale in new[]{1d,1.25d,1.5d})
            foreach(var count in new[]{1,2,12})
            {
                var root=new Grid{Width=width,Height=width>500?680:360};
                var page=new WidgetViewPresenter{Width=root.Width,Height=root.Height};
                root.Children.Add(page);PresentationLayers.Attach(root);host.Children.Add(root);
                var popup=new NativePopupTheme();popup.Attach(page);
                var appearance=AppearanceSettings.Default with{Motion=MotionPreference.Reduced,TextScale=textScale};
                page.ApplyAppearance(appearance,false);
                try
                {
                    var links=UI.VerticalScroll("short.links",Enumerable.Range(0,count).Select(index=>(WidgetElement)
                        UI.Button(index==0?"Video Player UI: Best Examples, Patterns & UX Tips":"Another link "+index,
                            "link."+index,"short.link."+index).Classes("link")).ToArray()).Classes("links");
                    var owner=UI.FocusPresentationSurface(UI.Button("Message","source","short.source").PresentOnFocus(links),
                        UI.Text("","short.empty"),"short.owner") with
                        {Preview=new(520,440,DelayMilliseconds:0){AllowMedia=true,EnterButton=ControllerButton.A,SizeToContentHeight=true}};
                    var snapshot=new WidgetView(UI.Stack("short.page",owner).InputScope("short.scope"),"short.source").CreateSnapshot("short-preview",1);
                    var styles=new Dictionary<string,BridgeNodeRenderStyles>();
                    void Visit(ViewNode node){styles[node.Id]=Compute(theme,node);foreach(var child in node.Children)Visit(child);if(node.FocusPresentation is {} preview)Visit(preview);if(node.DefaultFocusPresentation is {} fallback)Visit(fallback);}
                    Visit(snapshot.Root);foreach(var role in new[]{"body","canvas","panel","preview","hint"})styles[role]=Compute(theme,role,role,[]);
                    popup.Update(styles,appearance);
                    var descriptor=new BridgeWidgetDescriptor{Id="short-preview",Name="Short preview",InstanceId=snapshot.WidgetInstanceId,
                        RuntimeGeneration="short",PresentationGeneration="short",Icon=WidgetGlyph.Connection,PackageContentDigest=""};
                    page.Apply(new(new(descriptor.Id,descriptor.RuntimeGeneration,descriptor.PresentationGeneration,1,snapshot.WidgetInstanceId,1,snapshot.ActiveInputScopeId),descriptor,snapshot,styles));
                    await Wait(()=>Find<Button>("Widget.short.source",root) is {IsLoaded:true});
                    page.Enter(restoreNativeFocus:false);Find<Button>("Widget.short.source",root)!.Focus(FocusState.Keyboard);
                    await Wait(()=>page.IsFocusPreviewOpen&&Find<ScrollViewer>("Widget.short.links",root) is {IsLoaded:true,ViewportHeight:>0});
                    await Task.Delay(100);
                    var scroll=Find<ScrollViewer>("Widget.short.links",root)!;
                    var frame=page.FocusPreviewFrame!;
                    void Sample(string phase)=>measurements.Add(new{width,textScale,count,phase,frameHeight=frame.ActualHeight,bodyHeight=frame.Body.ActualHeight,
                        scroll.ActualHeight,scroll.DesiredSize,scroll.ExtentHeight,scroll.ViewportHeight,scroll.ScrollableHeight,
                        contentHeight=(scroll.Content as FrameworkElement)?.ActualHeight,contentViewport=WidgetScrollViewport.ContentBounds(scroll)});
                    Sample("passive");
                    Check(count<=2?scroll.ScrollableHeight<=1:scroll.ScrollableHeight>1,
                        $"{width} DIP / {textScale} text / {count} links: only genuinely overflowing content scrolls");
                    await page.HandleControllerButtonAsync(ControllerButton.A);
                    await page.HandleControllerButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                    await Wait(()=>page.IsPreviewEnteredForValidation);
                    Sample("interactive");
                    Check(count<=2?scroll.ScrollableHeight<=1:scroll.ScrollableHeight>1,
                        $"{width} DIP / {textScale} text / {count} links: entering preserves fitted scroll geometry");
                    await page.HandleControllerButtonAsync(ControllerButton.B);
                    await page.HandleControllerButtonAsync(ControllerButton.B,ControllerEventPhase.Released);
                    Check(!page.IsPreviewEnteredForValidation,$"{width} DIP / {textScale} text / {count} links: Back returns to source");
                }
                finally{await page.DisposeAsync();host.Children.Remove(root);}
            }
        }
        catch(Exception error){failure=error;}
        await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{passed=failure is null,checks,measurements,error=failure?.ToString()}));
        if(failure is not null)throw failure;
    }
}
