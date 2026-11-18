# Selected files and explicit sharing

`HostServices.Files` provides read-only file references for attachments. It is a
host-granted service (`files.references.v1`), not a manifest permission for arbitrary
filesystem access. References contain an opaque ID, display name, content type,
byte length and expiry; no host path is exposed to the widget.

## Obtain and read a file

Use `ChooseAsync` in an interactive action to open the existing native host file
picker. Cancellation returns an empty list. The host binds completion to the
request and exact worker owner; a path embedded in a widget payload never grants
access. Selection is limited to 16 files, each at most 64 GiB. A provider can impose
smaller limits before upload.

The native widget picker shares browser uploads' shell input/focus handoff and
the existing Windows common-dialog cancellation lifetime. The frontend binds it
to the requesting widget view or active pin, rechecks broker authority while it
is open, and closes and joins the actual dialog on cancellation or teardown.
Late completion cannot select files for a newer request. Selecting more than
16 files shows a limit message and cancels; files are never silently dropped.

`FromCaptureAsync` retains a reference from an existing, owned `CaptureAttachment`.
Keep that capture alive until a share is accepted or canceled. Before acceptance,
revoking its source authority invalidates pending offers. The API reuses the
original read-only file; it does not copy a full recording into RAM or temporary
storage.

`OpenReadAsync` returns a seekable read-only `Stream` with a long `Length`. Reads
transfer at most 64 KiB. Stream disposal cancels pending reads but does not release
the file reference: a composer may reopen it for an explicit retry. Call
`ReleaseAsync` when removing an attachment or finishing its use. Worker retirement
also retires all its remaining references. Idle references expire after 15 minutes;
successful reads refresh that lease. Metadata is descriptive, not a guarantee that
an external provider accepts the file's type or size.

## Transfer authority through an intent

Create an offer with `CreateShareOfferAsync` and pass `{ "offerId": offer.Id }`
through `WidgetIntentContracts.Files` (version 1, ID `WidgetIntentContracts.FileShare`,
`widgetrail.files.share`). Declare that exact standard contract in the sender's
requests and recipient's handlers. An offer is not a readable token. The bridge checks
the source, selected destination and current worker run using the existing intent
delivery path before authorizing receipt. The recipient calls `AcceptShareAsync`
from its intent handler to obtain references belonging to its exact worker.

File-share discovery is catalog-only: opening Share does not start every matching
receiver. The host starts/presents only the selected destination. If its mapping
declares dynamic availability, that callback is used for selected-target
validation, not for populating the chooser. All receiver admission and file-grant
checks still apply. Prefer accepting a cancellable local pending-share view while
initial account data loads; never upload or send merely because the handoff was
accepted, and do not replace an existing draft.

Offers expire after 60 seconds. Canceled/rejected/stale deliveries discard their
provisional recipient references. After acceptance the recipient's explicit per-file
grant is independent: sender idle, uninstall, source-capture release and broader
linked-folder revocation do not remove the accepted file. Receiver retirement,
explicit release and idle expiry still do. The host keeps a read-only handle to the
same file, so a later path replacement cannot redirect the accepted reference.
Sharing never modifies or deletes the original.

The receiver owns provider policy, destination selection and the final Send action.
Receiving an attachment intent does not authorize sending to a contact. Prefer the
stream API with a bounded upload transport; do not assemble the whole file into a
byte array or use a path from intent JSON as read authority.

Each incoming share should begin with destination selection, even when the receiver
has an existing unsent draft. Keep new files separate until that choice is made.
Then merge only into the selected destination's compatible draft, preserving its
text and skipping duplicates by supported file identity rather than filename guesses.
Canceling destination selection releases only that incoming share. It must not
erase another conversation's draft or reuse its recipient automatically. Attachment
limits and a send in progress need truthful feedback; an ordinary unfinished draft
is not a reason to report that the destination is unavailable.

## Ownership and applicability

| Consumer | Applicability |
|---|---|
| Capture Library | Source of an explicitly selected capture share |
| Discord widget | Recipient and interactive arbitrary-file picker consumer |
| Game Help | Existing capture/provider document workflow remains unchanged; adopt only when an explicit file-share workflow is added |
| Playnite and other widgets | May offer owned captures through the same contract; no provider-specific host code is required |
| Templates and SDK examples | Existing consumers remain compatible; the service is additive and old hosts reject unavailable capabilities |

The native picker and transfer commit are trusted host operations. File references
do not change navigation, collection caching, media playback or document rendering.
