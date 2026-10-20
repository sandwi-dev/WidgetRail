using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private Task PobItemInspectionAsync(string directory)=>PobPreviewMatrixAsync(directory,["inventory","unique"]);
    private Task PobReadonlyPreviewsAsync(string directory)=>PobPreviewMatrixAsync(directory,["skills","equipment"]);
    private async Task PobPreviewMatrixAsync(string directory,string[] kinds)
    {
        Exception? failure=null;
        var root=new Grid();var page=new WidgetViewPresenter();root.Children.Add(page);PresentationLayers.Attach(root);
        presenter.Visibility=Visibility.Collapsed;host.Children.Add(root);
        var popup=new NativePopupTheme();popup.Attach(page);
        var catalog=new ThemeCatalog(new PlatformSettingsPaths(directory));
        var package=WrssParser.Parse(File.ReadAllText(Path.Combine(directory,"pob.wrss")),"pob.wrss");
        var scale=XamlRoot.RasterizationScale;App.Window.AppWindow.Resize(new((int)(1480*scale),(int)(1100*scale)));
        page.Failed=error=>failure??=error;
        try
        {
            foreach(var themeId in new[]{ThemeIdentity.BuiltInMonoInk,ThemeIdentity.BuiltInMineralLight,"widgetrail.builtin.velvet-dusk","high-contrast"})
            foreach(var game in new[]{1,2})
            foreach(var kind in kinds)
            foreach(var size in new[]{(Width:1320d,Height:900d),(Width:820d,Height:600d)})
            {
                var scenario=$"poe{game}-{kind}-{themeId}-{size.Width}";
                root.Width=page.Width=size.Width;root.Height=page.Height=size.Height;
                var contrast=themeId=="high-contrast";WidgetViewPresenter.SetHighContrastStyleOverride(contrast);
                var appearance=AppearanceSettings.Default with{Motion=MotionPreference.Reduced,TextScale=contrast?1.25:1};
                page.ApplyAppearance(appearance,false);
                var theme=ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,new((WrssDocument[])[package.Document],[]),
                    catalog.Load(contrast?ThemeIdentity.BuiltInMonoInk:themeId,"1.0.0").Package).Theme!;
                var gemEffects=kind is "gem1" or "gem2";
                var readOnly=gemEffects||kind is "skills" or "equipment";
                var filename=gemEffects?$"poe{game}-{kind}.json":$"poe{game}-{kind}-"+(readOnly?"readonly":"inspection")+".json";
                var snapshot=SnapshotJson.Deserialize(File.ReadAllBytes(Path.Combine(directory,filename)));
                var row=readOnly?Nodes(snapshot.Root).First(node=>node.FocusPresentation is not null):JsonSerializer.Deserialize<ViewNode>(File.ReadAllBytes(Path.Combine(directory,$"poe{game}-{kind}-row.json")))!;
                row=row with{CollectionItemKey=null};
                var preview=row.FocusPresentation!;
                // The exact exported content is rendered in the real parent layout.
                // Only indexed materialization is substituted; lease admission has separate tests.
                ViewNode Materialize(ViewNode node)=>node.Kind==ViewNodeKind.IndexedCollection
                    ?new(){Id=node.Id,Kind=ViewNodeKind.Scroll,ScrollAxis=ScrollAxis.Vertical,StyleClasses=node.StyleClasses,Children=(ViewNode[])[row]}
                    :node with{Children=node.Children.Select(Materialize).ToArray()};
                snapshot=snapshot with{Root=Materialize(snapshot.Root),InitialFocusId=row.Id,FocusGroupEntryRequest=null,Sequence=1};
                var styles=new Dictionary<string,BridgeNodeRenderStyles>();
                void Visit(ViewNode node){styles[node.Id]=Compute(theme,node);foreach(var child in node.Children)Visit(child);
                    if(node.FocusPresentation is {} content)Visit(content);if(node.DefaultFocusPresentation is {} fallback)Visit(fallback);}
                Visit(snapshot.Root);foreach(var role in new[]{"body","canvas","panel","hint","tray-item"})styles[role]=Compute(theme,role,role,[]);
                popup.Update(styles,appearance);
                var descriptor=new BridgeWidgetDescriptor{Id="pob",Name="PoB preview",InstanceId=snapshot.WidgetInstanceId,
                    RuntimeGeneration=scenario,PresentationGeneration=scenario,Icon=WidgetGlyph.Settings,PackageContentDigest=""};
                var actions=0;page.DispatchActionAsync=_=>{actions++;return Task.CompletedTask;};
                page.Apply(new(new(descriptor.Id,scenario,scenario,1,snapshot.WidgetInstanceId,1,snapshot.ActiveInputScopeId),descriptor,snapshot,styles));
                page.UpdateLayout();
                await Wait(()=>Find<Button>("Widget."+row.Id,root) is {IsLoaded:true});
                var source=Find<Button>("Widget."+row.Id,root)!;source.Focus(FocusState.Keyboard);
                await Wait(()=>page.IsFocusPreviewOpen&&page.FocusPreviewChrome is {ActualHeight:>0});
                var chrome=page.FocusPreviewChrome!;
                if(readOnly)
                {
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),source)&&actions==0,scenario+": read-only preview preserves source focus");
                    Check(!Nodes(preview).Any(node=>node.ActionId is not null),scenario+": no duplicated editing controls");
                    var panelBounds=chrome.TransformToVisual(page).TransformBounds(new(0,0,chrome.ActualWidth,chrome.ActualHeight));
                    Check(panelBounds.Left>=-1&&panelBounds.Top>=-1&&panelBounds.Right<=page.ActualWidth+1&&panelBounds.Bottom<=page.ActualHeight+1,scenario+": read-only panel fits viewport");
                    if(game==2&&size.Width==1320&&themeId==ThemeIdentity.BuiltInMonoInk)await Capture(Path.Combine(directory,$"native-{kind}.png"));
                    if(gemEffects)
                    {
                        if(game==2&&kind=="gem1"&&size.Width==820&&themeId==ThemeIdentity.BuiltInMonoInk)
                            await Capture(Path.Combine(directory,"native-gem-compact.png"));
                        var optionId="pob.gem-options."+kind[3..];
                        var returned=snapshot with{Sequence=2,FocusGroupEntryRequest=new(){RequestId=checks.Count+1,GroupId=optionId+".return"}};
                        page.Apply(new(new(descriptor.Id,scenario,scenario,1,returned.WidgetInstanceId,2,returned.ActiveInputScopeId),descriptor,returned,styles));
                        await Wait(()=>Find<Button>("Widget."+optionId,root) is {} button&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),button));
                        Check(true,scenario+": return request focuses the exact Gem options button");
                        async Task Move(FocusNavigationDirection direction,string target)
                        {
                            page.MoveFocus(direction);
                            await Task.Delay(40);
                            var focused=FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                            var actual=focused is null?"none":Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused);
                            Check(actual=="Widget."+target,scenario+$": {direction} reaches {target} (actual {actual})");
                        }
                        var gemId=kind[3..];
                        await Move(FocusNavigationDirection.Down,"pob.gem-remove."+gemId);
                        await Move(FocusNavigationDirection.Left,"pob.gem-replace."+gemId);
                        await Move(FocusNavigationDirection.Up,"pob.gem."+gemId+"level");
                        await Move(FocusNavigationDirection.Right,"pob.gem."+gemId+"quality");
                        await Move(FocusNavigationDirection.Right,"pob.gem."+gemId+"count");
                        await Move(FocusNavigationDirection.Right,optionId);
                        await Move(FocusNavigationDirection.Left,"pob.gem."+gemId+"count");
                        await Move(FocusNavigationDirection.Left,"pob.gem."+gemId+"quality");
                        await Move(FocusNavigationDirection.Left,"pob.gem."+gemId+"level");
                        await Move(FocusNavigationDirection.Down,"pob.gem-replace."+gemId);
                        await Move(FocusNavigationDirection.Right,"pob.gem-remove."+gemId);
                        var more=Find<Button>("Widget.pob.skill-options",root)!;
                        more.Focus(FocusState.Keyboard);
                        more.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
                        await Task.Delay(40);
                        await Move(FocusNavigationDirection.Left,"pob.group-manage");
                        await Move(FocusNavigationDirection.Right,"pob.skill-options");
                        await Move(FocusNavigationDirection.Left,"pob.group-manage");
                        var groupRow=Nodes(snapshot.Root).Single(node=>node.Id=="pob.group-manage").Focus!.Left!;
                        await Move(FocusNavigationDirection.Left,groupRow);
                    }
                    continue;
                }
                var first=Nodes(preview).First(node=>node.Kind==ViewNodeKind.Button&&node.ActionId is not null);
                await Wait(()=>Find<Button>("Widget."+first.Id,root) is {IsLoaded:true});
                var action=Find<Button>("Widget."+first.Id,root)!;
                var scrollNode=Nodes(preview).Single(node=>node.Kind==ViewNodeKind.Scroll);
                var scroll=Find<ScrollViewer>("Widget."+scrollNode.Id,root)!;
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),source)&&actions==0,scenario+": full preview preserves source focus");
                var bounds=chrome.TransformToVisual(page).TransformBounds(new(0,0,chrome.ActualWidth,chrome.ActualHeight));
                Check(bounds.Left>=-1&&bounds.Top>=-1&&bounds.Right<=page.ActualWidth+1&&bounds.Bottom<=page.ActualHeight+1,scenario+": panel remains inside widget");
                var actionBounds=action.TransformToVisual(chrome).TransformBounds(new(0,0,action.ActualWidth,action.ActualHeight));
                Check(actionBounds.Width>0&&actionBounds.Height>0&&actionBounds.Top>=0&&actionBounds.Bottom<=chrome.ActualHeight,scenario+": action visible before entry");
                await page.HandleControllerButtonAsync(ControllerButton.X);
                await Wait(()=>ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),action));
                Check(ReferenceEquals(chrome,page.FocusPreviewChrome)&&ReferenceEquals(action,Find<Button>("Widget."+first.Id,root)),scenario+": X retains the same controls");
                scroll.ChangeView(null,scroll.ScrollableHeight,null,true);await Task.Delay(40);var offset=scroll.VerticalOffset;
                await page.HandleControllerButtonAsync(ControllerButton.B);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),source)&&ReferenceEquals(chrome,page.FocusPreviewChrome),scenario+": B returns without closing");
                Check(Math.Abs(scroll.VerticalOffset-offset)<1,scenario+": B retains reading position");
                await page.HandleControllerButtonAsync(ControllerButton.X);
                Check(Math.Abs(scroll.VerticalOffset-offset)<1&&actions==0,scenario+": re-entry neither scrolls nor dispatches");
                if(game==2&&size.Width==1320&&themeId==ThemeIdentity.BuiltInMonoInk)
                    await Capture(Path.Combine(directory,$"native-{kind}.png"));
                await page.HandleControllerButtonAsync(ControllerButton.B);
                page.ActivateFocused();Check(actions==1,scenario+": source A remains independent");
                if(failure is not null)throw failure;
            }
        }
        catch(Exception error){failure=error;await Capture(Path.Combine(directory,"native-failure.png"));}
        finally{WidgetViewPresenter.SetHighContrastStyleOverride(false);await page.DisposeAsync();host.Children.Remove(root);}
        await File.WriteAllTextAsync(Path.Combine(directory,"native-result.json"),JsonSerializer.Serialize(new{passed=failure is null,checks,error=failure?.ToString()}));
        if(failure is not null)throw failure;
        static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
        async Task Capture(string path)
        {
            var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(root);
            using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync();stream.Seek(0);using var output=File.Create(path);await stream.AsStreamForRead().CopyToAsync(output);
        }
    }
}
