using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyDialogPresentationAsync(Action<bool, string> check)
    {
        var originalAppearance = Appearance;
        var palette = new Dictionary<string, BridgeNodeRenderStyles>(ShellPalette ?? new Dictionary<string, BridgeNodeRenderStyles>());
        palette["dialog"] = Role(("background", "#123456"), ("color", "#ffffff"));
        palette["dialog-title"] = Role(("color", "#fedcba"));
        palette["dialog-footer"] = Role(("background", "#123456"));
        palette["dialog-button:focused"] = Role(("background", "#225522"), ("color", "#ffffff"));
        try
        {
            foreach (var position in new[] { OverlayPosition.BottomLeft, OverlayPosition.BottomRight, OverlayPosition.Center })
            {
                Appearance = Appearance with { OverlayPosition = position };
                ConfigureProductionViewport(shellViewport);
                surface!.UpdateLayout();
                popupTheme.Update(palette, Appearance);
                foreach (var kind in new[] { "choice", "library", "install", "keyboard" })
                {
                    ContentDialog dialog = kind switch
                    {
                        "choice" => new HostChoiceDialog("Placement fixture", "Local synthetic choices", (HostDialogChoice[])[new("one", "One"), new("two", "Two")]),
                        "library" => new BrowserLibraryDialog(true, (BrowserLibraryEntry[])[new("https://shell.fixture.invalid", "Local page", DateTimeOffset.UtcNow)]),
                        "install" => new LocalWidgetInstallDialog(),
                        _ => new WidgetTextEntryDialog(new ViewNode { Id = "placement", Kind = ViewNodeKind.TextEntry, TextEntryMaximumLength = 100, AccessibilityLabel = "Synthetic keyboard" }, _ => { }),
                    };
                    dialog.XamlRoot = XamlRoot;
                    if (dialog is WidgetTextEntryDialog keyboard) keyboard.BindRoot(XamlRoot);
                    using var theme = NativePopupTheme.Dialog(dialog, surface);
                    var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    dialog.Opened += (_, _) => opened.TrySetResult();
                    var showing = dialog.ShowAsync().AsTask();
                    try
                    {
                        await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        var panel = NativeDialogPresentation.FindPart<FrameworkElement>(dialog, "BackgroundElement");
                        await WebAppUntil(() => Centered());
                        check(true, $"{kind}: {position} dialog is centered over its widget and clamped inside the window");
                        check(dialog.Background is SolidColorBrush { Color: { R: 0x12, G: 0x34, B: 0x56 } } &&
                            NativeDialogPresentation.FindPart<Grid>(dialog, "CommandSpace")?.Background is SolidColorBrush { Color: { R: 0x12, G: 0x34, B: 0x56 } } &&
                            NativeDialogPresentation.FindPart<Control>(dialog, "Title")?.Foreground is SolidColorBrush { Color: { R: 0xfe, G: 0xdc, B: 0xba } },
                            $"{kind}: dedicated dialog surface, footer and title theme roles are applied");
                        // Updating an open theme must retain the same native
                        // dialog and shared resources, not reparent brush objects.
                        popupTheme.Update(palette, Appearance with { TextScale = Appearance.TextScale + .05 });
                        popupTheme.Update(palette, Appearance);
                        check(dialog.IsLoaded, $"{kind}: an open dialog survives live appearance updates");
                        if (dialog is WidgetTextEntryDialog)
                            check(FindControl<Button>(dialog, "Widget.TextEntry.Key.10")!.Focus(FocusState.Keyboard), "Keyboard keys retain controller focus after a theme update");
                        else
                        {
                            var close = NativeDialogPresentation.FindPart<Button>(dialog, "CloseButton")!;
                            check(close.Focus(FocusState.Keyboard), $"{kind}: native action button accepts controller focus after a theme update");
                            await WebAppUntil(() => close.FocusState != FocusState.Unfocused && close.Background is SolidColorBrush { Color: { R: 0x22, G: 0x55, B: 0x22 } });
                            check(true, $"{kind}: controller-focused dialog actions use their theme role");
                        }
                        var contrast = Appearance with { Contrast = ContrastPreference.High };
                        popupTheme.Update(palette, contrast);
                        check(dialog.Background is SolidColorBrush highContrast && highContrast.Color == ShellChromePalette.Resolve(palette, contrast).Surface,
                            $"{kind}: high contrast overrides authored dialog colors");
                        popupTheme.Update(palette, Appearance);

                        bool Centered()
                        {
                            panel ??= NativeDialogPresentation.FindPart<FrameworkElement>(dialog, "BackgroundElement");
                            if (panel is null || !panel.IsLoaded) return false;
                            var anchor = surface.TransformToVisual(XamlRoot.Content).TransformBounds(new(0, 0, surface.ActualWidth, surface.ActualHeight));
                            var bounds = panel.TransformToVisual(XamlRoot.Content).TransformBounds(new(0, 0, panel.ActualWidth, panel.ActualHeight));
                            var expectedX = Math.Clamp(anchor.X + (anchor.Width - bounds.Width) / 2, 16, Math.Max(16, XamlRoot.Size.Width - bounds.Width - 16));
                            var expectedY = Math.Clamp(anchor.Y + (anchor.Height - bounds.Height) / 2, 16, Math.Max(16, XamlRoot.Size.Height - bounds.Height - 16));
                            return bounds.Width > 0 && bounds.Height > 0 && Math.Abs(bounds.X - expectedX) < 2 && Math.Abs(bounds.Y - expectedY) < 2 &&
                                bounds.X >= 15 && bounds.Y >= 15 && bounds.Right <= XamlRoot.Size.Width - 15 && bounds.Bottom <= XamlRoot.Size.Height - 15;
                        }
                    }
                    finally
                    {
                        dialog.Hide();
                        await showing;
                        if (dialog is WidgetTextEntryDialog finishedKeyboard) finishedKeyboard.Erase();
                    }
                }
            }
        }
        finally
        {
            Appearance = originalAppearance;
            popupTheme.Update(ShellPalette, Appearance);
            ConfigureProductionViewport(shellViewport);
            surface?.Enter(restoreNativeFocus: true);
        }

        static BridgeNodeRenderStyles Role(params (string Property, string Color)[] values)
        {
            var properties = values.ToDictionary(value => value.Property, value => new BridgeComputedStyleValue { Kind = WrssValueKind.Color, Text = value.Color });
            return new() { Base = properties, Focused = properties, Pressed = properties };
        }
    }
}
