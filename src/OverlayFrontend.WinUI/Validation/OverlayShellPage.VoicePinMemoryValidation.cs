using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableVoicePinMemoryValidation(string path,bool resume)
    {
        Loaded+=async(_,_)=>
        {
            var checks=new List<string>();
            try
            {
                if(startup is not null)await startup;
                await Until(()=>MainFocusEnabled&&HasCall());
                var widget=activeWidget!;
                if(resume)
                {
                    await Until(()=>VoicePins()==2);
                    Check(pins.All(p=>!p.Window.Interactive)&&!HostChoiceActive,
                        "A fresh host process restores both remembered voice pins without foreground/input or a chooser");
                    Check(pins.All(p=>!p.LayoutId.StartsWith("discord.stream",StringComparison.Ordinal)),"Shared-screen pins are not restored");
                }
                else
                {
                    Check(VoicePins()==0,"First use does not pin anything without an explicit user choice");
                    await PinAsync(widget,"discord.call");await PinAsync(widget,"discord.people");
                    Check(pinnedPreferences.Remembered.Count==2,"Explicit voice selections store two independent preferences");
                    await CheckRadialPinCommandAsync();
                    await Leave();
                    Check(VoicePins()==0&&pinnedPreferences.Remembered.Count==2,"Leaving closes both windows but retains the user's choices");
                    // Fill all six slots with ordinary authored pins from two
                    // isolated Gallery instances. Their default policy is unchanged.
                    foreach(var gallery in new[]{"memory-gallery-a","memory-gallery-b"})
                        foreach(var layout in new[]{"gallery.summary","gallery.readout","gallery.level"})await PinAsync(gallery,layout);
                    Check(pins.Count==6&&pinnedPreferences.Remembered.Count==2,"Ordinary widgets fill six slots without opting into restoration");
                    var others=pins.ToArray();
                    await Join();await Task.Delay(500);
                    Check(pins.Count==6&&VoicePins()==0&&!HostChoiceActive,"Rejoin at capacity neither replaces pins nor opens a chooser");
                    await ManagePinAsync(others[5],()=>true);
                    await UnpinAsync(true,others[0]);await Task.Delay(250);
                    Check(PinnedAdjustmentActive&&VoicePins()==0,"Freeing a slot during adjustment defers restoration until that interaction ends");
                    CancelPinnedAdjustment();await Until(()=>VoicePins()==1&&pins.Count==6);
                    NativePickerVisibilityForValidation!(false);
                    await Until(()=>!App.Window!.AppWindow.IsVisible);
                    var externalForeground=PinnedNativeForeground();
                    await UnpinAsync(true,others[1]);await Until(()=>VoicePins()==2&&pins.Count==6);
                    Check(!visible&&PinnedNativeForeground()==externalForeground&&pins.All(p=>!p.Window.Interactive)&&
                        pins.Where(p=>p.LayoutId is "discord.call" or "discord.people").All(p=>p.Window.IsVisible),
                        "Remembered voice windows reopen while the overlay is hidden without stealing foreground or content input");
                    NativePickerVisibilityForValidation(true);await Until(()=>MainFocusEnabled);
                    Check(others.Skip(2).All(p=>pins.Contains(p)&&p.IsCurrent),"Deferred voice pins use newly freed slots without disturbing surviving pins");
                    foreach(var other in others.Skip(2))await UnpinAsync(true,other);
                    await UnpinAsync(true,pins.Single(p=>p.LayoutId=="discord.people"));
                    await Leave();await Join();await Until(()=>VoicePins()==1);
                    Check(pins.Single().LayoutId=="discord.call"&&pinnedPreferences.Remembered.Count==1,
                        "Explicitly unpinning participants prevents only that pin from reopening");
                    await UnpinAsync(true,pins.Single());await Leave();await Join();await Task.Delay(400);
                    Check(pins.Count==0&&pinnedPreferences.Remembered.Count==0,"Explicitly unpinning both keeps both closed on subsequent calls");
                    await PinAsync(widget,"discord.call");await PinAsync(widget,"discord.people");
                    await Leave();
                    var stored=await new PinnedPreferencesStore(options.SettingsRoot).LoadAsync();
                    Check(stored.Remembered.Count==2&&stored.Remembered.All(p=>p.WidgetId==widget),
                        "Disconnected preferences survive disk round-trip without remembering Gallery or shared screens");
                }
                File.WriteAllText(path,JsonSerializer.Serialize(new{passed=true,checks}));
            }
            catch(Exception error){File.WriteAllText(path,JsonSerializer.Serialize(new{passed=false,checks,error=error.ToString(),pins=pins.Select(p=>p.LayoutId).ToArray(),
                text=surface is null?[]:ChoiceDescendants(surface).OfType<TextBlock>().Select(t=>t.Text).Where(t=>!string.IsNullOrWhiteSpace(t)).Take(40).ToArray()}));}

            int VoicePins()=>pins.Count(p=>p.LayoutId is "discord.call" or "discord.people");
            async Task CheckRadialPinCommandAsync()
            {
                await Until(()=>MainFocusEnabled&&!switching&&ManagePinsButton.IsEnabled);
                var disabled=false;
                var changed=ManagePinsButton.RegisterPropertyChangedCallback(Control.IsEnabledProperty,
                    (_,_)=>disabled|=!ManagePinsButton.IsEnabled);
                try
                {
                    for(var cycle=0;cycle<3;cycle++)
                    {
                        await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Pressed);
                        await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Released);
                        await Until(()=>RadialOpen&&!switching);
                        var returning=RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Pressed);
                        Check(switching,"Radial return exercises pending widget admission");
                        await ManageOpenPinsAsync();
                        Check(!openingPinManagement&&!HostChoiceActive&&!PinnedAdjustmentActive,
                            "Manage pins remains blocked during widget admission");
                        await returning;
                        await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Released);
                        await Until(()=>MainFocusEnabled&&!switching&&!RadialOpen);
                    }
                    Check(!disabled&&ManagePinsButton.IsEnabled,
                        "Manage pins never flashes its disabled state across three radial B return cycles");
                }
                finally{ManagePinsButton.UnregisterPropertyChangedCallback(Control.IsEnabledProperty,changed);}
            }
            bool HasCall()=>surface?.CurrentBinding?.Frame.Snapshot.PinnedLayouts.Any(p=>p.Id=="discord.call")==true;
            async Task Leave()
            {
                await Until(()=>MainFocusEnabled&&HasCall());
                if(!surface!.FocusHostTarget("discord.main.leave"))throw new InvalidOperationException("Cannot focus Leave");
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await Until(()=>!HasCall()&&VoicePins()==0);
            }
            async Task Join()
            {
                WidgetIndexedCollectionView? Collection()=>ChoiceDescendants(surface!).OfType<WidgetIndexedCollectionView>()
                    .SingleOrDefault(c=>AutomationProperties.GetAutomationId(c)=="Widget.discord.channels");
                await Until(()=>MainFocusEnabled&&Collection()?.NativeView.Items.Count>1);
                var list=Collection()!;
                list.NativeView.ScrollIntoView(list.NativeView.Items[1]);
                await Until(()=>list.NativeView.ContainerFromIndex(1) is Control{IsLoaded:true});
                await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,
                    (Control)list.NativeView.ContainerFromIndex(1),coordinateRoot:ProductionRoot);
                await Until(()=>HasCall());
            }
            async Task Until(Func<bool> condition)
            {
                var end=Environment.TickCount64+20000;
                while(!condition())
                {
                    if(RecoveryVisible||validationFailure is not null)throw new InvalidOperationException("Voice memory fixture entered recovery",validationFailure);
                    if(Environment.TickCount64>end)throw new TimeoutException("Voice memory did not settle: "+string.Join(",",pins.Select(p=>p.LayoutId)));
                    await Task.Delay(25);
                }
            }
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        };
    }
}
