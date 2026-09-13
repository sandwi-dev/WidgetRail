# Worker-authority recovery

The host records worker file-access cleanup failures in its own authority journal.
Recovery retries a recorded operation; it is not a general permission editor.

## Review a recorded failure

Use **Settings → Diagnostics** for user-facing recovery. The confirmation's
initial focus is **Cancel**. The UI never renders or speaks the opaque confirmation token.

For maintenance, the CLI offers:

```text
wrail authority-recovery list
wrail authority-recovery retry <confirmation-token-from-list>
```

The retry token refers to one reviewed journal entry. A stale token must be
rejected rather than applied to changed content.

## Scope

It does not accept a journal path, content path, security descriptor, raw clear,
or force option. It works only with the product-owned journal and verified
recorded identities. Community widgets cannot request this channel.

Do not edit a journal or file ACL manually to make a check pass. Keep the safe
error and exact build identity when escalating a failure. See
[Diagnostics](diagnostics-and-recovery.md) and the
[runtime source](../../src/WidgetRuntime/AppContainerAuthorityJournal.cs).
