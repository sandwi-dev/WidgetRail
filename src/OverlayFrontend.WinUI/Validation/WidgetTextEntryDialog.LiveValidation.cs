namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetTextEntryDialog
{
    internal void InsertForValidation(string text) => Insert(text);
    internal Microsoft.UI.Xaml.Controls.StackPanel BodyForValidation => body;
    internal Microsoft.UI.Xaml.Controls.Control EditorForValidation => (Microsoft.UI.Xaml.Controls.Control?)text ?? password!;
    internal (double EditorY, double KeyY, double Top) SuggestionPositionForValidation
    {
        get
        {
            var panel = NativeDialogPresentation.FindPart<Microsoft.UI.Xaml.FrameworkElement>(this, "BackgroundElement")!;
            var top = keyboardPopup?.VerticalOffset ?? 0;
            return (top + ((Microsoft.UI.Xaml.FrameworkElement?)text ?? password!).TransformToVisual(panel).TransformPoint(new()).Y,
                top + characters[10].TransformToVisual(panel).TransformPoint(new()).Y, top);
        }
    }
    internal bool SuggestionsAnimatingForValidation => suggestionResize is not null;
    internal void AcceptSuggestionForValidation(int index) => AcceptSuggestion(index);
    internal void AnchorNearWorkAreaTopForValidation()
    {
        var top = keyboardPopup!.VerticalOffset + suggestionRegion!.ActualHeight - suggestionAvailableHeight;
        FieldAnchor = new Windows.Foundation.Rect(keyboardPopup.HorizontalOffset, top + 1, 200, 1);
    }
    internal bool SuggestionViewportScrollsForValidation => suggestionViewport?.ScrollableWidth > 0;
    internal bool FocusedSuggestionVisibleForValidation
    {
        get
        {
            if (suggestionViewport is null || Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is not Microsoft.UI.Xaml.Controls.Button focused || !suggestionButtons.Contains(focused)) return false;
            var bounds = focused.TransformToVisual(suggestionViewport).TransformBounds(new(0, 0, focused.ActualWidth, focused.ActualHeight));
            return bounds.Left >= -1 && bounds.Right <= suggestionViewport.ActualWidth + 1;
        }
    }
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
