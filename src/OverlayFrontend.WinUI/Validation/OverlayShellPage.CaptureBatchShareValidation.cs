using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Explicit live-candidate smoke: real indexed selection, broker file custody
    // and host intent delivery. Never selects a recipient or invokes Send.
    internal void EnableCaptureBatchShareValidation(string output)
    {
        var started=false;
        Loaded+=async(_,_)=>
        {
            if(started)return;started=true;
            const string captureId="widgetrail.firstparty.capture-library",discordId="widgetrail.samples.discord-controls";
            var checks=new List<string>();var step="startup";Exception? failure=null;string? cleanupFailure=null;object? failedState=null;
            var ownsSelection=false;var shareStarted=false;string? ownedCancel=null;Task? invocation=null;
            try
            {
                if(startup is not null)await startup;
                if(owner is null || !catalogItems.Any(item=>item.Id==captureId) || !catalogItems.Any(item=>item.Id==discordId))
                    throw new InvalidOperationException("Both production widgets must be installed for live batch-share validation.");
                if(HostChoiceActive || SystemFilePickerOpen || intentCancellation is not null)
                    throw new InvalidOperationException("Another host interaction is active; live sharing smoke was not started.");
                await SelectAsync(captureId,true);
                await Until(()=>MainFocusEnabled && Node("capture-library.entries")?.IndexedCollection is {Count:>=2});
                App.Window!.Activate();await Until(()=>App.Window is MainWindow { ValidationOwnsForeground: true });
                var selection=Node("capture-library.entries")!.IndexedSelection;
                if(selection is {Count:>0})throw new InvalidOperationException("Existing user selection was preserved; smoke was not started.");
                if(selection?.IsActive!=true)await Activate("capture-library.selection.toggle");
                ownsSelection=true;
                await Until(()=>Node("capture-library.entries")?.IndexedSelection is {IsActive:true,Count:0});
                Check(true,"Real Capture Library selection mode starts without replacing an existing selection");

                step="select two available indexed captures";
                var count=Node("capture-library.entries")!.IndexedCollection!.Count;
                for(var index=0;index<Math.Min(count,16) && Node("capture-library.entries")!.IndexedSelection!.Count<2;index++)
                {
                    var collection=Collection();var slot=(IndexedItem<WidgetIndexedRow>)collection.NativeView.Items[index];
                    collection.NativeView.ScrollIntoView(slot,ScrollIntoViewAlignment.Leading);
                    await Until(()=>slot.Value is {Lease.IsCurrent:true});
                    var row=slot.Value!;
                    // Available gallery items declare scoped artwork; unavailable
                    // placeholders do not. Preparation independently revalidates
                    // the actual capture revision/availability in the broker.
                    if(!Nodes(row.Item.Root).Any(node=>node.ArtworkHandle is not null))continue;
                    var previous=Node("capture-library.entries")!.IndexedSelection!.Count;
                    await collection.ToggleSelectionAsync(row);
                    await Until(()=>Node("capture-library.entries")?.IndexedSelection?.Count==previous+1);
                }
                Check(Node("capture-library.entries")?.IndexedSelection?.Count==2,"Two real available captures are selected through their current indexed leases");
                var share=Node("capture-library.batch.share");
                Check(share is {IsDisabled:not true,Intent:not null},"The production batch Share control declares the standard host intent");
                Check(share!.Intent!.ContractId==WidgetIntentContracts.FileShare,"Batch sharing uses the generic file-share contract");
                Check(surface!.FocusHostTarget("capture-library.batch.share"),"Batch Share is natively controller focusable");
                await Until(()=>FocusId()=="Widget.capture-library.batch.share");
                step="actual host intent delivery";shareStarted=true;
                invocation=Press(ControllerButton.A);
                await Until(()=>activeWidget==discordId && Node("discord.messages.cancel-share") is not null || hostChoiceDialog is {IsOpenForValidation:true});
                if(hostChoiceDialog is {IsOpenForValidation:true} choice)
                {
                    if(!choice.ChoiceIdsForValidation.Contains(discordId))throw new InvalidOperationException("Discord is unavailable or the share needs another decision; no recipient was selected.");
                    choice.CompleteChoice(discordId);
                    Check(true,"The opened host handler chooser resolves only to the production Discord widget");
                }
                await Until(()=>activeWidget==discordId && Node("discord.messages.cancel-share") is not null && intentCancellation is null);
                await invocation.WaitAsync(TimeSpan.FromSeconds(10));
                ownedCancel=Node("discord.messages.cancel-share")!.ActionId;
                await Until(()=>Node("discord.messages.page") is not null);
                ownedCancel=Node("discord.messages.cancel-share")!.ActionId;
                Check(Node("discord.messages.page") is not null && Node("discord.message.draft") is null && Node("discord.message.send") is null,
                    "Actual Capture-to-Discord delivery opens recipient selection without a composer or Send action");
                Check(Node("discord.messages.notice")?.Text=="Choose a recipient for 2 shared file(s).",
                    "The production recipient picker reports exactly two shared files");
                step="cancel unsent share";
                await Activate("discord.messages.cancel-share");
                await Until(()=>Node("discord.messages.cancel-share") is null);
                ownedCancel=null;
                Check(true,"Cancel sharing clears the unsent batch without selecting a contact or uploading");
            }
            catch(Exception error)
            {
                failure=error;failedState=new{activeWidget,requestedWidget,preparingIntent,intentActive=intentCancellation is not null,
                    choiceOpen=hostChoiceDialog?.IsOpenForValidation,choices=hostChoiceDialog?.ChoiceIdsForValidation,
                    invocationCompleted=invocation?.IsCompleted,recipientPicker=Node("discord.messages.cancel-share") is not null,
                    selectionCount=Node("capture-library.entries")?.IndexedSelection?.Count};
            }
            finally
            {
                try
                {
                    if(shareStarted && intentCancellation is {} pending)
                    {pending.Cancel();if(hostChoiceDialog is {} choice)choice.Hide();}
                    if(invocation is not null)try{await invocation.WaitAsync(TimeSpan.FromSeconds(10));}catch(Exception){}
                    if(ownedCancel is not null && activeWidget==discordId && Node("discord.messages.cancel-share")?.ActionId==ownedCancel)
                    {await Activate("discord.messages.cancel-share");await Until(()=>Node("discord.messages.cancel-share") is null);}
                    if(ownsSelection)
                    {
                        await SelectAsync(captureId,true);await Until(()=>MainFocusEnabled && Node("capture-library.selection.toggle") is not null);
                        if(Node("capture-library.entries")?.IndexedSelection?.IsActive==true)await Activate("capture-library.selection.toggle");
                        await Until(()=>Node("capture-library.entries")?.IndexedSelection is {IsActive:false,Count:0});
                        Check(true,"Smoke returns to Capture Library and ends its temporary selection");
                    }
                }
                catch(Exception error){cleanupFailure=error.ToString();}
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=failure is null && cleanupFailure is null,checks,step,
                    error=failure?.ToString(),cleanupFailure,failedState,activeWidget,shareStarted,recipientSelected=false,sendInvoked=false}));
            }

            ViewNode? Node(string id)=>surface?.CurrentBinding is {} binding?Nodes(binding.View.Root).FirstOrDefault(node=>node.Id==id):null;
            WidgetIndexedCollectionView Collection()=>Walk(surface!).OfType<WidgetIndexedCollectionView>()
                .Single(collection=>AutomationProperties.GetAutomationId(collection)=="Widget.capture-library.entries");
            string FocusId()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focus?AutomationProperties.GetAutomationId(focus):"";
            async Task Activate(string id)
            {
                await Until(()=>MainFocusEnabled && Node(id) is {IsDisabled:not true});
                if(!surface!.FocusHostTarget(id))throw new InvalidOperationException("Control not focusable: "+id);
                await Until(()=>FocusId()=="Widget."+id);await Press(ControllerButton.A);
            }
            async Task Press(ControllerButton button)
            {await RouteButtonAsync(button,ControllerEventPhase.Pressed);await RouteButtonAsync(button,ControllerEventPhase.Released);}
            async Task Until(Func<bool> condition)
            {
                var deadline=Environment.TickCount64+20000;
                while(!condition())
                {
                    if(Environment.TickCount64>deadline)throw new TimeoutException("Batch share smoke did not settle: "+step);
                    await Task.Delay(20,lifetime.Token);
                }
            }
            void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);}
        };
        static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
        static IEnumerable<DependencyObject> Walk(DependencyObject node)
        {yield return node;for(var index=0;index<VisualTreeHelper.GetChildrenCount(node);index++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,index)))yield return child;}
    }
}
