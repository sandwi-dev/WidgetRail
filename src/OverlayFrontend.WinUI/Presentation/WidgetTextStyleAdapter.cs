using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native text layout and reversible display casing; source strings remain authored data.</summary>
internal sealed class WidgetTextStyleAdapter : IDisposable
{
    private static readonly ConditionalWeakTable<TextBlock, WidgetTextStyleAdapter> owners = new();
    private readonly TextBlock text;
    private readonly Dictionary<DependencyProperty, object> originals = [];
    private readonly long textChanged;
    private string source;
    private string? transform;
    private bool writing;

    internal WidgetTextStyleAdapter(TextBlock text)
    {
        this.text = text;
        source = text.Text;
        owners.Add(text, this);
        textChanged = text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
        {
            if (writing) return;
            source = text.Text;
            ApplyText();
        });
    }

    // A snapshot can replace "Mixed" with "MIXED" while uppercase is displayed.
    // Set the source explicitly even when Text's displayed value would not change.
    internal static void SetSource(TextBlock text, string value)
    {
        if (owners.TryGetValue(text, out var adapter))
        {
            if (adapter.source == value) return;
            adapter.source = value; adapter.ApplyText();
        }
        else text.Text = value;
    }

    internal void Apply(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style, double fontSize)
    {
        var maxLines = style?.GetValueOrDefault("max-lines")?.Number is { } lines && double.IsFinite(lines)
            ? (int?)Math.Clamp(Math.Round(lines), 1, 128) : null;
        Put(TextBlock.MaxLinesProperty, maxLines);
        Put(TextBlock.TextTrimmingProperty, style?.GetValueOrDefault("text-overflow")?.Text switch
        { "ellipsis" => TextTrimming.CharacterEllipsis, "clip" => TextTrimming.Clip, _ => (TextTrimming?)null });
        var wrap = style?.GetValueOrDefault("overflow-wrap")?.Text;
        // TextBlock exposes EMERGENCY_BREAK as Wrap, but has no legacy DirectWrite
        // WRAP equivalent. Keep native wrapping for both modes rather than changing
        // normal to WHOLE_WORD, which would overflow long unbroken catalog titles.
        Put(TextBlock.TextWrappingProperty, maxLines == 1 ? TextWrapping.NoWrap :
            maxLines is > 1 || wrap is "normal" or "anywhere" ? TextWrapping.Wrap : (TextWrapping?)null);
        var lineHeight = style?.GetValueOrDefault("line-height")?.Number is { } ratio && double.IsFinite(ratio)
            ? Math.Clamp(ratio, .8, 3) * fontSize : (double?)null;
        Put(TextBlock.LineHeightProperty, lineHeight);
        Put(TextBlock.LineStackingStrategyProperty, lineHeight is null ? null : LineStackingStrategy.BlockLineHeight);
        var nextTransform = style?.GetValueOrDefault("text-transform")?.Text;
        if (nextTransform != transform) { transform = nextTransform; ApplyText(); }
    }

    private void ApplyText()
    {
        var rendered = transform switch { "uppercase" => source.ToUpperInvariant(), "lowercase" => source.ToLowerInvariant(), _ => source };
        if (text.Text == rendered) return;
        writing = true;
        try { text.Text = rendered; }
        finally { writing = false; }
    }

    private void Put(DependencyProperty property, object? value)
    {
        if (value is null)
        {
            if (!originals.Remove(property, out var original)) return;
            if (ReferenceEquals(original, DependencyProperty.UnsetValue)) text.ClearValue(property);
            else text.SetValue(property, original);
        }
        else
        {
            if (!originals.ContainsKey(property)) originals.Add(property, text.ReadLocalValue(property));
            if (!Equals(text.GetValue(property), value)) text.SetValue(property, value);
        }
    }

    public void Dispose()
    {
        text.UnregisterPropertyChangedCallback(TextBlock.TextProperty, textChanged);
        transform = null; ApplyText();
        foreach (var property in originals.Keys.ToArray()) Put(property, null);
        owners.Remove(text);
    }
}
