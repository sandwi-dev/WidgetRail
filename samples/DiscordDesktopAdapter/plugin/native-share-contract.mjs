// Parameter/identity contracts only. No vendor action or native capture API is
// accepted by this module, and no start/stop executor is exported. This file is
// not wired into the current read-only plugin. See discord-desktop-share-contract.md.
//
// Source560595: permission await -> STREAM_UPDATE_SETTINGS -> STREAM_START.
// Source338771: own setGoLiveSource(null) -> STREAM_STOP. Numeric module IDs are
// evidence identifiers, never runtime lookup keys. The full desktop binding and
// native source/confirmation semantics still require a qualified runtime probe.

const id = value => typeof value === 'string' && /^\d{1,32}$/.test(value);
const windowId = value => typeof value === 'string' && value.startsWith('window:') &&
    value.length > 7 && value.length <= 128 && !/[\x00-\x20\x7f]/.test(value);
const fingerprint = value => typeof value === 'string' && value.length > 0 && value.length <= 512 && !/[\x00-\x1f\x7f]/.test(value);
const mode = raw => {
    const resolution = raw?.resolution; const frameRate = raw?.frameRate;
    return [480, 720, 1080, 1440].includes(resolution) && [15, 30, 60].includes(frameRate)
        ? Object.freeze({ resolution, frameRate }) : null;
};
const sameMode = (left, right) => left?.resolution === right?.resolution && left?.frameRate === right?.frameRate;
const unavailable = reason => ({ status: 'unavailable', reason, bindingQualified: false });
// Keep raw provider references out of enumerable/serializable contract data.
// A copied/forged descriptor has no identity authority; weak keys do not retain
// abandoned contracts or introduce a second connection-lifecycle registry.
const startIntents = new WeakMap();
const stopIntents = new WeakMap();

function context(raw) {
    const owner = raw?.owner; const accountId = raw?.accountId; const channelId = raw?.channelId;
    const guildId = raw?.guildId; const sharing = raw?.sharing; const pending = raw?.pending;
    if (!owner || typeof owner !== 'object' || !id(accountId) || !id(channelId) ||
        guildId !== null && !id(guildId) || typeof sharing !== 'boolean' || typeof pending !== 'boolean') return null;
    return Object.freeze({ owner, accountId, channelId, guildId, sharing, pending });
}
function sameOwner(expected, actual) {
    return !!actual && expected.owner === actual.owner && expected.accountId === actual.accountId &&
        expected.channelId === actual.channelId && expected.guildId === actual.guildId;
}
function source(raw) {
    const sourceId = raw?.sourceId; const sourceIdentity = raw?.sourceIdentity;
    const processId = raw?.processId; const name = raw?.name; const applicationAudioAvailable = raw?.applicationAudioAvailable;
    if (!windowId(sourceId) || !fingerprint(sourceIdentity) || !Number.isSafeInteger(processId) ||
        processId <= 1 || processId > 0xffffffff || typeof name !== 'string' || name.length > 240 ||
        /[\x00-\x1f\x7f]/.test(name) || typeof applicationAudioAvailable !== 'boolean') return null;
    return Object.freeze({ sourceId, sourceIdentity, processId, name, applicationAudioAvailable });
}
function policy(raw, selectedMode) {
    const customPreset = raw?.customPreset; const previewDisabled = raw?.previewDisabled;
    const canShare = raw?.canShare; const allowed = raw?.allowed;
    if (!Number.isSafeInteger(customPreset) || customPreset < 1 || customPreset > 32 ||
        typeof previewDisabled !== 'boolean' || canShare !== true || !Array.isArray(allowed) || allowed.length > 12)
        return null;
    const seen = new Set(); let admitted = false;
    for (const value of allowed) {
        const item = mode(value); if (!item) return null;
        const key = `${item.resolution}:${item.frameRate}`;
        if (seen.has(key)) return null;
        seen.add(key); if (sameMode(item, selectedMode)) admitted = true;
    }
    return admitted ? Object.freeze({ customPreset, previewDisabled }) : null;
}

/**
 * Observe permission only after user-approved native admission; never request it
 * implicitly here. readContext/readPolicy are synchronous store projections.
 * observePermission/readSource are injected read-only observations and may await.
 * All context/source/quality values are copied before crossing an async boundary.
 * Returned steps are inert descriptors with bindingQualified:false, not callable
 * actions or proof that a selected source will produce remote picture/audio.
 */
export async function buildNativeStartContract(selection, { readContext, observePermission, readSource, readPolicy } = {}) {
    try {
        const owner = selection?.owner; const sourceId = selection?.sourceId; const sourceIdentity = selection?.sourceIdentity;
        const applicationAudio = selection?.applicationAudio; const quality = mode(selection?.quality);
        if (selection?.status !== 'selection_validated' || !owner || typeof owner !== 'object' ||
            !windowId(sourceId) || !fingerprint(sourceIdentity) || typeof applicationAudio !== 'boolean' || !quality)
            return unavailable('invalid_selection');
        if ([readContext, observePermission, readSource, readPolicy].some(value => typeof value !== 'function'))
            return unavailable('binding_unavailable');
        const initial = context(readContext());
        if (!initial || initial.owner !== owner || initial.sharing || initial.pending) return unavailable('owner_or_share_changed');
        const permission = await observePermission(initial);
        const afterPermission = context(readContext());
        if (!sameOwner(initial, afterPermission) || afterPermission.sharing || afterPermission.pending)
            return unavailable('owner_or_share_changed');
        if (permission !== 'granted') return unavailable('permission_unconfirmed');
        const observedSource = source(await readSource({ sourceId, sourceIdentity, applicationAudio }, initial));
        const afterSource = context(readContext());
        if (!sameOwner(initial, afterSource) || afterSource.sharing || afterSource.pending) return unavailable('owner_or_share_changed');
        if (!observedSource || observedSource.sourceId !== sourceId || observedSource.sourceIdentity !== sourceIdentity ||
            applicationAudio && !observedSource.applicationAudioAvailable) return unavailable('source_or_audio_changed');
        const observedPolicy = policy(readPolicy(initial), quality);
        const final = context(readContext());
        if (!sameOwner(initial, final) || final.sharing || final.pending) return unavailable('owner_or_share_changed');
        if (!observedPolicy) return unavailable('quality_or_preview_policy_unconfirmed');
        const intent = Object.freeze({ context: initial, source: observedSource, applicationAudio, quality,
            customPreset: observedPolicy.customPreset, previewDisabled: observedPolicy.previewDisabled });
        const contract = Object.freeze({ status: 'contract_ready', bindingQualified: false,
            steps: Object.freeze([
                Object.freeze({ kind: 'updateSettings', parameters: Object.freeze({ preset: observedPolicy.customPreset,
                    resolution: quality.resolution, frameRate: quality.frameRate, soundshareEnabled: applicationAudio }) }),
                Object.freeze({ kind: 'startStream', guildId: initial.guildId, channelId: initial.channelId,
                    options: Object.freeze({ sourceId, sourceName: observedSource.name, sourcePid: observedSource.processId,
                        sound: applicationAudio, previewDisabled: observedPolicy.previewDisabled }) })
            ]) });
        startIntents.set(contract, intent);
        return contract;
    } catch { return unavailable('observation_unavailable'); }
}

// This is a read-only identity predicate for a FUTURE qualified executor. It
// does not make bindingQualified true or authorize executing the descriptors.
export function isNativeStartContractCurrent(contract, observation) {
    try {
        if (contract?.status !== 'contract_ready') return false;
        const expected = startIntents.get(contract); if (!expected) return false;
        const observedContext = context(observation?.context);
        const observedSource = source(observation?.source); const observedPolicy = policy(observation?.policy, expected.quality);
        return sameOwner(expected.context, observedContext) && !observedContext.sharing && !observedContext.pending &&
            !!observedSource && observedSource.sourceId === expected.source.sourceId &&
            observedSource.sourceIdentity === expected.source.sourceIdentity && observedSource.processId === expected.source.processId &&
            (!expected.applicationAudio || observedSource.applicationAudioAvailable) && !!observedPolicy &&
            observedPolicy.customPreset === expected.customPreset && observedPolicy.previewDisabled === expected.previewDisabled;
    } catch { return false; }
}

function broadcast(raw) {
    const observedContext = context(raw?.context); const streamKey = raw?.streamKey;
    const metadata = raw?.metadata; const connection = raw?.connection; const metadataSourceId = raw?.metadataSourceId;
    const broadcastOwnerId = raw?.broadcastOwnerId; const currentSource = raw?.source === null ? null : source(raw?.source);
    if (!observedContext || !metadata || typeof metadata !== 'object' || !connection || typeof connection !== 'object' ||
        !windowId(metadataSourceId) || broadcastOwnerId !== observedContext.accountId) return null;
    const expectedKey = observedContext.guildId === null ? `call:${observedContext.channelId}:${broadcastOwnerId}` :
        `guild:${observedContext.guildId}:${observedContext.channelId}:${broadcastOwnerId}`;
    if (streamKey !== expectedKey || raw?.source !== null && !currentSource) return null;
    return { context: observedContext, streamKey, metadata, connection, metadataSourceId, broadcastOwnerId, source: currentSource };
}

// Capture only an observed own exact-window broadcast. Stable metadata + native
// connection references distinguish a restart that reuses the same public key.
export function buildNativeStopContract(observation) {
    try {
        const current = broadcast(observation);
        if (!current || !current.context.sharing && !current.context.pending ||
            current.source !== null && current.source.sourceId !== current.metadataSourceId)
            return unavailable('broadcast_identity_unavailable');
        const contract = Object.freeze({ status: 'stop_contract_ready', bindingQualified: false,
            steps: Object.freeze([
                Object.freeze({ kind: 'clearOwnNativeSource', parameters: null }),
                Object.freeze({ kind: 'stopStream', streamKey: current.streamKey, canShowFeedback: false, close: true })
            ]) });
        stopIntents.set(contract, Object.freeze(current));
        return contract;
    } catch { return unavailable('broadcast_identity_unavailable'); }
}

export function isNativeStopContractCurrent(contract, observation, afterSourceClear = false) {
    try {
        if (contract?.status !== 'stop_contract_ready' || typeof afterSourceClear !== 'boolean') return false;
        const expected = stopIntents.get(contract); if (!expected) return false;
        const actual = broadcast(observation);
        if (!actual || !sameOwner(expected.context, actual.context) || actual.streamKey !== expected.streamKey ||
            actual.metadata !== expected.metadata || actual.connection !== expected.connection ||
            actual.metadataSourceId !== expected.metadataSourceId) return false;
        if (afterSourceClear) return actual.source === null;
        if (expected.source === null) return actual.source === null;
        return !!actual.source && actual.source.sourceId === expected.source.sourceId &&
            actual.source.sourceIdentity === expected.source.sourceIdentity && actual.source.processId === expected.source.processId;
    } catch { return false; }
}
