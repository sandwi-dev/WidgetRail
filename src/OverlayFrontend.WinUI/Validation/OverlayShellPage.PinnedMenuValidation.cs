using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

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
                foreach (var opacity in new[] { false, true })
                foreach (var save in new[] { false, true })
                {
                    await SelectAsync(descriptor.Id, enterWidget: false); FocusTray();
                    await Until(() => foreground && !switching && FocusedTrayWidget()?.Id == descriptor.Id);
                    Receive(Sample());
                    Receive(Sample(0x10, pressed: 0x10)); Receive(Sample(released: 0x10));
                    await Until(() => trayMenu is not null);
                    var menu = trayMenu!;
                    var group = menu.Flyout.Items.OfType<MenuFlyoutSubItem>().Single(i => AutomationProperties.GetAutomationId(i) == "Overlay.TrayGroup.Pin");
                    ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(group).GetPattern(PatternInterface.ExpandCollapse)).Expand();
                    var item = menu.Items.Single(i => AutomationProperties.GetAutomationId(i) == "Overlay.TrayCommand.Pin." + (opacity ? "Opacity" : "Adjust"));
                    await Until(() => item.IsLoaded && item.ActualWidth > 0);
                    item.Focus(FocusState.Keyboard);
                    Receive(Sample(0x1000, pressed: 0x1000));
                    await Task.Delay(250);
                    Check(pinnedAdjustment is not null && !PinnedInputActive && pin.Window.FocusIndicator.Visibility == Visibility.Visible,
                        (opacity ? "Opacity" : "Move/resize") + " enters through the actual Manage pin menu and remains active after it closes");
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
                    Receive(Sample(4, pressed: 4)); Receive(Sample(released: 4));
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
            async Task Until(Func<bool> ready)
            {
                for (var attempt = 0; attempt < 400; attempt++)
                { if (ready()) return; await Task.Delay(25, lifetime.Token); }
                throw new TimeoutException("Pinned menu transition did not settle.");
            }
        };
    }
}
