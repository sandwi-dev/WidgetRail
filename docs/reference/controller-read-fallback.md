# Ordinary controller read fallback

A GameInput client can initialize successfully while its provider supplies no
gamepad readings. If a visible overlay receives `GAMEINPUT_E_READING_NOT_FOUND`,
ordinary input falls back to the existing XInput reader for that visible session.
The next overlay open tries GameInput again. Reader changes prime the input
tracker so a held button is not replayed as a fresh press.

The fallback reports shared XInput input, not GameInput foreground exclusivity.
It does not restart Windows services, acquire universal controller ownership,
or alter HidHide/virtual-controller configuration. Hidden ordinary reads remain
dormant. An active Exclusive control session retains its separate physical input
and virtual output routing; it does not use this fallback.

View + Menu uses its own shortcut reader, which explains why the shortcut can
remain usable when the ordinary GameInput reader is unavailable. Service state
and GameInput errors identify an unavailable provider but do not establish the
cause of a Windows service shutdown hang.
