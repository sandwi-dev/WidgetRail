# Overlay introduction

The host owns the welcome surface and optional contextual tour. The tray,
widgets, Commands, and overlay show/hide controls remain available. Tips hide
while a competing dialog or menu is open. **Settings → Replay introduction**
restarts the welcome; an unfinished tour also exposes **Continue introduction**
in the tray's Commands menu.

## Progress file

Progress is stored separately from settings, under the active profile:

`%LOCALAPPDATA%\WidgetRail\introduction-progress.json`

Close WidgetRail before editing this file, then start it again. A running
tutorial saves its own progress and can overwrite external edits. The file is
read on application startup, not watched continuously.

```json
{
  "version": 1,
  "step": "welcome",
  "paused": false
}
```

Supported steps, in order: `welcome`, `guide`, `commands`, `utilities`,
`catalogue`, `appearance`, `appearance-details`, and `complete`.

- A missing file starts the welcome on the next normal opening.
- `paused: true` leaves normal navigation untouched; use Continue introduction
  or Replay introduction to resume.
- `step: "complete"` suppresses automatic tips across restarts and updates.
- `appearance-details` waits for the Appearance page; it does not navigate
  there automatically. Use `appearance` to test its Open Appearance action.
- Comments and trailing commas are accepted for manual testing.
- Invalid JSON, invalid field types, unknown versions/steps, excessive depth,
  files over 8 KB, and unreadable files fall back to a paused introduction.
  Loading never overwrites the problematic file. An explicit replay can replace
  it with valid progress. Save failures leave the overlay usable.

Development launches and explicit initial-widget launches do not automatically
start the introduction. Replay remains available in Settings.

## Automated coverage

`IntroductionProgressTests` covers bounded persistence, corrupt and unavailable
files, completion, all stages, and concurrent saves. Settings tests cover the
Appearance intent; presentation-session tests validate the replay command's
Settings authority.

Validation-enabled WinUI builds support `--validate-introduction=<result.json>`
with `--shell-no-controller`. Use an isolated profile and a bundled installation
containing Settings and Catalogue. This exercises production controller routing,
menu and utility transitions, real Appearance navigation, skipping, replay,
saved completion, and controller glyph changes. Never point the fixture at a
user's normal profile.
