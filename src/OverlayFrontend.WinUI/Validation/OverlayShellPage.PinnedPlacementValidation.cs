using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Requires an isolated profile with an existing saved pin. No widget action
    // is invoked; only the host's placement transaction and tray focus are used.
    internal void EnablePinnedPlacementValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            PinnedSurface? tested = null;
            PinnedPlacement? original = null;
            Exception? failure = null;
            try
            {
                if (startup is not null) await startup;
                tested = pinned ?? throw new InvalidOperationException("The isolated validation profile must contain an existing saved pin.");
                await SelectAsync(tested.WidgetId, enterWidget: false);
                FocusTray();
                await Until(() => foreground && FocusedTrayWidget()?.Id == tested.WidgetId);
                await ValidatePinnedAppearanceAsync(tested, Check);
                await ValidatePinnedOperationOwnershipAsync(tested, Check);
                await CheckPassiveFocusAsync();
                var originalBounds = tested.Window.Bounds;
                original = PinnedPlacementPolicy.Capture(originalBounds, tested.Monitor, tested.Limits,
                    tested.LayoutId, tested.Window.OpacityPercent);
                await ValidatePinnedAdjustmentFocusAsync(tested, Check);
                var selection = tested.Selection;
                Receive(Sample());
                await BeginPinnedAdjustmentAsync();
                Check(pinnedAdjustment is not null && !PinnedInputActive && tested.Window.FocusIndicator.Visibility == Visibility.Visible,
                    "production adjustment owns host input and displays the themed pin outline while the pin remains passive");
                var move = originalBounds.X > tested.Monitor.WorkArea.X ? (ushort)4 : (ushort)8;
                Receive(Sample(move, pressed: move));
                Receive(Sample());
                Check(tested.Window.Bounds != originalBounds, "production controller D-pad changes native pinned position");
                Receive(Sample(rightX: -32767));
                Receive(Sample());
                Check(tested.Window.Bounds.Width < originalBounds.Width || originalBounds.Width <= tested.Limits.MinWidth * tested.Monitor.Scale,
                    "production right stick resizes the native window or respects its declared minimum");
                Receive(Sample(0x2000, pressed: 0x2000));
                Receive(Sample(released: 0x2000));
                Check(pinnedAdjustment is null && !savingPinnedAdjustment && tested.Window.Bounds == originalBounds && !PinnedInputActive,
                    "B press and release cancel the preview without entering the passive widget");
                await AssertTrayNavigation("Cancel");

                await BeginPinnedAdjustmentAsync();
                Receive(Sample(move, pressed: move)); Receive(Sample());
                var committed = tested.Window.Bounds;
                Receive(Sample(0x1000, pressed: 0x1000)); Receive(Sample(released: 0x1000));
                await Until(() => !savingPinnedAdjustment && pinnedAdjustment is null);
                var persisted = await new PinnedPreferencesStore(options.SettingsRoot).LoadAsync(lifetime.Token);
                var expected = PinnedPlacementPolicy.Capture(committed, tested.Monitor, tested.Limits, tested.LayoutId, tested.Window.OpacityPercent);
                Check(persisted.Placements.GetValueOrDefault(tested.WidgetId) == expected && tested.Window.Bounds == committed,
                    "A saves the visible preview into the actual profile's atomic DIP/anchor placement file");
                Check(ReferenceEquals(pinned?.Selection, selection) && selection!.IsCurrent && !PinnedInputActive &&
                    tested.Window.FocusIndicator.Visibility == Visibility.Collapsed,
                    "Save preserves pin selection and restores passive presentation without retaining adjustment input");
                await AssertTrayNavigation("Save");

                var opacityBefore = tested.Window.OpacityPercent;
                var opacityDirection = opacityBefore > 30 ? (ushort)4 : (ushort)8;
                var expectedOpacity = opacityBefore + (opacityDirection == 4 ? -5 : 5);
                await BeginPinnedAdjustmentAsync(opacityOnly: true);
                Receive(Sample(opacityDirection, pressed: opacityDirection)); Receive(Sample());
                Check(tested.Window.OpacityPercent == expectedOpacity && tested.Window.Bounds == committed,
                    "opacity controller input previews one five-percent step without moving the pin");
                Check(PinnedPlacementHints().Any(hint => hint.Label.Contains(expectedOpacity + "%")),
                    "opacity adjustment guide reports its current percentage");
                Receive(Sample(rightX: -32767)); Receive(Sample());
                Check(tested.Window.Bounds == committed, "opacity mode consumes resize input without changing bounds");
                Receive(Sample(0x2000, pressed: 0x2000)); Receive(Sample(released: 0x2000));
                Check(tested.Window.OpacityPercent == opacityBefore && tested.Window.Bounds == committed,
                    "opacity Cancel restores original alpha and placement");
                await BeginPinnedAdjustmentAsync(opacityOnly: true);
                Receive(Sample(opacityDirection, pressed: opacityDirection)); Receive(Sample());
                Receive(Sample(0x1000, pressed: 0x1000)); Receive(Sample(released: 0x1000));
                await Until(() => !savingPinnedAdjustment && pinnedAdjustment is null);
                persisted = await new PinnedPreferencesStore(options.SettingsRoot).LoadAsync(lifetime.Token);
                Check(persisted.Placements[tested.WidgetId].OpacityPercent == expectedOpacity,
                    "opacity Save persists the native preview percentage in the existing pin preferences");
                await BeginPinnedAdjustmentAsync(opacityOnly: true);
                Receive(Sample(opacityDirection, pressed: opacityDirection)); Receive(Sample());
                SetVisible(false);
                Check(tested.Window.OpacityPercent == expectedOpacity, "hiding cancels uncommitted opacity while retaining the saved value");
                SetVisible(true);
                await Until(() => !switching && foreground);

                await BeginPinnedAdjustmentAsync();
                Receive(Sample(move, pressed: move)); Receive(Sample());
                SetVisible(false);
                Check(pinnedAdjustment is null && tested.Window.Bounds == committed && tested.Window.IsVisible,
                    "overlay hide cancels an uncommitted placement and retains its passive window");
                SetVisible(true);
                await Until(() => !switching && foreground);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                CancelPinnedAdjustment();
                if (tested is not null && original is not null && ReferenceEquals(pinned, tested))
                {
                    try
                    {
                        var restore = PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(), original, tested.Limits)
                            ?? throw new InvalidOperationException("No display can restore the test pin.");
                        tested.Monitor = restore.Monitor; tested.Window.Place(restore.Bounds);
                        tested.Window.SetOpacity(original.OpacityPercent);
                        tested.LogicalPlacement = PinnedPlacementPolicy.Capture(restore.Bounds, tested.Monitor, tested.Limits,
                            tested.LayoutId, tested.Window.OpacityPercent);
                        await SavePinnedAsync(tested, required: true);
                        Check(true, "isolated test restores the original durable placement after validation");
                    }
                    catch (Exception error) { failure ??= error; }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
            }

            async Task CheckPassiveFocusAsync()
            {
                var current = tested ?? throw new InvalidOperationException("No pinned validation target.");
                await EnterPinnedAsync();
                await Until(() => PinnedInputActive &&
                    FocusManager.GetFocusedElement(current.Window.AutomationRoot.XamlRoot) is Control control &&
                    !ReferenceEquals(control, current.Window.AutomationRoot), "initial-pin-entry");
                // Navigate only: no playback, provider, or slider-value action.
                current.Presenter!.MoveFocus(FocusNavigationDirection.Right);
                var rememberedControl = FocusManager.GetFocusedElement(current.Window.AutomationRoot.XamlRoot) as Control
                    ?? throw new InvalidOperationException("Pinned entry did not focus a native control.");
                NativeComputedStyleAdapter? focusedStyle = null;
                for (DependencyObject? element = rememberedControl; element is not null && focusedStyle is null; element = VisualTreeHelper.GetParent(element))
                    if (element is FrameworkElement target) focusedStyle = NativeComputedStyleAdapter.For(target);
                if (focusedStyle is null) throw new InvalidOperationException("Pinned focus has no native computed style owner.");
                await Until(() => focusedStyle.Interaction.Focused);
                var content = current.Window.Child;
                ExitPinnedInteraction(restoreMain: true);
                await Until(() => foreground && !PinnedInputActive && !focusedStyle.Interaction.Focused && !focusedStyle.Interaction.Pressed);
                Check(ReferenceEquals(current.Window.Child, content) && rememberedControl.IsEnabled && current.Window.IsVisible &&
                    ReferenceEquals(FocusManager.GetFocusedElement(current.Window.AutomationRoot.XamlRoot), rememberedControl) &&
                    (focusedStyle.Target is not Control painted || !painted.UseSystemFocusVisuals) && focusedStyle.FocusDecoration is null,
                    "leaving real pinned interaction suppresses child focus presentation while retaining its logical target and enabled content");
                focusedStyle.SetControllerPressed(true);
                Check(focusedStyle.Interaction == (false, false) && foreground,
                    "late pressed-style updates cannot highlight a passive production pin or reacquire foreground");
                await EnterPinnedAsync();
                await Until(() => PinnedInputActive &&
                    ReferenceEquals(FocusManager.GetFocusedElement(current.Window.AutomationRoot.XamlRoot), rememberedControl) && focusedStyle.Interaction.Focused,
                    "remembered-pin-reentry");
                Check(true, "explicit pin reentry restores focus presentation on the same remembered control");
                ExitPinnedInteraction(restoreMain: true);
                await Until(() => foreground && !PinnedInputActive && !focusedStyle.Interaction.Focused);
                FocusTray();
                await Until(() => FocusedTrayWidget()?.Id == current.WidgetId);
            }

            async Task AssertTrayNavigation(string exit)
            {
                await Until(() => FocusedTrayWidget()?.Id == tested!.WidgetId &&
                    FocusManager.GetFocusedElement(XamlRoot) is FrameworkElement element &&
                    AutomationProperties.GetAutomationId(element).StartsWith("Overlay.Widget.", StringComparison.Ordinal));
                Check(!interactive && !PinnedInputActive, exit + " returns visible native focus to the main rail input owner");
                Check(surface is { IsHitTestVisible: true }, exit + " restores main presenter admission before any widget reselection");
                var current = FocusedTrayWidget()!;
                var index = catalogItems.IndexOf(current);
                if (catalogItems.Count < 2) throw new InvalidOperationException("Tray-resume validation requires at least two installed widgets.");
                var frame = Sample();
                frame.DpadNavigation = new() { Direction = index + 1 < catalogItems.Count ? NavigationDirection.Right : NavigationDirection.Left,
                    Phase = NavigationPhase.Pressed };
                Receive(frame);
                await Until(() => FocusedTrayWidget()?.Id is { } id && id != current.Id);
                Check(true, exit + " releases input so the next D-pad navigation moves the actual rail focus");
                await SelectAsync(tested!.WidgetId, enterWidget: false);
                FocusTray();
                await Until(() => foreground && FocusedTrayWidget()?.Id == tested.WidgetId);
            }
            async Task Until(Func<bool> predicate, string phase = "placement")
            {
                for (var i = 0; i < 400; ++i)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    if (predicate()) return;
                    await Task.Delay(20, lifetime.Token);
                }
                var pinFocus = tested?.Window.AutomationRoot.XamlRoot is { } root
                    ? FocusManager.GetFocusedElement(root) as FrameworkElement : null;
                throw new TimeoutException("Pinned placement production validation did not settle: " + JsonSerializer.Serialize(new
                {
                    phase, visible, foreground, interactive, pinIntent, samePin = ReferenceEquals(pinned, tested),
                    pinCurrent = tested?.IsCurrent, pinInteractive = tested?.Window.Interactive, pinForeground = tested?.HasForeground,
                    awaitingPinFocus = tested?.FocusAcquired is not null, menuOpen = trayMenu is not null,
                    pinFocus = pinFocus is null ? null : AutomationProperties.GetAutomationId(pinFocus),
                }));
            }
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        };
    }

    private static ControllerFrame Sample(ushort buttons = 0, ushort pressed = 0, ushort released = 0, short rightX = 0) => new()
    {
        Connected = 1, State = new() { Buttons = buttons, RightThumbX = rightX }, PressedButtons = pressed, ReleasedButtons = released,
        LastInputFamily = ControllerFamily.Xbox,
    };
}
