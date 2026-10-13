# Reusable webapps and Discord implementation

## Objective and authority

Implement the user's full webapp goal, with Discord as a pinnable consumer of public SDK and CLI authoring mechanisms. Work independently until a complete candidate is ready for physical acceptance. Do not merge, push, publish, create installers, or remove worktrees. Account login, sending real messages, and joining calls are reserved for the user. Local fixtures may exercise equivalent behaviour without accessing user credentials or accounts.

Baseline: `e819cf80` on main. Work branch: `codex/webapp-framework` in the existing `platform-feature-roadmap/WidgetRail` worktree. Preserve main's unrelated `docs/implementation-agent-goal.md` edit. The previous capture candidate may be closed and relaunched as necessary.

## Decisions

- Extend `WebBrowserDocument` / `UI.WebBrowser`; no Discord-only browser or DOM adapter.
- Browser chrome is a presentation choice independent of profile persistence, background policy, and interaction entry. Webapps use full-page content with LS-accessible controls. Preserve B's framework behaviour.
- Browser state remains host-owned. Profile keys are bounded logical names scoped by widget identity, never paths. Separate persistent and private namespaces. Widget IPC never carries cookies, tokens, browser objects, or arbitrary scripts.
- Keep existing ordinary browsers private by default for compatibility; the explicit webapp configuration opts into persistent sessions. Restricted provider documents stay private and cannot request webapp privileges.
- Website permissions are user decisions scoped to widget/profile/origin and permission kind. A widget declaration is not a permission grant. Clear-data and revoke flows require explicit user action and must affect all live views of the profile coherently.
- Controller text entry must support password fields and standards-based rich editors through browser editing mechanisms. No Discord selectors or direct DOM text replacement. Keep submission explicit and preserve physical keyboard/mouse input.
- Background eligibility is explicit, bounded, and user-visible. Active calls must not silently disappear due to hidden-surface eviction. Stopping a session must terminate its media activity rather than only muting it. Authority revocation, disable/uninstall, and app shutdown still retire sessions.
- Authentication child windows must preserve opener relationships when required, with bounded host-owned lifecycle. Offsite navigation, downloads/uploads, dialogs, and context actions must be accessible and maintain origin visibility.
- CLI scaffolding extends the existing transactional `wrail new` template system; name and HTTPS URL produce a pinnable webapp widget. SDK authors have the same capabilities without the CLI.
- Ship Discord using that authoring path, document honest platform limitations, and verify actual signed-out compatibility before claiming it works.

## Work and evidence ledger

| Workstream | Status | Required evidence |
| --- | --- | --- |
| Browser contract and version negotiation | Initial implementation verified | Protocol 81; SDK browser contract passes round-trip and old-host rejection. Public API updated. Full compatibility suite still pending. |
| Chrome and controller menu | Native foundation verified | Local native fixture proves LS open, no viewport resize, B close then framework back. Permission/context commands, responsive layout and complete layer checks still pending. |
| Persistent widget/account profiles | Native foundation verified | Seed/verify in separate processes proves cookie/localStorage persistence, private nonpersistence, widget/account/private isolation. Clear-data UI still pending. |
| Site-data and permission management | Implemented, native validation incomplete | Shared HostChoice routing, profile-wide reload/reset and permission controls added. Origin tests pass. Notification permission fixture currently denied before callback; see below. |
| Password and rich editor input | Pending | Local password/contenteditable/controlled editor fixtures; multiline/caret/submit; physical keyboard/mouse |
| Popup/authentication lifecycle | Pending | Local opener/postMessage auth fixture, close/return focus, bounds and teardown |
| Upload/download and context actions | Pending | Local file fixture, native picker cancellation, safe destination choice, controller access |
| Background activity and recovery | Pending | Hide/pin/switch/resource-pressure/revoke/process-failure fixtures; activity indicator and disconnect |
| Screen sharing and notifications | Pending | Early runtime/API experiments; supported paths implemented, platform limits documented |
| CLI webapp scaffold and Discord | Authoring foundation verified | Existing `wrail new widget --template webapp --url` extended; all six templates generate/build/test/preview/validate/package deterministically. Discord generated through CLI and builds. Checked-in consumer and candidate catalog added; real-site/pin checks pending. |
| Documentation and gallery | Pending | CLI help, SDK public API, author docs, gallery examples match final implementation |
| Complete candidate | Pending | Focused tests, native fixtures, release-style folder validation, signed-out Discord, launched candidate |

## Baseline investigation

- BrowserSurface currently derives one profile per widget and forces InPrivate. BrowserOwner shares an environment at `<settingsRoot>/WebBrowser`; sharing an environment does not share distinct profiles.
- Ordinary toolbar is mandatory; provider documents collapse it. All permissions are denied, downloads cancelled, new windows replace the current page, context menus/script dialogs disabled.
- Text entry supports only input/textarea (excludes passwords and contenteditable). Hidden surfaces can be evicted at four resident entries. Worker residency also needs coordination for background calls.
- Local WebView2 API documentation and runtime fixtures will be primary evidence. An initial connector web search returned no usable result; no compatibility conclusions drawn from it.

## Progress: 2026-10-13 foundation checkpoint

- Implemented independent `Chrome`, `Storage`, `ProfileKey`, `ExternalLinks`, and `Background` options. `WebBrowserOptions.WebApp` is a convenience configuration, not a second renderer. The external-link setting was renamed from a broad navigation-scope name to describe its actual purpose; popup/auth flows remain outstanding.
- `Background.KeepAlive` is a declared contract only at this checkpoint: do not claim background calls work yet. Need coordination with worker residency and native activity, not just surface eviction.
- Native profile names are hash-derived, length bounded, separate for persistent/private/provider content. Changing account/storage recreates the controller. Bookmarks/history are also account scoped; existing private/default library filenames remain readable without moving user data.
- Added `WebAppValidationPage` with a synthetic HTTPS document intercepted locally. Result files `artifacts/webapps/profiles-seed.json` (10 checks) and `profiles-verify.json` (11 checks) both pass. The verify phase used a fresh frontend process and the same test-only data directory. No real login/account or user profile was accessed.
- Native fixture launch syntax uses equals, e.g. `--validate-webapp=<absolute-result>` and `--webapp-profile=<absolute-test-profile>`, plus `--verify-webapp-profile` for restart verification. `FrontendArguments.Value` does not accept space-separated values. The first launch without equals opened the empty validation shell; it was closed and corrected, not counted as a test.
- Full Debug frontend compiled with explicit native DLLs from the baseline release build. Default native artifact paths were missing; use `artifacts/release-build/5f879909f1b74eccb008aeac1ddbb37e/native/platform/OverlayPlatformInterop.dll` and sibling `native/preview/WinUiWindowPreviewNative.dll`. No native code changed yet. Build evidence: `artifacts/webapps/frontend-04.log/.binlog`.
- Managed checks: browser SDK contract 1/1; WinUiShell 196/196; public API compatibility 17/17; unsafe scaffold URL rejection 1/1; scaffold transactional checks 1/1; all-six-template end-to-end 1/1 (each generated project is built, tested, previewed, validated and packaged twice). Evidence under `artifacts/webapps`.
- The SDK public API baseline tool covers the SDK assembly, not all protocol DTO members. Its six additions were previously accepted capture discovery/OpenFolder members missing from the baseline on main; they are a baseline catch-up, not evidence that new webapp protocol members were checked by reflection. Webapp options are covered by explicit protocol/SDK snapshot tests and native fixtures.
- Latest frontend compile after external-link/account-library changes: `artifacts/webapps/frontend-05.log/.binlog`, 0 warnings/errors. Code review checked profile changes retire controllers, provider documents reject webapp options, raw URLs are escaped before C# generation, and navigation callbacks capture URL values before async handoff.
- Discord was generated at `artifacts/webapps/Discord` with the ordinary CLI and built using its generated offline feed. Source/manifest copied into `samples/DiscordWidget`, using a repository project reference. No production Discord-specific logic.
- The previous capture candidate and the native fixture windows were closed. No final webapp candidate is running yet. Do not mark goal complete or claim signed-out Discord/voice compatibility.

## Next implementation work

1. Complete host-owned site controls and permission decisions, including safe asynchronous prompt cancellation, per-origin revocation and profile-wide clear-data semantics.
2. Extend controller editing to passwords and rich editors through browser input; preserve existing editor structures and explicit submission. Add local native fixtures.
3. Implement bounded popup/auth child lifecycle, files/context actions, and background-session ownership across the host and worker. Verify screen capture and notification API/runtime paths early.
4. Finish Discord behaviour, SDK/gallery/docs and complete candidate validation. User physical acceptance remains separate.

## Progress: site permission and data controls

- Added `BrowserSurface.Permissions.cs` with bounded queued permission requests (four per surface), deferrals, secure ASCII origin display, user allow-once/remember/block choices and cancellation on navigation/hide/retirement. Unknown/unsupported permissions remain denied. Uses the existing shell `HostChoiceDialog` through `ShowBrowserChoiceAsync`, including pinned XamlRoot and existing controller release/neutral handling.
- Added toolbar site controls using the native profile permission settings API. Revoke changes a granted decision to deny; blocked decisions can be reset to ask next time. Clear website data explicitly resets permissions and keeps WidgetRail bookmarks/history.
- `BrowserOwner.Profiles.cs` parks all live views in a profile on about:blank before mutation and restores them afterwards. New controllers for that profile wait; input and authored navigation are held during reset. The operation belongs to owner/native lifetime after the user's confirmation, rather than the page prompt's cancellation token.
- Build `frontend-11` passed with zero warnings/errors; a small subsequent navigation-during-reset guard still needs compile verification. WinUiShell 197/197 (`shell-03`) includes credential-free origin display, IDN, port and IPv6 checks.
- WinUI's actual WebView2 projection lacks the `PersistentStorage` permission enum found in newer .NET reference XML. Removed that unsupported permission branch. Explicit `HostDialogChoice[]` casts are needed for CsWinRT trim/AOT compatibility.
- Native permission experiment: `permissions-native-01` timed out; `-02` showed missing script state during initial navigation; fixed fixture readiness to require the loaded document title and inspect CDP exceptions. `permissions-native-03` now records `permissionResult=denied`, `Notification.permission=default`, secure HTTPS origin, and **no consent callback**. Notification prompting is unresolved and is not a pass. The retry budget for this UI scenario is exhausted for this turn; do not keep blindly rerunning it. Investigate runtime notification eligibility/host event admission with diagnostics, and independently verify fake-device microphone permission (no real device) and the shared shell dialog path. Original profile persistence fixtures had passed before notification checks were added; do not confuse those results with this failing permission experiment.
- New clear-data/revoke checks are currently located after that failing notification assertion, so they have NOT executed. Need separate fixtures that do not allow notification limitations to conceal other coverage.
- Subsequent fixture split: notification checks now require `--webapp-permissions`; ordinary `--validate-webapp` independently exercises data clearing in a separate synthetic profile, preserving restart-test markers. `artifacts/webapps/data-native-01.json` passes 11 checks, including native cookies/localStorage cleared after navigating to blank and restoring the page. This proves single-surface native data clearing, not the untested shared-shell consent/multiple-view owner flow. `frontend-12` compiles all current production changes with 0 warnings/errors. The last fixture PID 65916 was closed; no overlay is running.
- Validation-only permission-admission traces now record kind/admission flags (no URLs/tokens) in fixture output for the next evidence-driven diagnosis. Notifications remain unresolved; no additional notification retry was run after `permissions-native-03`.
- The installed WebView2 XML explicitly says push notifications are unavailable. `NotificationReceived` supports non-persistent document notifications; do not promise background push when the app is closed. Real Discord notifications still need validation.
- Site-control changes are a separate local checkpoint from the authoring foundation `d3300402`. Remaining first priorities are shared-shell consent/revocation validation, fake-device media permission experiments, rich/password editor support and background lifecycle. Goal remains active and incomplete.

## Physical acceptance remaining

User signs into Discord, verifies account persistence and actual channel/chat editing, joins a real voice/video call and exercises hide/reopen/pin/reconnect, then checks screen sharing if runtime support is established. These account-dependent checks are not prerequisites for preparing the candidate but must not be represented as automated passes.
