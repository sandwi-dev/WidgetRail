namespace WidgetRail.SpotifyPlaybackHost;

internal static class SpotifyPlaybackPage
{
    internal const string TopLevelUri = "https://spotify-playback.widgetrail.internal/index.html";

    internal const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline' https://sdk.scdn.co; connect-src https://*.spotify.com wss://*.spotify.com https://*.scdn.co wss://*.scdn.co https://*.spotifycdn.com wss://*.spotifycdn.com; media-src blob: https://*.scdn.co https://*.spotifycdn.com https://*.akamaized.net; style-src 'unsafe-inline'; img-src data:; frame-src https://*.spotify.com; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'">
          <meta name="referrer" content="no-referrer">
          <title>Spotify Playback Host</title>
        </head>
        <body>
          <script>
          (() => {
            'use strict';
            const MAX_STRING = 512;
            let player = null;
            let sdkReady = false;
            const pendingTokens = new Map();
            let tokenSequence = 0;

            const text = (value, max = MAX_STRING) =>
              typeof value === 'string' ? value.replace(/[\u0000-\u001f\u007f]/g, ' ').slice(0, max) : '';
            const integer = (value, min, max) =>
              Number.isSafeInteger(value) ? Math.max(min, Math.min(max, value)) : 0;
            const post = (type, requestId, payload = {}) =>
              chrome.webview.postMessage({ version: 1, type, requestId: requestId || null, payload });
            const failure = (requestId, code) =>
              post('command_failed', requestId, { code: text(code, 64) || 'sdk_command_failed' });
            const invoke = (requestId, operation) => {
              try {
                Promise.resolve(operation()).then(
                  value => post('command_completed', requestId, { value: value ?? null }),
                  () => failure(requestId, 'sdk_command_failed'));
              } catch (_) { failure(requestId, 'sdk_command_failed'); }
            };
            const query = (requestId, operation, resultType, project) => {
              try {
                Promise.resolve(operation()).then(
                  value => post(resultType, requestId, project(value)),
                  () => failure(requestId, 'sdk_command_failed'));
              } catch (_) { failure(requestId, 'sdk_command_failed'); }
            };
            const requirePlayer = (requestId, operation) => {
              if (!player) { failure(requestId, 'player_unavailable'); return; }
              invoke(requestId, operation);
            };
            const track = value => {
              if (!value || typeof value !== 'object') return null;
              const artists = Array.isArray(value.artists)
                ? value.artists.slice(0, 16).map(item => text(item && item.name, 256)).filter(Boolean) : [];
              const images = value.album && Array.isArray(value.album.images) ? value.album.images : [];
              return {
                uri: text(value.uri, 512) || null,
                id: text(value.id, 128) || null,
                type: text(value.type, 32),
                mediaType: text(value.media_type, 32),
                name: text(value.name, 512),
                isPlayable: value.is_playable === true,
                albumName: text(value.album && value.album.name, 512) || null,
                artworkUrl: text(images[0] && images[0].url, 1024) || null,
                artists
              };
            };
            const state = value => {
              if (!value || typeof value !== 'object') return {
                isAvailable: false,
                paused: true,
                positionMilliseconds: 0,
                durationMilliseconds: 0,
                repeatMode: 0,
                shuffle: false,
                disallows: {
                  pausing: false, resuming: false, seeking: false,
                  skippingNext: false, skippingPrevious: false
                },
                currentTrack: null
              };
              const disallows = value.disallows || {};
              return {
                isAvailable: true,
                paused: value.paused === true,
                positionMilliseconds: integer(value.position, 0, 604800000),
                durationMilliseconds: integer(value.duration, 0, 604800000),
                repeatMode: integer(value.repeat_mode, 0, 2),
                shuffle: value.shuffle === true,
                disallows: {
                  pausing: disallows.pausing === true,
                  resuming: disallows.resuming === true,
                  seeking: disallows.seeking === true,
                  skippingNext: disallows.skipping_next === true,
                  skippingPrevious: disallows.skipping_prev === true
                },
                currentTrack: track(value.track_window && value.track_window.current_track)
              };
            };
            const addListeners = () => {
              player.addListener('ready', value => post('ready', null, { deviceId: text(value && value.device_id, 128) }));
              player.addListener('not_ready', value => post('not_ready', null, { deviceId: text(value && value.device_id, 128) }));
              player.addListener('player_state_changed', value => post('player_state_changed', null, state(value)));
              player.addListener('autoplay_failed', () => post('autoplay_failed', null));
              for (const type of ['initialization_error', 'authentication_error', 'account_error', 'playback_error']) {
                player.addListener(type, value => post('sdk_error', null, {
                  code: type,
                  message: text(value && value.message)
                }));
              }
            };
            const createPlayer = payload => {
              if (player) return;
              player = new Spotify.Player({
                name: text(payload.deviceName, 64),
                volume: Number(payload.initialVolume),
                enableMediaSession: false,
                getOAuthToken: callback => {
                  const tokenRequestId = `token-${++tokenSequence}`;
                  pendingTokens.set(tokenRequestId, callback);
                  post('token_requested', null, { tokenRequestId });
                }
              });
              addListeners();
            };

            window.onSpotifyWebPlaybackSDKReady = () => {
              sdkReady = true;
              post('sdk_loaded', null);
            };

            // Define the global callback before requesting Spotify's script. The SDK
            // invokes it from the document load path and fails closed when it is absent.
            // Dynamic insertion also gives the trusted host an explicit download error
            // instead of making every bootstrap failure look like a generic timeout.
            const sdkScript = document.createElement('script');
            sdkScript.src = 'https://sdk.scdn.co/spotify-player.js';
            sdkScript.async = true;
            sdkScript.onerror = () => post('sdk_error', null, {
              code: 'initialization_error',
              message: 'The Spotify playback SDK could not be downloaded.'
            });
            document.head.appendChild(sdkScript);

            chrome.webview.addEventListener('message', event => {
              const message = event.data;
              if (!message || message.version !== 1 || typeof message.type !== 'string') return;
              const payload = message.payload || {};
              const requestId = text(message.requestId, 64);
              switch (message.type) {
                case 'connect':
                  if (!sdkReady) { failure(requestId, 'sdk_not_loaded'); return; }
                  createPlayer(payload);
                  invoke(requestId, async () => {
                    const connected = await player.connect();
                    if (!connected) throw new Error('connect failed');
                  });
                  break;
                case 'provide_token': {
                  const tokenRequestId = text(payload.tokenRequestId, 64);
                  const callback = pendingTokens.get(tokenRequestId);
                  if (!callback) { failure(requestId, 'unknown_token_request'); return; }
                  pendingTokens.delete(tokenRequestId);
                  let token = typeof payload.accessToken === 'string' ? payload.accessToken : '';
                  callback(token);
                  token = '';
                  post('command_completed', requestId);
                  break;
                }
                case 'disconnect':
                  if (player) player.disconnect();
                  pendingTokens.clear();
                  post('command_completed', requestId);
                  post('disconnected', null);
                  break;
                case 'get_current_state':
                  if (!player) { failure(requestId, 'player_unavailable'); break; }
                  query(requestId, () => player.getCurrentState(), 'current_state', state);
                  break;
                case 'set_name': requirePlayer(requestId, () => player.setName(text(payload.name, 64))); break;
                case 'get_volume':
                  if (!player) { failure(requestId, 'player_unavailable'); break; }
                  query(requestId, () => player.getVolume(), 'volume', value => ({ volume: value }));
                  break;
                case 'set_volume': requirePlayer(requestId, () => player.setVolume(Number(payload.volume))); break;
                case 'pause': requirePlayer(requestId, () => player.pause()); break;
                case 'resume': requirePlayer(requestId, () => player.resume()); break;
                case 'toggle_play': requirePlayer(requestId, () => player.togglePlay()); break;
                case 'seek': requirePlayer(requestId, () => player.seek(Number(payload.positionMilliseconds))); break;
                case 'previous_track': requirePlayer(requestId, () => player.previousTrack()); break;
                case 'next_track': requirePlayer(requestId, () => player.nextTrack()); break;
                case 'activate_element': requirePlayer(requestId, () => player.activateElement()); break;
              }
            });
          })();
          </script>
        </body>
        </html>
        """;
}
