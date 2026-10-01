# Hosted browser (in development)

The protocol, SDK declaration and Browser intent receiver are implemented. The
native WebView owner, controller interaction and pinned placement are still being
connected. This component is not enabled in a user candidate yet.

```csharp
UI.WebBrowser(new WebBrowserDocument(
    "guide.page", navigationId, "https://example.com/guide", "Game guide"), "guide.browser");
```

This is a reusable host component, available to widget authors as an ordinary
element. Its native navigation chrome is shared so each widget does not need to
reimplement browser input, history and current-page status. The Browser sample
provides address entry and an intent handler; it does not render HTML itself.

The declaration requires protocol 66. Keep the document ID stable across
compatible presentations. Advance the navigation ID only for a new authored
navigation, including deliberately reopening the same URL. Re-rendering must not
reload a page, erase its history or move its scroll position. Native history and
page contents stay host-owned and are not sent to the widget.

URLs are bounded to 2,048 characters and must be absolute HTTP(S), without
credentials, surrounding whitespace or control characters. IDs follow the normal
identifier contract. Navigation IDs are positive integers up to 2^53 - 1. An
accessible label is required and bounded to 256 characters. The element has no
widget action, script, HTML, DOM, native-handle or host-object payload. Navigation
changes carry resource and authority impact in atomic presentation updates.

The Browser widget handles `widgetrail.web.open` and opts that mapping into
passive delivery. It starts empty and declares no browser until an accepted
intent or address-entry action. Invalid requests preserve the existing document.
Its current SDK address-entry control retains the framework's 96-character
limit; longer addresses are accepted through intents. Revisit general text-entry
bounds for browser addresses and the Game Help composer before candidate release.

Native integration requirements remain: lazy isolated profiles, bounded resident
controllers, exact worker/document lifetime, safe cross-XAML-root transfer,
host-owned Back/Forward/Reload/Stop/Open externally controls, controller cursor and
scroll, ordinary B behavior, and no host scripting bridge. The browser must stay
separate from the sealed embedded-media adapter and must not weaken that adapter's
origin or resource restrictions. Site permissions, downloads, external schemes
and unexpected popup windows need explicit host policy before qualification.
