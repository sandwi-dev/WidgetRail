// Read-only Phase A evidence contract. No identifiers, labels, paths, credentials,
// raw provider objects or command arguments are permitted across this boundary.
export const build = 1;
export const storeNames = Object.freeze([
    'UserStore', 'SelectedChannelStore', 'MediaEngineStore',
    'ApplicationStreamingStore', 'RTCConnectionStore', 'ChannelStore',
    'GuildStore', 'GuildChannelStore', 'VoiceStateStore', 'RelationshipStore',
    'PresenceStore', 'ApplicationStreamingSettingsStore'
]);
export const nativeInterfaceNames = Object.freeze([
    'createDiscordStream', 'engineVideo', 'setGoLiveSource',
    'getWindowPreviews', 'getScreenPreviews', 'getSingleWindowPreview'
]);
export const observationNames = Object.freeze([
    'signedIn', 'inVoiceChannel', 'selfMuted', 'effectiveMuted', 'selfDeafened',
    'effectiveDeafened', 'microphoneReady', 'screenSharing', 'soundSharing'
]);

function record(value, keys) {
    if (!value || typeof value !== 'object' || Array.isArray(value) ||
        Object.keys(value).length !== keys.length ||
        keys.some(key => !Object.hasOwn(value, key))) throw new Error('invalid_probe');
}

export function validateProbe(value) {
    record(value, ['schema', 'build', 'embeddedRenderer', 'stores', 'nativeInterfaces', 'observations']);
    if (value.schema !== 1 || value.build !== build || typeof value.embeddedRenderer !== 'boolean')
        throw new Error('invalid_probe');
    for (const [name, keys] of [['stores', storeNames], ['nativeInterfaces', nativeInterfaceNames]]) {
        record(value[name], keys);
        if (keys.some(key => typeof value[name][key] !== 'boolean')) throw new Error('invalid_probe');
    }
    record(value.observations, observationNames);
    if (observationNames.some(key => value.observations[key] !== null && typeof value.observations[key] !== 'boolean'))
        throw new Error('invalid_probe');
    // Return a detached schema-owned value; callers never retain provider objects.
    return JSON.parse(JSON.stringify(value));
}

export function admittedOrigin(frameUrl, isMainFrame) {
    if (!isMainFrame || typeof frameUrl !== 'string') return false;
    try {
        const url = new URL(frameUrl);
        return url.protocol === 'https:' &&
            ['discord.com', 'ptb.discord.com', 'canary.discord.com'].includes(url.hostname) &&
            !url.username && !url.password && (!url.port || url.port === '443') &&
            (url.pathname === '/app' || url.pathname === '/login' ||
                url.pathname === '/channels' || url.pathname.startsWith('/channels/'));
    } catch { return false; }
}
