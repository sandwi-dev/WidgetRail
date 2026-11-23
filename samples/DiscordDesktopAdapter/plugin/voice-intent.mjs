// Provider-only intent translation. NOT imported by the read-only plugin entrypoint.
// Source basis: public module827343 setSelfMute(context, mute, playSoundEffect)
// and MediaEngineStore.getSettings(context); see desktop-provider-evidence.md.
// Deafen source: module827343 toggleSelfDeaf({context,syncRemote,usedKeybind})
// synchronously dispatches AUDIO_TOGGLE_SELF_DEAF; module360906 changes only the
// raw context.deaf value. Translate desired state through one compare-and-toggle,
// never a blind toggle or an invented setSelfDeaf signature.
//
// Important source constraint: module17928's dispatch returns a Promise but
// normally flushes synchronously. During store-change emission, isDispatching()
// can be false while _processingWaitQueue is true; another action would only be
// queued. Reject that state before issuing either action. These private queue
// fields are an explicit compatibility gate, not a generic framework contract.
//
// The caller owns serialization and admission. getOwner() must return the same
// opaque object for one account/call/provider incarnation, and a new object after
// any replacement (even when public account/channel IDs match). This module does
// not create request counters, retry, enable audio, request permission or persist.

function result(status, mutationAttempted, reason, observed = null) {
    return { status, mutationAttempted, reason, observed };
}

function effective(media, name) {
    try {
        const getter = media[name];
        const value = typeof getter === 'function' ? getter.call(media) : undefined;
        return typeof value === 'boolean' ? value : null;
    } catch { return null; }
}

function rawPreference(media, getter) {
    const settings = getter.call(media, 'default');
    // Snapshot primitive settings rather than returning a live provider object.
    const muted = settings?.mute;
    const deafened = settings?.deaf;
    if (typeof muted !== 'boolean' || typeof deafened !== 'boolean') return null;
    return { muted, deafened };
}

function observe(media, getter) {
    const preference = rawPreference(media, getter);
    if (!preference) return null;
    return {
        preference,
        effective: { muted: effective(media, 'isMute'), deafened: effective(media, 'isDeaf') }
    };
}

function dispatchAdmission(dispatcher) {
    const isDispatching = dispatcher?.isDispatching;
    const processing = dispatcher?._processingWaitQueue;
    const queue = dispatcher?._waitQueue;
    if (typeof isDispatching !== 'function' || typeof processing !== 'boolean' || !Array.isArray(queue))
        return result('unsupported', false, 'dispatch_contract_unverified');
    const dispatching = isDispatching.call(dispatcher);
    if (typeof dispatching !== 'boolean') return result('unsupported', false, 'dispatch_contract_unverified');
    return processing || dispatching || queue.length > 0 ? result('busy', false, 'provider_dispatch_busy') : null;
}

/**
 * Confirmed means the requested saved preference was observed, not that the
 * microphone is audible: permissions, server mute and deafen can override it.
 * A mutation attempt may have applied even when its result is unconfirmed.
 * Never automatically retry based on this result.
 */
export function applyVoiceIntent(request, { getOwner, media, actions, dispatcher } = {}) {
    let intent, desired, owner;
    try { ({ intent, value: desired, owner } = request ?? {}); }
    catch { return result('invalid', false, 'invalid_intent'); }
    if (!['mute', 'deafen'].includes(intent) || typeof desired !== 'boolean' ||
        !owner || typeof owner !== 'object' || typeof getOwner !== 'function')
        return result('invalid', false, 'invalid_intent');
    const current = () => {
        try { return getOwner() === owner; } catch { return false; }
    };
    if (!current()) return result('stale', false, 'owner_changed');

    const key = intent === 'mute' ? 'muted' : 'deafened';
    const otherKey = intent === 'mute' ? 'deafened' : 'muted';
    let getter, setter, before, compared;
    try {
        getter = media?.getSettings;
        if (typeof getter !== 'function') return result('unavailable', false, 'settings_unavailable');
        before = observe(media, getter);
        if (!current()) return result('stale', false, 'owner_changed');
        if (!before) return result('unavailable', false, 'settings_unavailable');
        if (before.preference[key] === desired) return result('confirmed', false, 'already_satisfied', before);
        setter = intent === 'mute' ? actions?.setSelfMute : actions?.toggleSelfDeaf;
        if (typeof setter !== 'function') return result('unsupported', false,
            intent === 'mute' ? 'mute_setter_unavailable' : 'deafen_action_unavailable', before);
        const supported = media.isSupported;
        if (typeof supported !== 'function' || supported.call(media) !== true)
            return result('unsupported', false, 'media_support_unconfirmed');
        const admission = dispatchAdmission(dispatcher);
        if (!current()) return result('stale', false, 'owner_changed');
        if (admission) return admission;
        if (media.getSettings !== getter) return result('stale', false, 'settings_binding_changed');
        // Discovery/support getters can synchronously change raw state. Compare
        // again immediately before a toggle, after all other provider discovery.
        compared = rawPreference(media, getter);
        if (!current()) return result('stale', false, 'owner_changed');
        if (!compared) return result('unavailable', false, 'settings_unavailable');
        if (compared[key] === desired)
            return result('confirmed', false, 'already_satisfied', { preference: compared,
                effective: { muted: null, deafened: null } });
        const finalAdmission = dispatchAdmission(dispatcher);
        if (!current()) return result('stale', false, 'owner_changed');
        if (finalAdmission) return finalAdmission;
        // A provider getter can replace account/call during binding discovery.
        // Read only once, then recheck the exact captured owner before mutation.
        if (!current()) return result('stale', false, 'owner_changed');
    } catch {
        return current() ? result('unavailable', false, 'provider_read_failed') : result('stale', false, 'owner_changed');
    }

    try {
        // Both qualified actions are synchronous at this idle dispatch boundary.
        // The explicit mute setter changes only raw mute; deafen toggles exactly
        // once because the latest raw value differs. Neither action invokes
        // toggleSelfMute's enable/permission path or silently changes raw mute
        // while changing deaf. Do not add an await between comparison and action.
        const returned = intent === 'mute' ? setter.call(actions, 'default', desired, true) :
            setter.call(actions, { context: 'default', syncRemote: true, usedKeybind: false });
        if (returned && typeof returned.then === 'function') {
            // The qualified action is synchronous. An unexpected async action
            // cannot be confirmed from an immediate read, even if state matches.
            Promise.resolve(returned).catch(() => {});
            return result('unconfirmed', true, 'asynchronous_setter_unqualified');
        }
        if (!current()) return result('unconfirmed', true, 'owner_changed');
        if (media.getSettings !== getter) return result('unconfirmed', true, 'settings_binding_changed');
        const after = observe(media, getter);
        if (!current()) return result('unconfirmed', true, 'owner_changed');
        if (!after || after.preference[key] !== desired || after.preference[otherKey] !== compared[otherKey])
            return result('unconfirmed', true, 'setting_not_confirmed', after);
        return result('confirmed', true, 'preference_observed', after);
    } catch {
        // A setter may throw after applying its side effect. No retry and no
        // sensitive provider error text are returned across the future boundary.
        return result('unconfirmed', true, 'provider_mutation_unconfirmed');
    }
}
