// Discord store identities and semantics stay in the provider. The host owns
// delivery bounds and coalescing; these notifications contain no account data.
function createWidgetRailDiscordObserver(discover, invalidate) {
    const names = ['UserStore','SelectedChannelStore','VoiceStateStore','SpeakingStore',
        'MediaEngineStore','RTCConnectionStore','StreamRTCConnectionStore','ApplicationStreamingStore',
        'RelationshipStore','PresenceStore','GuildStore','GuildChannelStore','ChannelStore','PermissionStore','NotificationSettingsStore'];
    const subscriptions = new Map();
    let active = true, queued = false;
    const changed = () => {
        if (!active || queued) return;
        queued = true;
        Promise.resolve().then(() => { queued = false; if (active) invalidate(); });
    };
    function remove(value, entry) {
        entry.active = false;
        try { value.removeChangeListener(entry.listener); } catch { /* Provider already retired. */ }
        subscriptions.delete(value);
    }
    return Object.freeze({
        refresh() {
            if (!active) return;
            const current = new Set();
            for (const name of names) {
                let value;
                try { value = discover()?.findStore(name); } catch { continue; }
                if (value && typeof value.addChangeListener === 'function' && typeof value.removeChangeListener === 'function') current.add(value);
            }
            for (const [value, entry] of subscriptions) if (!current.has(value)) remove(value, entry);
            for (const value of current) if (!subscriptions.has(value)) {
                const entry = {active:true,listener:null};
                entry.listener = () => { if (entry.active) changed(); };
                try { value.addChangeListener(entry.listener); subscriptions.set(value,entry); }
                catch { entry.active = false; }
            }
        },
        dispose() {
            active = false;
            for (const [value, entry] of subscriptions) remove(value,entry);
        }
    });
}
