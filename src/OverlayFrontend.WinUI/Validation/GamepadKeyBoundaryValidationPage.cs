using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>
/// Native SendInput probe of WinUI routing. It deliberately does not emulate
/// hardware sampling or claim that a physical controller duplication is fixed.
/// </summary>
internal sealed class GamepadKeyBoundaryValidationPage : Page, IDisposable
{
    private readonly StackPanel scene = new() { Spacing = 12, XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled };
    private readonly Button action = new() { Content = "Native action" };
    private readonly ListView list = new() { Height = 240, SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBlock status = new() { Text = "Waiting for native gamepad-key probe" };
    private readonly List<string> checks = [];
    private readonly List<object> keys = [];
    private GamepadKeyBoundary? boundary;
    private bool adapterOwnsInput;
    private readonly CancellationTokenSource lifetime = new();
    private int actions;
    private int gamepadDown, gamepadUp;
    private Task? run;

    internal GamepadKeyBoundaryValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "GamepadBoundary.Status");
        AutomationProperties.SetAutomationId(action, "GamepadBoundary.Action");
        AutomationProperties.SetAutomationId(list, "GamepadBoundary.List");
        action.Click += (_, _) => ++actions;
        for (var index = 0; index < 5; ++index) list.Items.Add("Native row " + index);
        scene.Children.Add(status); scene.Children.Add(action); scene.Children.Add(list);
        Content = scene;
        scene.AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, args) => Observe(args, false)), true);
        scene.AddHandler(PreviewKeyUpEvent, new KeyEventHandler((_, args) => Observe(args, true)), true);
        Loaded += (_, _) => run ??= RunAsync();
    }

    private void Observe(KeyRoutedEventArgs args, bool up)
    {
        keys.Add(new { key = args.Key.ToString(), original = args.OriginalKey.ToString(), up, handled = args.Handled });
        if (!GamepadKeyBoundary.IsGamepadKey(args.OriginalKey)) return;
        if (up) ++gamepadUp; else ++gamepadDown;
    }

    private async Task RunAsync()
    {
        try
        {
            await Task.Delay(300, lifetime.Token);
            action.Focus(FocusState.Keyboard);
            await Key(VirtualKey.Space);
            Check(actions == 1, "physical keyboard Space reaches native Button down/up activation");
            var baseline = actions;
            await Key(VirtualKey.GamepadA);
            Check(gamepadDown > 0 && gamepadUp > 0, "SendInput enters real WinUI OriginalKey gamepad down and up route");
            Check(actions == baseline + 1, "positive control: no adapter owner permits native gamepad Button activation");

            adapterOwnsInput = true;
            boundary = new(scene, () => adapterOwnsInput);
            baseline = actions;
            Invoke(action); // Existing controller semantic route terminates at the native control action.
            await Key(VirtualKey.GamepadA);
            Check(actions == baseline + 1, "one semantic action plus duplicate native GamepadA produces exactly one Button action");
            baseline = actions;
            await Key(VirtualKey.Space);
            Check(actions == baseline + 1, "keyboard Space remains available with the adapter owning gamepad input");
            Invoke(action);
            Check(actions == baseline + 2, "UI Automation invoke remains available");

            await FocusRow(1);
            await Key(VirtualKey.Down);
            Check(FocusedRow() == 2, "keyboard list navigation remains native");
            await FocusRow(1);
            Check(FocusManager.TryMoveFocus(FocusNavigationDirection.Down, new FindNextElementOptions { SearchRoot = list }),
                "normalized controller semantic route moves list focus");
            await Key(VirtualKey.GamepadDPadDown);
            Check(FocusedRow() == 2, "duplicate native D-pad route does not move the list a second row");
            await Key(VirtualKey.GamepadLeftThumbstickDown);
            Check(FocusedRow() == 2, "native left-stick mapped navigation is also owned");

            await Menu(toggle: false);
            await Menu(toggle: true);
            await Dialog();

            action.Focus(FocusState.Keyboard);
            boundary.Dispose(); boundary = null;
            baseline = actions;
            await Key(VirtualKey.GamepadA);
            Check(actions == baseline + 1, "disposing the owner restores native gamepad activation");
            adapterOwnsInput = false;
            boundary = new(scene, () => adapterOwnsInput);
            baseline = actions;
            await Key(VirtualKey.GamepadA);
            Check(actions == baseline + 1, "inactive adapter leaves native gamepad input available");
            adapterOwnsInput = true;
            await Key(VirtualKey.GamepadA);
            Check(actions == baseline + 1, "recreated active owner consumes both phases without a stale handler");
            Check(gamepadDown == gamepadUp, "native injected gamepad down/up phases remain balanced");
            status.Text = "Passed " + checks.Count + " native gamepad-key checks";
            await Save(null);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            status.Text = "Failed: " + error.Message;
            await Save(error.ToString());
        }
        finally { boundary?.Dispose(); boundary = null; }
    }

    private async Task Menu(bool toggle)
    {
        action.Focus(FocusState.Keyboard);
        var flyout = new MenuFlyout();
        MenuFlyoutItemBase first = toggle ? new ToggleMenuFlyoutItem { Text = "First option" } : new MenuFlyoutItem { Text = "First action" };
        MenuFlyoutItemBase second = toggle ? new ToggleMenuFlyoutItem { Text = "Second option" } : new MenuFlyoutItem { Text = "Second action" };
        flyout.Items.Add(first); flyout.Items.Add(second);
        GamepadKeyBoundary.ObserveFlyout(flyout, action);
        var menuActions = 0;
        if (first is ToggleMenuFlyoutItem toggleItem) toggleItem.Click += (_, _) => ++menuActions;
        else ((MenuFlyoutItem)first).Click += (_, _) => ++menuActions;
        flyout.ShowAt(action);
        await Until(() => first.IsLoaded);
        first.Focus(FocusState.Keyboard);
        await Key(VirtualKey.GamepadDPadDown);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first), (toggle ? "select" : "context menu") + " popup owns native gamepad navigation");
        await Key(VirtualKey.GamepadA);
        Check(menuActions == 0 && first.IsLoaded, "popup gamepad down/up does not invoke or dismiss " + (toggle ? "select" : "context menu"));
        await Key(VirtualKey.Down);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), second), "popup physical keyboard remains native");
        flyout.Hide();
        await Until(() => !first.IsLoaded);
    }

    private async Task Dialog()
    {
        var completed = 0;
        WidgetTextEntryDialog? dialog = null;
        dialog = new(new ViewNode { Id = "editor", Kind = ViewNodeKind.TextEntry, TextEntryValue = "", TextEntryMaximumLength = 32 },
            _ => { ++completed; dialog!.Hide(); });
        dialog.BindRoot(XamlRoot);
        var showing = dialog.ShowAsync();
        await Until(() => dialog.IsLoaded);
        dialog.RestoreFocus();
        await Key(VirtualKey.GamepadA);
        await Key(VirtualKey.GamepadB);
        Check(dialog.TakeValue() == "" && completed == 0, "text dialog does not reinterpret mapped gamepad Space/Escape");
        dialog.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        var value = dialog.TakeValue();
        await Key(VirtualKey.GamepadA);
        Check(value.Length == 1 && dialog.TakeValue() == value, "text dialog semantic activation remains single with duplicate key route");
        await Key(VirtualKey.Space);
        Check(dialog.TakeValue() == value + " ", "text dialog still accepts physical keyboard Space");
        await Key(VirtualKey.Enter);
        await showing;
        Check(completed == 1, "text dialog keyboard commit remains single");
        dialog.Erase();
    }

    private async Task FocusRow(int index)
    {
        list.ScrollIntoView(list.Items[index]);
        await Until(() => list.ContainerFromIndex(index) is Control);
        list.SelectedIndex = index;
        ((Control)list.ContainerFromIndex(index)).Focus(FocusState.Keyboard);
        await Task.Delay(40, lifetime.Token);
        Check(FocusedRow() == index, "native list starting focus is row " + index);
    }
    private int FocusedRow() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? list.IndexFromContainer(focused) : -1;
    private static void Invoke(FrameworkElement element) => ((IInvokeProvider)(FrameworkElementAutomationPeer.CreatePeerForElement(element)
        ?? throw new InvalidOperationException("Missing native peer")).GetPattern(PatternInterface.Invoke)).Invoke();
    private async Task Key(VirtualKey key)
    { lifetime.Token.ThrowIfCancellationRequested(); GamepadKeyProbeInput.Send(key); await Task.Delay(120, lifetime.Token); }
    private async Task Until(Func<bool> condition)
    {
        var end = Environment.TickCount64 + 5000;
        while (!condition()) { if (Environment.TickCount64 > end) throw new TimeoutException("Native UI did not settle"); await Task.Delay(20, lifetime.Token); }
    }
    private void Check(bool passed, string description)
    { if (!passed) throw new InvalidOperationException(description); checks.Add(description); }
    private Task Save(string? error)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(folder);
        return File.WriteAllTextAsync(Path.Combine(folder, "gamepad-boundary-result.json"), JsonSerializer.Serialize(new
        { pid = Environment.ProcessId, passed = error is null, checks, keys, error, evidence = "Native SendInput virtual-key routing; physical controller behavior not established" }));
    }
    public void Dispose() { lifetime.Cancel(); boundary?.Dispose(); boundary = null; }
}

internal static partial class GamepadKeyProbeInput
{
    internal static unsafe void Send(VirtualKey key)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var process);
        if (process != (uint)Environment.ProcessId)
            throw new InvalidOperationException("Native input probe requires its own window in the foreground.");
        var input = stackalloc NativeInput[2];
        input[0] = new() { Type = 1, Data = new() { Keyboard = new() { Key = (ushort)key } } };
        input[1] = new() { Type = 1, Data = new() { Keyboard = new() { Key = (ushort)key, Flags = 2 } } };
        if (SendInput(2, input, sizeof(NativeInput)) != 2) throw new InvalidOperationException("Native SendInput failed: " + Marshal.GetLastPInvokeError());
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { internal uint Type; internal InputUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] internal KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { internal ushort Key, Scan; internal uint Flags, Time; internal nuint ExtraInfo; }
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint SendInput(uint count, NativeInput* inputs, int size);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);
}
