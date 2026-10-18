# Whole-entry text suggestions

Use optional suggestions for a gem name, app title, saved location, or recent
search. This is whole-field completion: selecting a suggestion replaces all text,
leaves the keyboard open, and does not execute an action. Done sends the existing
final-value action; Cancel sends nothing. Arrival never moves focus to a choice.

![PoB gem suggestions in the host keyboard](../images/text-suggestions.png)

```csharp
private readonly TextSuggestionCatalog catalog = new(new[]
{
    new TextSuggestion("fireball", "Fireball", "Fireball", "Skill gem"),
    new TextSuggestion("fire-trap", "Fire Trap", "Fire Trap", "Skill gem"),
});

// In Render(), keep this key stable until the catalog or eligibility changes.
// UI.TextEntry(search, "Gem name", "search", "gem-search", 100)
//     .Suggest("gem-catalog-v1");

public override ValueTask<TextSuggestionResult> OnSuggestTextAsync(
    TextSuggestionQuery query, CancellationToken cancellationToken)
{
    if (query.FieldId != "gem-search" || query.SourceKey != "gem-catalog-v1")
        return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
    return ValueTask.FromResult(catalog.Search(query, cancellationToken));
}
```

`Suggest(sourceKey, minimumLength: 1, maximumResults: 6)` opts in. Use an opaque
source key and change it when the catalog, filter, or eligibility changes. Never
change it on every render. Providers receive transient editor text, unlike ordinary
TextEntry actions, so do not persist it, log it, or invalidate the view per edit.
The callback runs independently of the serialized action lane. Capture a small
immutable index under your state lock, then search outside it. Do not invoke the
calculation engine per keystroke. Honour cancellation for asynchronous providers.

`TextSuggestionCatalog` is an optional immutable helper for up to 10,000 entries.
It sorts once, then returns case-insensitive prefix matches before substring
matches. Search the whole query; it does not split words. A provider that already
ranks matches can return its own results; the host preserves their order and does
not filter them again. No external fuzzy-matching package is required.

## Host defaults and limits

| Setting | Host behavior |
|---|---|
| Pacing | 180 ms typing pause; at least 250 ms between request starts |
| Work | One active query and one replaceable pending text value per editor |
| Results | Six by default, at most eight; no suggestion scrolling |
| Text | At most 512 UTF-16 units, also bounded by the field maximum |
| Labels/descriptions | At most 96/128 UTF-16 units; no control characters |
| Provider deadline | Two seconds; failure shows unavailable and Done remains usable |
| Late replies | Session, revision, source, field, scope and owner revalidated |
| Provider misuse | Invalid/duplicate/oversized output rejected; one worker callback at a time, even after timeout |

Timeout sends a terminal result promptly but keeps worker capacity reserved until
the actual callback finishes. Ignoring cancellation cannot accumulate background
callbacks. The bridge also limits concurrent queries to one per widget and eight
globally. Suggestions never start an unloaded worker. A custom worker that fails
to respond to protocol cancellation can be terminated by the existing process
boundary; provider errors alone do not terminate a healthy worker.

Sensitive fields, pinned fragments, indexed-item text fields and browser live-edit
keyboards do not support suggestions in this version. Ordinary widget fields do.
Per-word prediction, token/range replacement, fuzzy matching and submit-on-choice
are intentionally separate from this API. They require caret/token semantics and
different user expectations; do not emulate them by silently changing this contract.

## Design references

- [WinUI AutoSuggestBox](https://learn.microsoft.com/en-us/windows/apps/design/controls/auto-suggest-box)
  separates editing, choosing and submitting. WidgetRail keeps that separation.
- [MUI Autocomplete](https://mui.com/material-ui/react-autocomplete/)
  recommends throttling async search and avoiding duplicate filtering of server results.
- [Qt QCompleter](https://doc.qt.io/qt-6/qcompleter.html)
  exposes distinct prefix/path completion policies. Token completion should be
  explicit rather than inferred from spaces in an item name.

Requires protocol 88 and SDK 0.4.5-dev. Existing fields remain commit-only.
