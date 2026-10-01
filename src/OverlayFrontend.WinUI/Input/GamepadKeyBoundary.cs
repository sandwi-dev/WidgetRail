using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>
/// One XAML root's platform-adapter ownership. WinUI retains keyboard, pointer,
/// automation and focus behavior; its additional gamepad key route is consumed.
/// This owns no polling, navigation, key repeat or action dispatch.
/// </summary>
internal sealed class GamepadKeyBoundary : IDisposable
{
    private static readonly ConditionalWeakTable<XamlRoot, GamepadKeyBoundary> Owners = new();
    private readonly FrameworkElement scene;
    private readonly Func<bool> ownsInput;
    private readonly HashSet<UIElement> roots = [];
    private readonly KeyEventHandler keyHandler;
    private XamlRoot? registeredRoot;
    private bool disposed;

    internal GamepadKeyBoundary(FrameworkElement scene, Func<bool> ownsInput)
    {
        this.scene = scene;
        this.ownsInput = ownsInput;
        keyHandler = OnKey;
        scene.Loaded += Loaded;
        scene.Unloaded += Unloaded;
        Attach(scene);
        Register();
    }

    internal static bool IsGamepadKey(VirtualKey originalKey) =>
        originalKey is >= VirtualKey.GamepadA and <= VirtualKey.GamepadRightThumbstickLeft;

    /// <summary>Handled-events-too observers must not reinterpret mapped gamepad keys.</summary>
    internal static bool Owns(FrameworkElement element, KeyRoutedEventArgs args) =>
        IsGamepadKey(args.OriginalKey) && element.XamlRoot is { } root &&
        Owners.TryGetValue(root, out var owner) && owner.Active;

    private bool Active => !disposed && ownsInput();
    private void OnKey(object sender, KeyRoutedEventArgs args)
    {
        if (Active && IsGamepadKey(args.OriginalKey)) args.Handled = true;
    }

    private void Loaded(object sender, RoutedEventArgs args) => Register();
    private void Unloaded(object sender, RoutedEventArgs args)
    {
        if (!scene.IsLoaded) Unregister();
    }
    private void Register()
    {
        if (disposed || scene.XamlRoot is not { } root || ReferenceEquals(root, registeredRoot)) return;
        Unregister();
        if (Owners.TryGetValue(root, out var existing) && !ReferenceEquals(existing, this))
            throw new InvalidOperationException("A XAML root already has a platform input owner.");
        Owners.Add(root, this);
        registeredRoot = root;
        AttachOpenPopups(root);
    }
    private void Unregister()
    {
        if (registeredRoot is { } root && Owners.TryGetValue(root, out var owner) && ReferenceEquals(owner, this))
            Owners.Remove(root);
        registeredRoot = null;
        foreach (var element in roots.Where(element => !ReferenceEquals(element, scene)).ToArray()) Detach(element);
    }
    private void Attach(UIElement element)
    {
        if (!roots.Add(element)) return;
        element.AddHandler(UIElement.PreviewKeyDownEvent, keyHandler, true);
        element.AddHandler(UIElement.PreviewKeyUpEvent, keyHandler, true);
        if (element is FrameworkElement framework && !ReferenceEquals(element, scene)) framework.Unloaded += PopupUnloaded;
    }
    private void Detach(UIElement element)
    {
        if (!roots.Remove(element)) return;
        element.RemoveHandler(UIElement.PreviewKeyDownEvent, keyHandler);
        element.RemoveHandler(UIElement.PreviewKeyUpEvent, keyHandler);
        if (element is FrameworkElement framework) framework.Unloaded -= PopupUnloaded;
    }
    private void PopupUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { IsLoaded: false } element) Detach(element);
    }
    private void AttachOpenPopups(XamlRoot root)
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            if (popup.Child is { } child) Attach(child);
    }

    // Popup trees are not descendants of scene. Register at their native open
    // event, before options gain focus, and detach with their native lifetime.
    internal static void ObserveFlyout(FlyoutBase flyout, FrameworkElement anchor)
    {
        flyout.Opened += (_, _) =>
        {
            if (anchor.XamlRoot is { } root && Owners.TryGetValue(root, out var owner)) owner.AttachOpenPopups(root);
        };
    }

    internal static void ObserveDialog(ContentDialog dialog)
    {
        dialog.Loaded += (_, _) =>
        {
            if (dialog.XamlRoot is not { } root || !Owners.TryGetValue(root, out var owner)) return;
            owner.AttachOpenPopups(root);
            owner.Attach(dialog);
        };
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        scene.Loaded -= Loaded;
        scene.Unloaded -= Unloaded;
        Unregister();
        foreach (var element in roots.ToArray()) Detach(element);
    }
}
