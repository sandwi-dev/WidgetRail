using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class FocusPolicyValidationPage
{
    private bool publicationValidationStarted;
    private async Task ValidatePublicationContinuityAsync(string path)
    {
        if(publicationValidationStarted)return;publicationValidationStarted=true;
        var checks=new List<string>();var traces=new List<string>();string? error=null;var step="foreground";
        presenter.FocusTrace=message=>{if(traces.Count<500)traces.Add(message);};
        try
        {
            App.Window!.Activate();
            await Until(OwnsForeground,60000);
            Check(true,"Generic native focus fixture owns Windows foreground");
            step="consecutive publications";
            await Prepare();
            Reparent();Apply();
            await Until(()=>FocusId()=="Widget.second");
            Check(true,"Two consecutive publications preserve the exact pending native focus target");

            step="partial subtree reparent";
            await Prepare(partial:true);Reparent();Apply();
            await Until(()=>FocusId()=="Widget.second");
            Check(true,"Partial subtree reparent preserves the target while sibling controls remain loaded");

            step="outside native focus";
            await Prepare();Reparent();
            Check(outside.Focus(FocusState.Keyboard),"Host control can take native focus");
            Apply();await Settle();
            Check(FocusId()=="FocusPolicy.Outside","Intervening publication does not steal actual host focus");

            step="outside focus after publication";
            await Prepare();Reparent();Apply();
            Check(outside.Focus(FocusState.Keyboard),"Host control can take focus after the final publication");
            await Settle();
            Check(FocusId()=="FocusPolicy.Outside","Queued callback does not steal a later native host focus choice");

            step="explicit direction";
            await Prepare();Reparent();Apply();
            presenter.MoveFocus(FocusNavigationDirection.Down);
            var selected=FocusId();
            Check(presenter.PendingNativeRestoreForValidation is null,"Directional input cancels the old pending restoration");
            Apply();await Settle();
            Check(FocusId()==selected,"Publication cannot replay restoration after explicit directional intent");

            step="retired target";
            await Prepare();Reparent();publicationRetired=true;Apply();
            await Until(()=>FocusId()=="Widget.header");
            Check(presenter.PendingNativeRestoreForValidation is null,"Retiring the pending target uses valid initial focus instead");

            step="new group entry";
            await Prepare();Reparent();exactTarget="third";request=focusSource.Request().Id;Apply();
            await Until(()=>FocusId()=="Widget.third");
            Check(presenter.PendingNativeRestoreForValidation is null,"A newer exact group-entry request wins over pending restoration");

            step="new scope";
            await Prepare();Reparent();publicationScope="replacement-scope";Apply();
            await Until(()=>FocusId()=="Widget.header");
            Check(presenter.PendingNativeRestoreForValidation is null,"Navigation into a new input scope retires old pending focus authority");

            step="same-owner scope return";
            await Prepare();publicationScope="detail-scope";Apply();
            await Until(()=>FocusId()=="Widget.header");
            publicationScope="page";Apply();Apply();
            await Until(()=>FocusId()=="Widget.second");
            Check(true,"Returning to a remembered scope restores its exact control after native subtree replacement");

            step="scope return explicit host departure";
            await Prepare();publicationScope="detail-scope";Apply();
            await Until(()=>FocusId()=="Widget.header");
            publicationScope="page";Apply();
            Check(outside.Focus(FocusState.Keyboard),"A host control can take focus during scope return");
            await Settle();
            Check(FocusId()=="FocusPolicy.Outside","Scope-return entry does not replace an explicit host focus choice");

            step="loading-to-ready scope return";
            await Prepare();publicationScope="loading-scope";Apply();
            await Until(()=>FocusId()=="Widget.header");
            ready=false;publicationScope="page";Apply();
            ready=true;Apply();
            await Until(()=>FocusId()=="Widget.second");
            Check(true,"Loading-to-ready replacement rebases pending entry to the remembered control");

            step="loading-to-ready explicit outside choice";
            await Prepare();publicationScope="loading-scope";Apply();
            await Until(()=>FocusId()=="Widget.header");
            ready=false;publicationScope="page";Apply();
            Check(outside.Focus(FocusState.Keyboard),"Host focus is admitted before a loading-to-ready publication");
            ready=true;Apply();await Settle();
            Check(FocusId()=="FocusPolicy.Outside","A ready publication preserves the outside focus choice from the loading state");

            step="targetless loading explicit outside choice";
            await Prepare();publicationScope="loading-scope";Apply();
            await Until(()=>FocusId()=="Widget.header");
            ready=false;publicationHeaderDisabled=true;publicationScope="page";Apply();
            Check(outside.Focus(FocusState.Programmatic),"Automation focus is admitted while the new scope has no navigable target");
            ready=true;publicationHeaderDisabled=false;Apply();await Settle();
            Check(FocusId()=="FocusPolicy.Outside","Ready content does not adopt a later outside focus as its initial native fallback");

            step="completed keyboard scope return";
            await Prepare();publicationScope="keyboard-scope";Apply();
            await Until(()=>FocusId()=="Widget.header");
            WidgetTextEntryDialog? keyboard=null;
            keyboard=new(new ViewNode { Id="fixture.editor",Kind=ViewNodeKind.TextEntry,ActionId="fixture.editor",TextEntryValue="",TextEntryMaximumLength=32 },_=>
            {
                publicationScope="page";Apply();
                // Exercise the native recovery shape observed in Discord: a
                // completed keyboard still owns focus while the new scope's
                // first control receives a programmatic, non-directional move.
                var fallback=Descendants(presenter).OfType<Control>().Single(control=>AutomationProperties.GetAutomationId(control)=="Widget.header");
                Check(fallback.Focus(FocusState.Programmatic),"Native completed-keyboard recovery can propose its default control");
                keyboard!.Hide();
            });
            keyboard.BindRoot(XamlRoot);
            var keyboardLifetime=keyboard.ShowKeyboardAsync(presenter);
            try
            {
                await Until(()=>keyboard.IsLoaded&&FocusId().StartsWith("Widget.TextEntry.",StringComparison.Ordinal));
                keyboard.Handle(ControllerButton.RightTrigger,ControllerEventPhase.Pressed);
                await keyboardLifetime.WaitAsync(TimeSpan.FromSeconds(10));
                await Until(()=>FocusId()=="Widget.second");
                Check(true,"A completed floating keyboard cannot replace the intended scope-return control with native fallback");
                Check(traces.Any(trace=>trace.Contains("entry-completed-keyboard-recovery",StringComparison.Ordinal)),"Native recovery redirects before fallback can overwrite remembered scope focus");
            }
            finally{keyboard.Hide();await keyboardLifetime.WaitAsync(TimeSpan.FromSeconds(10));keyboard.Erase();}

            step="new owner";
            await Prepare();Reparent();++owner;Apply();
            await Until(()=>FocusId()=="Widget.header");
            Check(presenter.PendingNativeRestoreForValidation is null,"Worker owner replacement cannot inherit a pending native target");
            Check(OwnsForeground(),"Windows foreground remains with the generic fixture throughout");
        }
        catch(Exception failure){error=failure.ToString()+"; "+presenter.FocusDiagnostics();}
        finally
        {
            presenter.FocusTrace=null;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new{passed=error is null,step,checks,traces,error}));
            File.Move(path+".tmp",path,true);
        }
        async Task Prepare(bool partial=false)
        {
            ++owner;publicationScroll=false;publicationRetired=false;publicationPartial=partial;publicationHeaderDisabled=false;publicationScope="page";ready=true;request=0;exactTarget=null;
            Apply();await Until(()=>FocusId()=="Widget.header");
            await Until(()=>presenter.FocusHostTarget("second"));
            Check(FocusId()=="Widget.second","Scenario starts with an actual native second control: "+step);
        }
        void Reparent()
        {
            publicationScroll=true;Apply();
            Check(presenter.PendingNativeRestoreForValidation=="second","Structural publication queues the retained second control: "+step);
        }
        async Task Settle(){await Task.Delay(120);Check(OwnsForeground(),"Native foreground retained: "+step);}
        string FocusId()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused?AutomationProperties.GetAutomationId(focused):"";
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);++index)
                foreach(var child in Descendants(VisualTreeHelper.GetChild(root,index)))yield return child;
        }
        bool OwnsForeground()=>App.Window is {} window&&PublicationForegroundWindow()==WinRT.Interop.WindowNative.GetWindowHandle(window);
        async Task Until(Func<bool> condition,int milliseconds=10000)
        {
            var deadline=Environment.TickCount64+milliseconds;
            while(!condition())
            {
                if(Environment.TickCount64>deadline)throw new TimeoutException($"Focus publication fixture: {step}; focus={FocusId()}");
                await Task.Delay(20);
            }
        }
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);}
    }
    [LibraryImport("user32.dll",EntryPoint="GetForegroundWindow"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint PublicationForegroundWindow();
}
