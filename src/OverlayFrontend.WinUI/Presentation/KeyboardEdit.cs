namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

// Host-only keyboard transport. Widget TextEntry retains its committed-value
// contract; a browser consumes ordered native editing commands instead.
internal enum KeyboardEditKind { Read, Insert, Key, Clear }
internal sealed record KeyboardEdit(KeyboardEditKind Kind, string? Text = null, string? Key = null, int KeyCode = 0, int Modifiers = 0, bool Wordwise = false);
internal sealed record KeyboardTextState(string Display, int Caret, int SelectionLength, bool Sensitive = false);
