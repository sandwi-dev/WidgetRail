using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

// A presenter-scoped setting, not a process-wide multiplier. Nested row and
// surface presenters inherit it through the native tree without copying state.
internal sealed class NativeTextScaleScope
{
    private static readonly ConditionalWeakTable<FrameworkElement, NativeTextScaleScope> roots = new();
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
            current = current is FrameworkElement { Parent: { } parent } ? parent : VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement candidate && roots.TryGetValue(candidate, out var scope)) return scope;
        return null;
    }
    internal static double Normalize(double value) => double.IsFinite(value)
        ? Math.Clamp(value, AppearanceSettings.MinimumTextScale, AppearanceSettings.MaximumTextScale) : 1;
}
