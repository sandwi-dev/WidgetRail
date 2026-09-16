using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.NetworkControls;

internal static class NetworkManagementPresentation
{
    internal static WidgetView RenderWifi(NetworkWifiManagementState state, bool interactive, WidgetSurfaceHints surface)
    {
        var profile = state.Profiles.FirstOrDefault(item => item.ProfileId == state.ProfileId);
        if (state.ConfirmForget && profile is not null)
        {
            var sheet = UI.ActionSheet($"Forget {profile.DisplayName}?", "network.forget.sheet", "network.forget.scope",
                "wifi.forget.cancel",
                [new ActionSheetItem("network.forget.cancel", "Cancel", "wifi.forget.cancel", null, "Keep saved network"),
                 new ActionSheetItem("network.forget.confirm", "Forget network", "wifi.forget.confirm", WidgetGlyph.Warning,
                     "Forget saved network", ActionSheetItemTone.Danger, IsDisabled: !interactive || state.Busy, IsBusy: state.Busy)],
                "Windows will remove this saved network. Connecting again may require its password.");
            return new WidgetView(sheet, "network.forget.cancel", Surface: surface);
        }
        var title = state.Network?.DisplayName ?? profile?.DisplayName ?? "Saved networks";
        var items = new List<WidgetElement>
        {
            UI.Text(title, "network.manage.title", title).Classes("network-title"),
            UI.Button("Back", "wifi.manage.close", "network.manage.back").Disabled(state.Busy).Classes("network-secondary-action"),
            UI.Text(state.Message, "network.manage.message", state.Message)
                .Classes("network-help", state.IsError ? "is-error" : "is-neutral"),
        };
        ButtonElement Action(string title, string action, string id) => UI.Button(title, action, id)
            .Disabled(!interactive || state.Busy).Busy(state.Busy).Classes("network-secondary-action");
        if (state.Network is not null)
            items.Add(Action("Disconnect", "wifi.disconnect", "network.manage.disconnect"));
        else if (profile is not null)
        {
            items.Add(Action("Connect", "wifi.saved.connect", "network.manage.connect"));
            items.Add(Action(profile.AutoConnect switch { true => "Connect automatically: On", false => "Connect automatically: Off", _ => "Automatic connection unavailable" },
                "wifi.auto.toggle", "network.manage.auto").Disabled(!interactive || state.Busy || !profile.CanManage || profile.AutoConnect is null));
            items.Add(Action("Forget network…", "wifi.forget.open", "network.manage.forget")
                .Disabled(!interactive || state.Busy || !profile.CanManage));
            if (!profile.CanManage)
                items.Add(UI.Text("Windows policy or profile access prevents changes to this network.", "network.manage.policy", "Network managed by Windows policy")
                    .Classes("network-help"));
        }
        else
        {
            items.Add(Action("Refresh saved networks", "wifi.saved.refresh", "network.saved.refresh"));
            if (!state.Busy && state.Profiles.Count == 0)
                items.Add(UI.Text("No saved networks", "network.saved.empty", "No saved networks").Classes("network-help"));
            foreach (var entry in state.Profiles)
                items.Add(UI.Button(entry.DisplayName, "wifi.profile.open", NetworkControlsElementIds.SavedProfile(entry.ProfileId))
                    .Disabled(!interactive || state.Busy).Classes("network-secondary-action"));
        }
        var root = UI.Stack("network.manage.root", UI.VerticalScroll("network.manage.scroll", items.ToArray())
                .Classes("network-view-scroll"))
            .InputScope("network-manage").Shortcut(ControllerButton.B, "wifi.manage.close")
            .Classes("network-controls-widget", "network-management-view");
        return new WidgetView(root, "network.manage.back", Surface: surface);
    }

    internal static WidgetView RenderBluetooth(WidgetBluetoothDevice device, bool busy, bool interactive, WidgetSurfaceHints surface)
    {
        var sheet = UI.ActionSheet(device.DisplayName, "network.bluetooth.options", "network.bluetooth.options.scope",
            "bluetooth.details.close",
            [new ActionSheetItem("network.bluetooth.options.back", "Back", "bluetooth.details.close", null, "Back to Bluetooth devices"),
             new ActionSheetItem(NetworkControlsElementIds.Bluetooth(device.DeviceId), "Manage in Windows Settings",
                 "bluetooth.device.manage", WidgetGlyph.Connection, "Open Windows Bluetooth Settings", IsDisabled: !interactive || busy),
             new ActionSheetItem(NetworkControlsElementIds.Bluetooth(device.DeviceId) + ".remove", "Remove device…", "bluetooth.device.unpair.open",
                 WidgetGlyph.Warning, "Remove this pairing", ActionSheetItemTone.Danger, IsDisabled: !interactive || busy || !device.IsPaired)],
            device.IsConnected ? "Connected" : device.IsPresent ? "Paired and nearby" : "Paired, not currently nearby");
        return new WidgetView(sheet, "network.bluetooth.options.back", Surface: surface);
    }
}
