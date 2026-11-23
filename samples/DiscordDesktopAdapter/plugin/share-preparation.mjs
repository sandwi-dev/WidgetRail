// Phase A provider preparation only. NOT wired into the read-only plugin.
// No share/start/stop action, native module load, getDesktopSource, capture,
// permission request, setting write or encoder policy is invoked here.
//
// Source ordering is now identified in cached module560595 startStreamWithSource:
// SCREEN_RECORDING permission await -> quality selection -> STREAM_UPDATE_SETTINGS
// -> STREAM_START with source/sound options. Its owner is captured before the
// await and some window sources become game-PID sources. Do not call that helper
// blindly from an external intent. Native stop module338771 clears the own
// setGoLiveSource(null) before STREAM_STOP. Actual start/stop wiring and native
// confirmation remain unimplemented until source mapping/admission are qualified.
//
// Desktop list helper742459 may capture thumbnails. listSources below must be a
// separately qualified metadata/identity projection of the native source list,
// not a guessed HWND string or raw thumbnail export. identity is the host's
// exact source incarnation fingerprint; source id/name alone are insufficient.

const resolutions = [480, 720, 1080, 1440];
const frameRates = [15, 30, 60];
const sameQuality = (a, b) => a?.resolution === b?.resolution && a?.frameRate === b?.frameRate;
const quality = value => {
    const resolution = value?.resolution; const frameRate = value?.frameRate;
    return resolutions.includes(resolution) && frameRates.includes(frameRate) ? { resolution, frameRate } : null;
};
const unavailable = reason => ({ available: false, reason, allowed: [], preferred: null });

// Source basis: module327649 canStreamWithSettings and the custom preset path in
// module560595. Unknown policy never falls back to a supposedly allowed baseline.
// canShareInChannel is a qualified provider permission query, not quality policy.
export function readNativeSharePolicy({ getOwner, userStore, channelSelection, channelStore,
    guildStore, settingsStore, presets, canStreamWithSettings, canShareInChannel }, owner) {
    try {
        if (!owner || typeof owner !== 'object' || getOwner() !== owner) return unavailable('owner_changed');
        const user = userStore.getCurrentUser();
        const channelId = channelSelection.getVoiceChannelId();
        const channel = channelStore.getChannel(channelId);
        const preset = presets.PRESET_CUSTOM;
        if (!user?.id || !channelId || channel?.id !== channelId || !Number.isSafeInteger(preset) ||
            canShareInChannel(channel) !== true) return unavailable('sharing_not_admitted');
        const guildId = channel.getGuildId();
        const tier = guildId == null ? undefined : guildStore.getGuild(guildId)?.premiumTier;
        const saved = settingsStore.getState();
        const allowed = [];
        for (const resolution of resolutions) for (const frameRate of frameRates) {
            const admitted = canStreamWithSettings(preset, resolution, frameRate, user, tier, channel);
            if (typeof admitted !== 'boolean') return unavailable('quality_policy_unconfirmed');
            if (admitted) allowed.push({ resolution, frameRate });
        }
        const preferred = saved?.preset === preset ? quality({ resolution: saved.resolution, frameRate: saved.fps }) : null;
        if (getOwner() !== owner) return unavailable('owner_changed');
        return { available: true, reason: null, allowed,
            preferred: allowed.some(item => sameQuality(item, preferred)) ? preferred : null };
    } catch { return unavailable('quality_policy_unavailable'); }
}

function normalizePolicy(value) {
    if (value?.available !== true || !Array.isArray(value.allowed) || value.allowed.length > 12) return null;
    const seen = new Set(); const allowed = [];
    for (const raw of value.allowed) {
        const item = quality(raw);
        if (!item || seen.has(`${item.resolution}:${item.frameRate}`)) return null;
        seen.add(`${item.resolution}:${item.frameRate}`); allowed.push(item);
    }
    const preferred = quality(value.preferred);
    return { allowed, preferred: allowed.some(item => sameQuality(item, preferred)) ? preferred : null };
}

function normalizeSources(value) {
    if (!Array.isArray(value) || value.length > 256) throw new Error('invalid_source_inventory');
    const seen = new Set(); const sources = [];
    for (const raw of value) {
        // Window IDs are opaque provider values. Only the provider-issued exact
        // ID and the separately verified incarnation can authorize that window.
        const id = raw?.id; const name = raw?.name; const identity = raw?.identity;
        if (typeof id !== 'string' || !id.startsWith('window:') || id.length > 128 ||
            id.length === 7 || /[\x00-\x20\x7f]/.test(id) || seen.has(id) ||
            typeof name !== 'string' || name.length < 1 || name.length > 240 || /[\x00-\x1f\x7f]/.test(name) ||
            typeof identity !== 'string' || identity.length < 1 || identity.length > 512 || /[\x00-\x1f\x7f]/.test(identity))
            throw new Error('invalid_source_inventory');
        seen.add(id);
        sources.push(Object.freeze({ id, name, identity, applicationAudioAvailable: raw.applicationAudioAvailable === true }));
    }
    return sources;
}

/**
 * One pending domain preparation per adapter owner, not a UI operation runner.
 * The caller still owns transport admission, foreground and user consent. The
 * sourceIsCurrent callback must validate both the exact source incarnation and
 * its fourth argument's requested application-audio policy against current state.
 * lifetime is checked on access rather than by a polling loop or background timer.
 * takeSelection consumes its lease even on stale/invalid selection. A returned
 * selection is only a revalidated observation: a later start implementation must
 * recheck its owner/source immediately before the side effect and never replay it.
 */
export function createNativeSharePreparation({ getOwner, listSources, readPolicy, sourceIsCurrent,
    now = () => performance.now(), makeLeaseId = () => crypto.randomUUID(), lifetimeMilliseconds = 30000 }) {
    if (typeof getOwner !== 'function' || typeof listSources !== 'function' || typeof readPolicy !== 'function' ||
        typeof sourceIsCurrent !== 'function' || typeof now !== 'function' || typeof makeLeaseId !== 'function' ||
        !Number.isFinite(lifetimeMilliseconds) || lifetimeMilliseconds < 1000 || lifetimeMilliseconds > 120000)
        throw new Error('invalid_preparation_dependencies');
    let pending = null;
    const current = held => {
        try {
            const at = now();
            return pending === held && getOwner() === held.owner && Number.isFinite(at) && at >= held.started && at < held.expires;
        } catch { return false; }
    };
    const fail = reason => ({ status: 'unavailable', reason });
    return {
        async prepare(owner) {
            let held;
            try {
                if (!owner || typeof owner !== 'object' || getOwner() !== owner) return fail('owner_changed');
                // A late request from a retired account/call must not cancel a
                // newer owner's already-visible source chooser.
                pending = null;
                const started = now(); const lease = makeLeaseId();
                if (!Number.isFinite(started) || typeof lease !== 'string' || !/^[a-zA-Z0-9-]{16,64}$/.test(lease)) return fail('preparation_unavailable');
                held = { owner, lease, started, expires: started + lifetimeMilliseconds, sources: [], consumed: false }; pending = held;
                const sources = normalizeSources(await listSources(owner));
                if (!current(held)) return fail('preparation_retired');
                // Policy readers are synchronous store queries. A Promise or
                // unqualified result is unavailable; do not add an async gap.
                const policy = normalizePolicy(readPolicy(owner));
                if (!current(held)) return fail('preparation_retired');
                if (!policy || policy.allowed.length === 0) { pending = null; return fail('quality_unavailable'); }
                held.sources = sources;
                return { status: 'prepared', lease, expiresAfterMilliseconds: Math.max(0, held.expires - now()),
                    sources: sources.map(({ id, name, applicationAudioAvailable }) => ({ id, name, applicationAudioAvailable })),
                    allowed: policy.allowed, preferred: policy.preferred };
            } catch { if (pending === held) pending = null; return fail('preparation_unavailable'); }
            finally { if (pending === held && held && !current(held)) pending = null; }
        },
        async takeSelection(selection) {
            let held;
            try {
                const lease = selection?.lease;
                if (!pending || pending.consumed || lease !== pending.lease) return fail('preparation_missing');
                held = pending; held.consumed = true;
                if (!current(held)) return fail('preparation_retired');
                const explicitUserChoice = selection.explicitUserChoice;
                const applicationAudio = selection.applicationAudio;
                const sourceId = selection.sourceId;
                const selectedQuality = quality(selection.quality);
                if (explicitUserChoice !== true || typeof applicationAudio !== 'boolean') return fail('explicit_choice_required');
                const source = held.sources.find(item => item.id === sourceId);
                if (!source || !selectedQuality) return fail('selection_invalid');
                if (applicationAudio && !source.applicationAudioAvailable) return fail('application_audio_unavailable');
                const sourceCurrent = await sourceIsCurrent(source.id, source.identity, held.owner, { applicationAudio });
                if (!current(held)) return fail('preparation_retired');
                if (sourceCurrent !== true) return fail('source_changed');
                const policy = normalizePolicy(readPolicy(held.owner));
                if (!current(held)) return fail('preparation_retired');
                if (!policy?.allowed.some(item => sameQuality(item, selectedQuality))) return fail('quality_no_longer_allowed');
                return { status: 'selection_validated', owner: held.owner, sourceId: source.id,
                    sourceIdentity: source.identity, applicationAudio,
                    quality: selectedQuality, startImplemented: false };
            } catch { return fail('selection_unavailable'); }
            finally { if (pending === held) pending = null; }
        },
        cancel(lease) {
            if (!pending || pending.lease !== lease) return false;
            pending = null; return true;
        }
    };
}
