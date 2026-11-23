# Native GIF resource fixtures

These tests run the production artwork demand, public-image decode and WinUI
renderer with locally generated GIF bytes. They do not contact Discord, change
system preferences or exercise authentication. A validation-enabled frontend is
required; shipping builds exclude the command-line entry point.

Generate inputs with `Generate-GifResourceFixtures.py --output <fresh-directory>`
using Python with Pillow installed in an isolated environment. The generator
creates two independently decoded palette frames, repeats their encoded records,
and verifies the frame count and logical dimensions. It includes:

- 10,000 one-pixel frames;
- 120 full 512x512 frames;
- 600 full 1024x1024 frames;
- 10,000 64x64 delta frames on a 4096x4096 composition canvas.

All encoded files remain below the production 8-MiB admission bound. Record the
generator version from `cases.json`; generated files belong in artifacts rather
than the source tree.

Run `Test-GifResources.ps1` serially with `-CandidateDirectory`, `-InputManifest`
(the generated `cases.json`), `-Name` (a case name from that manifest), and
`-Output` (a fresh result directory). Its watchdog samples only its own frontend
process, stops the test above 768 MiB private bytes or 90 seconds, and preserves
the failure receipt. Those are test-harness ceilings, not production limits.

Each case performs six acquire/play/suspend/release cycles. Assertions require
actual pixel changes, stable suspended pixels and collection of both decoded
image projections and retired artwork elements. The latter covers the weak
element-lifetime viewport observation. `result.json` contains process memory,
CPU, UI heartbeat and acquisition-to-first-play timing; the historical
`DecodeMilliseconds` field includes layout/admission as well as decoding.
`watchdog.json` contains independent process samples. The overall heartbeat
includes startup and deliberate test-only GC; the playback heartbeat excludes
the deliberate collection periods.

Interpretation limits: these short runs traverse the beginning of each animation,
not all 10,000 frames or every disposal/compression combination. Private bytes
are not GPU allocation measurements. Passing checks demonstrate the named
lifetimes/cases, not an absolute memory ceiling for all native codecs. Retain
separate malformed-input, reduced-motion, offscreen, authority and long-duration
evidence. Do not make an idle animation pass by bypassing the production demand's
visibility policy.

## Finite endurance mode

Add `--endurance` when generating a fresh input directory to produce two finite
1,200-frame sequences: full1024x1024 frames with keep disposal, and64x64 updates
on a4096x4096 canvas with restore-previous disposal. Each alternates red/blue for
119.9 seconds before reaching a unique green terminal frame. There is no loop
extension. The generator independently decodes the two alternating records and
the terminal record, verifies dimensions/count and records the expected terminal
time in the manifest. It does not claim to composite every frame itself.

The same runner reads `terminalMilliseconds` from the generated manifest. On the
first acquisition, the native fixture requires the final green pixels after
sustained playback, within15 seconds of the authored duration, then verifies the
terminal frame persists. It subsequently performs the usual suspension and six
total release/collection cycles. Results include the observed terminal time and
one-second process samples. The independent memory ceiling remains768 MiB; the
external timeout is the generated duration plus90 seconds. Duration admission
is capped at180 seconds in both runner and fixture. Existing manifests without
the optional field retain the short test behavior.

This covers a complete finite animation timeline, not proof that every frame was
presented, every GIF disposal combination, GPU allocation bounds, or hours-long
application stability. The test's RenderTargetBitmap sampling also contributes
to process CPU and memory; compare phases within this harness, not against an
unsampled idle app. All endurance switches are validation-only.
