using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WinUIEx;
using WinUIEx.Messaging;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal enum PeerWindowLayer { Pin, Notification, Toolbar }

/// <summary>Peer native window. The coordinator owns widget/session and input authority.</summary>
internal sealed partial class PinnedWidgetWindow : IDisposable
{
    private readonly AppWindow window;
    private readonly DesktopWindowXamlSource island;
    private readonly Input.GamepadKeyBoundary gamepadKeys;
    private WindowMessageMonitor? messages;
    private readonly Grid content = new();
    private readonly DesktopAcrylicSurface surfaceBackground = new();
    // A sibling overlay keeps the ownership cue out of widget measurement and
    // never steals pointer/controller input from the presentation beneath it.
    private readonly Border focusIndicator = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly PinnedWindowRoot automationRoot = new();
    private readonly OverlayScaleRoot scaleRoot = new();
    private readonly Microsoft.UI.System.ThemeSettings theme;
    private bool disposed;
    private bool hasForeground;
    private bool ownedBrowserFocus;
    private bool placementActive;
    private double focusWidth = 4;
    internal event Action? CloseRequested;
    internal event Action<bool>? ForegroundChanged;
    internal event Action? ThemeChanged;
    internal event Action? PlacementEnvironmentChanged;
    internal Border SurfaceBackground => surfaceBackground.Paint;
    internal Border FocusIndicator => focusIndicator;
    internal FrameworkElement AutomationRoot => automationRoot;
    internal bool SystemHighContrast => theme.HighContrast;
    internal double InterfaceScale
    {
        get => scaleRoot.InterfaceScale;
        set { scaleRoot.InterfaceScale = value; UpdateFocusGeometry(); }
    }
    internal bool Interactive { get; private set; }
    internal bool NativeInputEnabled => IsWindowEnabled(Handle);
    internal bool IsVisible => !disposed && window.IsVisible;
    internal nint Handle { get; }
    internal FrameworkElement? Child { get; private set; }
    internal void Activate() { DemandAlive(); window.Show(true); HwndExtensions.SetForegroundWindow(Handle); }
    internal ExtendedWindowStyle ExtendedStyle => HwndExtensions.GetExtendedWindowStyle(Handle);
    internal Windows.Graphics.SizeInt32 ClientSize => window.ClientSize;
    internal PinnedBounds Bounds => new(window.Position.X, window.Position.Y, window.Size.Width, window.Size.Height);
    internal int OpacityPercent { get; private set; } = 100;
    internal void SetHostIdentity(string title, string automationId)
    {
        window.Title = title;
        AutomationProperties.SetName(automationRoot, title);
        AutomationProperties.SetAutomationId(automationRoot, automationId);
    }

    private readonly nint mainWindow;
    private readonly PeerWindowLayer layer;
    internal PinnedWidgetWindow(string name, nint mainWindow = 0, PeerWindowLayer layer = PeerWindowLayer.Pin)
    {
        this.mainWindow = mainWindow;
        this.layer = layer;
        AutomationProperties.SetAutomationId(automationRoot, "Overlay.PinnedContent");
        AutomationProperties.SetName(automationRoot, "Pinned widget — " + name);
        scaleRoot.Children.Add(content);
        surfaceBackground.Child = scaleRoot;
        automationRoot.Children.Add(surfaceBackground);
        AutomationProperties.SetAccessibilityView(focusIndicator, AccessibilityView.Raw);
        automationRoot.Children.Add(focusIndicator);
        automationRoot.Loaded += (_, _) => UpdateFocusVisibility();
        window = AppWindow.Create();
        window.Title = "WidgetRail pinned — " + name;
        window.TitleBar.ExtendsContentIntoTitleBar = true;
        Handle = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.Id);
        // Window's implicit island defers its initial visibility until activation.
        // A passive peer instead owns an explicit island whose site can be shown
        // without activating the top-level HWND or manufacturing focus messages.
        island = new DesktopWindowXamlSource();
        gamepadKeys = new(automationRoot, () => Interactive);
        theme = Microsoft.UI.System.ThemeSettings.CreateForWindowId(window.Id);
        theme.Changed += OnThemeChanged;
        try
        {
            island.Initialize(window.Id);
            island.Content = automationRoot;
            // Rounded XAML leaves uncovered client pixels. Preserve the transparent
            // native backdrop used by the former Window host on this explicit island.
            island.SystemBackdrop = new TransparentTintBackdrop();
            window.IsShownInSwitchers = false;
            var presenter = (OverlappedPresenter)window.Presenter;
            presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            messages = new(Handle);
            messages.WindowMessageReceived += (_, args) =>
            {
                // AppWindow reapplies WS_DLGFRAME for a nonresizable presenter.
                // Own nonclient calculation so the whole HWND remains content,
                // instead of fighting that framework-owned style on each update.
                if (args.Message.MessageId == 0x0083) { args.Handled = true; args.Result = 0; return; } // WM_NCCALCSIZE
                if (args.Message.MessageId is 0x007e or 0x02e0 or 0x001a) PlacementEnvironmentChanged?.Invoke();
                if (args.Message.MessageId == 0x0006)
                {
                    hasForeground = ((long)args.Message.WParam & 0xffff) != 0;
                    UpdateFocusVisibility(); ForegroundChanged?.Invoke(hasForeground);
                }
                if (Interactive) return;
                if (args.Message.MessageId == 0x0084) { args.Handled = true; args.Result = -1; } // HTTRANSPARENT
                else if (args.Message.MessageId == 0x0021) { args.Handled = true; args.Result = 3; } // MA_NOACTIVATE
            };
            window.Closing += (_, args) =>
            {
                if (disposed) return;
                args.Cancel = true;
                CloseRequested?.Invoke();
            };
            window.Changed += (_, args) =>
            {
                if (args.DidSizeChange) SizeIsland();
                if (args.DidVisibilityChange) UpdateFocusVisibility();
            };
            SetInteraction(false);
            SetOpacity(100);
        }
        catch
        {
            disposed = true;
            theme.Changed -= OnThemeChanged;
            messages?.Dispose();
            gamepadKeys.Dispose();
            island.Dispose();
            window.Destroy();
            throw;
        }
    }

    internal void SetContent(FrameworkElement element)
    {
        DemandAlive();
        if (ReferenceEquals(Child, element)) return;
        content.Children.Clear(); content.Children.Add(element); Child = element;
    }
    internal void Place(PinnedBounds bounds)
    {
        DemandAlive();
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        if (Bounds != bounds) window.MoveAndResize(new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        pointerPlacement?.Place(bounds);
        pointerActivation?.Place(bounds);
        SizeIsland();
    }
    internal void PrepareContent()
    {
        DemandAlive();
        SizeIsland();island.SiteBridge.Show();
    }
    internal void Show()
    {
        DemandAlive();
        if (!IsVisible) window.Show(activateWindow: false);
        PrepareContent();
        OverlayWindowFrame.SuppressBorder(Handle,suppressNativeCorners:true);
        ReconcileOrder();
        UpdateFocusVisibility();
    }
    private void SizeIsland()
    {
        if (!disposed) island.SiteBridge.MoveAndResize(new(0, 0, window.ClientSize.Width, window.ClientSize.Height));
    }
    internal void Hide()
    {
        if (disposed) return;
        SetPointerActivation(null);
        SetPointerPlacement(null);
        placementActive = false;
        SetInteraction(false);
        hasForeground = false;
        window.Hide();
    }
    internal void SetInteraction(bool enabled)
    {
        DemandAlive();
        if(enabled)SetPointerActivation(null);
        Interactive = enabled;
        if(!enabled)ownedBrowserFocus=false;
        // Gate the HWND and native descendants without applying disabled XAML
        // styling to the rendered content. Entry reenables it before activation.
        EnableWindow(Handle,enabled);
        var style = ExtendedStyle | ExtendedWindowStyle.ToolWindow | ExtendedWindowStyle.Layered;
        style = enabled ? style & ~(ExtendedWindowStyle.NoActivate | ExtendedWindowStyle.Transparent)
            : style | ExtendedWindowStyle.NoActivate | ExtendedWindowStyle.Transparent;
        OverlayWindowFrame.SetExtendedStyleWithoutActivation(Handle, (nint)(uint)style);
        content.IsHitTestVisible = enabled;
        ReconcileOrder();
        // Passive content remains readable. The coordinator must also reject
        // automation actions through its presentation admission callback.
        AutomationProperties.SetAccessibilityView(content, enabled ? AccessibilityView.Content : AccessibilityView.Raw);
        UpdateFocusVisibility();
        // Do not set IsEnabled on the presentation: that would apply disabled
        // widget styles just because the host window is click-through. The
        // coordinator separately gates presenter entry and action admission.
    }
    internal void ReconcileOrder(nint backdrop = 0)
    {
        if (disposed || mainWindow == 0 || !IsVisible) return;
        if (OverlayWindowFrame.IsWindowVisible(mainWindow) != 0)
        {
            // Relative layer constraints are enough. Requiring every peer to be
            // adjacent to main rotates all siblings on every overlay refresh.
            if (layer == PeerWindowLayer.Notification || Interactive || placementActive)
                OverlayWindowFrame.EnsureAbove(Handle, mainWindow);
            else
            {
                OverlayWindowFrame.EnsureBelow(Handle, mainWindow);
                if(backdrop!=0&&OverlayWindowFrame.IsWindowVisible(backdrop)!=0)
                    OverlayWindowFrame.EnsureAbove(Handle,backdrop);
            }
            if(pointerActivation is { } activation)
            {
                // A guard belongs directly to its visual peer, never over a
                // different overlapping pin. Each pair has a distinct slot.
                OverlayWindowFrame.PlaceAbove(activation.Handle,Handle);
            }
        }
        else OverlayWindowFrame.KeepTopmost(Handle);
    }
    internal void SetOpacity(int percent)
    {
        DemandAlive();
        OpacityPercent = Math.Clamp(percent, 30, 100);
        HwndExtensions.SetWindowOpacity(Handle, (byte)Math.Round(OpacityPercent * 255d / 100));
    }
    private sealed partial class PinnedWindowRoot : Grid
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new RootPeer(this);
        private sealed partial class RootPeer(PinnedWindowRoot owner) : FrameworkElementAutomationPeer(owner)
        {
            protected override string GetClassNameCore() => nameof(PinnedWindowRoot);
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
            protected override bool IsControlElementCore() => true;
        }
    }
    internal void ApplyFocusAppearance(ShellChromePalette palette)
    {
        DemandAlive();
        if (focusIndicator.BorderBrush is Microsoft.UI.Xaml.Media.SolidColorBrush brush) brush.Color = palette.Focus;
        else focusIndicator.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(palette.Focus);
        focusWidth = Math.Max(4, palette.FocusWidth);
        UpdateFocusGeometry();
    }
    internal void SetPlacementActive(bool active)
    {
        DemandAlive();
        placementActive = active;
        ReconcileOrder();
        UpdateFocusVisibility();
    }
    private void UpdateFocusGeometry()
    {
        focusIndicator.BorderThickness = new Thickness(focusWidth * InterfaceScale);
        focusIndicator.CornerRadius = surfaceBackground.SurfaceCornerRadius;
    }
    private void UpdateFocusVisibility()
    {
        var active = !disposed && IsVisible && Interactive && (hasForeground||ownedBrowserFocus);
        focusIndicator.Visibility = !disposed && IsVisible && (placementActive || active&&layer==PeerWindowLayer.Pin) ? Visibility.Visible : Visibility.Collapsed;
        if (automationRoot.XamlRoot is { } root) NativeComputedStyleAdapter.SetRootFocusPresentation(root, active);
    }
    internal void SetOwnedBrowserFocus(bool focused)
    {if(disposed)return;ownedBrowserFocus=focused;UpdateFocusVisibility();}
    private void DemandAlive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!content.DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Pinned windows require their XAML dispatcher.");
    }
    private void OnThemeChanged(Microsoft.UI.System.ThemeSettings sender, object args) => ThemeChanged?.Invoke();
    public void Dispose()
    {
        if (disposed) return;
        DemandAlive();
        SetPointerActivation(null);
        SetPointerPlacement(null);
        placementActive = false;
        SetInteraction(false); window.Hide();
        disposed = true;
        theme.Changed -= OnThemeChanged;
        messages?.Dispose(); messages = null;
        content.Children.Clear(); Child = null;
        surfaceBackground.Retire();
        gamepadKeys.Dispose();
        island.Dispose();
        window.Destroy();
    }
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnableWindow(nint window,[MarshalAs(UnmanagedType.Bool)] bool enabled);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowEnabled(nint window);
}
