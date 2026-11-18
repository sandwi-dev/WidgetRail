using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Explicit native content input, under the dialog's existing modal/neutral/focus ownership.</summary>
internal interface IHostDialogContent
{
    FrameworkElement View { get; }
    event Action? InteractionChanged;
    IReadOnlyList<ControllerGuideHint>? Guide { get; }
    bool Move(FocusNavigationDirection direction, bool repeated);
    bool Handle(ControllerButton button, ControllerEventPhase phase);
    void ResetInput();
}

internal sealed partial class HostChoiceDialog
{
    private IHostDialogContent? interactiveContent;
    internal IReadOnlyList<ControllerGuideHint>? ContentGuide => interactiveContent?.Guide;
    internal void FocusCloseAction() => (GetTemplateChild("CloseButton") as Microsoft.UI.Xaml.Controls.Control)?.Focus(FocusState.Keyboard);
    internal void SetInteractiveContent(IHostDialogContent value, double maximumWidth)
    {
        if (opened || interactiveContent is not null) throw new InvalidOperationException("Dialog content must be configured once, before opening.");
        if (!double.IsFinite(maximumWidth) || maximumWidth is < 320 or > 1200) throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        interactiveContent = value;
        content.MaxWidth = maximumWidth;
        Resources["ContentDialogMaxWidth"] = maximumWidth + 32;
        details.Children.Add(value.View);
        value.InteractionChanged += ContentInteractionChanged;
        GotFocus += ContentFocusChanged;
        Closing += (_, _) => value.ResetInput();
        Closed += (_, _) => { value.ResetInput(); value.InteractionChanged -= ContentInteractionChanged; GotFocus -= ContentFocusChanged; };
    }
    private void ContentInteractionChanged() => SelectionContextChanged?.Invoke();
    private void ContentFocusChanged(object sender, RoutedEventArgs args) => ContentInteractionChanged();
}
