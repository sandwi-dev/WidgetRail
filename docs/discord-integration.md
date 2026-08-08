# Discord integration research

Status: **research complete; implementation deferred pending Discord approval
and an explicit authenticated product decision, 2026-08-08**.

Discord is not currently a bundled widget, public capability, broker provider,
or community SDK example. Do not infer a Discord capability ID or open the
desktop client's private storage/IPC from widget code. The useful social and
voice surfaces all require user authentication, application registration, or
Discord production approval, so they are outside the current no-third-party-
login implementation queue.

## Capability matrix

| Desired overlay feature | Official route | Current conclusion |
| --- | --- | --- |
| Identify the linked user | OAuth2 / Social SDK account linking | Feasible only after an explicit user authorization flow and token lifecycle. |
| Friends and presence | Discord Social SDK presence scopes | Technically supported for game integrations, but requires Social SDK integration and account linking. It is not exposed by ordinary `identify` OAuth as a general user-friends endpoint. |
| Application-owned parties, invites, text, and voice | Discord Social SDK lobbies/communications | Feasible for a game-owned lobby. Communications have development rate limits and production access is reviewed. This does not mirror arbitrary existing Discord desktop calls. |
| Inspect or control the desktop client's selected voice channel, mute, deafen, devices, or participant state | Local Discord RPC | The documented commands/events are a strong technical match, but RPC authorization is restricted to approved applications or named testers during development. It is not a generally distributable unauthenticated contract. |
| Ordinary bot integration | HTTP/Gateway bot APIs | Useful inside servers where the bot is installed; it does not become the signed-in desktop user or provide their complete friends/DM/current-call surface. |
| Open Discord home/channel/message/invite links | Windows launch/deep-link surface | A small launchpad could be built, but it would not satisfy the requested friends/party/voice widget and is not worth a first-party integration by itself. |

This matrix is deliberately conservative. The standard [User
resource](https://docs.discord.com/developers/resources/user) documents current
user, guild, and linked-account endpoints but no general OAuth endpoint for a
user's complete friend graph or current desktop call. That absence plus the
separate Social SDK/RPC contracts is why ordinary OAuth or a bot is not treated
as a shortcut to the desired UI.

## Social SDK boundary

Discord documents default Social SDK presence scopes for account linking,
friends/relationships, and presence. Communication features use a broader
scope and are explicitly limited-access. See [OAuth2 scopes for the Social
SDK](https://docs.discord.com/developers/discord-social-sdk/core-concepts/oauth2-scopes).

The Social SDK voice guide creates or joins voice inside an application lobby;
voice participants must first be lobby members. It supports application-call
mute/deafen/volume and participant state, but it is not documented as a remote
control for the user's unrelated existing Discord call. See [Managing Voice
Chat](https://docs.discord.com/developers/discord-social-sdk/development-guides/managing-voice-chat).

Development communication limits are per application. Production increases
require an application and supporting-material review, including account
linking, Rich Presence/joins, a full friends surface, end-to-end negative flows,
and age safeguards. Discord may approve or deny the request. See [Communication
Features](https://docs.discord.com/developers/discord-social-sdk/core-concepts/communication-features).

That is a poor fit for a general-purpose Windows overlay unless Discord first
confirms that this non-game shell is eligible and the product accepts the full
account-linking/review obligation.

## Local RPC boundary

Discord's documented local RPC exposes selected voice-channel, voice-state,
voice-settings, relationship, and speaking events/commands over its desktop IPC
transport. This is the closest official surface to the originally requested
"friends, parties, and voice chat" control-center widget. It still requires RPC
authorization. Discord states that unapproved applications are usable only by
people on the application's tester list (currently up to 50); approval removes
that restriction. See [Discord RPC](https://docs.discord.com/developers/topics/rpc).

The overlay must not ship a widget that reads an undocumented Discord pipe,
tokens, LevelDB/profile data, or client internals to evade those restrictions.
That would be brittle, unsafe, and unsuitable for a public plugin platform.

## Architecture if access is granted

If Discord confirms eligibility, implementation should follow the same public
widget boundary used by other integrations:

1. A trusted, lazy Discord provider owns OAuth/RPC/Social SDK state and secrets.
2. A closed versioned capability vocabulary separates identity/presence read,
   party/lobby actions, and voice controls. No generic Discord or socket grant
   is acceptable.
3. Settings shows declarations and explicit user consent; Discord's own OAuth
   remains an independent required authorization.
4. The widget receives sanitized bounded DTOs and opaque IDs, never Discord
   access tokens, client secrets, pipe handles, or raw SDK objects.
5. Subscriptions run only while Visible/Interactive unless a separately
   reviewed background use is essential. Voice mutations are Interactive or
   one exact declared dashboard gesture.
6. Disconnect/revoke clears provider tokens and cancels streams without
   deleting unrelated Discord state.
7. A simulator and contract tests land before the native Discord SDK/provider,
   so Community authors do not need private workarounds.

No capability names are reserved yet. Authentication, eligibility, licensing,
redistribution, rate limits, privacy, and resource measurements must be settled
before an SDK contract is frozen.

## Decision

Do not implement Discord in the current local-only roadmap. Revisit only after
one of these external gates is satisfied:

- Discord approves the overlay for broadly distributable RPC access; or
- Discord confirms Social SDK eligibility for this general-purpose overlay and
  the project accepts account linking plus the production communications
  review.

Until then, prioritize locally testable, authentication-free platform work.
