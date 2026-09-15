# Ordinary controller read fallback

When a native DualSense reading is available, it is the ordinary input source.
The host's HID reader handles USB and Bluetooth reports on a background thread;
the UI consumes a freshness-checked copy. It also supplies PS and Create +
Options shortcuts, so a missing GameInput gamepad does not prevent opening the
overlay. Native HID reads are shared unless Exclusive control is enabled.

A GameInput client can initialize successfully while its provider supplies no
gamepad readings. If a visible overlay receives `GAMEINPUT_E_READING_NOT_FOUND`,
ordinary input falls back to the existing XInput reader for that visible session.
The next overlay open tries GameInput again. Reader changes prime the input
tracker so a held button is not replayed as a fresh press.

The fallback reports shared XInput input, not GameInput foreground exclusivity.
It does not restart Windows services, acquire universal controller ownership,
or alter HidHide/virtual-controller configuration. Hidden ordinary reads remain
dormant for the GameInput/XInput navigation lease. The native DualSense reader
stays active for opening shortcuts. An active Exclusive control session retains
its separate physical input and virtual output routing; it does not use this
fallback.

View + Menu uses its own shortcut reader, which explains why the shortcut can
remain usable when the ordinary GameInput reader is unavailable. For DualSense,
that shortcut consumes the existing native reader instead of opening another
HID connection. Service state and GameInput errors identify an unavailable
provider but do not establish the
cause of a Windows service shutdown hang.
