using WidgetRail.Samples.DiscordPrototype;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    // This is Discord encoder policy, not a capture-source or host transport
    // option. The ordinary SDK Select and account-scoped preference store own
    // presentation and persistence; no capture authority changes here.
    private enum SharePreference { Bandwidth, Encoder }
    private static string SharePreferenceKey(SharePreference preference)=>preference==SharePreference.Bandwidth?"bandwidth":"encoder";
    private static (string Value,string Label)[] SharePreferenceChoices(SharePreference preference)=>preference==SharePreference.Bandwidth
        ?[("automatic","Automatic"),("high-detail","Higher detail · up to 18 Mb/s")]
        :[("discord","Discord default"),("prefer-hardware","Prefer hardware H.264")];
    private static string SharePreferenceValue(ChannelAccountPreferences saved,SharePreference preference)=>preference==SharePreference.Bandwidth
        ?saved.BandwidthPreference=="high-detail"?"high-detail":"automatic"
        :saved.EncoderPreference=="prefer-hardware"?"prefer-hardware":"discord";
    private string SharePreferenceValue(string account,SharePreference preference)=>SharePreferenceValue(PreferencesFor(account),preference);
    private string BandwidthPreference(string account)=>SharePreferenceValue(account,SharePreference.Bandwidth);
    private string EncoderPreference(string account)=>SharePreferenceValue(account,SharePreference.Encoder);

    private string SharePreferenceAction(DiscordState data,SharePreference preference,string value)=>DomainAction(SharePreferenceKey(preference),client?.Reference?.Id,data.Account,data.ChannelId,
        SharePreferenceValue(data.Account,preference),value);

    private bool CanChangeSharePreferences(DiscordState? data)=>LifecycleState==WidgetLifecycleState.Interactive&&navigation.Value.Route==Route.Home&&
        data is{Ready:true,ChannelId:not null,Capabilities.Share:true,Sharing:false,SharePending:false}&&
        view.Value.Outgoing is null&&view.Value.PreparingShare is null&&!Operations.IsBusy(ShareStartOperation)&&
        !Operations.IsBusy(ShareStopOperation)&&channelPreferences.Snapshot.Value is not null;

    private WidgetElement SharePreferenceControls(DiscordState data)=>UI.ResponsiveGrid("discord.share.preferences",230,2,
        SharePreferenceControl(data,SharePreference.Bandwidth,"Bandwidth",CanStopSharing(view.Value,data)?"Applies to your next share.":
            "Higher detail uses more upload bandwidth; Discord still adapts to the connection."),
        SharePreferenceControl(data,SharePreference.Encoder,"Encoder",EncoderPreference(data.Account)=="prefer-hardware"
            ?"Receiver compatibility is still being tested.":"Applies to your next share."))
        .Classes("discord-call-actions");

    private WidgetElement SharePreferenceControl(DiscordState data,SharePreference preference,string label,string hint)
    {
        var selected=SharePreferenceValue(data.Account,preference);var choices=SharePreferenceChoices(preference);
        var id="discord.share."+SharePreferenceKey(preference);
        return UI.Stack(id+".field",
            UI.Select(label,
                choices.Select(choice=>new SelectOption(choice.Value,choice.Label,SharePreferenceAction(data,preference,choice.Value),selected==choice.Value)).ToArray(),id,"Stream "+label.ToLowerInvariant())
                // Pending preference writes retain the initiating native Select
                // as a focus target. The action gate below rejects extra input.
                .Disabled(!CanChangeSharePreferences(data)).Classes("discord-voice-select"),
            UI.Text(hint,id+".hint").Classes("discord-secondary")).Classes("discord-voice-volume");
    }

    private bool TrySharePreferenceAction(WidgetActionEvent action,CancellationToken token)
    {
        SharePreference preference;
        if(action.ActionId.StartsWith("bandwidth.",StringComparison.Ordinal))preference=SharePreference.Bandwidth;
        else if(action.ActionId.StartsWith("encoder.",StringComparison.Ordinal))preference=SharePreference.Encoder;
        else return false;
        token.ThrowIfCancellationRequested();
        var data=CurrentState();
        if(!CanChangeSharePreferences(data)||Operations.IsBusy(PreferenceWriteOperation))return true;
        var value=SharePreferenceChoices(preference).Select(choice=>choice.Value).FirstOrDefault(value=>action.ActionId==SharePreferenceAction(data!,preference,value));
        if(value is null)return true;
        var account=data!.Account;var channel=data.ChannelId;var session=client?.Reference;
        var previous=SharePreferenceValue(account,preference);
        Operations.RunSerial(PreferenceWriteOperation,async context=>
        {
            // This selection was admitted before sharing began. A subsequent
            // Share waits for this serial lane before capturing its preference.
            // Recheck account/session and prior setting after async storage reads.
            bool Current()=>context.IsCurrent&&CurrentState() is{Ready:true}&&CurrentState()?.Account==account&&
                CurrentState()?.ChannelId==channel&&client?.Reference==session&&SharePreferenceValue(account,preference)==previous;
            if(!Current())return;
            await MutatePreferencesAsync(account,old=>!Current()||SharePreferenceValue(old,preference)==value?old:
                preference==SharePreference.Bandwidth?old with{BandwidthPreference=value}:old with{EncoderPreference=value},
                context.CancellationToken,true,"Could not save stream "+SharePreferenceKey(preference)+". Refresh and try again.");
        });
        return true;
    }
}
