# Discord integration verification record

Updated: 2026-11-12. Current direction: maintain the accepted WebView2 integration
from local main (`23d542cb`), with documentation and local installer packaging.
The desktop-client migration was cancelled. Later bitrate/codec experiments are
excluded. Earlier investigation notes remain in Git history.

## Source ownership cleanup — 2026-11-12

The retired Vesktop sample, DevTools transport/launcher, prototype-only managed
tests and two-stage adapter-invocation tests have been removed. Current provider
sources now live in `samples/DiscordWebViewWidget/web/provider`, domain models in
its `Domain` directory and incoming receiver assets in its `web` directory.
Builds use the WebView discovery binding directly, without replacing a Vencord
expression in source. The obsolete Vesktop window-focus call and `show` command
are removed; neither was part of the hosted backend's admitted command surface.
The still-used internal JavaScript adapter name is retained to avoid unrelated
changes to callers. Capture/host verification tools remain maintained under
`tools/DiscordWebViewPrototype`; the directory name alone does not imply dead code.

Provider, activity, receiver and packaged-backend checks follow their new sources.
The direct provider downgrade-rejection check was preserved when removing the
obsolete transport gate tests. The Discord shard now has ten steps rather than
twelve because the runnable comparison widget and its transport gate are gone.
Before cleanup all twelve steps passed. After cleanup all ten remaining steps
passed, including 408 managed widget checks and source/minified provider checks.
A fresh 32-file Discord archive was validated and packed; backend, receiver and
SDK web messaging assets are present. The managed native-fixture harness builds
with zero warnings/errors; staged provider syntax, verification-runner self-tests
and documentation contracts (242 Markdown files) also pass. Evidence: `artifacts/discord-cleanup-baseline`,
`artifacts/discord-cleanup-checks` and `artifacts/discord-cleanup-package`.

Consumer matrix: current Discord widget and its managed fixture/tests migrated;
backend builder, staged WebView fixture and CI paths migrated; obsolete Vesktop
consumer removed; other widgets, SDK/protocol and host unchanged. This source-only
cleanup does not rerun live Discord acceptance. It does not modify installed apps,
profiles or the previously delivered installers. The separately installed desktop
diagnostic patch is outside this source cleanup.

## Acceptance and distribution

The user accepted the merged behavior for continued use: it was working for them,
and further issues will be assessed through everyday use. This is acceptance for
local testing, not exhaustive certification of every audio device, controller,
game, network or long-running session. Do not reopen the cancelled migration or
resume repeated performance experiments as a prerequisite for these installers.

The local app candidate is `0.1.0-preview.40.local.1`, SDK/CLI `0.4.8-dev`, with
presentation protocol 105. Latest published app release remains preview.40.
Build both Bare and Production through the existing clean-checkout Inno pipeline.
No GitHub release, tag, push, installer execution or user-profile change is requested.

The accepted prototype's distribution identity is now
`widgetrail.samples.discord-controls` version `0.1.0`, publisher `widgetrail.samples`,
display name **Discord**. Catalogue describes the actual call/share/pin controls,
full-access requirement and Discord Terms of Service risk. The new package does
not patch the desktop app; it modifies the hosted Discord web client through
private APIs. See the [user guide](users/discord.md).

The older `widgetrail.samples.discord` is a sandboxed webapp. The updater rejects
trust-model changes, so a distinct full-access package is intentional. No host
policy is relaxed. Old installs and profiles remain intact; a new install needs
full-access review and sign-in. Prototype credentials/preferences are not copied.
The legacy widget remains buildable from `samples/DiscordWidget`.

Catalogue downloads require a published matching asset. Because publication is
not requested, deliver the new `.wrwidget` beside the local installers and install
it through Settings after the host update. The new listing cannot accidentally
resolve to the old webapp archive because their package IDs differ.

## Accepted implementation and limits

The maintained widget includes the call/activity home, server/channel picker,
ranked search/suggestions, favorites/recents, participant audio, voice devices,
independent stream audio, a voice-controls pin and incoming video pin. The host
owns WebView2 lifecycle, permissions, pin requests, native capture and the chooser.
The provider owns Discord account/call semantics. Discord offers account-eligible modes up to
1080p60, saves resolution/FPS and fixes the capture content hint to clarity.

Picture and the corrected 1440p60 receiver quality label were observed live in
the accepted 0.0.14 baseline. That label is not proof of encoded pixels or sustained
60 FPS. Source size, encoder/network adaptation, motion noise and frame drops remain
explicit limitations. Individual audio/hardware/game cases were not all independently
verified; the user's acceptance does not retroactively manufacture those results.

Hiding the overlay should retain an active call, admitted share and selected pin.
Closing the app ends its hosted session. Uncertain starts are never replayed;
unconfirmed stops remain visible for explicit reconciliation. Permission and
source selection remain user actions. No account tokens or profiles are copied.

## Existing evidence and proportional local checks

At the merge, Discord managed/Node/minified-backend checks, SDK compatibility,
protocol parity, runtime, presentation session, broker and gallery checks passed.
Documentation passed after protocol-summary repair; shell tests passed on a bounded
rerun of an unchanged timing assertion. Native capture quality/audio policy probes
passed. Historical evidence is retained under the previous worktree's
`artifacts/discord-webview/merge-main-*` directories.

The bridge fixture reported 234/235, rejecting a valid Playnite stopped/setup
snapshot. This is recorded non-green evidence, not a Discord defect or a fully
passing bridge suite. Exact-commit CI has not run because local main has not been
pushed. Local installer preparation must not claim CI or broad physical acceptance.

For this distribution change, check Catalogue metadata/trust separation, version
ordering, documentation, the Discord regression shard and the produced package.
Build and inventory-validate both editions and the local installers. Record fresh
results and hashes alongside local artifacts; do not publish or install them.

Local preparation checks on 2026-10-28 passed: documentation (243 Markdown
files), Catalogue engine (31 cases), application updates (13 cases, including
local-version ordering), release-folder assembly and installer contracts. All
12 steps in the Discord regression shard passed, including the widget, provider,
capture policy, receiver and minified-backend fixtures. Evidence is in this
worktree's `artifacts/local-installer-checks` and `artifacts/local-discord-checks`.
These checks do not launch a live call or certify the final installer; artifact
build/inventory results are recorded separately when packaging completes.

Consumer matrix: Catalogue's Discord listing and new package metadata are updated;
legacy Discord remains source-buildable; other optional widget builders retain
unchanged publish/validate/pack behavior. SDK/runtime rendering consumers need no
migration because this step adds no shared presentation or lifecycle API.

## Resume/release boundary

Continue refining this maintained widget when specific issues arise. A future
public release needs a deliberate version/publication decision, green exact-commit
CI or an explicit disposition of failures, installer journey checks and publication
of matching immutable widget assets. Do not advertise the local candidate as a
published download. User-facing setup and limitations belong in `docs/users/discord.md`;
backend details remain in the widget README and hosted-session documentation.
