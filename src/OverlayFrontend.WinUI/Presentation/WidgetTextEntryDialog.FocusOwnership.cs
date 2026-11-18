using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetTextEntryDialog
{
    // ContentDialog may move its template outside the dialog's own visual tree.
    // The authored body remains the exact input owner in either presentation.
    // Weak ownership survives native close recovery without retaining old dialogs.
    private static readonly ConditionalWeakTable<DependencyObject, WidgetTextEntryDialog> FocusBodies = new();
    private WeakReference<WidgetViewPresenter>? focusPresenter;
    private WidgetPresentationAuthority? focusAuthority;

    private void CaptureFocusOwner(FrameworkElement origin)
    {
        focusPresenter = origin is WidgetViewPresenter presenter ? new(presenter) : null;
        focusAuthority = (origin as WidgetViewPresenter)?.KeyboardFocusAuthority;
    }

    internal static bool IsCompletedFocusOrigin(DependencyObject? element) => FocusOwner(element)?.completed == true;

    internal static bool SharesCompletedFocusOrigin(DependencyObject? first, DependencyObject? second, WidgetViewPresenter origin) =>
        FocusOwner(first) is { completed: true, focusAuthority: { } authority } dialog && ReferenceEquals(dialog, FocusOwner(second)) &&
        dialog.focusPresenter?.TryGetTarget(out var presenter) == true && ReferenceEquals(presenter, origin) && origin.OwnsKeyboardFocusAuthority(authority);

    private static WidgetTextEntryDialog? FocusOwner(DependencyObject? element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (FocusBodies.TryGetValue(current, out var dialog)) return dialog;
        return null;
    }
}
