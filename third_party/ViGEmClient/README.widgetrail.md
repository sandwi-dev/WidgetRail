# ViGEmClient provenance

WidgetRail vendors the minimal native source closure from the archived official
ViGEmClient release below. The upstream files are copied byte-for-byte and are
compiled only into the dormant `ControllerIsolationWorker.exe`; OverlayHost and
OverlayPlatformInterop do not link this dependency.

- Project: https://github.com/nefarius/ViGEmClient
- Tag: `v1.16.18.0`
- Commit: `9e91a124d179bf26a878a952153042ac871da243`
- Source archive SHA-256:
  `72256A77B6B68E4D57DF91052A2862BC07FBC6B292E21BD4492225DB21B12ACA`
- License: MIT; see `LICENSE` and the repository-level
`THIRD_PARTY_NOTICES.md`.

ViGEmClient documents that the native API is not thread-safe, so the worker
serializes all client and target calls. Those calls are synchronous and expose
no caller-supplied deadline. The guardian can terminate only the exact worker
process after a bounded heartbeat and final identity recheck; whether abrupt
process termination yields a sufficiently bounded driver-side target removal
remains a required physical measurement and is not established by fake tests.

Vendored file SHA-256 values:

| Path | SHA-256 |
| --- | --- |
| `LICENSE` | `445B3BCCD103D39CAB7E9B276DD966D60269CF5C44B4B8A118E48BC4C7C4F0DB` |
| `include/ViGEmClient.h` | `B95A0A36530D329B2C88791629492BDD6BA5FA66215840267AE55CA4DAA6F63F` |
| `include/ViGEmCommon.h` | `2FF57DD39D70A320A1C2CF729C3ED1D3A6DD5115C4F5B246AA8FD55F7CE0FF63` |
| `include/ViGEmUtil.h` | `7678BE819BA00358D96E45C68BC21A047A3F3ECDC7DBB671A9F61178920119DF` |
| `include/km/ViGEmBusShared.h` | `E03B4F028F87256EF730B89F6DE0CC79CC1932963C8739D35F6AF4FA94EC85FB` |
| `src/ViGEmClient.cpp` | `B4AE84AC4AA7837C446446C9D580A34CE52BF671AAC680087D5D067B13EBFC96` |

The upstream static-link contract also requires `setupapi.lib`. No ViGEmClient
DLL, package manager, runtime downloader, driver installer, or driver service is
included in this source closure.
