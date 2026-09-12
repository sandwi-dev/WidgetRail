# Spatial audio selection feasibility

Investigated on 2026-09-12 from audio-device candidate 0a5e93b6. The running
candidate was not replaced and no product UI or provider contracts were changed.

## Result on the user's current output

The documented Windows.Media.Audio.SpatialAudioDeviceConfiguration API works
from an unpackaged ordinary desktop .NET process on this Windows installation.
A private COM policy interface is not necessary for the demonstrated Sonic path.

- Initial selected and active format: Off (zero GUID).
- Windows Sonic support: true. Selection: Succeeded; readback confirmed Sonic.
- Restoring Off: Succeeded; readback confirmed the original value.
- ConfigurationChanged: one notification after Sonic, another after restoring Off.
- Existing WindowsAudioProvider session count: 4 before, 4 with Sonic, 4 after
  restoring Off. This is bounded evidence for this endpoint/run, not proof for all
  drivers or audio workloads.
- Dolby Atmos for headphones support: true, but selection returned
  LicenseNotValidForAudioEndpoint. It did not change the selected format.
- Dolby Atmos for home theater support: false; switching was not attempted.
- The probe's original default format was restored and independently reread.

The setter documentation says that the caller must own the spatial format.
The observed Sonic success is more permissive than that description. Do not infer
that every format, OS build, packaging context or license is therefore usable.
Handle AccessDenied, LicenseExpired, LicenseNotValidForAudioEndpoint,
NotSupportedOnAudioEndpoint and UnknownError as ordinary bounded outcomes.
IsSpatialAudioFormatSupported establishes endpoint support, not usable licensing.
There is no reason to bypass a provider's license checks.

## Recommended product implementation

Add a Spatial sound selector below Output device, containing Off and the known
formats supported by the selected endpoint. Keep the selected/default format
separate from the currently active format, which Windows can change dynamically.
Unknown currently selected formats need an honest generic label, not Off.
Provider-owned format tokens and opaque output-device IDs should cross the broker;
WinRT configuration objects and endpoint identities should not.

Use a separately granted control operation, validate the captured output identity
at dispatch, and adopt a choice only after successful status plus readback.
Subscribe to ConfigurationChanged; retire the subscription on output change or
provider shutdown. Marshal notifications into the existing coalesced owner-thread
refresh. Reconcile audio sessions after spatial changes, since some drivers can
rebuild their session manager. Keep errors local to spatial controls.

A denied or unlicensed format should preserve the confirmed choice and show
user-friendly guidance. Example: Dolby Atmos is not licensed for this output.
Configure it in Dolby Access or choose Windows Sonic. Do not present every
reported-supported format as ready to use. A Windows sound-settings link can
provide a fallback when direct switching is unavailable.

## Reproduction artifact

`tools/SpatialAudioProbe` defaults to read-only inspection. Explicit --test-sonic
or --test-atmos briefly attempts that format and restores the original choice in
finally. It reports bounded status/format IDs and counts, never endpoint identity.
The initial disposable prototype produced logs under this worktree's logs folder.
The cleaned tool is retained for repeatable investigation. Production integration
is not implemented by this feasibility change.

## Primary references

- https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.spatialaudiodeviceconfiguration
- https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.spatialaudiodeviceconfiguration.setdefaultspatialaudioformatasync
- https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.spatialaudioformatsubtype
- https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.setdefaultspatialaudioformatstatus
- https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings
