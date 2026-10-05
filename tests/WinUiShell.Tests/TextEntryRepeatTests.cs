using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class TextEntryRepeatTests
{
    [TestMethod]
    [DataRow(0x1000, ControllerButton.A)]
    [DataRow(0x4000, ControllerButton.X)]
    [DataRow(0x100, ControllerButton.LeftBumper)]
    [DataRow(0x200, ControllerButton.RightBumper)]
    public void FreshPressDelaysAndRepeatsWithoutBurst(int mask, ControllerButton button)
    {
        var repeat = new TextEntryRepeat();
        var key = new object();
        Assert.IsNull(repeat.Sample((ushort)mask, (ushort)mask, key, 0));
        Assert.IsNull(repeat.Sample((ushort)mask, 0, key, 399));
        Assert.AreEqual(button, repeat.Sample((ushort)mask, 0, key, 400));
        Assert.IsNull(repeat.Sample((ushort)mask, 0, key, 489));
        Assert.AreEqual(button, repeat.Sample((ushort)mask, 0, key, 490));
        Assert.AreEqual(button, repeat.Sample((ushort)mask, 0, key, 5000));
        Assert.IsNull(repeat.Sample((ushort)mask, 0, key, 5000));
        Assert.IsNull(repeat.Sample(0, 0, key, 5100));
        Assert.IsNull(repeat.Sample((ushort)mask, 0, key, 6000));
    }

    [TestMethod]
    public void ChangedKeyAndLayerNeverRetargetHeldCharacter()
    {
        var repeat = new TextEntryRepeat();
        repeat.Sample(0x1000, 0x1000, "q", 0);
        Assert.IsNull(repeat.Sample(0x1000, 0, "Q", 500));
        Assert.IsNull(repeat.Sample(0x1000, 0, "q", 1000));
        repeat.Sample(0x1000, 0x1000, "q", 1100);
        repeat.CancelCharacter();
        Assert.IsNull(repeat.Sample(0x1000, 0, "q", 2000));
    }

    [TestMethod]
    public void OneShotKeysConflictsAndLostOwnershipRequireFreshPress()
    {
        var repeat = new TextEntryRepeat();
        repeat.Sample(0x1000, 0x1000, null, 0); // focused Done/Clear/Shift is not a character
        Assert.IsNull(repeat.Sample(0x1000, 0, null, 1000));
        repeat.Sample(0x4000, 0x4000, null, 0);
        Assert.IsNull(repeat.Sample(0x4100, 0x100, null, 500));
        Assert.IsNull(repeat.Sample(0x4000, 0, null, 1000));
        repeat.Sample(0x4000, 0x4000, null, 1100);
        repeat.Reset(); // disconnect, hide, focus loss, or replacement dialog
        Assert.IsNull(repeat.Sample(0x4000, 0, null, 2000));
        Assert.IsNull(repeat.Sample(0x8000, 0x8000, null, 2100));
        Assert.IsNull(repeat.Sample(0x8000, 0, null, 3100));
    }
}
