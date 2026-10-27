using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

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
            ++owner;publicationScroll=false;publicationRetired=false;publicationPartial=partial;publicationScope="page";ready=true;request=0;exactTarget=null;
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
