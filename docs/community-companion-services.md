# Local companion HTTP and private secrets

Status: implemented typed SDK, broker, and Windows provider foundation,
2026-08-07. YT Music is the first migration consumer; consult its [reference
README](../samples/YtMusicWidget/README.md) for the addon workflow and the
[known-issues ledger](known-issues.md) for product evidence still open.

Community workers have no ambient network capability and cannot access Windows
Credential Manager. These host services cover the narrower recurring case of a
widget talking to one local desktop companion without exposing a raw socket or
persisted bearer value:

- exact-port, IPv4-loopback, JSON-only HTTP; and
- package-scoped, write-only private secret slots with host-side Bearer
  injection.

They are independent permissions. Bearer injection requires both.

## Manifest declarations

Declare one dynamic capability for each exact local port. Ports 1024–65535 are
valid; a range, host name, scheme, URI, or wildcard is not.

```json
{
  "permissions": [
    "network.loopback:13091"
  ],
  "optionalPermissions": [
    "storage.private-secrets.v1"
  ]
}
```

Each exact port appears as its own controller consent item. Private secrets are
a separate consent item. Required declarations are still blocked until the
user grants them. Put the vault in `optionalPermissions` when an unauthenticated
companion remains useful; pairing/persistence must then show a bounded denied
state rather than silently storing plaintext.

## Typed SDK

Use `HostServices.Loopback`; do not construct broker messages or operation IDs.

```csharp
var publicState = await HostServices.Loopback.GetJsonAsync(
    13091,
    "/track/state",
    cancellationToken: activeLifetime);

var command = await HostServices.Loopback.PostJsonAsync(
    13091,
    "/track/next",
    "{}",
    new WidgetLoopbackRequestOptions
    {
        BearerSecretSlot = "companion.bearer",
        InvalidateBearerSecretOnUnauthorized = true,
        Timeout = TimeSpan.FromSeconds(10),
        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Client-Version"] = "1",
        },
    },
    cancellationToken);
```

`GetJsonAsync(int port, string path, WidgetLoopbackRequestOptions? options,
CancellationToken)` and `PostJsonAsync(int port, string path, string jsonBody,
WidgetLoopbackRequestOptions? options, CancellationToken)` return
`WidgetLoopbackJsonResponse` with `StatusCode`, `JsonBody`, and bounded response
headers. The SDK validates arguments before IPC; the broker and trusted
provider validate again.

Private secrets expose presence and metadata, never values:

```csharp
const string slot = "companion.bearer";

if (!await HostServices.PrivateSecrets.ExistsAsync(slot, cancellationToken))
{
    var token = await CompleteCompanionPairingAsync(cancellationToken);
    await HostServices.PrivateSecrets.SaveAsync(slot, token, cancellationToken);
}

var metadata = await HostServices.PrivateSecrets.GetMetadataAsync(
    slot, cancellationToken);

await HostServices.PrivateSecrets.DeleteAsync(slot, cancellationToken);
```

`SaveAsync` creates or replaces one slot. `DeleteAsync` is idempotent.
`GetMetadataAsync` returns `Exists` and, when present,
`LastWrittenUnixMilliseconds`. There is deliberately no read/get-secret API.
After `SaveAsync`, discard the caller's in-memory token as soon as practical;
never render, log, cache, serialize, or place it in widget state.

## Lifecycle, consent, and dashboard actions

| Operation | Normal lifecycle | Dashboard gesture |
| --- | --- | --- |
| Loopback GET | Visible or Interactive | No |
| Loopback POST | Interactive | One exact declared action while Visible |
| Secret exists/metadata | Visible or Interactive | No |
| Secret save/delete | Interactive | No |

A dashboard POST is not ambient Visible control. The host reserves one input
sequence for one declared quick action and activates a short single-use lease
only when the worker invokes that exact operation. A queued callback cannot use
the lease for another port or operation.

When `BearerSecretSlot` is present, the broker obtains a second secret-service
lease and holds both leases for the complete request. Missing declaration,
denied consent, lifecycle loss, revocation, or expiry fails or cancels the
request. The worker never receives the stored value; the trusted provider reads
it for that request and sets `Authorization: Bearer …` itself.

Set `InvalidateBearerSecretOnUnauthorized = true` only when the companion uses
HTTP 401 to reject that injected credential. The option is invalid without
`BearerSecretSlot`. On an actual 401, the trusted provider deletes that exact
authenticated-authority/package/slot before returning the response while the
dependent secret lease is still held. This narrow host-side hygiene path does
not grant a Visible widget general secret-delete authority. It uses lifecycle/
consent cancellation rather than the shorter HTTP deadline; deletion failure is
surfaced instead of returning a 401 while the rejected durable credential
remains. Clear local connection state, but do not issue a second `DeleteAsync`.

Tie reads and polling to the shared Visible/Interactive lifetime. Run secret
writes only from an Interactive controller action. Treat lifecycle cancellation
as normal teardown. Do not retry consent, declaration, lifecycle, or secret-
not-found failures on a timer.

## HTTP security boundary

The Windows provider constructs `http://127.0.0.1:<declared-port>`. It forces
IPv4 loopback through a socket connect callback and never accepts a host,
authority, scheme, DNS result, IP address, or URI from the widget. It uses
HTTP/1.1 and disables:

- proxies and environment proxy inheritance;
- redirects;
- cookies;
- automatic decompression; and
- arbitrary sockets or connection handles.

Paths must be origin-form, begin with one `/`, and contain no authority (`//`),
backslash, fragment, CR/LF, or control character. Only JSON GET and POST exist.
Request headers cannot set authorization, host, cookie, connection, proxy, or
HTTP framing/hop-by-hop fields. Authorization is available only through a
declared secret slot.

Responses are streamed into a hard limit and decoded as strict UTF-8 JSON. A
non-JSON 2xx response is rejected. A non-JSON non-2xx body is replaced with
`{"error":"non_json_response"}` so a companion cannot reflect a private path,
stack trace, or HTML error through the broker. Returned headers are limited to
`Content-Type`, `ETag`, `Last-Modified`, `Retry-After`, and `X-RateLimit-*`.

This authority does **not** include internet, LAN, IPv6 loopback, WebSocket,
server/listen, TLS exceptions, file URLs, arbitrary HTTP methods, form or
multipart bodies, streaming to widget code, raw bytes, client certificates,
Windows integrated authentication, or a general OAuth broker. Add a reviewed
capability instead of widening this one.

## Limits

| Input or resource | Limit |
| --- | --- |
| Port | 1024–65535; one declaration per exact port |
| Origin-form path | 2,048 characters |
| Request headers | 16; name 64 characters; value 1,024; 8,192 total name/value characters |
| Request JSON | 16 KiB UTF-8 |
| Response JSON | 96 KiB UTF-8, enforced while streaming |
| Request timeout | 10 seconds default; 40 seconds maximum |
| Secret slot | 1–64 ASCII letters, digits, `.`, `_`, or `-` |
| Secret | 1–2,048 UTF-8 bytes; NUL rejected |
| Concurrency | Two loopback requests per widget broker; eight globally in the Windows provider |

Only validated loopback operations can extend the ordinary three-second broker
pipe deadline. Their IPC deadline is the requested provider timeout plus a
fixed two-second completion budget. Cancellation remains effective while
waiting for either concurrency gate, connecting, sending, or reading.

## Secret identity and persistence

The Windows provider stores a Generic Credential under a SHA-256-derived target
that binds authenticated publisher authority, package ID, and slot. The
instance/version ID is excluded so an authenticated update from the same
publisher/package authority can retain pairing. Another publisher, package, or
slot receives a different target, and target names do not disclose identifiers.

The provider contract deliberately depends on **authenticated** publisher
authority, not manifest publisher text alone. For today's unsigned packages,
that authority is the host-verified content-tree digest: changed bytes receive a
different secret namespace even if publisher/package/version text is reused,
while rollback to the exact verified bytes regains the same namespace. Future
signed publisher authority may intentionally retain secrets across authenticated
updates. Publisher signing/revocation and uninstall/profile cleanup still need
product policy before public distribution, so document unsigned updates as
re-pair boundaries and do not promise uninstall cleanup yet.

## Errors and recovery

Catch `WidgetCapabilityException` and branch on `ErrorCode`:

| Code | Meaning / author response |
| --- | --- |
| `capability_not_declared`, `invalid_declaration`, `unsupported_capability` | Fix and repackage the manifest/API use; do not retry. |
| `permission_denied`, `capability_revoked` | Explain the disabled feature; let the user review Settings. |
| `lifecycle_denied` | Move the call to the documented lifecycle/action. |
| `invalid_payload` | Fix SDK inputs; do not expose raw request data. |
| `platform_unavailable`, `secret_store_unavailable` | Show bounded provider-unavailable state and permit explicit retry. |
| `secret_not_found` | Return to pairing/auth-required state. |
| `invalid_secret` | Delete/replace the slot through an Interactive recovery action. |
| `loopback_timeout`, `loopback_unavailable` | Keep cached UI and show a short local-companion status. |
| `response_too_large`, `invalid_response`, `invalid_backend_data` | Treat the response as invalid; never render its raw body. |

Unknown future codes are a generic bounded provider failure. Cancellation from
the supplied active/action token is normal. HTTP status is not a broker error:
inspect `StatusCode`, parse only understood bounded JSON, map 401 to pairing/
authorization recovery, and keep raw service bodies out of logs and UI. When
the request opted into rejected-Bearer invalidation, a returned 401 guarantees
the exact durable slot was deleted first; otherwise deletion failure is the
typed request error.

## Testing pattern

`WidgetTestHostServicesBuilder` accepts handlers for
`WidgetLoopbackCapabilities.GetJson(port)`, `PostJson(port)`, and the four
`WidgetPrivateSecretCapabilities` operations. Assert exact port, path, JSON,
optional bearer **slot name** (never a token), timeout, lifecycle cancellation,
unauthorized-invalidation flag, and safe UI state. Assert that 401 removes only
the exact scoped slot before the response and that widget code does not perform
a second delete. Provider tests should use a bounded local listener and fake
secret store; remove real Credential Manager test values in `finally`.

Package acceptance must still use `gbar pack`, install into a Community
catalog, grant both declarations through Settings, start the generic worker in
its package AppContainer, and exercise the authenticated broker. A direct
`HttpClient` unit test does not prove the Community isolation boundary.
