using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

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
                await Until(() => introductionCard is { Focused: true });
                Check(introduction.Step == "welcome", "First-run welcome is reachable");
                await Press(ControllerButton.A);
                await Until(() => introduction.Step == "guide" && !interactive);
                Check(introductionCard!.Focused, "Show me around keeps focus in the tutorial after the button release");
                await Press(ControllerButton.A);
                await Until(() => introduction.Step == "commands");
                Check(introductionCard.Focused, "Next advances the tour and retains tutorial focus");
                await Press(ControllerButton.B);
                await Until(() => !introductionCard.Focused && FocusedTrayWidget() is not null);
                Check(introduction.Step == "commands", "B explicitly returns to the rail without advancing the tour");
                await Press(ControllerButton.A);
                await Until(() => interactive && !switching && introduction.Step == "commands");
                Check(introductionCard?.Visibility == Visibility.Collapsed, "Entering a widget suspends the tip without consuming its opening action");
                FocusDirectionalTray();
                await Until(() => FocusedTrayWidget() is not null);
                await Press(ControllerButton.Menu);
                await Until(() => trayMenu is not null && introduction.Step == "utilities");
                Check(introductionCard?.Visibility == Visibility.Collapsed, "Commands remain usable and explanations hide while the menu is open");
                await Press(ControllerButton.B);
                await Until(() => trayMenu is null && FocusedTrayWidget() is not null);
                Receive(new() { Connected = 1, State = new() { Buttons = 0x8000 }, PressedButtons = 0x8000 });
                await Task.Delay(350);
                Receive(new() { Connected = 1, State = new() { Buttons = 0x8000 } });
                Receive(new() { Connected = 1, ReleasedButtons = 0x8000 });
                await Until(() => UtilityListFocused && introduction.Step == "catalogue");
                Check(shownUtility == ShellUtility.Tasks, "Hold Y enters Task Switcher when there are no notifications");
                await Press(ControllerButton.B);
                Check(UtilityIconFocused, "B retains the normal utility-list back behavior");
                CloseUtilityPanel(); FocusDirectionalTray();
                ContinueIntroduction();
                await IntroductionActionAsync("open");
                await Until(() => activeWidget == "marketplace" && introduction.Step == "appearance" && !switching);
                Check(interactive, "Catalogue opens as an ordinary interactive widget");
                FocusDirectionalTray(); ContinueIntroduction();
                await IntroductionActionAsync("open");
                await Until(() => IntroductionAppearanceVisible && introduction.Step == "appearance-details" && !switching);
                Check(FindIntroductionTarget(surface!, "appearance.theme") is not null, "Appearance intent reaches the real theme control");
                Check(FindIntroductionTarget(surface!, "interface.stepper") is not null, "Appearance exposes interface size");
                await Until(() => introductionCard?.Visibility == Visibility.Visible);
                introductionCard!.FocusPrimary();
                await Until(() => introductionCard.Focused);
                await Press(ControllerButton.A);
                await Until(() => introduction.Finished);
                await introductionWrite;
                Check((await introductionStore!.LoadAsync()).Finished, "Completed tutorial is saved");
                ReplayIntroduction();
                await Press(ControllerButton.B);
                await Until(() => introduction.Paused && !introductionCard.Focused);
                Check(introductionCard.Visibility == Visibility.Collapsed, "B can leave welcome without accepting a tutorial");
                ContinueIntroduction(); await IntroductionActionAsync("skip");
                await introductionWrite;
                Check((await introductionStore.LoadAsync()).Finished, "Explore on my own persists the skipped tutorial");
                SetVisible(false); SetVisible(true);
                await Task.Delay(200);
                Check(introductionCard.Visibility == Visibility.Collapsed, "Reopening does not restart a completed introduction");
                ReplayIntroduction();
                Check(introduction.Step == "welcome" && introductionCard.Visibility == Visibility.Visible, "Replay explicitly restarts the introduction");
                foreach (var family in new[] { ControllerFamily.Xbox, ControllerFamily.PlayStation })
                {
                    WidgetControllerPrompts.Set(family);
                    Check(introductionCard.Visibility == Visibility.Visible, family + " controller glyph refresh retains the current tutorial");
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
            }
            catch (Exception error) { failure = error; }
            var result = new { passed = failure is null, checks, error = failure?.ToString() };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
            async Task Press(ControllerButton button)
            {
                await RouteButtonAsync(button, ControllerEventPhase.Pressed);
                await RouteButtonAsync(button, ControllerEventPhase.Released);
                await Task.Delay(80);
            }
            async Task Until(Func<bool> predicate)
            {
                var end = Environment.TickCount64 + 12000;
                while (!predicate() && Environment.TickCount64 < end) await Task.Delay(30);
                if (!predicate()) throw new TimeoutException("Introduction native condition timed out after: " + string.Join("; ", checks));
            }
        };
    }
}
