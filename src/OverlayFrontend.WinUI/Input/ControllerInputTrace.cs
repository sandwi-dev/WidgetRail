using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Opt-in bounded event trace. Records navigation identities, never entered text.</summary>
internal sealed class ControllerInputTrace : IDisposable
{
    private readonly UIElement root;
    private readonly Action<string> write;
    private readonly KeyEventHandler down;
    private readonly KeyEventHandler up;
    private int remaining = 256;

    internal ControllerInputTrace(UIElement root, Action<string> write)
    {
        this.root = root;
        this.write = write;
        down = (_, args) => Key("down", args);
        up = (_, args) => Key("up", args);
        root.AddHandler(UIElement.PreviewKeyDownEvent, down, true);
        root.AddHandler(UIElement.PreviewKeyUpEvent, up, true);
        FocusManager.GettingFocus += GettingFocus;
    }

    private void Key(string phase, KeyRoutedEventArgs args)
    {
        var key = args.OriginalKey;
        if (key is not (>= VirtualKey.GamepadA and <= VirtualKey.GamepadRightThumbstickLeft) &&
            key is not (VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right)) return;
        Record($"XAML key={phase} original={key} mapped={args.Key} handled={args.Handled} focused={Identity(FocusManager.GetFocusedElement(root.XamlRoot))}");
    }

    private void GettingFocus(object? sender, GettingFocusEventArgs args)
    {
        if (args.NewFocusedElement is not UIElement target || target.XamlRoot != root.XamlRoot) return;
        Record($"XAML focus device={args.InputDevice} direction={args.Direction} state={args.FocusState} old={Identity(args.OldFocusedElement)} new={Identity(args.NewFocusedElement)} handled={args.Handled} canceled={args.Cancel}");
    }

    private void Record(string value)
    {
        if (remaining-- > 0) write(value);
    }

    private static string Identity(object? element)
    {
        if (element is not DependencyObject node) return "none";
        var id = AutomationProperties.GetAutomationId(node);
        return node.GetType().Name + ":" + id[..Math.Min(id.Length, 160)];
    }

    public void Dispose()
    {
        root.RemoveHandler(UIElement.PreviewKeyDownEvent, down);
        root.RemoveHandler(UIElement.PreviewKeyUpEvent, up);
        FocusManager.GettingFocus -= GettingFocus;
    }
}
