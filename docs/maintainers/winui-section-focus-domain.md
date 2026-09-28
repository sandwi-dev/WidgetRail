# Preserve widget interaction during page replacement

The production shell reproduced an unintended Home-to-Library transfer on the
physically tested `f1f5557e` base: WinUI focused the Playnite tray item and shell
diagnostics changed `interactive` to false. This disabled the new widget page's
automatic focus restoration. Categories opened from a menu followed the same
replacement path. This was an implementation defect, not a pending widget feature.

`PrepareTransitions` already changes native focusability and detaches outgoing
controls. Previously it ran before `ApplyCore` captured logical focus and before
its `applying` transaction began. Native focused-element-removal recovery could
therefore reach `TrayGotFocus` as an apparently ordinary focus change.

The presenter now captures focus before transition/modal-exit preparation and
includes that preparation in its publication transaction. The shell cancels tray
focus during that transaction while the widget owns interaction, and does not
demote ownership if native focus recovery cannot be canceled. The incoming page's
existing scope/default/remembered-focus logic then completes normally. Explicit
Back, tray clicks and UI Automation focus retain their ordinary paths. There is no
timer-based focus lock, polling focus loop, new input scope or widget workaround.

## Validation

`Test-WinUiSectionFocus.ps1` uses actual isolated Playnite packages and the real
bridge. Launch the shell with both `--shell-no-controller` and
`--replay-shell-input`; only that combination enables F5–F9 to replay Menu, B,
LB, RB and A through the existing normalized input consumer without hardware.
The driver never launches, installs, uninstalls or changes a game's metadata.

The final run passed 13 checks: three Home/Library round trips with normalized
bumper input, Categories from each section and return, explicit Back-to-tray,
A re-entry, and explicit UIA tray focus. Each page change is checked again after
the outgoing animation/popup completion interval. The existing 36 native modal
and 19 native focus-policy checks also passed; analyzer build was clean.

Earlier replay-driver failures used F16/F17 names that the UI tool delivered as
literal text rather than the intended function keys. Native UIA ItemStatus
inspection showed `Number7` after the F17 attempt. The driver now uses supported
F5–F9 keys and waits for explicit entry completion. Those failures were not
additional production focus defects; their artifacts are retained separately.

Evidence: `artifacts/focus-domain/native-normalized/results.json`,
`modal-result.json`, `focus-policy/results.json`, and unique build logs. The
original failed behavior was observed before the correction. Physical retest of
the correction remains separate. This isolated branch is based on `f1f5557e`, so
it does not bundle the later production-shell/theme work or deferred pin changes.

Combined-shell follow-up: the driver now reads the stable `Overlay.Shell` root
and uses PID-verified normal window closure. All 13 checks pass again with the
production geometry, semantic guide and passive status integrated, using a newly
published matching Bridge. Evidence: `artifacts/winui-shell/section-combined-01`.
Physical retest remains pending; no game launches or metadata edits were performed.
