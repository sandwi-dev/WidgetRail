# Discord

Discord provides controller-friendly messages, voice channels, call and stream
audio controls, application sharing, friend activity and voice/video pins. Discord
sign-in and media run in a persistent WidgetRail WebView2 session; no separate
Discord desktop client, Vesktop or Vencord installation is required.

> **Unofficial client modification:** this integration modifies Discord's hosted
> web client and uses private APIs. It may violate [Discord's Terms of Service](https://discord.com/terms)
> and put your account at risk of restrictions or termination. Use it at your own
> risk. WidgetRail is not affiliated with or endorsed by Discord, and cannot
> guarantee continued access or compatibility. It does not patch the installed
> Discord desktop app.

## Install and sign in

Install the matching WidgetRail host update first. The new package is
`widgetrail.samples.discord-controls`, shown as **Discord**.
It requires explicit full-access approval because it runs as a normal application
under your Windows account. Microphone and application capture still have their
own permission prompts.

For the current local testing build, use the supplied `.wrwidget` file through
**Settings → Widgets** after updating WidgetRail. No public release has been
published for this package yet: its Catalogue listing becomes downloadable only
when a matching release asset is published. Do not expect the older public
Discord download to contain these features.

Open Discord in the widget and sign in using Discord's page, including QR sign-in
where offered. Join a voice channel and grant microphone access when prompted.
Do not copy sign-in files from another client or the earlier isolated prototype.

The old `widgetrail.samples.discord` widget is a sandboxed website wrapper.
Changing it into a full-access application is intentionally not an ordinary
update, so the new integration has a different identity. Existing installs and
profiles remain intact. Sign in once in the new widget, then disable or remove
the older widget if you no longer need it. The older webapp remains buildable
from [its source](../../samples/DiscordWidget/README.md).

Choose **Enable notifications** on the native controls page to receive Discord’s
website notifications in WidgetRail. Choose **Remember for this profile** to keep
the decision across restarts. The native widget and the older website wrapper have
separate Discord profiles. A saved block can be changed in **Website settings**.

Setup also checks Discord's separate **Enable Desktop Notifications** setting.
After permission is granted, it enables that setting when the current Discord
client supports it and confirms the result. Existing notification preferences
are preserved. If it cannot confirm the setting, follow the displayed instruction:
**Discord → User Settings → Notifications → Enable Desktop Notifications**.
Permission alone does not enable Discord's own notifications.

## Messages and attachments

Open **Messages** and select a conversation. Search is above the conversation
list, which loads more rows as you scroll. In the expanded layout, move right
from the list to the composer and left from the composer or history to the
selected conversation. In the compact layout, Back returns from chat to the
list. Back from the list returns to WidgetRail's chooser. **X** focuses the
composer and **Y** goes to the latest messages. The top bar has **Refresh** and
**Open Discord**.
Switching to Servers and back preserves your conversation and draft.

Conversation rows show snippets, timestamps, unread indicators and call activity
when available. Presence appears on the avatar as a filled circle for online, a
crescent for idle, a minus for do-not-disturb, or an outline for offline. An
avatar without an indicator has no reported status.

Opening a chat updates your Discord unread marker through the latest message
known when you opened it, once history appears. Messages that arrive while you
read older history stay unread until you reach the latest messages. Opening a
notification for an older message preserves newer unread messages. If you hide
the overlay while a chat is loading, it stays unread until you open it again,
even if a call pin is visible. If Discord cannot update the marker, use
**Open Discord**.

After Discord confirms a send, the chat scrolls to that message and focuses the
composer for your next reply. This also works when you send from older history.
An unconfirmed send leaves your reading position in place until you check delivery.

Attach files beside the composer. When another widget shares files to Discord,
choose a recipient and then **Send**. Canceling an upload keeps the draft and
files. If delivery is uncertain, choose **Check delivery** before trying again.
When Discord confirms your send, the open chat scrolls to that message and
keeps the composer available for your next reply. This also works while reading
older history. Incoming messages leave your position alone while you read back.

A single video has an inline **Play** button. The first playback starts muted.
WidgetRail remembers your playback audio settings for subsequent videos in the
Discord widget. Leaving the chat or scrolling the player offscreen pauses it.
GIFs play inline while
visible and stay still with reduced motion. On the tested Windows decoder,
animated PNG and WebP display a static frame.

Select an image or attachment group to open the gallery. The group shows up to
two thumbnail tiles, with `+N` for additional items. A video accompanied by
other attachments uses the gallery. Spoiler media stays concealed until you
reveal it. When a thumbnail is unavailable, a media icon identifies the item.
Move between attachments with LB/RB or the arrows. Image zoom controls sit
below the picture. The arrows stay available in fullscreen, and B or Escape
returns from fullscreen. Links open
from their inline text or link card.

## Calls and controls

In a conversation, choose **Call** to start a private call, or **Join call** when
one is already active. Incoming invitations show **Answer** and **Decline** in
the native widget. Answering or starting a call uses the existing microphone
consent prompt; declining does not need microphone access. **Switch & answer**
or **Switch & call** indicates that the action will replace your current call.
Use **Leave** to disconnect. Private calls reuse the same audio, sharing and pin
controls as server voice channels.

Incoming calls use WidgetRail's **Incoming calls** notification category. Opening
an alert goes to the native call controls; it does not answer automatically.
Alerts are withdrawn when ringing ends. Configure banners/list delivery in
WidgetRail's notification settings. Synthetic native tests cover incoming alerts,
Open, Answer, Decline, outgoing calls and Leave at compact and expanded sizes.
Real peer calls, microphone/device audio and physical-controller acceptance
remain to be checked; synthetic calls do not establish those results.

Use **Servers / Messages** at the top, or LB/RB, to switch destinations. Open child
controls keep their own button bindings. During a call, the bar beneath Messages
shows participant avatars. Select the bar to return to the call, or use its
Mute, Deafen, Share and Leave controls.

Without a call, the home view shows the server/channel picker. Search and keyboard
suggestions include both server and voice-channel names. Favorites and recent
channels are saved per account.

On Servers during a call, Xbox **X** toggles your microphone, **Y** starts sharing
when available, and **L3** opens the server picker. In a chat, **L3** focuses the
header's Call/Join/Answer button without starting or answering a call. Follow the
on-screen hints for your controller.
Hover or focus another participant to preview local voice volume and mute. Click
a preview control directly, or press X (F6 on keyboard) to enter. B/Escape finishes
slider adjustment first, then returns to the participant.
**Menu → Voice settings** selects microphone and output devices. Input gain is
unavailable because the current browser engine does not apply it.

Select the call's connection status or **Menu → Connection details** to inspect
available ping measurements and a short history. When watching a screen, the same
panel distinguishes Discord's incoming video from playback in the pin. Measurements
update only while the panel and overlay are open. An unchanged picture is reported
as idle or waiting, not automatically as a failure. Use **Refresh** to measure again
or **Reconnect pin** to explicitly retry the selected screen. The video pin remains
unobstructed. Ping is not a measurement of end-to-end audio latency.

## Watch, pin and share

Under **Screens in this call**, choose **Watch** to view the stream inside the
widget or **Watch & pin** to put it in a floating window. In the viewer, focus
the picture and press A, or click **Fullscreen**. B or Escape returns to the
viewer. Back returns to the same shared-screen row. If the stream is already
pinned, the viewer borrows its picture. Closing the viewer or hiding the overlay
returns the picture to the pin. If you chose **Watch** without pinning that
stream, hiding the overlay pauses its picture and sound. Returning to the
viewer resumes the live stream. Other pinned streams keep playing.

If the sender stops sharing while you’re in fullscreen, the widget returns to
the viewer and focuses **Back**. Choose it to return to the call.

The viewer and the shared-screen card expose mute and volume when Discord supplies
them. These controls affect stream audio independently of participant voice volume.
Deafen also silences received audio. Audio plays once through Discord.

Use the pin-adjustment button beside a pinned item for movement, opacity and unpinning.
The host outlines the matching window. Video and participant-only pins remain
passive; voice-control pins also offer Interact. The eye beside a participant means
that person is watching **your** stream. LIVE separately indicates that they are
broadcasting; watching their stream does not give them an eye indicator.

The separate voice-controls pin exposes Mute, Deafen and Leave, with participant
avatars and speaking indicators. Hiding the overlay keeps an active call,
incoming pin and admitted application share running. To stop them, use Leave,
stop watching or Stop sharing; closing WidgetRail ends its hosted session.

Sharing requires selecting an application and explicitly choosing whether to
include application audio. Available quality options depend on Discord's account
and call eligibility, up to 1080p at 60 FPS. The default is 720p30. Discord
remembers the selected resolution and frame rate and always requests clarity.
WidgetRail shows the capture badge on the widget icon while the host captures.
**Menu → Stream details** distinguishes captured pixels, outbound
resolution/FPS and Discord's advertised quality label.

Requested quality is not a delivery guarantee. Source size, motion, encoder load
and network conditions can reduce quality or FPS; motion noise and frame drops
remain known limitations. No experimental higher-bitrate or forced-codec options
are included in this accepted baseline.

## Recovery and current testing status

Open Discord's page to check sign-in or connection problems, then Refresh the
controls. Private provider APIs can change independently of WidgetRail. An
unavailable control or unconfirmed action must not be treated as a successful
join/share. Stop calls and sharing before clearing browsing data or uninstalling.
Sign out through Discord's page when needed; uninstalling a widget does not delete
your Discord account. See [lifecycle and saved data](lifecycle.md).

On 2026-10-28 the user accepted the merged behavior for continued everyday testing:
it was working for them, with further issues to be assessed through use. This is
not a claim that every audio device, controller, game or long-running session has
been exhaustively qualified. The desktop-client migration was cancelled; the
WebView2 integration described here is the maintained direction.
