using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnablePinnedMenuValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            Exception? failure = null;
            try
            {
                if (startup is not null) await startup;
                await Until(() => foreground && activeWidget is not null && !switching);
                var descriptor = catalogItems.Single(w => w.Id == activeWidget);
                var choice = GetPinChoices(descriptor).First(c => !c.Media && !c.Popup);
                await PinAsync(descriptor.Id, choice.LayoutId);
                var pin = pinned ?? throw new InvalidOperationException("Could not create validation pin.");
                await SelectAsync(descriptor.Id, enterWidget: false); FocusTray();
                await Until(() => foreground && !switching && ManagePinsButton.IsEnabled && ManagePinsButton.ActualWidth > 0);
                Check(ManagePinsButton.IsTabStop && AutomationProperties.GetName(ManagePinsButton) == "Manage pins",
                    "The host exposes a named keyboard-focusable Manage pins button without opening a widget context menu");
                Check(ReturnFromPinButton.Visibility == Visibility.Collapsed && !ReturnFromPinButton.IsEnabled,
                    "Unavailable Return is absent from the rail and keyboard navigation");
                await CaptureControlsAsync("manage");
                var originalOpacity = pin.Window.OpacityPercent;
                foreach (var save in new[] { false, true })
                {
                    ManagePinsButton.Focus(FocusState.Keyboard);
                    NativeContextProbeInput.PressKey(save ? VirtualKey.Enter : VirtualKey.Space);
                    await Until(() => pinnedAdjustment is not null && pinAdjustmentToolbar!.Opacity.Visibility == Visibility.Visible);
                    Check(!HostChoiceActive,"The sole pin enters adjustment without an intermediate action menu");
                    Check(pinAdjustmentToolbar!.Save.Visibility == Visibility.Visible && pinAdjustmentToolbar!.Cancel.Visibility == Visibility.Visible &&
                        pinAdjustmentToolbar.Window.IsVisible,
                        "Mouse opacity adjustment exposes a slider, instructions, and Save/Cancel controls");
                    if (!save) await CaptureControlsAsync("opacity");
                    var desired = originalOpacity == 65 ? 70 : 65;
                    ((IRangeValueProvider)FrameworkElementAutomationPeer.CreatePeerForElement(pinAdjustmentToolbar!.Opacity).GetPattern(PatternInterface.RangeValue)).SetValue(desired);
                    await Until(() => pin.Window.OpacityPercent == desired);
                    var geometry = pin.Window.Bounds;
                    pinAdjustmentToolbar!.Opacity.Focus(FocusState.Keyboard);
                    await Until(() => pinAdjustmentToolbar!.Opacity.FocusState != FocusState.Unfocused);
                    NativeContextProbeInput.PressKey(VirtualKey.Right);
                    await Until(() => pin.Window.OpacityPercent != desired);
                    desired += 5;
                    Check(pin.Window.OpacityPercent == desired, $"Physical slider Right changes opacity by five points (actual {pin.Window.OpacityPercent}, expected {desired})");
                    NativeContextProbeInput.PressKey(VirtualKey.Tab);
                    await Until(() => pinAdjustmentToolbar!.Close.FocusState != FocusState.Unfocused);
                    NativeContextProbeInput.PressKey(VirtualKey.Tab);
                    await Until(() => pinAdjustmentToolbar!.Save.FocusState != FocusState.Unfocused);
                    NativeContextProbeInput.PressKey(VirtualKey.Tab, shift: true);
                    await Until(() => pinAdjustmentToolbar!.Close.FocusState != FocusState.Unfocused);
                    NativeContextProbeInput.PressKey(VirtualKey.Tab, shift: true);
                    await Until(() => pinAdjustmentToolbar!.Opacity.FocusState != FocusState.Unfocused);
                    Check(pin.Window.Bounds == geometry, "Physical slider arrows and Tab/Shift+Tab change opacity/focus without moving or resizing the pin");
                    Check(pinnedAdjustment!.Placement.OpacityPercent == desired,
                        "The native opacity slider updates the shared reversible preview");
                    (save ? pinAdjustmentToolbar!.Save : pinAdjustmentToolbar!.Cancel).Focus(FocusState.Keyboard);
                    NativeContextProbeInput.PressKey(save ? VirtualKey.Enter : VirtualKey.Space);
                    await Until(() => !PinnedAdjustmentActive && ManagePinsButton.FocusState != FocusState.Unfocused);
                    Check(pin.Window.OpacityPercent == (save ? desired : originalOpacity),
                        "The shell " + (save ? "Save commits" : "Cancel restores") + " opacity and returns focus to Manage pins");
                }
                Invoke(ManagePinsButton);
                await Until(() => pinnedAdjustment is not null);
                var beforeEscape = pin.Window.OpacityPercent;
                pinAdjustmentToolbar!.Opacity.Value = 40; pinAdjustmentToolbar!.Opacity.Focus(FocusState.Keyboard);
                NativeContextProbeInput.Escape();
                await Until(() => !PinnedAdjustmentActive && ManagePinsButton.FocusState != FocusState.Unfocused);
                Check(pin.Window.OpacityPercent == beforeEscape, "Physical Escape from the opacity slider cancels preview and restores launcher focus");
                Invoke(ManagePinsButton);
                await Until(() => pinnedAdjustment is not null);
                await CaptureControlsAsync("move");
                Invoke(pinAdjustmentToolbar!.Cancel);
                await Until(() => !PinnedAdjustmentActive);
                foreach (var opacity in new[] { false, true })
                foreach (var save in new[] { false, true })
                {
                    await SelectAsync(descriptor.Id, enterWidget: false); FocusTray();
                    await Until(() => foreground && !switching && FocusedTrayWidget()?.Id == descriptor.Id);
                    Receive(Sample());
                    Receive(Sample(0x10, pressed: 0x10)); Receive(Sample(released: 0x10));
                    await Until(() => trayMenu is not null);
                    var menu = trayMenu!;
                    Check(!menu.Flyout.Items.OfType<MenuFlyoutSubItem>().Any(i=>AutomationProperties.GetAutomationId(i)=="Overlay.TrayGroup.Pin"),
                        "Tray pin controls have no intermediate Manage pin submenu");
                    var item = menu.Items.Single(i => AutomationProperties.GetAutomationId(i) == "Overlay.TrayCommand.Pin.Adjust");
                    await Until(() => item.IsLoaded && item.ActualWidth > 0);
                    item.Focus(FocusState.Keyboard);
                    Receive(Sample(0x1000, pressed: 0x1000));
                    await Task.Delay(250);
                    Check(pinnedAdjustment is not null && !PinnedInputActive && pin.Window.FocusIndicator.Visibility == Visibility.Visible,
                        (opacity ? "Opacity edit" : "Geometry edit") + " enters through the direct tray Adjust action and remains active after the tray closes");
                    Receive(Sample(0x1000, pressed: 0x1000));
                    await Task.Delay(100);
                    Check(pinnedAdjustment is not null && !savingPinnedAdjustment, "A duplicate opening press cannot immediately save adjustment");
                    var boundsBeforePrime = pin.Window.Bounds;
                    var opacityBeforePrime = pin.Window.OpacityPercent;
                    var primed = Sample(0x1000, pressed: 0x1000, rightX: -32767); primed.Primed = 1;
                    Receive(primed); await Task.Delay(100);
                    Check(pinnedAdjustment is not null && !savingPinnedAdjustment && pin.Window.Bounds == boundsBeforePrime &&
                        pin.Window.OpacityPercent == opacityBeforePrime, "Controller re-priming cannot save or move an adjustment");
                    if (save)
                    {
                        // A re-primed neutral sample can replace the old source's release edge.
                        var neutral = Sample(); neutral.Primed = 1; Receive(neutral);
                    }
                    else Receive(Sample(released: 0x1000));
                    var change=(ushort)(opacity?0x0100:4);
                    Receive(Sample(change, pressed: change)); Receive(Sample(released: change));
                    var finish = (ushort)(save ? 0x1000 : 0x2000);
                    Receive(Sample(finish, pressed: finish)); Receive(Sample(released: finish));
                    await Until(() => !PinnedAdjustmentActive);
                    Check(pin.Window.FocusIndicator.Visibility == Visibility.Collapsed && !PinnedInputActive,
                        "A fresh " + (save ? "Save" : "Cancel") + " ends adjustment and restores the main input owner");
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                CancelPinnedAdjustment(); CloseTrayMenu(false);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
            }
            void Check(bool valid, string message)
            { if (!valid) throw new InvalidOperationException(message); checks.Add(message); }
            static void Invoke(Button button) => ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(button)
                .GetPattern(PatternInterface.Invoke)).Invoke();
            async Task CaptureControlsAsync(string mode)
            {
                var theme = RailRegion.RequestedTheme;
                var background = RailRegion.Background;
                var viewport = shellViewport;
                try
                {
                    foreach (var size in new[] { 420d, 480d, 900d })
                    foreach (var variant in new[] { ElementTheme.Light, ElementTheme.Dark })
                    {
                        ConfigureProductionViewport(new(size + 48 / Appearance.InterfaceScale, viewport.Height));
                        RailRegion.RequestedTheme = variant;
                        RailRegion.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            variant == ElementTheme.Light ? Microsoft.UI.Colors.WhiteSmoke : Microsoft.UI.Colors.Black);
                        UpdateLayout(); RefreshPinManagementCommands(); UpdateLayout();
                        await Task.Delay(100);
                        var fits = RailRegion.ActualWidth <= size + 1 && PinManagementCommands.ActualWidth < size &&
                            PinAdjustmentHelp.DesiredSize.Height <= GuideRegion.ActualHeight + 1;
                        var detail = $"rail={RailRegion.ActualWidth}, commands={PinManagementCommands.ActualWidth}, help={PinAdjustmentHelp.DesiredSize.Height}, guide={GuideRegion.ActualHeight}";
                        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                        await bitmap.RenderAsync(RailRegion);
                        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
                        await encoder.FlushAsync(); stream.Seek(0);
                        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
                        await reader.LoadAsync((uint)stream.Size);
                        var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
                        await File.WriteAllBytesAsync(Path.ChangeExtension(path, $".{mode}-{size}-{variant}.png"), bytes);
                        Check(fits, $"{mode} commands fit {size}px {variant} rail without reducing the guide or content height ({detail})");
                    }
                }
                finally { RailRegion.RequestedTheme = theme; RailRegion.Background = background; ConfigureProductionViewport(viewport); UpdateLayout(); RefreshPinManagementCommands(); }
            }
            async Task Until(Func<bool> ready)
            {
                for (var attempt = 0; attempt < 400; attempt++)
                { if (ready()) return; await Task.Delay(25, lifetime.Token); }
                throw new TimeoutException("Pinned menu transition did not settle.");
            }
        };
    }
}
