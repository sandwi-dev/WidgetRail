# ViGEmClient provenance

WidgetRail builds this pinned dependency into the production
`OverlayPlatformInterop.dll` for controller isolation.

- Project: https://github.com/nefarius/ViGEmClient
- Tag: `v1.16.18.0`
- Commit: `9e91a124d179bf26a878a952153042ac871da243`
- Source archive SHA-256:
  `72256A77B6B68E4D57DF91052A2862BC07FBC6B292E21BD4492225DB21B12ACA`
- License: MIT; see `LICENSE` and the repository-level
`THIRD_PARTY_NOTICES.md`.

WidgetRail patches the X360 feedback reader to own its cancellation event and
join its thread before releasing the controller target or client. Device
discovery keeps its enumeration list alive through the loop and closes a bus
handle if version negotiation fails. Controller isolation serializes the
client and target management calls. The callback only publishes rumble values.
The synthetic policy suite exercises the same reader with real overlapped pipe
I/O without changing the user's devices.

Original upstream SHA-256 values:

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
