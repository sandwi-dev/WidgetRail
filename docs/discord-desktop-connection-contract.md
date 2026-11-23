# Discord Desktop local connection v1

Status: implementation foundation, not installed or connected to the live client.
The installed probe remains read-only and does not import the connection module.

This private protocol connects the Discord main-process adapter to a WidgetRail
desktop connection owner. It is not an extension of the generic widget capability
broker protocol. Reuse its established length-bound, current-user pipe and OS peer
identity patterns without exposing unrelated broker methods to Discord.

## Endpoint and ownership

- Windows pipe name: `wrail-discord-desktop-` followed by32 lowercase hex digits.
  The Node client constructs only a local `\\.\pipe\...` path; caller-supplied
  remote server paths, TCP addresses and arbitrary pipe names are rejected.
- The .NET server uses byte mode, asynchronous IO, `CurrentUserOnly` and
  `FirstPipeInstance`, and accepts one client for its captured connection owner.
- Before accepting messages, verify the OS-reported client PID against an expected
  process identity (PID, creation time and exact executable path). A PID field in
  JSON is not identity. The library never discovers/starts/restarts Discord itself.
- Each connection gets a new32-hex connection ID. Renderer/account/call/broadcast
  epochs are additional domain scopes, not interchangeable with pipe identity.
- Mutual authentication uses a32-byte random pairing secret. Provisioning,
  user-scoped storage/ACLs, revocation and selected-installation enrollment are
  separate unfinished work. Fixtures use ephemeral synthetic secrets only.
- This does not defend against malicious code with the same Windows-user authority
  that can read the pairing store or modify either process. No publisher or runtime
  attestation claim follows from a correct HMAC or executable path.

## Frames

Four-byte unsigned little-endian payload length, then UTF-8 JSON object. Length
must be1..262144 inclusive and is checked before body allocation. No BOM, comments,
trailing commas, arrays or null top-level values. Multiple frames can share a read;
both header and multibyte payload may be fragmented arbitrarily. UTF-8 is strict.
Never expose unfiltered parse errors or raw messages to logs.

The Node reader permits one pending read and at most eight queued frames; overflow
retires the connection. The writer permits one in-flight write without an unbounded
queue. Domain routing will own ordered command/event delivery. Truncation, cancellation
or IO failure retires the channel and settles pending reads/writes. A write callback
is transport completion, not confirmation that a Discord action happened.

## Handshake

Every field below is required and additional fields are rejected. IDs/nonces/proofs
are lowercase hexadecimal; the version is integer1. No account state or commands
are sent before mutual authentication and the ready response.

1. Client sends `{type:"clientHello",version:1,clientNonce,clientInstance}`.
   Nonce is32 random bytes encoded as64 hex; instance is16 bytes encoded as32 hex.
2. Server sends `{type:"serverHello",version:1,connectionId,serverNonce,serverProof}`.
   Server nonce is independently random32 bytes; connection ID is new16 bytes.
3. Client verifies server proof before sending `{type:"clientProof",proof}`.
4. Server verifies client proof, then sends `{type:"ready",connectionId}`.
   Client requires exactly the ID from the authenticated challenge.

For each role, HMAC-SHA256 takes the32-byte pairing key and this UTF-8 transcript
with LF separators and **no trailing newline**:

```text
WidgetRail.DiscordDesktop.v1
<role: server or client>
<clientNonce>
<serverNonce>
<connectionId>
<clientInstance>
```

Proof is64 lowercase hex and compared in constant time after shape validation.
Role separation rejects reflected proofs; fresh nonces and connection/instance
binding reject old transcripts. Five-second setup/handshake deadlines must be
removed from a successfully established socket. A setup timeout attached to the
socket's entire lifetime would incorrectly kill every session after five seconds.
The returned channel is disposed explicitly by its connection owner; reads/writes
receive cancellation for their own operation/lifetime. No automatic retries occur.

Deterministic interoperability vector (synthetic key, never an installed secret):

- Key:32 bytes each equal to`0x05`.
- Client/server nonce:64 repetitions of character`3`.
- Connection ID:32 repetitions of`2`; client instance:32 repetitions of`1`.
- Server proof: `0f52d675bb5b22879df225661c204290acf4acbf9d16613b704ddf6525b96f1c`.
- Client proof: `a385790eb52eb3c48265b2fc5116e0b5964d61ddcecbb81187cf37a9291e9fa3`.

## Following stages

Do not expose a general JSON/eval endpoint because this transport exists. The
[initial typed router](../samples/DiscordDesktopAdapter/plugin/desktop-command-router.md)
now allowlists state reads and explicit mute/deafen intents, binds requests to
connection/renderer/account/call epochs and bounds outstanding work. Its required
provider-owned gate keeps uncertain mutations and abort-ignoring reads quarantined
across reconnects. It is not yet connected to this pipe or the installed client.
Pairing, exact renderer IPC admission, broader typed commands, authoritative event
publication and provider reconciliation remain unfinished. No media frames cross
this pipe.

An authenticated fake peer is not proof of production pairing, renderer IPC
admission, Discord command correctness or full widget integration. Required checks
include real Windows named-pipe interop between .NET and Node, actual peer PID
rejection, fragmentation/malformed input, bad/reflected/replayed proof, cancellation,
teardown and survival beyond the initial setup deadline. That test uses generated
processes only and must not attach to or restart the user's Discord.
