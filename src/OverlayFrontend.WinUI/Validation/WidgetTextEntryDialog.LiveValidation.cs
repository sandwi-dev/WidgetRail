namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetTextEntryDialog
{
    internal void InsertForValidation(string text) => Insert(text);
    internal bool InitialFocusForValidation => characters[10].FocusState == Microsoft.UI.Xaml.FocusState.Keyboard;
    internal bool AllKeysVisibleForValidation
    {
        get
        {
            var panel = NativeDialogPresentation.FindPart<Microsoft.UI.Xaml.FrameworkElement>(this, "BackgroundElement");
            return panel is { ActualWidth: > 0, ActualHeight: > 0 } && commands.Keys.All(key =>
            {
                var bounds = key.TransformToVisual(panel).TransformBounds(new(0, 0, key.ActualWidth, key.ActualHeight));
                return bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1 &&
                    bounds.Right <= panel.ActualWidth + 1 && bounds.Bottom <= panel.ActualHeight + 1;
            });
        }
    }
}
