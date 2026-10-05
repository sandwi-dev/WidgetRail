using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetTextEntryDialog
{
    private readonly Grid editorHost = new();
    private readonly Canvas caretLayer = new() { IsHitTestVisible = false };
    private readonly Rectangle controllerCaret = new() { Visibility = Visibility.Collapsed };
    private TextBox? maskedEditor;
    private bool updatingCaret;

    private void InitializeCaret()
    {
        var editor = (Control?)text ?? password!;
        body.Children.Remove(editor);
        editorHost.Children.Add(editor);
        body.Children.Insert(0, editorHost);
        if (password is not null)
        {
            // PasswordBox deliberately exposes no selection API. The native
            // editor keeps mouse/keyboard/paste and protected UIA semantics;
            // controller mode displays only grapheme masks, never the secret.
            maskedEditor = new ProtectedControllerDisplay
            {
                Header = password.Header, IsReadOnly = true, IsTabStop = false,
                IsHitTestVisible = false, IsSpellCheckEnabled = false,
            };
            AutomationProperties.SetAccessibilityView(maskedEditor, AccessibilityView.Raw);
            AutomationProperties.SetAutomationId(maskedEditor, "Widget.TextEntry.ProtectedDisplay");
            editorHost.Children.Add(maskedEditor);
            password.LostFocus += (_, _) =>
            {
                // A native selection cannot be read safely. Switching back to
                // the controller starts at the end, visibly, then LB/RB edit it.
                secretCaret = password.Password.Length;
                RefreshCaret();
            };
        }
        caretLayer.Children.Add(controllerCaret);
        AutomationProperties.SetAccessibilityView(caretLayer, AccessibilityView.Raw);
        AutomationProperties.SetAutomationId(controllerCaret, "Widget.TextEntry.Caret");
        editorHost.Children.Add(caretLayer);
        editorHost.LayoutUpdated += CaretLayoutUpdated;
        ApplyCaretMetrics();
    }

    private void ApplyCaretMetrics()
    {
        if (maskedEditor is not null && password is not null)
        {
            maskedEditor.FontSize = password.FontSize;
            maskedEditor.FontWeight = password.FontWeight;
            maskedEditor.FontFamily = password.FontFamily;
            maskedEditor.MinHeight = password.MinHeight;
            maskedEditor.Padding = password.Padding;
        }
        RefreshCaret();
    }

    private void CaretLayoutUpdated(object? sender, object args) => RefreshCaret();

    private void RefreshCaret()
    {
        if (updatingCaret || completed || XamlRoot is null || !editorHost.IsLoaded) return;
        updatingCaret = true;
        try
        {
            var focused = FocusManager.GetFocusedElement(XamlRoot);
            var native = (Control?)text ?? password!;
            var nativeFocused = ReferenceEquals(focused, native);
            var controllerFocused = focused is Button key && commands.ContainsKey(key);
            var display = text ?? maskedEditor!;
            var position = text?.SelectionStart ?? 0;
            if (maskedEditor is not null && password is not null)
            {
                var boundaries = StringInfo.ParseCombiningCharacters(password.Password);
                var masked = PasswordVisible ? password.Password : new string('●', boundaries.Length);
                if (maskedEditor.Text != masked) maskedEditor.Text = masked;
                position = PasswordVisible ? secretCaret : boundaries.Count(index => index < secretCaret);
                if (maskedEditor.SelectionStart != position || maskedEditor.SelectionLength != 0) maskedEditor.Select(position, 0);
                maskedEditor.Visibility = nativeFocused ? Visibility.Collapsed : Visibility.Visible;
                password.Opacity = nativeFocused ? 1 : 0;
            }
            if (!controllerFocused || text?.SelectionLength > 0 || display.ActualWidth <= 0)
            { controllerCaret.Visibility = Visibility.Collapsed; return; }
            var scroll = FindEditorScroll(display);
            if (scroll is null || scroll.ActualWidth <= 0) { controllerCaret.Visibility = Visibility.Collapsed; return; }
            var clip = scroll.TransformToVisual(editorHost).TransformBounds(new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight));
            var rect = new Rect(clip.X, clip.Y, 0, Math.Min(clip.Height, display.FontSize * 1.4));
            if (display.Text.Length > 0)
            {
                try
                {
                    rect = display.GetRectFromCharacterIndex(Math.Min(position, display.Text.Length - 1),
                        position > 0 && position == display.Text.Length);
                    // TextBox's character layout excludes its editor inset.
                    // Keep the supplemental caret on the same glyph edge as
                    // the native caret instead of drawing through the last glyph.
                    rect.X += display.Padding.Left + display.BorderThickness.Left;
                    rect.Y += display.Padding.Top + display.BorderThickness.Top;
                    rect = display.TransformToVisual(editorHost).TransformBounds(rect);
                }
                catch (ArgumentException)
                {
                    // Text changed before TextBoxView completed layout. Its next
                    // LayoutUpdated supplies geometry; never steal native focus.
                    controllerCaret.Visibility = Visibility.Collapsed;
                    return;
                }
            }
            var width = Math.Max(2, 2 * interfaceScale);
            var delta = rect.X < clip.Left ? rect.X - clip.Left : rect.X + width > clip.Right ? rect.X + width - clip.Right : 0;
            if (Math.Abs(delta) > .5)
                scroll.ChangeView(Math.Clamp(scroll.HorizontalOffset + delta, 0, scroll.ScrollableWidth), null, null, true);
            controllerCaret.Width = width;
            controllerCaret.Height = Math.Min(rect.Height, clip.Height);
            controllerCaret.Fill = native.Foreground;
            Canvas.SetLeft(controllerCaret, Math.Clamp(rect.X, clip.Left, Math.Max(clip.Left, clip.Right - width)));
            Canvas.SetTop(controllerCaret, Math.Clamp(rect.Y, clip.Top, Math.Max(clip.Top, clip.Bottom - controllerCaret.Height)));
            var bounds = new Rect(clip.X, clip.Y, Math.Max(0, clip.Width), Math.Max(0, clip.Height));
            if (caretLayer.Clip is not RectangleGeometry geometry || geometry.Rect != bounds)
                caretLayer.Clip = new RectangleGeometry { Rect = bounds };
            controllerCaret.Visibility = Visibility.Visible;
        }
        finally { updatingCaret = false; }
    }

    private static ScrollViewer? FindEditorScroll(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ScrollViewer scroll) return scroll;
            if (FindEditorScroll(child) is { } nested) return nested;
        }
        return null;
    }

    private void RetireCaret()
    {
        editorHost.LayoutUpdated -= CaretLayoutUpdated;
        controllerCaret.Visibility = Visibility.Collapsed;
        if (maskedEditor is not null) maskedEditor.Text = string.Empty;
        PasswordVisible = false;
        if (password is not null) password.PasswordRevealMode = PasswordRevealMode.Hidden;
    }

    // Even explicit visual reveal must not turn a protected field into an
    // ordinary text/value provider. This display is not an input control.
    private sealed partial class ProtectedControllerDisplay : TextBox
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new ProtectedDisplayPeer(this);
    }
    private sealed partial class ProtectedDisplayPeer(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsPasswordCore() => true;
        protected override string GetNameCore() => "Protected text";
        protected override string GetClassNameCore() => "PasswordBox";
        protected override object GetPatternCore(PatternInterface patternInterface) => null!;
        protected override IList<AutomationPeer> GetChildrenCore() => [];
    }
}
