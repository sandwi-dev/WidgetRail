# Playnite background return and game launch splash

| Step | Status | Completion evidence |
| --- | --- | --- |
| 1. Trace the return to Resume playing | Reproduced on cold return | The retained hero stayed visible until the replacement worker’s first publication. The hero was then absent for nine rendering callbacks in the unrelated-window run. Warm returns kept the requested background and restored Resume focus. |
| 2. Correct the first visible background | Background and focus verified; one frame of shelf reposition remains | Both matching-game and unrelated-window warm/cold returns kept the correct paint and Resume focus. The matching recording contains one transient vertical shelf movement. |
| 3. Verify the game launch splash | Four live launches recorded; Home fix verified on 007 | The ACE COMBAT 8 driver prompt and outstanding verification are detailed below. |

### 1. Trace the return to Resume playing

With a running game in front, open the overlay, focus **Resume playing**, and confirm the game is visible through the widget. Switch to another widget and back without closing the overlay. The reported sequence is a brief frame of the game's hero artwork followed by transparency. Determine whether the artwork comes from the cached view, the restored focus, or a fresh snapshot that arrives before the foreground match.

The user also observed the flash when reopening the overlay after Playnite's worker retired. With **Resume playing** focused, test reopening over the game and over another app. For each background, also switch away and back while the overlay is closed. Record the last settled view before closing, the window in front immediately before reopening, the first visible cached frame, and the frame where the replacement snapshot takes over. Check the artwork identity and opacity at both boundaries. Foreground changes while the overlay was hidden should not replay as visible transitions.

### 2. Correct the first visible background

- **Warm return.** Prefer removing the extra artwork step. When focus returns to Resume playing and the host can still confirm the same game window behind the overlay, the first visible Playnite frame should already be transparent. Moving to a poster should still bring its artwork in normally.
- **Cold worker, matching cached view.** When the cached Resume playing card still corresponds to the foreground game window, show its transparent presentation immediately. Keep it visible while the replacement worker loads, including through any initial snapshot that lacks game identity. Apply a matching fresh snapshot in place. Acceptance requires continuous transparency, stable card focus and geometry, and the navigation bar staying hidden throughout the handoff. A single hero or blank frame, or a replayed background fade, fails this case even if the final frame is correct.
- **Hero artwork already visible.** With the game running in the background and the overlay opening over another app, keep the cached hero visible at its settled opacity while the worker starts. If the fresh snapshot calls for the same hero, retain the displayed image even if its resource handle changes or the replacement is still decoding. Check warm returns as well. Any transparent frame, blank placeholder, or fade back into the same artwork fails this case.
- **Changed state or empty cache.** Repeat after switching the foreground app during retirement, then with no cached Playnite view. If the host can already establish the correct background, use it on first paint. If fresh evidence changes what should be visible, transition once from the currently displayed state. Keep widget entry responsive.
- **Interrupted or invalid match.** Switch away and back rapidly, move focus to a poster while the match is pending, and repeat with the game minimized, closed, or replaced by another foreground window. A late match must respect the current widget, card, and game. Check the navigation bar and card surfaces during the transition as well as after it.

Use the existing foreground and focus notifications. Playnite supplies the game identity and the host owns window matching and background transitions. Verify at normal and reduced motion settings. Close this step only after reviewing the transition recording frame by frame.

#### November 24 reproduction and consumer scope

An isolated native frontend used the installed Playnite provider, a running Silksong game, and a five-second worker idle policy. Both warm runs returned from Settings. The cold runs closed the overlay, confirmed worker retirement, established the desired foreground window and reopened. The fixture records the displayed background and native focus on each rendering callback. The first attempt to reproduce a cold matching window had a different window in front and is excluded from that comparison.

The first correction kept the background and game artwork stable. The screen recording still showed the Resume row moving for about 200 ms while its cover, summary and adjacent cards arrived. The native switch trace shows a concurrent lifecycle refresh applying the replacement worker's first snapshot to the visible presenter before selection preparation. That route bypassed the cached presentation and its focus state.

| Consumer | Treatment |
| --- | --- |
| Playnite Home on a running-game card | Home uses its existing event feed and warm library read to publish the running-game identity promptly. The initial wait is bounded to 350 ms. The shell can prepare a fresh worker behind the retained card, with the new worker’s own controls, input checks and artwork readers. |
| Playnite posters, details and other widgets | A focused control without a declared foreground process follows the ordinary switch. Source inspection across shipping samples, first-party widgets and templates found only the Playnite game shelf using this declaration. |
| Other windows and passive pins | Foreground matching remains restricted to the main opening window and captured process instance. Invalid matches use ordinary artwork. |
| Interrupted selections and lifecycle changes | An already-running lifecycle refresh yields native presentation ownership when a newer selection starts. Old controls retain no command authority during preparation. |

Artifacts for this work are in `E:/WidgetRail/background-return-1124/`. `baseline-mismatch.json.frames` records the original cold artwork loss. `fixed-match-reduced.mp4` and `return-review.png` show why the first correction was insufficient.

Final return checks used the .99 widget with the same production binaries and a fixture-only five-second idle policy. In `final-match.json`, the game remained visible throughout a warm return and a cold worker restart (11 checks). In `qualified-mismatch-reduced.json`, the decoded hero remained visible through the same returns over an unrelated foreground window with reduced motion (7 checks). Resume stayed the native focus target in all 303 sampled warm and cold return frames across these two runs. The Settings retained-entry fixture passed its 9 checks. All 298 Playnite managed checks passed. Render callbacks capture XAML state; the screen recording additionally exposed one roughly 33 ms shelf reposition despite the correct foreground and focus. The `final-match-late-review.png` contact sheet shows the remaining frame. A prior mismatch run was dismissed while switching back from Settings and produced zero warm samples (`hidden-or-retired` in the switch trace); it was excluded and rerun alone.

The shell prepares a restarted game-card presenter using the existing worker, package, input-scope, card and process identities. During preparation it uses the new worker's own card presentation to resolve artwork. At commit it restores native focus through the existing retained-focus path. It compares the two native declarations by identity and bounds until ready or a 350 ms maximum additional visual wait expires. A genuinely different route, surface or game uses the ordinary layout. A 1.5 second trial was rejected: waiting did not supply the unselected card�s artwork. The final cold request-to-input timings were 1.47 s over Silksong and 1.10 s over the unrelated window, of which 1.01 s and 0.70 s respectively elapsed before preparing the replacement presenter. These are two observed runs, not benchmark percentiles. Switching to a different foreground *during* the retirement interval, loss of the selected game mid-return, and physical-controller input were not independently exercised in these final runs.

### 3. Verify the game launch splash

The user excluded God of War after its Windows elevation prompt interrupted the overlay. The live Playnite add-on supplied the recent installed games. Evidence is in `E:/WidgetRail/splash-verification-1124`. Times are relative to the start of each recording.

| Game | Launch path | Result |
| --- | --- | --- |
| God of War | Game Details → Play | Excluded at the user's request. |
| Hollow Knight: Silksong | Game Details → Play | Correct title and hero artwork. A single continuous splash matched frames 19–97 (1.30–6.51 s at 15 fps). The game gained foreground as the overlay hid at 6.53 s. Desktop pixels were briefly visible before the game drew its studio logo. |
| 007 First Light | Home poster → Play | Baseline: correct splash at 1.84–3.98 s, then Home reappeared while the game started. On 0.4.100, the correct title and artwork were continuous from 1.04 to 16.03 s. An independent foreground trace measured the game taking focus at 15.62 s and the shell hiding at 15.87 s. A separate reopen check displayed Playnite Home. |
| Deadlock | Game Details → Play | One continuous title and artwork splash at 1.65–19.90 s. The game gained foreground and the shell hid at 19.88 s. The game took several more seconds to paint its own screen. Reopening displayed Game Details. |
| ACE COMBAT 8: WINGS OF THEVE | Home poster → Play | On the baseline, the confirmation returned to Home. One-second samples showed no sustained splash. The game opened its graphics-driver warning before its regular Unreal window. Playnite reopened without a residual splash. 007 supplied the post-fix repeat for the Home launch path. Post-fix ACE behavior and handoff past its separate driver prompt remain unverified. |

Starting a game updates LastActivity and can reorder the poster that owns a Home launch. In 0.4.100 the widget records catalog revisions during the admitted splash and defers the automatic reorder. Interactive completion checks the revision after clearing the splash. A hidden widget follows normal refresh-on-return. The existing query, route, lifecycle, PID and creation-time checks still apply. The `firstlight` and `firstlight100-confirm` recordings, foreground traces and image classifications show the regression and its correction. The latter used the shipping package on an isolated profile.

Resume switched to the same Silksong PID at 0.70 s. The recording showed the existing game menu without a launch splash. A separate reopen displayed Game Details. The first Resume attempt using concurrent automation did not establish handoff and was superseded by `resume-live.mp4`. Three focused managed tests passed for failed/stale/cancelled launches, exact-focus retry, and generation invalidation (`failure-cancel.log`). The isolated frontend and both 007 and Deadlock sessions exited normally. ACE ignored normal close requests and its test-started process was terminated. Silksong was left running.

#### Discord to Playnite material change

The installed Discord widget requests desktop acrylic. Returning to Playnite commits a transparent shell material. The foreground surface previously treated the resulting eligibility change as an animated focus transition. On the real Silksong window the selected paint flag was already correct while the Recording showed the hero fading out over the game. The host now applies a shell material change immediately. Moving focus between the poster rail and the running game still uses the existing focus transition.

Only Playnite Home declares a foreground process among the shipping sample widgets, first-party widgets and templates. Other widget backgrounds continue to use their existing paint. The native check switches from the installed Discord widget three times, with widget entry and tray selection. Over Silksong, all 58 sampled rendering callbacks had the expected paint and no queued reveal fade. Over an unrelated window all 75 retained ordinary paint. Screen recordings and aligned contact sheets for the reported flash are in `E:/WidgetRail/discord-return-1124`. The settings-return fixture checked logical selection and did not establish this material transition's visible behavior.
