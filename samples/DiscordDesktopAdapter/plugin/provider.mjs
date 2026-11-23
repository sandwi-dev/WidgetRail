import { build, storeNames, nativeInterfaceNames, observationNames, validateProbe } from './probe-contract.mjs';

// All dependencies are read-only queries. This does not import the WebView adapter
// or invoke source allocation, native capture, Discord actions or module factories.
export function readDesktopProbe({ findStore, discordNative, createDiscordStream }) {
    const stores = new Map(storeNames.map(name => {
        try { return [name, findStore(name) ?? null]; } catch { return [name, null]; }
    }));
    const read = (owner, name) => {
        try { return typeof owner?.[name] === 'function' ? owner[name]() : undefined; }
        catch { return undefined; }
    };
    const has = (owner, name) => {
        try { return typeof owner?.[name] === 'function'; } catch { return false; }
    };
    const media = stores.get('MediaEngineStore');
    const engine = read(media, 'getMediaEngine');
    const observations = Object.fromEntries(observationNames.map(name => [name, null]));
    const user = read(stores.get('UserStore'), 'getCurrentUser');
    if (user === null) observations.signedIn = false;
    else {
        try { if (typeof user?.id === 'string' && /^\d{1,32}$/.test(user.id)) observations.signedIn = true; }
        catch { }
    }
    const channel = read(stores.get('SelectedChannelStore'), 'getVoiceChannelId');
    if (channel === null) observations.inVoiceChannel = false;
    else if (typeof channel === 'string' && /^\d{1,32}$/.test(channel)) observations.inVoiceChannel = true;
    for (const [key, method] of Object.entries({
        selfMuted: 'isSelfMute', effectiveMuted: 'isMute', selfDeafened: 'isSelfDeaf',
        effectiveDeafened: 'isDeaf', microphoneReady: 'isNativeAudioPermissionReady',
        screenSharing: 'isScreenSharing', soundSharing: 'isSoundSharing'
    })) {
        const value = read(media, method);
        if (typeof value === 'boolean') observations[key] = value;
    }
    const nativeInterfaces = Object.fromEntries(nativeInterfaceNames.map(name => [name, false]));
    nativeInterfaces.createDiscordStream = typeof createDiscordStream === 'function';
    try { nativeInterfaces.engineVideo = typeof engine?.Video === 'function'; } catch { }
    for (const name of nativeInterfaceNames.slice(2)) nativeInterfaces[name] = has(engine, name);
    let embeddedRenderer = false;
    try { embeddedRenderer = discordNative?.isRenderer === true; } catch { }
    return validateProbe({ schema: 1, build, embeddedRenderer,
        stores: Object.fromEntries(storeNames.map(name => [name, stores.get(name) !== null])),
        nativeInterfaces, observations });
}
