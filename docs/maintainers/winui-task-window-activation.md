# WinUI task-window activation

The shell now consumes the existing authenticated `ActivateTaskWindow` host
effect. This does not expose HWNDs or process control to widgets or change the
SDK task-switch action.

The platform handoff preserves the native host's ordering:

1. Check the active widget/worker authority, current visible session, two-second
   uptime budget, and foreground ownership.
2. Validate the external top-level HWND against its process ID, creation time and
   window class. A reused handle, child window, or overlay-owned window is rejected.
3. Hide the overlay through its normal path, releasing controller ownership.
4. Recheck the worker, time budget and native target; restore a minimized target
   if needed and request foreground activation once.

Windows may decline the request. There is no retry, focus stealing loop, or
automatic overlay reopen. An accepted API request is not proof that an external
application finished becoming foreground.

The policy seam is `OverlayPlatformClient.TaskWindowActivation`; the Win32
implementation owns process-handle disposal and uses System32-only imports.
The shell checks authority again after hiding because lifecycle work may retire
the source. Native calls remain outside widget workers and presentation code.

Validation covers policy ordering, old/future requests, foreground loss, worker
retirement and target replacement during close, Windows denial, plus identity
checks against a real invisible helper HWND in a separate owned process.
End-to-end Task Switcher physical acceptance still requires its WindowPreview
presentation support and a full production overlay candidate.
