using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI;

internal static partial class Program
{
    static partial void IsValidationLaunch(IReadOnlyList<string> arguments, ref bool fixture)
        => fixture = arguments.Contains("--validate-trimmed-slider");
}

public sealed partial class MainWindow
{
    partial void ConfigureValidation(IReadOnlyList<string> arguments, ref bool handled)
    {
        if (!arguments.Contains("--validate-trimmed-slider")) return;
        handled = true;
        var result = Shell.FrontendArguments.Value(arguments, "--result")
            ?? throw new ArgumentException("Trimmed slider validation requires --result.");
        var panel = new StackPanel();
        RootFrame.Content = panel;
        panel.Loaded += async (_, _) =>
        {
            try
            {
                // A fractional range and nonzero initial volume reproduce the native
                // RangeBase callback used by Audio Mixer without touching a provider.
                for (var pass = 0; pass < 3; pass++)
                {
                    var slider = new WidgetSlider();
                    var changes = 0;
                    slider.ValueChanged += (_, _) => changes++;
                    slider.Maximum = 1;
                    slider.StepFrequency = 0.01;
                    slider.Value = 0.75;
                    panel.Children.Add(slider);
                    await Task.Delay(100);
                    slider.ApplyTemplate();
                    slider.Value = 0;
                    slider.Value = 1;
                    slider.Maximum = 0.5;
                    if (changes < 4 || slider.Value != 0.5)
                        throw new InvalidOperationException("Native range coercion or ValueChanged callback failed.");
                    panel.Children.Clear();
                }
                var button = new WidgetValueButton { Content = "Synthetic value" };
                button.SetAccessibleValue("42", false);
                panel.Children.Add(button);
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
                var provider = peer?.GetPattern(PatternInterface.Value) as IValueProvider;
                if (provider?.Value != "42") throw new InvalidOperationException("Native value-button accessibility projection failed.");
                var artwork = new WidgetArtworkView();
                panel.Children.Add(artwork);
                artwork.Measure(new(160, 90));
                artwork.Arrange(new(0, 0, 160, 90));
                if (FrameworkElementAutomationPeer.CreatePeerForElement(artwork)?.GetAutomationControlType() != AutomationControlType.Image)
                    throw new InvalidOperationException("Native artwork accessibility projection failed.");
                using var toggleAdapter = new WidgetNativeToggle();
                var nativeToggle = toggleAdapter.Control;
                var toggleActions = 0;
                toggleAdapter.ActivationRequested = () => toggleActions++;
                panel.Children.Add(nativeToggle);
                var toggleNode = new WidgetRail.WidgetProtocol.ViewNode { Id = "trim.toggle", Kind = WidgetRail.WidgetProtocol.ViewNodeKind.Button,
                    ActionId = "toggle", Text = "Example  On", IsSelected = true, StyleClasses = (string[])["wrail-switch"] };
                toggleAdapter.Publish(toggleNode);
                nativeToggle.ApplyTemplate();
                await Task.Delay(30);
                var toggleProvider = FrameworkElementAutomationPeer.CreatePeerForElement(nativeToggle).GetPattern(PatternInterface.Toggle) as IToggleProvider;
                if (toggleProvider?.ToggleState != ToggleState.On || toggleActions != 0)
                    throw new InvalidOperationException("Trimmed native ToggleSwitch publication or accessibility failed.");
                toggleProvider.Toggle();
                if (nativeToggle.IsOn || toggleActions != 1) throw new InvalidOperationException("Trimmed native ToggleSwitch activation failed.");
                toggleAdapter.Publish(toggleNode);
                if (!nativeToggle.IsOn || toggleActions != 1) throw new InvalidOperationException("Trimmed native ToggleSwitch update emitted another action.");
                File.WriteAllText(result, "PASS: three slider lifetimes; fractional range, value events, endpoints, range coercion and template application; value-button accessibility; artwork layout and accessibility; native ToggleSwitch template, Toggle provider and publication suppression.");
            }
            catch (Exception error) { File.WriteAllText(result, "FAIL: " + error); }
            finally { Close(); }
        };
    }
}
