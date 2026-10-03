# Rich text and inline links

`UI.RichText` displays a native paragraph containing `UI.Text` spans and inline
intent links. It requires presentation protocol 76. It does not create a WebView
or accept HTML. Existing plain-text elements are unchanged.

```csharp
UI.RichText("answer",
    UI.Text("Move the boxes to reveal the vent.", "answer.text"),
    UI.InlineLink("¹", "answer.source.1",
        WidgetIntentRequest.Create(WidgetIntentContracts.Web,
            JsonSerializer.SerializeToElement(new { url = "https://example.com/guide" }),
            WidgetIntentPresentation.PreferExistingSurface),
        "Source 1: Guide"),
    UI.Text(" Then enter the office.", "answer.rest"));
```

Text flows within one native paragraph. Each link is a native inline hyperlink
control: short labels, including Unicode superscript numbers, work well. Link
labels occupy an inline box and can move together to the next line. Text spans
wrap normally. A link does not create a separate paragraph or row.

Links have their own stable IDs, keyboard/controller focus, native accessibility
and click behavior. Give numeric links a descriptive accessibility label. Focus
stays on an unchanged link when surrounding spans update. Activation uses the
same declared-intent admission, permissions, routing and stale-action checks as
ordinary intent buttons; there is no direct native URL-navigation bypass.

Rich text accepts 1–512 text/link spans. Nested layouts, media, icons, transitions,
per-span responsive visibility, context menus and shortcuts are not supported.
Use ordinary layout controls around the paragraph for those features. Typography
can be applied to the paragraph or text spans using the existing WRSS classes.
The SDK uses existing intent-button declarations for links inside a RichText
node, so intent feedback and pinned-surface preferences retain their normal meaning.
