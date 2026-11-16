using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private bool restoringRememberedPins,rememberedPinsQueued,rememberedPinsDirty;
    private OwnedBridgeProcess? rememberedAttemptOwner;
    private readonly Dictionary<RememberedPin,BridgeWorkerRun?> rememberedAttempts=[];
    private bool RememberedPinAdmission => !retired&&!switching&&!PinnedAdjustmentActive&&!HostChoiceActive&&
        !LocalInstallActive&&!SystemFilePickerOpen&&!nativeBrowserPickerActive&&!IsMediaFullscreen&&trayMenu is null&&!openingPinManagement;

    private static RememberedPin? RememberedIdentity(BridgeWidgetDescriptor descriptor,string layout) =>
        descriptor.PackageId is {} package&&descriptor.PublisherId is {} publisher&&
        new RememberedPin(descriptor.Id,package,publisher,descriptor.InstanceId,layout) is {IsValid:true} identity?identity:null;

    // Called only inside the existing pin-transition gate. Native selections and
    // worker generations never become persisted authority.
    private async Task RememberPinAsync(BridgeWidgetDescriptor descriptor,string layout,bool enabled)
    {
        if(RememberedIdentity(descriptor,layout) is not {} identity)return;
        if(enabled?pinnedPreferences.Remembered.Contains(identity):!pinnedPreferences.Remembered.Any(p=>p.WidgetId==descriptor.Id&&p.LayoutId==layout))return;
        var next=pinnedPreferences.WithRemembered(identity,enabled);
        pinnedPreferences=next;
        rememberedAttempts.Remove(identity);
        if(pinnedStore is not null)
            try{await pinnedStore.SaveAsync(next,lifetime.Token);}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException)
            {System.Diagnostics.Trace.WriteLine("Pin reopening preference could not be saved: "+error.GetType().Name);}
    }

    private WidgetPresentationFrame? AvailableRememberedPin(RememberedPin preference)
    {
        if(owner?.Session.GetState(preference.WidgetId) is not {Failure:null,LastGood:{} frame}||
            frame.Descriptor.IsStopped||RememberedIdentity(frame.Descriptor,preference.LayoutId)!=preference||
            !catalogItems.Any(item=>item.Id==preference.WidgetId&&!item.IsStopped&&SameSurfaceOwner(item,frame.Descriptor))||
            !frame.Snapshot.PinnedLayouts.Any(layout=>layout.Id==preference.LayoutId&&layout.IndependentSelection&&layout.RestoreWhenAvailable))return null;
        return frame;
    }

    private void QueueRememberedPins()
    {
        if(!ReferenceEquals(rememberedAttemptOwner,owner))
        {rememberedAttemptOwner=owner;rememberedAttempts.Clear();}
        if(retired||pinnedPreferences.Remembered.Count==0)return;
        if(restoringRememberedPins){rememberedPinsDirty=true;return;}
        if(rememberedPinsQueued)return;
        rememberedPinsQueued=DispatcherQueue.TryEnqueue(async()=>
        {
            rememberedPinsQueued=false;
            if(retired||restoringRememberedPins||owner is null)return;
            restoringRememberedPins=true;
            try
            {
                foreach(var old in rememberedAttempts.Keys.Where(p=>!pinnedPreferences.Remembered.Contains(p)).ToArray())rememberedAttempts.Remove(old);
                foreach(var preference in pinnedPreferences.Remembered.ToArray())
                {
                    var frame=AvailableRememberedPin(preference);
                    if(frame is null){rememberedAttempts.Remove(preference);continue;}
                    if(pins.Any(pin=>pin.WidgetId==preference.WidgetId&&pin.LayoutId==preference.LayoutId))continue;
                    // Automatic restoration never replaces a window, opens a
                    // chooser, changes foreground, or interrupts a host interaction.
                    if(pins.Count>=MaximumPinnedSurfaces||!RememberedPinAdmission)continue;
                    if(rememberedAttempts.TryGetValue(preference,out var attempted)&&Equals(attempted,frame.Authority.WorkerRun))continue;
                    rememberedAttempts[preference]=frame.Authority.WorkerRun;
                    var session=owner;
                    var deferred=false;
                    bool Current()
                    {
                        var allowed=RememberedPinAdmission&&ReferenceEquals(owner,session)&&pinnedPreferences.Remembered.Contains(preference)&&
                            pins.Count<MaximumPinnedSurfaces&&AvailableRememberedPin(preference) is {} latest&&
                            Equals(latest.Authority.WorkerRun,frame.Authority.WorkerRun);
                        if(!allowed)deferred=true;
                        return allowed;
                    }
                    await PinAsync(preference.WidgetId,preference.LayoutId,admission:Current,restoring:true);
                    if(deferred)rememberedAttempts.Remove(preference);
                }
            }
            catch(OperationCanceledException)when(retired){}
            catch(Exception error){System.Diagnostics.Trace.WriteLine("Remembered pin reconciliation failed: "+error.GetType().Name);}
            finally
            {
                restoringRememberedPins=false;
                if(rememberedPinsDirty){rememberedPinsDirty=false;QueueRememberedPins();}
            }
        });
    }

    private void ObserveRememberedAvailability(WidgetPresentationState state)
    {
        foreach(var entry in rememberedAttempts.Keys.Where(p=>p.WidgetId==state.WidgetId).ToArray())
            if(state.Failure is not null||state.LastGood?.Snapshot.PinnedLayouts.Any(layout=>layout.Id==entry.LayoutId&&layout.RestoreWhenAvailable)!=true)
                rememberedAttempts.Remove(entry);
        QueueRememberedPins();
    }
}
