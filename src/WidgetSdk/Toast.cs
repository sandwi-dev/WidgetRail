using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>The closed visual and accessibility tones supported by <see cref="UI.Toast"/>.</summary>
public enum ToastTone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
}

/// <summary>
/// A brief, non-interactive notification composed from baseline protocol nodes.
/// It deliberately owns no input scope, shortcuts, buttons, or focus links, so
/// adding or removing it cannot take controller focus from the active surface.
/// </summary>
public sealed record ToastElement : WidgetElement
{
    internal ToastElement(
        string title,
        string message,
        ToastTone tone,
        TimeSpan duration,
        string id,
        WidgetGlyph? glyph) : base(RequireId(id))
    {
        StableIdentifier.Validate(id, nameof(id));
        _ = StableIdentifier.Child(id, "copy");
        _ = StableIdentifier.Child(id, "title");
        _ = StableIdentifier.Child(id, "message");
        _ = StableIdentifier.Child(id, "icon");
        ValidateText(title, nameof(title), UI.MaximumToastTitleCharacters);
        ValidateText(message, nameof(message), UI.MaximumToastMessageCharacters);
        if (!Enum.IsDefined(tone))
            throw new ArgumentOutOfRangeException(nameof(tone));
        if (duration < UI.MinimumToastDuration || duration > UI.MaximumToastDuration)
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                $"Toast duration must be between {UI.MinimumToastDuration.TotalSeconds:0} and " +
                $"{UI.MaximumToastDuration.TotalSeconds:0} seconds.");
        if (glyph is { } semanticGlyph && !Enum.IsDefined(semanticGlyph))
            throw new ArgumentOutOfRangeException(nameof(glyph));

        Title = title;
        Message = message;
        Tone = tone;
        Duration = duration;
        Glyph = glyph;
        StyleClasses = ["wrail-toast", $"wrail-toast--{Token(tone)}"];
    }

    public string Title { get; }
    public string Message { get; }
    public ToastTone Tone { get; }

    /// <summary>
    /// The bounded time the widget author intends to keep this Toast in its
    /// rendered tree. This value is intentionally not a host timer: widgets
    /// should remove expired Toasts through their normal lifecycle-aware state
    /// update rather than creating a background worker solely for animation.
    /// Themes may animate appearance and removal, but must suppress or shorten
    /// that motion when the platform reduced-motion preference is active.
    /// </summary>
    public TimeSpan Duration { get; }

    public WidgetGlyph? Glyph { get; }

    internal override ViewNode ToProtocolNode()
    {
        var tone = Token(Tone);
        var semanticGlyph = Glyph ?? Tone switch
        {
            ToastTone.Success => WidgetGlyph.Check,
            ToastTone.Warning or ToastTone.Danger => WidgetGlyph.Warning,
            _ => (WidgetGlyph?)null,
        };
        var children = new List<WidgetElement>();
        if (semanticGlyph is { } resolvedGlyph)
        {
            children.Add(new IconElement(
                StableIdentifier.Child(Id, "icon"),
                resolvedGlyph,
                $"{Tone} notification")
            {
                StyleClasses = ["wrail-toast__icon", $"wrail-toast__icon--{tone}"],
            });
        }

        children.Add(new StackElement(
            StableIdentifier.Child(Id, "copy"),
            [
                new TextElement(StableIdentifier.Child(Id, "title"), Title, Title)
                {
                    StyleClasses = ["wrail-toast__title"],
                },
                new TextElement(StableIdentifier.Child(Id, "message"), Message, Message)
                {
                    StyleClasses = ["wrail-toast__message"],
                },
            ])
        {
            StyleClasses = ["wrail-toast__copy"],
        });

        return new RowElement(Id, children)
        {
            StyleClasses = StyleClasses,
        }.ToProtocolNode();
    }

    private static void ValidateText(string? text, string parameterName, int maximumCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, parameterName);
        if (text.Length > maximumCharacters)
            throw new ArgumentException(
                $"Toast {parameterName} may not exceed {maximumCharacters} characters.",
                parameterName);
    }

    private static string Token(ToastTone tone) => tone.ToString().ToLowerInvariant();
}

public static partial class UI
{
    public const int MaximumToastTitleCharacters = 120;
    public const int MaximumToastMessageCharacters = 512;
    public static readonly TimeSpan DefaultToastDuration = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MinimumToastDuration = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaximumToastDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates a bounded notification with zero controller focus stops and no
    /// action. Keep the returned element in the view for <paramref name="duration"/>
    /// (five seconds by default), then remove it through normal widget state.
    /// </summary>
    public static ToastElement Toast(
        string title,
        string message,
        ToastTone tone,
        string id,
        TimeSpan? duration = null,
        WidgetGlyph? glyph = null) => new(
            title,
            message,
            tone,
            duration ?? DefaultToastDuration,
            id,
            glyph);
}
