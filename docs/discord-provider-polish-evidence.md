# Discord provider polish evidence

## Native-history conversation snippets — November 19

The parent agent observed a conversation row retaining its generic description
after native history had loaded. Source inspection found that
`messaging-provider.js:lastMessageMetadata` consulted only Discord's resident
`MessageStore`. The native widget's existing REST-history path populates the
provider's bounded `messageCache`, not that resident store. This is a provider
data-projection gap; the host cannot infer which fetched message is the
conversation's current head.

The provider now falls back to its already-fetched message only when its ID and
channel exactly match the head supplied by Discord, and the current watch is
for that account and channel. Resident exact-message data still takes priority.
The same normalization handles raw resident and normalized provider author,
attachment, animation and sticker fields. History enrichment increments the
existing catalog revision only if the visible summary changes. It does not
increment the message revision or invent a new-message event.

No extra request, summary cache, polling or lifecycle mechanism was added. The
existing 200-message cache bound, watch retirement and account clearing remain.
Consequently an evicted head or a retired visit falls back to unknown when the
resident store also lacks it; this change does not claim persistent snippets for
every previously opened chat. A second cache was not justified for this bounded
fix because it would need independent edit/delete and account reconciliation.

## Article/link artwork — November 19

`messaging-provider.js:embeds` previously projected every supplied image or
thumbnail as independently previewable media. An ordinary article then exposed
both its URL card and an attachment action in the native widget.

Explicit `article` and `link` embeds with a valid destination URL and no playable
video now retain their title, description and URL while omitting decorative
image media. Real `image`, `gifv` and `video` semantics remain unchanged, as do
provided posters for playable videos and image-animation flags. `rich` and
unknown embed types remain unchanged because their images can be intentional
content; do not broaden suppression without evidence about those cases.

This belongs in the Discord provider's semantic projection, not shared gallery
or host code. The existing managed gallery consumes the normalized image/video
fields and requires no new public contract or widget-side presentation filter.
Delayed `MESSAGE_UPDATE` changes can both admit actual media and retire
decoration while retaining the URL destination. No image URL is fetched or
rewritten by this normalization.

## Validation and applicability

`artifacts/discord-call-media-polish/provider-polish-1/raw.log` and `minified.log`
each pass **248 tests** across messaging provider, adapter and session backend.
The focused messaging suite passes 69 tests. New checks cover:

- exact-head enrichment, normalized author ownership and one catalog update;
- repeated identical history with no redundant invalidation;
- nearby/wrong-channel messages, replaced accounts and late retired-watch reads;
- reconciliation of a newer edit over a delayed history response;
- existing cache eviction and spoiler-safe media summaries;
- decorative article/link artwork, actual image/GIF/video preservation, and
  delayed semantic replacement without additional network requests.

| Consumer | Applicability |
|---|---|
| Discord native conversation list | Adopts exact available history-head metadata through the existing provider response. |
| Discord native chat and gallery | Article/link media omission uses existing nullable-media semantics; no managed/UI edit. |
| Discord hosted adapter and session backend | Raw/minified tests pass with the existing message contract. |
| Other widgets, SDK, protocol and host | Not applicable: no shared code or protocol changed. |

These are provider behavior checks. No desktop action, native screenshot,
managed build, package update or live service request was performed by this
subtask. Final live appearance and native navigation remain the parent task's
acceptance gates.
