using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetPresentationSession;
using Windows.System;
using Windows.UI.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal Action<bool>? KeyboardShortcutEditorVisibilityChanged { get; set; }
    internal Action<Action<OverlayShortcutSettings>?>? KeyboardShortcutCaptureChanged { get; set; }
    internal Func<HostKeyboardShortcutKind,OverlayShortcutSettings, bool>? KeyboardShortcutAvailable { get; set; }
    internal Func<HostKeyboardShortcutKind,OverlayShortcutSettings, OverlayShortcutSettings, CancellationToken, Task<string?>>? SaveKeyboardShortcutRequested { get; set; }

    private async Task EditKeyboardShortcutAsync(WidgetActionRequest request)
    {
        if (owner is null || SaveKeyboardShortcutRequested is null || HostChoiceActive || intentCancellation is not null ||
            retired || !visible || !foreground || switching || activeWidget != "settings") return;
        var requestOwner = owner;
        requestOwner.Session.ValidateKeyboardShortcutCommand(request.Displayed, request.Action);
        var kind=request.Action.ActionId==WidgetRail.WidgetPresentationSession.WidgetPresentationSession.EditReplayShortcutAction?HostKeyboardShortcutKind.SaveReplay:HostKeyboardShortcutKind.Overlay;
        var defaults=kind==HostKeyboardShortcutKind.Overlay?OverlayShortcutSettings.Default:OverlayShortcutSettings.ReplayDefault;
        var selection = selectionVersion;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        intentCancellation = cancellation;
        KeyboardShortcutEditorVisibilityChanged?.Invoke(true);
        var recording = false;
        var saving = false;
        Task? saveTask = null;
        try
        {
            var controllers=(await new PlatformSettingsStore(new(options.SettingsRoot)).LoadAsync(cancellation.Token)).Controllers;
            var expected=kind==HostKeyboardShortcutKind.Overlay?controllers.EffectiveOverlayShortcut:controllers.EffectiveReplaySaveShortcut;
            var selected = expected;
            var record = new Button { Content = "Change shortcut…", HorizontalAlignment = HorizontalAlignment.Stretch };
            var reset = new Button { Content = "Reset to "+defaults.DisplayName, HorizontalAlignment = HorizontalAlignment.Stretch };
            var label = new TextBlock { Text = selected.DisplayName, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap,
                Text = expected.Enabled && KeyboardShortcutAvailable?.Invoke(kind,expected) != true
                    ? "The saved shortcut is unavailable. Windows or another application may be using it."
                    : "Choose a key, with optional Ctrl, Alt, Shift or Windows modifiers." };
            var controls = new StackPanel { Spacing = 12 };
            controls.Children.Add(label); controls.Children.Add(record); controls.Children.Add(reset); controls.Children.Add(status);
            AutomationProperties.SetAutomationId(record, "Host.KeyboardShortcut.Record");
            AutomationProperties.SetAutomationId(reset, "Host.KeyboardShortcut.Reset");
            AutomationProperties.SetAutomationId(label, "Host.KeyboardShortcut.Value");
            AutomationProperties.SetAutomationId(status, "Host.KeyboardShortcut.Status");
            AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            void Capture(OverlayShortcutSettings value)
            {
                if (!recording || saving || cancellation.IsCancellationRequested) return;
                if (!value.IsWellFormed()) { status.Text = "Choose a keyboard key, not a modifier by itself."; return; }
                selected = value with { Enabled = selected.Enabled };
                recording = false;
                label.Text = selected.DisplayName;
                record.Content = "Change shortcut…";
                status.Text = selected.Modifiers == OverlayShortcutModifiers.None && selected.Key is >= 0x30 and <= 0x5A
                    ? "When enabled, this typing key triggers the shortcut from every application."
                    : "Select Save to check availability and apply this shortcut.";
            }
            KeyboardShortcutCaptureChanged?.Invoke(Capture);
            record.Click += (_, _) =>
            {
                if (saving) return;
                if (recording)
                {
                    recording = false;
                    record.Content = "Change shortcut…";
                    status.Text = "Recording cancelled. The current shortcut is unchanged.";
                    return;
                }
                recording = true; record.Content = "Cancel recording";
                status.Text = "Press a key combination to record it. Cancel recording keeps the current shortcut.";
                record.Focus(FocusState.Keyboard);
            };
            reset.Click += (_, _) =>
            {
                recording = true; Capture(defaults);
            };
            bool Current() => !retired && visible && foreground && !switching && activeWidget == "settings" &&
                selectionVersion == selection && ReferenceEquals(owner, requestOwner) && !cancellation.IsCancellationRequested;
            var result = await ShowHostChoiceAsync(kind==HostKeyboardShortcutKind.Overlay?"Overlay keyboard shortcut":"Save replay shortcut", "", (HostDialogChoice[])[new("save", "Save")], cancellation.Token,
                directDecisionButtons: true, configure: dialog =>
                {
                    var enabled = dialog.AddBooleanOption("Enable keyboard shortcut", "Host.KeyboardShortcut.Enabled", beforeChoices: true);
                    enabled.IsChecked = selected.Enabled;
                    enabled.Checked += (_, _) => selected = selected with { Enabled = true };
                    enabled.Unchecked += (_, _) => selected = selected with { Enabled = false };
                    dialog.AddDetails(controls);
                    dialog.PreviewKeyDown += (_, args) =>
                    {
                        if (!recording || saving || args.Handled || Input.GamepadKeyBoundary.IsGamepadKey(args.OriginalKey)) return;
                        args.Handled = true;
                        if (args.KeyStatus.WasKeyDown || !OverlayShortcutSettings.IsPrimaryKey((int)args.Key)) return;
                        static bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
                        var modifiers = OverlayShortcutModifiers.None;
                        if (Down(VirtualKey.Control)) modifiers |= OverlayShortcutModifiers.Control;
                        if (Down(VirtualKey.Menu)) modifiers |= OverlayShortcutModifiers.Alt;
                        if (Down(VirtualKey.Shift)) modifiers |= OverlayShortcutModifiers.Shift;
                        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= OverlayShortcutModifiers.Windows;
                        Capture(new() { Key = (int)args.Key, Modifiers = modifiers });
                    };
                    dialog.KeepOpenOnChoice = _ =>
                    {
                        if (saving) return true;
                        if (recording) { status.Text = "Record a shortcut before saving, or select Cancel recording."; return true; }
                        if (!Current()) return true;
                        saving = true;
                        record.IsEnabled = reset.IsEnabled = false; enabled.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
                        status.Text = "Saving shortcut…";
                        saveTask = SaveAsync();
                        return true;

                        async Task SaveAsync()
                        {
                            try
                            {
                                var error = await SaveKeyboardShortcutRequested(kind,expected, selected, cancellation.Token);
                                if (!Current()) return;
                                if (error is null)
                                {
                                    dialog.KeepOpenOnChoice = null;
                                    dialog.CompleteChoice("save");
                                }
                                else status.Text = error;
                            }
                            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                            catch (Exception error)
                            {
                                Diagnostics.FrontendFailureLog.Current.Write("keyboard-shortcut-editor", error);
                                if (Current()) status.Text = "The shortcut could not be saved. Your previous shortcut is unchanged.";
                            }
                            finally
                            {
                                saving = false;
                                if (Current()) { record.IsEnabled = reset.IsEnabled = true; enabled.IsEnabled = true; dialog.IsPrimaryButtonEnabled = true; }
                            }
                        }
                    };
                });
            if (result == "save" && Current())
                await requestOwner.Session.SendActionAsync(request.Displayed, request.Action, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            cancellation.Cancel();
            if (saveTask is not null) await saveTask;
            KeyboardShortcutCaptureChanged?.Invoke(null);
            KeyboardShortcutEditorVisibilityChanged?.Invoke(false);
            if (ReferenceEquals(intentCancellation, cancellation)) intentCancellation = null;
        }
    }
}
