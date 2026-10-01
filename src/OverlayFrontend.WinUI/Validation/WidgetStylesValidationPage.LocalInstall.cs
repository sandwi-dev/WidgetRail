using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task LocalInstallDialogAsync()
    {
        var dialog = new LocalWidgetInstallDialog { XamlRoot = XamlRoot };
        var showing = dialog.ShowAsync().AsTask();
        try
        {
            await Wait(() => dialog.IsLoaded && Descendants(dialog).OfType<Button>().Any(b => b.Name == "CloseButton"));
            Check(FocusManager.GetFocusedElement(XamlRoot) is Button { Name: "CloseButton" }, "installation progress focuses Cancel");
            var review = dialog.ReviewAsync(new("approval-required", "test.widget", "1.0.0", ""), CancellationToken.None);
            await Wait(() => Descendants(dialog).OfType<Button>().Any(b => b.Name == "PrimaryButton" && b.IsEnabled));
            var primary = Descendants(dialog).OfType<Button>().Single(b => b.Name == "PrimaryButton");
            primary.Focus(FocusState.Keyboard);
            dialog.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
            Check(!review.IsCompleted, "held opening A cannot approve the new trust dialog");
            dialog.ControllerArmed = true;
            dialog.Handle(ControllerButton.A, ControllerEventPhase.Repeated);
            Check(!review.IsCompleted, "repeat cannot approve full trust");
            dialog.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
            Check(await review.WaitAsync(TimeSpan.FromSeconds(2)), "fresh A invokes the focused full-trust decision");
            dialog.Complete(new("installed-disabled", "test.widget", "1.0.0", "Widget installed disabled. Review its permissions in Widgets."));
            Check(dialog.CloseButtonText == "Done" && dialog.PrimaryButtonText == "", "completion replaces approval with a single Done action");
            dialog.ControllerArmed = true;
            dialog.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            await showing;
            Check(true, "B dismisses completion");
        }
        finally { dialog.Hide(); }
        var cancelDialog = new LocalWidgetInstallDialog { XamlRoot = XamlRoot };
        var cancelled = cancelDialog.ShowAsync().AsTask();
        try
        {
            await Wait(() => cancelDialog.IsLoaded);
            var review = cancelDialog.ReviewAsync(new("approval-required", "test.widget", "1.0.0", ""), CancellationToken.None);
            cancelDialog.ControllerArmed = true;
            cancelDialog.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            Check(!await review.WaitAsync(TimeSpan.FromSeconds(2)), "B denies full-trust installation");
            await cancelled;
        }
        finally { cancelDialog.Hide(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
