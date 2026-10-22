using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WinUiShell.Tests;

[TestClass]
public sealed class WindowsTaskViewTests
{
    [TestMethod]
    public void ShortcutSubmitsOneBalancedWindowsTabChord()
    {
        var calls = new List<WindowsTaskView.NativeInput[]>();
        var result = WindowsTaskView.Request(_ => false, events => { calls.Add(events); return (uint)events.Length; });
        Assert.AreEqual(WindowsTaskViewResult.Requested, result);
        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new ushort[] { 0x5B, 9, 9, 0x5B }, calls[0].Select(x => x.Data.Keyboard.VirtualKey).ToArray());
        CollectionAssert.AreEqual(new uint[] { 1, 0, 2, 3 }, calls[0].Select(x => x.Data.Keyboard.Flags).ToArray());
        Assert.IsTrue(calls[0].All(x => x.Type == 1));
        Assert.AreEqual(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<WindowsTaskView.NativeInput>());
    }

    [TestMethod]
    public void HeldKeysAreNotInjectedOrReleased()
    {
        foreach (var held in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 9 })
            Assert.AreEqual(WindowsTaskViewResult.KeyboardBusy, WindowsTaskView.Request(key => key == held,
                _ => throw new AssertFailedException("User-held keys must not be changed.")));
    }

    [TestMethod]
    public void PartialSubmissionReleasesOnlyOurUnbalancedKeys()
    {
        foreach (uint accepted in new uint[] { 0, 1, 2, 3 })
        {
            var calls = new List<WindowsTaskView.NativeInput[]>();
            var result = WindowsTaskView.Request(_ => false, events =>
            { calls.Add(events); return calls.Count == 1 ? accepted : (uint)events.Length; });
            Assert.AreEqual(WindowsTaskViewResult.InputRejected, result);
            Assert.HasCount(accepted == 0 ? 1 : 2, calls);
            if (accepted == 0) continue;
            Assert.IsTrue(calls[1].All(x => (x.Data.Keyboard.Flags & 2) != 0));
            CollectionAssert.AreEqual(accepted == 2 ? new ushort[] { 9, 0x5B } : [0x5B],
                calls[1].Select(x => x.Data.Keyboard.VirtualKey).ToArray());
        }
    }
}
