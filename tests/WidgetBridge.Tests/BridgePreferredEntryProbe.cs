using WidgetRail.WidgetSdk;

internal sealed partial class IndexedOwnedBridgeProbeWidget
{
    private string? preferredProbeKey;
    private bool HandlePreferredProbe(string action)
    {
        if(!action.StartsWith("preferred-",StringComparison.Ordinal))return false;
        entry=null;reveal=null;grid=grouped=surfaces=focusPreviews=modalMode=deferredMode=endFollowingMode=entryRebaseMode=refreshProbeMode=false;
        if(action=="preferred-release")activationReadGate?.TrySetResult();
        else if(action=="preferred-change")preferredProbeKey="item.24";
        else if(action=="preferred-missing")preferredProbeKey="item.20000";
        else
        {
            activationReadGate?.TrySetResult();
            activationReadGate=action=="preferred-delay"?new(TaskCreationOptions.RunContinuationsAsynchronously):null;
            preferredProbeKey="item.7500";backgroundState="idle";
            source.PublishQuery(action=="preferred-delay"?803:800,10000);
        }
        status=action;Invalidate();return true;
    }
}
