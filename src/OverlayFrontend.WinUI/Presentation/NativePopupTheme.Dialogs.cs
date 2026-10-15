using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class NativePopupTheme
{
    private static void SetDialogResource(ContentDialog dialog, string key, object value)
    {
        // Brushes are deliberately shared and updated in place. Reinserting
        // the same WinRT resource can reject its existing dictionary owner.
        if (dialog.Resources.TryGetValue(key, out var existing) && ReferenceEquals(existing, value)) return;
        dialog.Resources[key] = value;
    }
    private readonly SolidColorBrush dialogSurface = new(), dialogFooter = new(), dialogText = new(), dialogTitle = new(),
        dialogDescription = new(), dialogBorder = new(), dialogChoice = new(), dialogChoiceText = new(), dialogSelected = new(), dialogSelectedText = new(),
        dialogFocus = new(), dialogButton = new(), dialogButtonText = new(), dialogButtonActive = new(), dialogButtonActiveText = new();

    private void UpdateDialogPalette(ShellChromePalette fallback)
    {
        Color Value(string role, string property, Color other) => !fallback.HighContrast &&
            styles?.GetValueOrDefault(role)?.Base.GetValueOrDefault(property)?.Text is { } text &&
            NativeComputedStyleAdapter.TryColor(text, out var value) ? value : other;
        Color Background(string role, Color other) => ShellChromePalette.Composite(other, Value(role, "background", other));
        dialogSurface.Color = Background("dialog", fallback.Surface);
        dialogFooter.Color = Background("dialog-footer", dialogSurface.Color);
        dialogText.Color = Value("dialog", "color", Value("body", "color", fallback.Text));
        dialogTitle.Color = Value("dialog-title", "color", dialogText.Color);
        dialogDescription.Color = Value("dialog-description", "color", dialogText.Color);
        dialogBorder.Color = Value("dialog", "border-color", fallback.HighContrast ? fallback.Focus : ShellChromePalette.Blend(dialogSurface.Color, dialogText.Color, .24));
        dialogChoice.Color = Value("dialog-choice", "background", Microsoft.UI.Colors.Transparent);
        dialogChoiceText.Color = Value("dialog-choice", "color", dialogText.Color);
        dialogSelected.Color = Background("dialog-choice:selected", ShellChromePalette.Composite(dialogSurface.Color, fallback.Selected));
        dialogSelectedText.Color = Value("dialog-choice:selected", "color", fallback.SelectedText);
        dialogFocus.Color = Value("dialog-choice:focused", "outline-color", fallback.Focus);
        dialogButton.Color = Background("dialog-button", fallback.HighContrast ? fallback.Surface : ShellChromePalette.Blend(dialogSurface.Color, dialogText.Color, .08));
        dialogButtonText.Color = Value("dialog-button", "color", dialogText.Color);
        dialogButtonActive.Color = Background("dialog-button:focused", dialogSelected.Color);
        dialogButtonActiveText.Color = Value("dialog-button:focused", "color", dialogSelectedText.Color);
    }

    private void ApplyDialogParts(ContentDialog dialog)
    {
        if (NativeDialogPresentation.FindPart<Control>(dialog, "Title") is { } title) title.Foreground = dialogTitle;
        if (NativeDialogPresentation.FindPart<Grid>(dialog, "CommandSpace") is { } footer) footer.Background = dialogFooter;
        if (dialog is Shell.HostChoiceDialog choice) choice.ApplyDescriptionBrush(dialogDescription);
        foreach (var name in new[] { "PrimaryButton", "SecondaryButton", "CloseButton" })
            if (NativeDialogPresentation.FindPart<Button>(dialog, name) is { } button)
            {
                var active = button.IsEnabled && button.FocusState != Microsoft.UI.Xaml.FocusState.Unfocused;
                button.Background = active ? dialogButtonActive : dialogButton;
                button.Foreground = !button.IsEnabled ? muted : active ? dialogButtonActiveText : dialogButtonText;
                button.FocusVisualPrimaryBrush = dialogFocus;
                button.UseSystemFocusVisuals = true;
            }
    }

    private void ApplyDialogResources(ContentDialog dialog)
    {
        dialog.Background = dialogSurface; dialog.Foreground = dialogText; dialog.BorderBrush = dialogBorder;
        SetDialogResource(dialog, "ContentDialogBackground", dialogSurface);
        SetDialogResource(dialog, "ContentDialogForeground", dialogText);
        SetDialogResource(dialog, "ContentDialogContentForegroundBrush", dialogText);
        SetDialogResource(dialog, "ContentDialogBorderBrush", dialogBorder);
        SetDialogResource(dialog, "ContentDialogTopOverlay", dialogSurface);
        SetDialogResource(dialog, "ContentDialogSeparatorBorderBrush", dialogBorder);
        SetDialogResource(dialog, "FocusVisualPrimaryBrush", dialogFocus);
        SetDialogResource(dialog, "ListViewItemSelectionIndicatorBrush", dialogFocus);
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Selected", "SelectedPointerOver", "SelectedPressed" })
        {
            var active = state.Length != 0;
            SetDialogResource(dialog, "ListViewItemBackground" + state, active ? dialogSelected : dialogChoice);
            SetDialogResource(dialog, "ListViewItemForeground" + state, active ? dialogSelectedText : dialogChoiceText);
        }
        foreach (var prefix in new[] { "Button", "AccentButton" })
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            var active = state is "PointerOver" or "Pressed";
            SetDialogResource(dialog, prefix + "Background" + state, active ? dialogButtonActive : dialogButton);
            SetDialogResource(dialog, prefix + "Foreground" + state, state == "Disabled" ? muted : active ? dialogButtonActiveText : dialogButtonText);
            SetDialogResource(dialog, prefix + "BorderBrush" + state, dialogBorder);
        }
    }
}
