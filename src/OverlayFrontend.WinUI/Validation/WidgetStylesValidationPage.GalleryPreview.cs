using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task GalleryPreviewAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var imagePath=Path.GetFullPath(Path.Combine(directory,"generated.png"));
        await File.WriteAllBytesAsync(imagePath,await ArtworkPngAsync(320,180));
        var root=new Grid{Width=820,Height=620};
        var page=new WidgetViewPresenter{Width=800,Height=600};
        root.Children.Add(page);PresentationLayers.Attach(root);new MediaFullscreenLayer().Attach(root);
        presenter.Visibility=Visibility.Collapsed;host.Children.Add(root);
        var popup=new NativePopupTheme();popup.Attach(page);
        var catalog=new ThemeCatalog(new PlatformSettingsPaths(directory));
        var css=WrssParser.Parse(".gallery-stage{width:100%;aspect-ratio:16/9;min-width:0px;min-height:0px;} .gallery-media{width:100%;height:100%;min-height:0px;padding:0px;border-width:0px;}","gallery-preview.wrss");
        var theme=ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,new((WrssDocument[])[css.Document],[]),new([],[])).Theme!;
        var appearance=AppearanceSettings.Default with{Motion=MotionPreference.Reduced};
        page.ApplyAppearance(appearance,false);
        TaskCompletionSource? gate=null;Exception? failure=null;
        page.ValidationMediaResolver=async(definition,token)=>
        {
            if(definition.Source.Attachment?.Id==new string('b',32)&&gate is {} waiting)await waiting.Task.WaitAsync(token);
            return new HostMediaPlayerSource(definition,new Uri(imagePath).AbsoluteUri,IsImage:true,Width:320,Height:180);
        };
        try
        {
            var media=UI.MediaGallery(new[]{Item('a'),Item('b')},"gallery.media").Classes("gallery-media");
            var content=UI.Grid("gallery.stage",(GridTrackDefinition[])[GridTrack.Star()],(GridTrackDefinition[])[GridTrack.Star()],media.InGrid()).Classes("gallery-stage");
            var preview=UI.FocusPresentationSurface(UI.Button("Message with two images","open","gallery.source").PresentOnFocus(content),
                UI.Text("","gallery.empty"),"gallery.owner") with
                {Preview=new(520,440,DelayMilliseconds:0){AllowMedia=true,EnterButton=ControllerButton.A,SizeToContentHeight=true}};
            var snapshot=new WidgetView(UI.Stack("gallery.page",UI.Button("Call","call","gallery.call"),preview).InputScope("gallery.scope"),"gallery.source")
                .CreateSnapshot("gallery-preview",1);
            var styles=new Dictionary<string,BridgeNodeRenderStyles>();
            void Visit(ViewNode node){styles[node.Id]=Compute(theme,node);foreach(var child in node.Children)Visit(child);if(node.FocusPresentation is {} focused)Visit(focused);if(node.DefaultFocusPresentation is {} fallback)Visit(fallback);}
            Visit(snapshot.Root);foreach(var role in new[]{"body","canvas","panel","preview","hint"})styles[role]=Compute(theme,role,role,[]);
            popup.Update(styles,appearance);
            var descriptor=new BridgeWidgetDescriptor{Id="gallery-preview",Name="Gallery preview",InstanceId=snapshot.WidgetInstanceId,
                RuntimeGeneration="gallery",PresentationGeneration="gallery",Icon=WidgetGlyph.Play,PackageContentDigest=""};
            page.Apply(new(new(descriptor.Id,descriptor.RuntimeGeneration,descriptor.PresentationGeneration,1,snapshot.WidgetInstanceId,1,snapshot.ActiveInputScopeId),descriptor,snapshot,styles));
            await Wait(()=>Find<Button>("Widget.gallery.source",root) is {IsLoaded:true});
            page.Enter(restoreNativeFocus:false);Find<Button>("Widget.gallery.source",root)!.Focus(FocusState.Keyboard);
            await Wait(()=>page.IsFocusPreviewOpen&&Find<MediaPlayerView>("Widget.gallery.media",root)?.CanControl==true);
            var player=Find<MediaPlayerView>("Widget.gallery.media",root)!;
            await Button(ControllerButton.A);
            await Wait(()=>page.IsPreviewEnteredForValidation);
            var canvas=Find<ScrollViewer>("MediaPlayer.Image.Viewport",root)!;
            Check(canvas.Focus(FocusState.Keyboard),"Entered gallery focuses the image canvas");
            gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Button(ControllerButton.RightBumper);
            await Wait(()=>player.SelectedGalleryItemId=="b"&&!player.CanControl);
            Check(page.IsPreviewEnteredForValidation&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),player),
                "RB retains the real entered preview and parks focus during delayed attachment replacement");
            await Button(ControllerButton.LeftBumper);
            await Wait(()=>player.SelectedGalleryItemId=="a"&&player.CanControl);
            Check(page.IsPreviewEnteredForValidation&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),canvas),
                "LB restores the previous attachment without closing the preview or focusing Call");
            gate.TrySetResult();gate=null;
            await Button(ControllerButton.LeftBumper);
            Check(page.IsPreviewEnteredForValidation&&player.SelectedGalleryItemId=="a","LB at the first attachment is consumed by the preview");
            await Button(ControllerButton.RightBumper);await Wait(()=>player.SelectedGalleryItemId=="b"&&player.CanControl);
            await Button(ControllerButton.RightBumper);
            Check(page.IsPreviewEnteredForValidation&&player.SelectedGalleryItemId=="b","RB at the last attachment is consumed by the preview");
            foreach(var (width,textScale) in new[]{(800d,1d),(420d,1d),(800d,1.5d),(420d,1.5d)})
            {
                page.ApplyAppearance(appearance with{TextScale=textScale},false);
                page.Width=width;root.Width=width+20;page.UpdateLayout();
                await Wait(()=>page.FocusPreviewFrame is {} frame&&player.ActualWidth>100&&
                    Math.Abs(player.ActualWidth/player.ActualHeight-16d/9)<.02);
                var frame=page.FocusPreviewFrame!;
                var bounds=player.TransformToVisual(frame.Body).TransformBounds(new(0,0,player.ActualWidth,player.ActualHeight));
                Check(bounds.Bottom<=frame.Body.ActualHeight+1&&bounds.Right<=frame.Body.ActualWidth+1,
                    width+" DIP widget at text scale "+textScale+" keeps the fitted gallery entirely inside the preview body");
                Check(!Walk(frame.Body).OfType<ScrollViewer>().Any(scroll=>!ReferenceEquals(scroll,canvas)&&scroll.ScrollableHeight>1),
                    width+" DIP media-only preview at text scale "+textScale+" has no redundant vertical scrolling");
            }
            Find<Button>("Widget.gallery.call",root)!.Focus(FocusState.Keyboard);
            await Wait(()=>!page.IsPreviewEnteredForValidation);
            Check(true,"Deliberately leaving the preview still retires its input ownership");
        }
        catch(Exception error){failure=error;}
        finally{gate?.TrySetResult();await page.DisposeAsync();host.Children.Remove(root);}
        await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{passed=failure is null,checks,error=failure?.ToString(),scope="Real native preview/input presenter with synthetic local media resolver; no account or network."}));
        if(failure is not null)throw failure;
        MediaPlayerGalleryItem Item(char id)=>new(id.ToString(),"Image "+id,MediaPlayerSource.FromCapture(new(new string(id,32),"image/png",320,180,new FileInfo(imagePath).Length,0,long.MaxValue)));
        async Task Button(ControllerButton button){await page.HandleControllerButtonAsync(button);await page.HandleControllerButtonAsync(button,ControllerEventPhase.Released);}
        static IEnumerable<DependencyObject> Walk(DependencyObject node){yield return node;for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,i)))yield return child;}
    }
}
