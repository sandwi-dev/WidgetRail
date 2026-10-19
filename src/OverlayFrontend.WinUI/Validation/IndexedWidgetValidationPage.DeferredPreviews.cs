using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ValidateDeferredPreviewsAsync(string path)
    {
        var checks=new List<string>();Exception? failed=null;
        var theme=new NativePopupTheme();theme.Attach(presenter);
        var layout=(Grid)Content;Content=null;var overlayRoot=new Grid();overlayRoot.Children.Add(layout);Content=overlayRoot;PresentationLayers.Attach(overlayRoot);
        try
        {
            await Send("deferred-mode");await Until(()=>View()?.Items.Count>2&&View()!.Items[1] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true }&&View()!.ContainerFromIndex(1) is Control {IsEnabled:true});
            var parent=Descendants(presenter).OfType<Button>().Single(button=>AutomationProperties.GetAutomationId(button)=="Widget.parent");
            parent.Focus(FocusState.Keyboard);await Task.Delay(180);
            Check(Reads()==0,"Unfocused rows construct no previews");
            var first=(Control)View()!.ContainerFromIndex(0);Check(first.Focus(FocusState.Keyboard),"First row accepts focus");
            await Until(()=>Reads()==1);var second=(Control)View()!.ContainerFromIndex(1);second.Focus(FocusState.Keyboard);
            await Until(()=>presenter.IsFocusPreviewOpen&&PreviewText()=="item.1");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),second),"Resolved preview does not steal row focus");
            await Task.Delay(450);Check(PreviewText()=="item.1","Superseded slow result never replaces the current item");
            var reads=Reads();var chrome=presenter.FocusPreviewChrome;
            await presenter.HandleControllerButtonAsync(ControllerButton.X);
            await Until(()=>AutomationProperties.GetAutomationId(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject)=="Widget.preview.apply");
            Check(ReferenceEquals(chrome,presenter.FocusPreviewChrome)&&Reads()==reads,"X enters the existing panel without another read");
            presenter.ActivateFocused();await Until(()=>FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"status")?.Text?.Contains("item.1:preview-apply")==true);
            Check(true,"Deferred preview action reaches its captured item through the real worker");
            await presenter.HandleControllerButtonAsync(ControllerButton.B);Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),second),"B returns to the exact indexed occurrence");
            var third=(Control)View()!.ContainerFromIndex(2);third.Focus(FocusState.Keyboard);await Until(()=>PreviewText()=="item.2");
            var afterThird=Reads();second.Focus(FocusState.Keyboard);await Until(()=>PreviewText()=="item.1");
            Check(Reads()==afterThird,"Revisiting a current key uses the bounded cache");
            await Send("deferred-replace");await Until(()=>!presenter.IsFocusPreviewOpen||PreviewContext()=="Context 2");
            second=(Control)View()!.ContainerFromIndex(1);second.Focus(FocusState.Keyboard);await Until(()=>PreviewText()=="item.1"&&PreviewContext()=="Context 2");
            Check(Reads()>afterThird,"Context replacement retires cached metadata");
            await presenter.SetPresentationActiveAsync(false);Check(!presenter.IsFocusPreviewOpen,"Suspension hides and retires preview content");
            await presenter.SetPresentationActiveAsync(true);
            await Send("deferred-readonly");await Until(()=>View()?.Items.Count>2&&View()!.Items[1] is IndexedItem<WidgetIndexedRow> { Value.Lease.IsCurrent: true }&&View()!.ContainerFromIndex(1) is Control {IsEnabled:true});
            second=(Control)View()!.ContainerFromIndex(1);second.Focus(FocusState.Keyboard);await Until(()=>presenter.IsFocusPreviewOpen&&PreviewText()=="item.1");
            Check(!Descendants(presenter.FocusPreviewChrome!).OfType<Button>().Any(),"Read-only resolved content has no action controls");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),second),"Read-only preview retains source focus");
            if(failure is not null)throw new Exception(failure);
        }
        catch(Exception error){failed=error;}
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=failed is null,checks,error=failed?.ToString()}));
        if(failed is not null)throw failed;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        int Reads()=>int.TryParse(FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"preview.reads")?.Text,out var value)?value:0;
        string? PreviewText()=>presenter.FocusPreviewChrome is {} chrome?Descendants(chrome).OfType<TextBlock>().FirstOrDefault(block=>AutomationProperties.GetAutomationId(block)=="Widget.preview.key")?.Text:null;
        string? PreviewContext()=>presenter.FocusPreviewChrome is {} chrome?Descendants(chrome).OfType<TextBlock>().FirstOrDefault(block=>AutomationProperties.GetAutomationId(block)=="Widget.preview.context")?.Text:null;
        async Task Send(string action)
        {
            var frame=session!.GetState("indexed-owned")!.LastGood!;var before=publication;
            await session.SendActionAsync(frame.Authority,new(action,"root",InputScopeId:"root"),lifetime.Token);await Until(()=>publication>before);
        }
        async Task Until(Func<bool> predicate)
        {var deadline=Environment.TickCount64+10000;while(!predicate()){if(Environment.TickCount64>deadline)throw new TimeoutException("Deferred preview did not settle: "+JsonSerializer.Serialize(presenter.FocusPreviewDiagnostics()));await Task.Delay(15,lifetime.Token);}}
    }
}
