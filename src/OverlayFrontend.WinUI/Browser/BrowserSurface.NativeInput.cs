using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private bool controllerPointerVisible = true;

    internal bool HasNativeKeyboardFocus
    {
        get
        {
            if (!input || web.XamlRoot is not { } root) return false;
            for (var node = FocusManager.GetFocusedElement(root) as DependencyObject;
                 node is not null; node = VisualTreeHelper.GetParent(node))
                if (ReferenceEquals(node, web)) return true;
            return false;
        }
    }

    private void UseControllerInput()
    {
        // Controller input goes through the host's existing single-owner route.
        // Physical keyboard input keeps Chromium focus until actual controller
        // activity resumes; neutral polling must never take it back.
        if (HasNativeKeyboardFocus) FocusInteraction?.Invoke();
        controllerPointerVisible = true;
        DrawCursor();
    }

    private void UseDesktopPointer()
    {
        if (!input) return;
        if (controllerPointerVisible || pointerPressed) CancelPointer();
        controllerPointerVisible = false;
        cursorDirty = false;
        wheelX = wheelY = 0;
        DrawCursor();
    }
}
