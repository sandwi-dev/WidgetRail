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
| Site-data and permission management | Pending | Native allow/deny/revoke and clear-data tests, origin identity shown, stale prompts cancelled |
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

## Physical acceptance remaining

User signs into Discord, verifies account persistence and actual channel/chat editing, joins a real voice/video call and exercises hide/reopen/pin/reconnect, then checks screen sharing if runtime support is established. These account-dependent checks are not prerequisites for preparing the candidate but must not be represented as automated passes.
