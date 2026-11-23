using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ValidatePreferredEntryAsync(string path)
    {
        var checks=new List<string>();Exception? error=null;
        try
        {
            await Wait(()=>View() is {IsLoaded:true} && App.Window is MainWindow{ValidationOwnsForeground:true});
            await Send("preferred-start");
            Parent().Focus(FocusState.Keyboard);
            NativeContextProbeInput.PressKey(VirtualKey.Down);
            await Wait(()=>Collection().CaptureFocusedItem()?.Index==7500);
            Check(true,"Native keyboard entry locates the preferred offscreen occurrence");
            var query=Query();
            Check(Collection().Enter(new("items",query.SourceId,query.QueryGeneration,"item.10",10)),"Explicit exact entry is admitted");
            await Wait(()=>Collection().CaptureFocusedItem()?.Index==10);
            Check(true,"Explicit exact entry is not overridden by the preferred occurrence");
            Parent().Focus(FocusState.Keyboard);
            Check(presenter.MoveFocus(FocusNavigationDirection.Down),"Controller navigation enters the collection");
            await Wait(()=>Collection().CaptureFocusedItem()?.Index==7500);
            Check(true,"Preferred entry outranks an unrelated remembered row");
            await Send("preferred-missing");Parent().Focus(FocusState.Keyboard);
            NativeContextProbeInput.PressKey(VirtualKey.Down);
            await Wait(()=>Collection().CaptureFocusedItem()?.Index==7500 && !Collection().IsEntryPending);
            Check(true,"Missing preferred occurrence falls back to remembered entry");

            foreach(var cancellation in new[]{"departure","change","replacement","hidden"})
            {
                Parent().Focus(FocusState.Keyboard);await Send("preferred-delay");
                Parent().Focus(FocusState.Keyboard);NativeContextProbeInput.PressKey(VirtualKey.Down);
                await Wait(()=>FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"background-state")?.Text=="preferred-lookup-waiting");
                Check(Collection().IsEntryPending,"Delayed preferred lookup owns pending entry: "+cancellation);
                if(cancellation=="hidden")await presenter.SetPresentationActiveAsync(false);
                else if(cancellation=="change")await Send("preferred-change");
                else if(cancellation=="replacement")await Send("preferred-start");
                Parent().Focus(FocusState.Keyboard);
                await Send("preferred-release");
                await Wait(()=>!Collection().IsEntryPending);
                await Task.Delay(150,lifetime.Token);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Parent()),"Late preferred lookup cannot acquire focus after "+cancellation);
                if(cancellation=="hidden")await presenter.SetPresentationActiveAsync(true);
            }
            Check(failure is null,"No presenter failure during preferred entry");
        }
        catch(Exception failure){error=failure;}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=error is null,checks,error=error?.ToString()}));

        WidgetIndexedCollectionView Collection()=>Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single();
        Control Parent()=>Descendants(presenter).OfType<Control>().Single(control=>AutomationProperties.GetAutomationId(control)=="Widget.parent");
        IndexedCollectionDescriptor Query()=>FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root,"items")!.IndexedCollection!;
        async Task Send(string action)
        {
            var frame=session!.GetState("indexed-owned")!.LastGood!;
            await session.SendActionAsync(frame.Authority,new(action,"root",InputScopeId:"root"),lifetime.Token);
            await Wait(()=>Descendants(presenter).OfType<TextBlock>().Any(text=>AutomationProperties.GetAutomationId(text)=="Widget.status"&&text.Text==action));
        }
        async Task Wait(Func<bool> condition)
        {
            var deadline=Environment.TickCount64+12000;
            while(!condition())
            {
                if(failure is not null)throw new InvalidOperationException(failure);
                if(Environment.TickCount64>deadline)throw new TimeoutException(presenter.FocusDiagnostics());
                await Task.Delay(20,lifetime.Token);
            }
        }
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);}
    }
}
