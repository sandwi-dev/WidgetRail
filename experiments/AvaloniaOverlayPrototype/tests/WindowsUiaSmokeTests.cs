using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public sealed class WindowsUiaSmokeTests
{
    [STATestMethod]
    [Timeout(35_000)]
    public void Standard_controls_expose_stable_uia_focus_invoke_and_range_semantics()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "AvaloniaOverlayPrototype.exe");
        Assert.IsTrue(File.Exists(executable), $"Prototype executable was not copied beside the focused tests: {executable}");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "--exit-after-seconds 25",
            UseShellExecute = false,
        });
        Assert.IsNotNull(process);

        try
        {
            var window = WaitForElement(
                AutomationElement.RootElement,
                new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
                TimeSpan.FromSeconds(10));
            Assert.IsNotNull(window, "Avalonia top-level UIA element was not discoverable.");
            Assert.AreEqual(ControlType.Window, window.Current.ControlType);
            Assert.AreEqual("avp.window", window.Current.AutomationId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(window.Current.Name));

            var audioButton = WaitForAutomationId(window, "tray.audio", TimeSpan.FromSeconds(5));
            Assert.IsNotNull(audioButton);
            Assert.AreEqual(ControlType.Button, audioButton.Current.ControlType);
            Assert.IsFalse(string.IsNullOrWhiteSpace(audioButton.Current.Name));
            Assert.IsTrue(audioButton.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject));
            ((InvokePattern)invokeObject).Invoke();

            var slider = WaitForAutomationId(window, "audio.master.volume", TimeSpan.FromSeconds(8));
            Assert.IsNotNull(slider);
            Assert.AreEqual(ControlType.Slider, slider.Current.ControlType);
            Assert.IsFalse(string.IsNullOrWhiteSpace(slider.Current.Name));
            Assert.IsTrue(slider.TryGetCurrentPattern(RangeValuePattern.Pattern, out var rangeObject));
            var range = (RangeValuePattern)rangeObject;
            Assert.AreEqual(0, range.Current.Minimum);
            Assert.AreEqual(100, range.Current.Maximum);
            Assert.IsFalse(range.Current.IsReadOnly);

            audioButton.SetFocus();
            SpinWait.SpinUntil(() => audioButton.Current.HasKeyboardFocus, TimeSpan.FromSeconds(2));
            Assert.IsTrue(audioButton.Current.HasKeyboardFocus);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
    }

    private static AutomationElement? WaitForAutomationId(
        AutomationElement root,
        string automationId,
        TimeSpan timeout) =>
        WaitForElement(
            root,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId),
            timeout,
            TreeScope.Descendants);

    private static AutomationElement? WaitForElement(
        AutomationElement root,
        Condition condition,
        TimeSpan timeout,
        TreeScope scope = TreeScope.Children)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            var element = root.FindFirst(scope, condition);
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(50);
        }
        while (stopwatch.Elapsed < timeout);

        return null;
    }
}
