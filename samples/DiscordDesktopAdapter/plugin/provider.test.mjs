import test from 'node:test';
import assert from 'node:assert/strict';
import { readDesktopProbe } from './provider.mjs';
import { admittedOrigin, validateProbe, storeNames } from './probe-contract.mjs';

const probe = stores => readDesktopProbe({ findStore: name => stores[name] });
test('missing stores remain unknown and do not claim native capability', () => {
    const result = probe({});
    assert.equal(result.embeddedRenderer, false);
    assert.ok(Object.values(result.observations).every(value => value === null));
    assert.ok(Object.values(result.nativeInterfaces).every(value => value === false));
});
test('reads only whitelisted state; never allocates a desktop source or exports provider objects', () => {
    let forbidden = 0;
    const engine = { Video() { forbidden++; }, setGoLiveSource() { forbidden++; }, getWindowPreviews() { forbidden++; },
        getDesktopSource() { forbidden++; throw new Error('NO_STREAM'); } };
    const result = readDesktopProbe({ discordNative: { isRenderer: true }, createDiscordStream() { forbidden++; },
        findStore: name => ({
            UserStore: { getCurrentUser: () => ({ id: '123', username: 'PRIVATE', token: 'NEVER_EXPORT' }) },
            SelectedChannelStore: { getVoiceChannelId: () => '456' },
            MediaEngineStore: { getMediaEngine: () => engine, isSelfMute: () => true, isMute: () => true,
                isSelfDeaf: () => false, isDeaf: () => false, isScreenSharing: () => true }
        })[name]
    });
    assert.equal(result.observations.signedIn, true);
    assert.equal(result.observations.inVoiceChannel, true);
    assert.equal(result.nativeInterfaces.setGoLiveSource, true);
    assert.equal(result.nativeInterfaces.engineVideo, true);
    assert.equal(result.nativeInterfaces.getScreenPreviews, false);
    assert.equal(forbidden, 0);
    assert.doesNotMatch(JSON.stringify(result), /PRIVATE|NEVER_EXPORT|123|456/);
});
test('provider failures and nonboolean state are unknown, not false success', () => {
    const result = probe({ UserStore: { getCurrentUser() { throw new Error('provider'); } },
        SelectedChannelStore: { getVoiceChannelId: () => null },
        MediaEngineStore: { isSelfMute: () => 'false', getMediaEngine() { throw new Error('provider'); } } });
    assert.equal(result.observations.signedIn, null);
    assert.equal(result.observations.inVoiceChannel, false);
    assert.equal(result.observations.selfMuted, null);
    assert.equal(result.nativeInterfaces.engineVideo, false);
});
test('signed out differs from unavailable provider', () => {
    assert.equal(probe({ UserStore: { getCurrentUser: () => null } }).observations.signedIn, false);
    const result = readDesktopProbe({ findStore() { throw new Error('late load'); } });
    assert.equal(result.observations.signedIn, null);
    assert.equal(Object.keys(result.stores).length, storeNames.length);
});
test('unexpected account/channel shapes do not claim a signed-in call', () => {
    for (const value of [false, true, 123, 'unexpected', {}, { id: 'invalid' }]) {
        const result = probe({ UserStore: { getCurrentUser: () => value },
            SelectedChannelStore: { getVoiceChannelId: () => value } });
        assert.equal(result.observations.signedIn, null);
        assert.equal(result.observations.inVoiceChannel, null);
    }
});
test('native boundary rejects extra data and malformed schemas', () => {
    const good = probe({});
    assert.deepEqual(validateProbe(good), good);
    for (const value of [null, [], { ...good, token: 'private' }, { ...good, build: 2 },
        { ...good, observations: { ...good.observations, accountId: '123' } },
        { ...good, stores: { ...good.stores, UserStore: {} } },
        { ...good, observations: { ...good.observations, signedIn: 'true' } }])
        assert.throws(() => validateProbe(value), /invalid_probe/);
    const copy = validateProbe(good); copy.stores.UserStore = true;
    assert.equal(good.stores.UserStore, false);
});
test('only top-level official Discord application origins are admitted', () => {
    for (const url of ['https://discord.com/channels/@me', 'https://ptb.discord.com/app', 'https://canary.discord.com/login'])
        assert.equal(admittedOrigin(url, true), true);
    for (const url of ['https://discord.com.evil.test/channels/@me', 'https://evil.test/app',
        'http://discord.com/app', 'https://user:pass@discord.com/app', 'https://discord.com:444/app',
        'https://discord.com/developers', 'file:///app', 'not a url'])
        assert.equal(admittedOrigin(url, true), false);
    assert.equal(admittedOrigin('https://discord.com/app', false), false);
});
