using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableIntroductionValidation(string resultPath)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            Exception? failure = null;
            try
            {
                if (startup is not null) await startup;
                await Until(() => introductionLoaded && foreground && !switching);
                ReplayIntroduction();
                await Until(() => introductionCard is { Visibility: Visibility.Visible } && FocusedTrayWidget() is not null);
                var card = introductionCard!;
                Check(!card.Focused, "Welcome leaves focus on the selected widget");
                var hiddenDuringTour = false;
                var watch = card.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => hiddenDuringTour |= card.Visibility != Visibility.Visible);
                try
                {
                    await Press(ControllerButton.X);
                    Check(introduction.Step == "guide" && !card.Focused, "Physical X advances without moving focus into the tip");
                    await Press(ControllerButton.A);
                    await Until(() => interactive && !switching);
                    Check(card.Visibility == Visibility.Visible && introduction.Step == "guide", "Users can enter a widget while the guide tip remains visible");
                    await Press(ControllerButton.X);
                    await Until(() => introduction.Step == "commands" && FocusedTrayWidget() is not null);
                    await Press(ControllerButton.Menu);
                    await Until(() => trayMenu is not null);
                    Check(card.Visibility == Visibility.Visible && introduction.Step == "commands", "Opening Commands neither hides nor advances the tour");
                    await Press(ControllerButton.X);
                    Check(trayMenu is null && introduction.Step == "utilities", "X continues from an open Commands menu");
                    await HoldY();
                    Check(card.Visibility == Visibility.Visible && introduction.Step == "utilities" && !card.Focused,
                        "Holding Y enters Task Switcher while the tour remains visible and focus stays in its list");
                    Check(IntroductionTargets().Contains(utilityPanel!), "Utility tip anchors to the opened panel");
                    await Press(ControllerButton.X);
                    await Until(() => activeWidget == "marketplace" && !introductionBusy && introductionPendingFocus is null && !switching);
                    Check(introduction.Step == "catalogue" && !card.Focused && surface?.HasTransientControl == false,
                        "Catalogue opens normally without targeting or opening search");
                    Check(card.Visibility == Visibility.Visible, "Catalogue keeps the tour visible");
                    await Press(ControllerButton.X);
                    await Until(() => IntroductionAppearanceVisible && !introductionBusy && introductionPendingFocus is null && !switching);
                    Check(introduction.Step == "appearance-details" && FocusId() == "Widget.appearance.theme", "Appearance step focuses the actual Theme control");
                    Check(FindIntroductionTarget(surface!, "interface.stepper") is not null, "Appearance highlights Interface size as well as Theme");
                    Check(!hiddenDuringTour && !card.Focused, "The full interactive tour stays visible without taking widget focus");
                }
                finally { card.UnregisterPropertyChangedCallback(VisibilityProperty, watch); }
                await Press(ControllerButton.X);
                await Until(() => introduction.Finished);
                await introductionWrite;
                Check((await introductionStore!.LoadAsync()).Finished, "X finishes and saves the tour");

                ReplayIntroduction();
                await Press(ControllerButton.X); await Press(ControllerButton.X); await Press(ControllerButton.X);
                await HoldY();
                var focus = FocusManager.GetFocusedElement(XamlRoot);
                await Press(ControllerButton.B);
                Check(introduction.Paused && card.Visibility == Visibility.Collapsed && ReferenceEquals(focus, FocusManager.GetFocusedElement(XamlRoot)),
                    "B closes the tour without moving focus out of Task Switcher");
                await Press(ControllerButton.B);
                Check(UtilityIconFocused, "After closing the tour B resumes normal utility navigation");
                CloseUtilityPanel(); FocusDirectionalTray();

                // Reproduce the cold Appearance destination: its controls are not
                // published yet, but its tip must stay alive until they arrive.
                await SelectAsync("marketplace");
                SetIntroduction(new(1, "appearance-details"));
                introductionPendingFocus = "appearance.theme";
                introductionBusy = true;
                RefreshIntroduction();
                await Task.Delay(120);
                Check(card.Visibility == Visibility.Visible && introduction.Step == "appearance-details" && !IntroductionAppearanceVisible,
                    "Appearance tip remains visible while its destination is loading");
                var opened = await InvokeIntentCoreAsync(prepareNotification: owner!.Session.PrepareAppearanceIntentAsync);
                introductionBusy = false; RefreshIntroduction();
                await Until(() => IntroductionAppearanceVisible && introductionPendingFocus is null);
                Check(opened && card.Visibility == Visibility.Visible && FocusId() == "Widget.appearance.theme",
                    "Delayed Appearance publication retains the tip and focuses Theme when ready");
                await Press(ControllerButton.B);
                await introductionWrite;
                Check((await introductionStore.LoadAsync()).Paused, "Closing the tour persists a resumable paused step");
                SetVisible(false); SetVisible(true);
                await Task.Delay(200);
                Check(card.Visibility == Visibility.Collapsed, "A paused tour does not reopen by itself");
                ContinueIntroduction();
                await Until(() => card.Visibility == Visibility.Visible);
                Check(introduction.Step == "appearance-details", "Continue introduction resumes the same step");
                foreach (var family in new[] { ControllerFamily.Xbox, ControllerFamily.PlayStation })
                {
                    WidgetControllerPrompts.Set(family);
                    Check(card.Visibility == Visibility.Visible, family + " hints refresh without dismissing the tip");
                }
                WidgetControllerPrompts.Set(ControllerFamily.Xbox);
                using var sizingCard = new IntroductionCard { Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
                ShellOverlayLayer.Children.Add(sizingCard);
                try
                {
                    foreach (var step in IntroductionProgress.Steps.Where(step => step != "complete"))
                    {
                        sizingCard.Present(step, false, "Show or hide", ControllerPrompt.View, false);
                        var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
                        sizingCard.Apply(palette, PreviewFrameStyle.Resolve(ShellPalette, palette, Appearance), 1.5, 360);
                        UpdateLayout();
                        Check(sizingCard.ActualWidth <= 421 && sizingCard.ActualHeight <= 361, step + " fits a bounded large-text card");
                    }
                }
                finally { ShellOverlayLayer.Children.Remove(sizingCard); }
                ReplayIntroduction();
                await Press(ControllerButton.X); await Press(ControllerButton.X); await Press(ControllerButton.X); await Press(ControllerButton.X);
                await Until(() => activeWidget == "marketplace" && !introductionBusy && !switching);
                await Press(ControllerButton.X);
                await Until(() => IntroductionAppearanceVisible && !introductionBusy && introductionPendingFocus is null && !switching);
                Check(card.Visibility == Visibility.Visible && FocusId() == "Widget.appearance.theme",
                    "Replaying the tour reopens retained Appearance without a stale-scope error");
                ReplayIntroduction();
            }
            catch (Exception error) { failure = error; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));

            string? FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : null;
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
            async Task Press(ControllerButton button)
            {
                if (button is ControllerButton.X or ControllerButton.B)
                {
                    var mask = (ushort)(button == ControllerButton.X ? 0x4000 : 0x2000);
                    Receive(new() { Connected = 1, State = new() { Buttons = mask }, PressedButtons = mask });
                    Receive(new() { Connected = 1, ReleasedButtons = mask });
                }
                else
                {
                    await RouteButtonAsync(button, ControllerEventPhase.Pressed);
                    await RouteButtonAsync(button, ControllerEventPhase.Released);
                }
                await Task.Delay(100);
            }
            async Task HoldY()
            {
                Receive(new() { Connected = 1, State = new() { Buttons = 0x8000 }, PressedButtons = 0x8000 });
                await Task.Delay(350);
                Receive(new() { Connected = 1, State = new() { Buttons = 0x8000 } });
                Receive(new() { Connected = 1, ReleasedButtons = 0x8000 });
                await Until(() => utilityPanel is { IsLoaded: true, ContainsFocus: true });
            }
            async Task Until(Func<bool> predicate)
            {
                var end = Environment.TickCount64 + 12000;
                while (!predicate() && Environment.TickCount64 < end) await Task.Delay(30);
                if (!predicate()) throw new TimeoutException("Introduction condition timed out after: " + string.Join("; ", checks) + "; focus=" + surface?.FocusDiagnostics());
            }
        };
    }
}
