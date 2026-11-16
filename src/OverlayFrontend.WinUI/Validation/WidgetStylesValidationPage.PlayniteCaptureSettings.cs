using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task PlayniteCaptureSettingsAsync(string path)
    {
        Exception? failure=null;
        try
        {
            using var file=JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            var snapshot=SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(file.RootElement.GetProperty("snapshot").GetRawText()));
            var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var styles=file.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string,BridgeNodeRenderStyles>>(options)!;
            App.Window.AppWindow.ResizeClient(new(1400,1000));
            host.Children.Clear();
            var layout=new Grid{Margin=new(16)};
            layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
            layout.Children.Add(status);layout.Children.Add(presenter);Grid.SetRow(presenter,1);Content=layout;
            presenter.ApplyAppearance(AppearanceSettings.Default,false);
            var descriptor=new BridgeWidgetDescriptor{Id="playnite.capture-settings",Name="Playnite capture settings",InstanceId=snapshot.WidgetInstanceId,
                RuntimeGeneration="capture-fixture",PresentationGeneration="capture-fixture",Icon=WidgetGlyph.Play,PackageContentDigest=""};
            string? action=null;presenter.DispatchActionAsync=request=>{action=request.Action.ActionId;return Task.CompletedTask;};
            presenter.Apply(new(new(descriptor.Id,descriptor.RuntimeGeneration,descriptor.PresentationGeneration,1,descriptor.InstanceId,snapshot.Sequence,snapshot.ActiveInputScopeId),descriptor,snapshot,styles));
            const string prefix="Widget.playnite-library.captures.";
            await Wait(()=>Find<WidgetModalPanel>(prefix+"modal") is {IsLoaded:true,ActualWidth:>0}&&Find<Button>(prefix+"modal.close") is {IsLoaded:true});
            var close=Find<Button>(prefix+"modal.close")!;var scroll=Find<ScrollViewer>(prefix+"modal.scroll")!;
            Check(close.Focus(FocusState.Keyboard),"Capture settings close button is a valid native focus target");
            Check(scroll.ScrollableHeight>0&&scroll.ViewportHeight>200,"Long capture settings use one bounded scroll viewport");
            Check(scroll.ExtentWidth<=scroll.ViewportWidth+1,"Capture settings have no horizontal overflow");
            foreach(var id in new[]{"screenshotsEnabled","screenshotClean","screenshotWithToast","screenshotFramed","recordingsEnabled","recordingResolution","recordingQuality","recordingAudio","recordingMicrophone"})
            {
                var control=Find<FrameworkElement>(prefix+id)!;
                Check(control.ActualWidth>30&&control.ActualHeight>=32,"Capture option has a usable target: "+id);
                var bounds=control.TransformToVisual(scroll).TransformBounds(new(0,0,control.ActualWidth,control.ActualHeight));
                Check(bounds.Left>=-1&&bounds.Right<=scroll.ViewportWidth+1,"Capture option fits viewport: "+id);
            }
            await presenter.HandleControllerButtonAsync(ControllerButton.A,origin:WidgetRail.WidgetSdk.ControllerInputOrigin.AccessibilityAutomation);
            await Wait(()=>action=="playnite-library.captures.close");
            await presenter.HandleControllerButtonAsync(ControllerButton.A,ControllerEventPhase.Released,WidgetRail.WidgetSdk.ControllerInputOrigin.AccessibilityAutomation);
            Check(true,"Controller A on Close requests capture settings dismissal");
            action=null;await NativeContextProbeInput.LeftClickAsync(close,new(close.ActualWidth/2,close.ActualHeight/2));
            await Wait(()=>action=="playnite-library.captures.close");
            Check(true,"Mouse close requests capture settings dismissal");
            action=null;NativeContextProbeInput.Escape();await Wait(()=>action=="playnite-library.captures.close");
            Check(true,"Keyboard Escape requests capture settings dismissal");
            App.Window.AppWindow.ResizeClient(new(960,720));await Task.Delay(250);
            Check(scroll.ViewportWidth>200&&scroll.ExtentWidth<=scroll.ViewportWidth+1,"Capture settings remain bounded at a narrower window");
            foreach(var id in new[]{"screenshotWithToast","recordingAudio","recordingMicrophone","recordingAudioSource"})
            {
                var control=Find<FrameworkElement>(prefix+id)!;
                var bounds=control.TransformToVisual(scroll).TransformBounds(new(0,0,control.ActualWidth,control.ActualHeight));
                Check(bounds.Left>=-1&&bounds.Right<=scroll.ViewportWidth+1,"Responsive capture option fits narrow viewport: "+id);
            }
        }
        catch(Exception error){failure=error;}
        await File.WriteAllTextAsync(path+".native.json",JsonSerializer.Serialize(new{passed=failure is null,checks,error=failure?.ToString()},new JsonSerializerOptions{WriteIndented=true}));
        if(failure is not null)throw failure;
    }
}
