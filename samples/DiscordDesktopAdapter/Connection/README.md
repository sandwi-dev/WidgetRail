# Desktop connection foundation

Windows-only `DiscordPipeServer` accepts one authenticated peer. The owner supplies
an unpredictable name from `CreatePipeName()`, a trusted `DiscordProcessIdentity`
(PID, creation FILETIME and full executable path), and an in-memory 32-byte secret.
The library clones the secret and wipes its copy on disposal; caller retains
responsibility for its own copy. No file-based pairing configuration is added.

```csharp
await using var server = new DiscordPipeServer(pipeName, expectedIdentity, secret);
await using var connection = await server.AcceptAsync(cancellationToken);
// Only now is connection.ReadObjectAsync / WriteObjectAsync available.
```

`Capture(pid)` obtains OS identity for a PID the owner has already selected through
trusted discovery. It is not authorization to trust a PID supplied by the client.
The server requires Windows' explicit local-pipe observation, checks the actual
named-pipe client PID and opens/retains that exact
process with query/synchronize rights; creation time and image path must match.
No peer-supplied identity substitutes for these checks. This is not a sandbox
against arbitrary malicious code already executing as the same Windows user.

Framing is four-byte little-endian length followed by a strict UTF-8 JSON object,
1..262144 bytes, bounded depth, no duplicate properties. Handshake schemas reject
unknown/missing fields, wrong types/version and non-lowercase hex. HMAC transcript
and ready ordering follow the adapter protocol document. Fresh server challenge
and connection ID prevent replay across server instances. One server permits one
attempt; failures close it, not silently accept another client.

The connection wait follows caller cancellation. Once a peer connects, all
handshake I/O shares a five-second deadline. Disposal cancels pending wait/read/
write and closes the pipe without terminating the peer process. There is at most
one read and one write in flight; the library does not hide an unbounded queue.
Any framed I/O error or cancellation closes the channel because partial framing
cannot safely be resumed. Domain snapshots/commands are a separate typed contract;
authenticated object transport alone does not authorize Discord actions.

The implementation follows `CollectorProtocol` framing and `BrokerPipeNames`
pipe-creation patterns without referencing their unrelated domain assemblies.
No installed Discord, app-global pipe descriptor or media API is used by the
fixture suite. Its real Windows pipe tests create only ephemeral synthetic peer
endpoints. The library has no startup hook into the installed diagnostic plugin.

The built fixture executable supports `--interop-server <pipe-name> <trusted-node-pid>`
for the parent's cross-language check. It captures that trusted test process's OS
identity, uses the synthetic key bytes `00..1f`, prints `LISTENING`, authenticates,
then exchanges `{ "type": "ping" }` / `{ "type": "pong" }` and exits. Its overall
deadline is 20 seconds; the five-second authentication deadline is disposed before
post-authentication I/O, so the parent can delay its ping to test lifetime separation.
This mode is test-only; it is not production pairing or an arbitrary command endpoint.
