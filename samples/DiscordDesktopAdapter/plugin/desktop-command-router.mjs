// Pure renderer-domain admission. Not imported by the installed diagnostic probe.
// The authenticated main/renderer bridge must supply this connection and owner;
// this module cannot authenticate IPC, discover clients or invoke arbitrary code.
import { applyVoiceIntent } from './voice-intent.mjs';

const epochPattern = /^[0-9a-f]{32}$/;
const methodNames = new Set(['state.read', 'voice.setMuted', 'voice.setDeafened']);
const connectionPhases = new Set(['disconnected', 'connecting', 'connected', 'reconnecting', 'unavailable']);
const voiceReasons = new Set(['already_satisfied', 'preference_observed', 'invalid_intent', 'owner_changed',
    'settings_unavailable', 'mute_setter_unavailable', 'deafen_action_unavailable', 'media_support_unconfirmed',
    'dispatch_contract_unverified', 'provider_dispatch_busy', 'settings_binding_changed', 'provider_read_failed',
    'asynchronous_setter_unqualified', 'setting_not_confirmed', 'provider_mutation_unconfirmed']);

function data(value, names) {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
    const prototype = Object.getPrototypeOf(value);
    if (prototype !== null && prototype !== Object.prototype) return null;
    const keys = Reflect.ownKeys(value);
    if (keys.length !== names.length || keys.some(key => typeof key !== 'string' || !names.includes(key))) return null;
    const copied = {};
    for (const name of names) {
        const property = Object.getOwnPropertyDescriptor(value, name);
        if (!property || !Object.hasOwn(property, 'value')) return null;
        copied[name] = property.value;
    }
    return copied;
}
const epoch = value => typeof value === 'string' && epochPattern.test(value);
const sequence = value => Number.isSafeInteger(value) && value > 0;
const nullableBoolean = value => value === null || typeof value === 'boolean';
function scope(value) {
    const result = data(value, ['rendererEpoch', 'accountEpoch', 'callEpoch']);
    return result && epoch(result.rendererEpoch) && epoch(result.accountEpoch) &&
        (result.callEpoch === null || epoch(result.callEpoch)) ? result : null;
}
function sameScope(left, right) {
    return left.rendererEpoch === right.rendererEpoch && left.accountEpoch === right.accountEpoch && left.callEpoch === right.callEpoch;
}
function voiceState(value) {
    const result = data(value, ['preference', 'effective']);
    const preference = data(result?.preference, ['muted', 'deafened']);
    const effective = data(result?.effective, ['muted', 'deafened']);
    return preference && effective && typeof preference.muted === 'boolean' && typeof preference.deafened === 'boolean' &&
        nullableBoolean(effective.muted) && nullableBoolean(effective.deafened) ? { preference, effective } : null;
}
function snapshot(value, ownerScope) {
    const result = data(value, ['revision', 'signedIn', 'voiceConnection', 'voice']);
    if (!result || !Number.isSafeInteger(result.revision) || result.revision < 0 || typeof result.signedIn !== 'boolean' ||
        !connectionPhases.has(result.voiceConnection)) return null;
    const voice = result.voice === null ? null : voiceState(result.voice);
    if (result.voice !== null && !voice) return null;
    if ((!result.signedIn && ownerScope.callEpoch !== null) ||
        (ownerScope.callEpoch === null && (voice !== null || !['disconnected', 'unavailable'].includes(result.voiceConnection)))) return null;
    return { revision: result.revision, signedIn: result.signedIn, voiceConnection: result.voiceConnection, voice };
}
function request(value) {
    const copied = data(value, ['type', 'version', 'requestId', 'connectionId', 'scope', 'method', 'args']);
    if (!copied || copied.type !== 'request' || copied.version !== 1 || !sequence(copied.requestId) ||
        !epoch(copied.connectionId) || !methodNames.has(copied.method)) return null;
    const capturedScope = scope(copied.scope);
    const args = data(copied.args, copied.method === 'state.read' ? [] : ['value']);
    if (!capturedScope || !args || (copied.method !== 'state.read' && typeof args.value !== 'boolean')) return null;
    return { ...copied, scope: capturedScope, args };
}

/** Owned by the provider lifetime, shared across every connection/router.
 * No reset API: reconnect or a state read cannot settle an unknown side effect.
 */
export class DesktopProviderGate {
    #active = null; #read = null; #quarantined = false; #generation = 0n;
    get generation() { return this.#generation; }
    acquireRead() {
        if (this.#read) return { reason: 'request_busy', lease: null };
        const lease = Object.freeze({}); this.#read = lease;
        return { reason: null, lease };
    }
    releaseRead(lease) { if (this.#read === lease) this.#read = null; }
    acquire() {
        if (this.#quarantined) return { reason: 'mutation_reconciliation_required', lease: null };
        if (this.#active) return { reason: 'request_busy', lease: null };
        const lease = Object.freeze({}); this.#active = lease; this.#generation++;
        return { reason: null, lease };
    }
    quarantine(lease) { if (this.#active === lease) this.#quarantined = true; }
    release(lease) {
        if (this.#active !== lease) return;
        this.#active = null; this.#generation++;
    }
}

/**
 * getOwner returns the same plain, immutable scope object for one renderer /
 * account / call / provider incarnation. Replace it on every such transition,
 * even if public IDs match. Renderer/account epochs also exist while signed out;
 * callEpoch is null outside a call. Never use wire-supplied objects as owners.
 *
 * readState(owner, {signal}) is a read-only bounded projection, not a provider
 * object dump. getVoiceBindings(owner) is synchronous qualified discovery of
 * {media, actions, dispatcher}; it is not a method-to-handler registry.
 */
export function createDesktopCommandRouter({ connectionId, getOwner, readState, getVoiceBindings, providerGate } = {}) {
    if (!epoch(connectionId) || typeof getOwner !== 'function' || typeof readState !== 'function' || typeof getVoiceBindings !== 'function' ||
        !(providerGate instanceof DesktopProviderGate))
        throw new TypeError('invalid_desktop_router_dependencies');
    let retired = false, lastRequestId = 0, pendingRead = null, pendingMutation = null, admitting = false;
    let revisionOwner = null, publishedRevision = -1;
    const lifetime = new AbortController();
    const pending = new Set(); // At most one read and one mutation, with no queue.

    function capture() {
        if (retired) return null;
        try {
            const owner = getOwner();
            const capturedScope = scope(owner);
            if (!capturedScope || getOwner() !== owner || retired) return null;
            return { owner, scope: capturedScope };
        } catch { return null; }
    }
    function current(held) {
        const observed = capture();
        return observed && observed.owner === held.owner && sameScope(observed.scope, held.scope);
    }
    function response(held, status, reason, mutationAttempted = false, state = null, voice = null) {
        return { type: 'response', version: 1, connectionId, requestId: held?.requestId ?? null,
            scope: held?.scope ? { ...held.scope } : null, status, reason, mutationAttempted, state, voice };
    }
    function rejection(held, reason) { return response(held, 'rejected', reason); }
    function finish(operation, value) {
        if (operation.settled) return;
        operation.settled = true; pending.delete(operation); operation.resolve(value);
    }
    function retire() {
        if (retired) return;
        retired = true;
        // Set terminal state before callbacks: even reentrant provider code can
        // no longer pass its owner check and admit another side effect.
        for (const operation of pending) {
            if (operation.stage === 'mutating') providerGate.quarantine(operation.mutationLease);
            finish(operation, operation.stage === 'mutating'
                ? response(operation.request, 'uncertain', 'connection_retired', null)
                : rejection(operation.request, 'connection_retired'));
        }
        lifetime.abort();
    }

    async function runRead(operation, held) {
        const generation = providerGate.generation;
        try {
            const returned = await readState(held.owner, { signal: lifetime.signal });
            if (!current(held)) return finish(operation, rejection(operation.request, retired ? 'connection_retired' : 'owner_changed'));
            if (generation !== providerGate.generation) return finish(operation, rejection(operation.request, 'state_changed_during_read'));
            const state = snapshot(returned, held.scope);
            if (!current(held)) return finish(operation, rejection(operation.request, 'owner_changed'));
            if (!state) return finish(operation, rejection(operation.request, 'invalid_state_projection'));
            if (revisionOwner === held.owner && state.revision < publishedRevision)
                return finish(operation, rejection(operation.request, 'stale_state_revision'));
            revisionOwner = held.owner; publishedRevision = state.revision;
            finish(operation, response(operation.request, 'read', 'state_observed', false, state));
        } catch {
            finish(operation, rejection(operation.request, retired ? 'connection_retired' : current(held) ? 'state_unavailable' : 'owner_changed'));
        } finally {
            // If a dependency ignores cancellation, its slot stays occupied
            // until actual settlement. Retirement admits no replacement work.
            if (pendingRead === operation) pendingRead = null;
            providerGate.releaseRead(operation.readLease);
        }
    }
    function runMutation(operation, held) {
        try {
            let bindings;
            try {
                const discovered = getVoiceBindings(held.owner);
                bindings = { media: discovered?.media, actions: discovered?.actions, dispatcher: discovered?.dispatcher,
                    getOwner: () => current(held) ? held.owner : null };
            } catch { return finish(operation, rejection(operation.request, 'voice_binding_unavailable')); }
            if (!current(held)) return finish(operation, rejection(operation.request, retired ? 'connection_retired' : 'owner_changed'));
            operation.stage = 'mutating';
            let outcome;
            try {
                outcome = applyVoiceIntent({ owner: held.owner,
                    intent: operation.request.method === 'voice.setMuted' ? 'mute' : 'deafen', value: operation.request.args.value }, bindings);
            } catch {
                providerGate.quarantine(operation.mutationLease);
                return finish(operation, response(operation.request, 'uncertain', 'mutation_outcome_unknown', null));
            }
            const attempted = outcome?.mutationAttempted;
            if (typeof attempted !== 'boolean' || !voiceReasons.has(outcome?.reason)) {
                providerGate.quarantine(operation.mutationLease);
                return finish(operation, response(operation.request, 'uncertain', 'mutation_outcome_unknown', null));
            }
            if (!current(held)) {
                if (attempted) providerGate.quarantine(operation.mutationLease);
                return finish(operation, attempted ? response(operation.request, 'uncertain', retired ? 'connection_retired' : 'owner_changed', true)
                    : rejection(operation.request, retired ? 'connection_retired' : 'owner_changed'));
            }
            if (outcome.status === 'confirmed') {
                const voice = voiceState(outcome.observed);
                if (!voice) {
                    if (attempted) providerGate.quarantine(operation.mutationLease);
                    return finish(operation, attempted ? response(operation.request, 'uncertain', 'mutation_outcome_unknown', true)
                        : rejection(operation.request, 'invalid_voice_projection'));
                }
                return finish(operation, response(operation.request, 'applied', outcome.reason, attempted, null, voice));
            }
            if (attempted || outcome.status === 'unconfirmed') {
                providerGate.quarantine(operation.mutationLease);
                return finish(operation, response(operation.request, 'uncertain', outcome.reason, attempted));
            }
            finish(operation, rejection(operation.request, outcome.reason));
        } finally {
            providerGate.release(operation.mutationLease);
            if (pendingMutation === operation) pendingMutation = null;
        }
    }
    function dispatch(raw) {
        let admitted;
        try { admitted = request(raw); } catch { admitted = null; }
        if (!admitted) return Promise.resolve(rejection(null, 'invalid_request'));
        if (retired) return Promise.resolve(rejection(admitted, 'connection_retired'));
        if (admitted.connectionId !== connectionId) return Promise.resolve(rejection(admitted, 'connection_changed'));
        // One monotonic connection-owned sequence is constant-memory replay
        // protection. No per-feature counters, evicting ID cache or auto retry.
        if (admitted.requestId <= lastRequestId) return Promise.resolve(rejection(admitted, 'duplicate_or_old_request'));
        lastRequestId = admitted.requestId;
        const mutation = admitted.method !== 'state.read';
        if (admitting || (mutation ? pendingMutation : pendingRead)) return Promise.resolve(rejection(admitted, 'request_busy'));
        const admission = mutation ? providerGate.acquire() : providerGate.acquireRead();
        if (admission.reason) return Promise.resolve(rejection(admitted, admission.reason));
        admitting = true;
        let transferred = false;
        try {
            const held = capture();
            if (!held || !sameScope(held.scope, admitted.scope)) return Promise.resolve(rejection(admitted, 'owner_changed'));
            if (mutation && held.scope.callEpoch === null) return Promise.resolve(rejection(admitted, 'call_required'));
            let resolve;
            const completion = new Promise(done => { resolve = done; });
            const operation = { request: admitted, resolve, settled: false, mutationLease: mutation ? admission.lease : null,
                readLease: mutation ? null : admission.lease,
                stage: mutation ? 'preparing' : 'reading' };
            pending.add(operation); transferred = true;
            if (mutation) { pendingMutation = operation; runMutation(operation, held); }
            else { pendingRead = operation; void runRead(operation, held); }
            return completion;
        } finally {
            admitting = false;
            if (!transferred) {
                if (mutation) providerGate.release(admission.lease);
                else providerGate.releaseRead(admission.lease);
            }
        }
    }
    return Object.freeze({ dispatch, retire, scope: () => { const held = capture(); return held ? { ...held.scope } : null; } });
}
