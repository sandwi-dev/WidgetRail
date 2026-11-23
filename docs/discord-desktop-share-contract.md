# Native Discord exact-window sharing contract

Date: 2026-10-27. This is a concrete parameter/identity contract and test plan,
not a working native broadcast implementation. No installed client changes,
live provider invocation, source capture or account/profile inspection occurred.

Code: [native-share-contract.mjs](../samples/DiscordDesktopAdapter/plugin/native-share-contract.mjs)
and [sequence tests](../samples/DiscordDesktopAdapter/plugin/native-share-contract.test.mjs).
It builds on [share-preparation.mjs](../samples/DiscordDesktopAdapter/plugin/share-preparation.mjs).
Neither module is imported by the current read-only plugin entrypoint.

## Evidence: source-supported ordering

The source/provenance limitations in
[provider evidence](discord-desktop-provider-evidence.md) still apply. The cached
public bundle includes native paths; it is not proof that the installed desktop
renderer uses precisely the same bindings.

New exact references under `artifacts/discord-desktop-provider-20261027/`:

| Module / location | Observation |
| --- | --- |
|560595, `startStreamWithSource`, lines22-49 | Captures user/channel/source; for the ordinary non-system-picker/non-activity path, awaits SCREEN_RECORDING permission; reads saved/custom quality and eligibility. |
|560595, lines45-51 | Dispatches `STREAM_UPDATE_SETTINGS` before `STREAM_START`. |
|560595, lines52-76 | Supports distinct `{pid}` and `{sourceId,sourceName,sourceIcon,sourcePid}` paths. Some game windows are converted to PID according to native experiments/game matching. |
|560595, lines77-83 | Adds selected `sound`, preview policy and optional camera/analytics fields. |
|401843, lines23-31 | `startStream(guildId,channelId,options)` dispatches STREAM_START with guild/call type and application context. |
|401843, lines83-103 | `stopStream(key,canShowFeedback,close)` optionally dispatches STREAM_CLOSE, then STREAM_STOP. |
|338771, lines8-17 | The own-stream stop wrapper first calls `setGoLiveSource(null)`, then that stream stop function. |
|360906 in the earlier formatted source, lines2310-2338 | Native source settings resolve desktop source/audio identity and quality before the media engine is updated. |
|206959, lines936-981 | Native connection eventually uses `setDesktopSourceWithOptions`; WidgetRail should not directly set this connection or its encoder flags. |

**The existing high-level helper cannot simply be called after an external
chooser.** It snapshots user/channel before a permission await and may convert the
chosen exact window into an application/game PID. Its successful return also does
not confirm native frames, actual audio isolation or remote receipt.

The normal source helper does not first call the browser `getDesktopSource()`.
The current native implementation of that method rejects `NO_STREAM`. It remains
prohibited throughout the proposed exact-window path.

## Parameter contract

`buildNativeStartContract` accepts the private result of one-use source preparation,
then invokes only injected read-only observations:

1. Read current owner/account/call and require no existing/pending share.
2. Observe the separately admitted screen-recording permission result.
3. After the await, revalidate the exact owner and all account/channel/guild values.
4. Observe the current source identity, PID and application-audio capability.
5. After that await, revalidate the owner/call and source fingerprint again.
6. Read current share eligibility, custom preset and saved preview policy
   synchronously. No default quality or preview preference is invented.
7. Produce two **inert, frozen descriptors**, in source order:

```text
updateSettings({
  preset: current PRESET_CUSTOM,
  resolution: selectedResolution,
  frameRate: selectedFPS,
  soundshareEnabled: explicitlySelectedApplicationAudio
})

startStream(currentGuildIdOrNull, currentChannelId, {
  sourceId: exactProviderWindowSourceId,
  sourceName: currentProviderLabel,
  sourcePid: verifiedWindowProcessId,
  sound: explicitlySelectedApplicationAudio,
  previewDisabled: currentProviderPreviewPreference
})
```

`sourcePid` is metadata accompanying the unchanged window source ID. The contract
never emits the distinct `pid`-only source-selection property, never substitutes a
screen, and never introduces `audioSourceId` from the camera branch. Application
PID must be greater than1; the screen/prepicked sentinel does not qualify.
Raw labels are presentation only. Source authority requires the separate current
window/process-incarnation fingerprint from the host/provider boundary.

Every descriptor includes `bindingQualified:false`. There is **no exported start,
stop, dispatcher or vendor-action executor**. Provider identity references are held
privately in weak maps keyed by the contract object. JSON cloning a descriptor
does not retain identity authority, and serialization cannot expose raw RTC or
metadata objects. A future runtime executor must be separately qualified, hold
this object in the adapter, and accept only typed user intent over the pipe.

`isNativeStartContractCurrent` is a read-only predicate for that future executor.
It checks owner/account/call, no current/pending share, exact source and process
identity, application-audio availability, current quality eligibility and unchanged
preview/custom-preset policy. It is not a permission grant or a successful-start
status. Recheck it immediately before each side effect. If the first settings
write succeeds and the second boundary fails, do not start, retry or restore old
preferences blindly over a newer user change.

## Stop contract and source-loss recovery

`buildNativeStopContract` captures only the user's current own exact-window
broadcast: canonical public stream key, account/call epoch, source metadata
incarnation and native connection identity. The public key alone is insufficient.

The inert sequence is:

```text
clearOwnNativeSource(null)
stopStream(exactStreamKey, canShowFeedback=false, close=true)
```

The first step corresponds to the normal own-stream wrapper's source cleanup.
The second retains STREAM_CLOSE -> STREAM_STOP ordering. Disabling feedback is a
source-supported argument, not a replacement stop command.

`isNativeStopContractCurrent` checks the exact broadcast before the first step,
then checks the same metadata/connection incarnation and expected cleared source
before the second. A newly started stream reusing the old public key cannot be
stopped as cleanup for its predecessor. A source that has already ended may still
have an owned provider broadcast; the contract allows explicit cleanup when the
local source is null and the exact own broadcast is still reported.

This contract deliberately does not cover PID-only game sources, cameras,
`prepicked:` sources or an unknown global desktop capture. Those need separate
qualified source mapping, not relaxed identity checks. Their unsupported status
must remain visible in the full migration ledger.

## Qualified runtime probe to build next

The following is the narrow wiring proposal, not permission to modify or use
the installed Discord client:

1. After approved plugin installation, verify loaded export shapes/signatures
   against the referenced source: source helper, streaming actions, quality
   policy/settings, permission observation, source/PID mapping and native own
   connection metadata. Bind only loaded verified modules. Do not instantiate
   another engine or execute unknown module factories.
2. Use an explicitly generated safe window for source-inventory qualification.
   Prove that list-only enumeration can omit previews, or obtain explicit
   capture admission and discard bounded thumbnails locally. Export no image
   payloads, raw stores or unrelated window details to the evidence log.
3. Confirm that the provider source ID maps to the exact host-observed HWND,
   PID and process creation identity. Test window destruction/handle reuse.
   Read the application-audio capability and native audio PID mapping; a true
   `sound` flag alone does not prove application-only audio.
4. Let the user choose that source/audio/quality. Run the new contract builder
   and display a bounded dry-run summary: unchanged window source, quality,
   source-binding result, audio policy and still-unqualified action status.
5. Only after this source mapping and action binding are accepted, add a narrow
   executor using the exact parameter order above, guarded by the existing
   dispatcher-idle/owner checks. Revalidate after every permission await and
   immediately before each mutation. Use no automatic retries.
6. Observe current native source, STREAM_START metadata, own RTC connection and
   first-frame state. Confirm captured source identity and distinguish
   starting/active/unconfirmed. Stop using the exact held incarnation, including
   source-ended/provider-still-sharing recovery.
7. User confirms remote picture and application audio. Missing native confirmation
   or unexpected PID conversion leaves the binding gate open. Do not infer
   success from method presence, descriptors or requested quality.

## Verification

The Node suite has19 checks covering exact parameter shapes/order, no PID-only
conversion, ownership changes during permission/source awaits, field snapshots,
quality/audio/preview rejection, frozen and non-transferable identity contracts,
same-key stream restarts, source-ended stop and private-object exclusion.

Test-only sequence drivers operate solely on in-memory records. They show that
settings precede start and source clear precedes stop, and that a changed owner
between those operations stops the sequence. They never call Discord, capture
content, request permission or establish actual engine behavior. The live binding,
source/audio equivalence and native/remote confirmation gates remain unresolved.
