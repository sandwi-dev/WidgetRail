using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Documents;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

// A presenter-scoped setting, not a process-wide multiplier. Nested row and
// surface presenters inherit it through the native tree without copying state.
internal sealed class NativeTextScaleScope
{
    private static readonly ConditionalWeakTable<FrameworkElement, NativeTextScaleScope> roots = new();
    // A styled text span is projected into a Run rather than inserted as a TextBlock.
    // Resolve its scale through the paragraph that owns it.
    private static readonly ConditionalWeakTable<FrameworkElement, FrameworkElement> spanParents = new();
    internal static void SetSpanParent(FrameworkElement span, FrameworkElement? paragraph)
    {
        spanParents.Remove(span);
        if (paragraph is not null) spanParents.Add(span, paragraph);
    }
    internal double Value { get; private set; } = 1;
    internal event Action? Changed;
    internal static void Set(FrameworkElement root, double value)
    {
        value = Normalize(value);
        var scope = roots.GetValue(root, static _ => new());
        if (scope.Value == value) return;
        scope.Value = value;
        scope.Changed?.Invoke();
    }
    internal static NativeTextScaleScope? Find(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null;
            current = current switch
            {
                FrameworkElement span when spanParents.TryGetValue(span, out var paragraph) => paragraph,
                TextElement text => text.ElementStart.VisualParent,
                FrameworkElement { Parent: { } parent } => parent,
                _ => VisualTreeHelper.GetParent(current),
            })
            if (current is FrameworkElement candidate && roots.TryGetValue(candidate, out var scope)) return scope;
        return null;
    }
    internal static double Normalize(double value) => double.IsFinite(value)
        ? Math.Clamp(value, AppearanceSettings.MinimumTextScale, AppearanceSettings.MaximumTextScale) : 1;
}
