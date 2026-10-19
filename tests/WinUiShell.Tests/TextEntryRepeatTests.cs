using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class TextEntryRepeatTests
{
    [TestMethod]
    [DataRow(0x10, ControllerButton.Menu)]
    [DataRow(0x20, ControllerButton.View)]
    public void UtilityShortcutsRequireFreshUnconflictedRelease(int mask, ControllerButton expected)
    {
        var shortcut = new TextEntryUtilityShortcuts();
        Assert.IsNull(shortcut.Sample((ushort)mask, 0));
        Assert.IsNull(shortcut.Sample(0, 0));
        Assert.IsNull(shortcut.Sample((ushort)mask, (ushort)mask));
        Assert.IsNull(shortcut.Sample((ushort)mask, 0));
        Assert.AreEqual(expected, shortcut.Sample(0, 0));
        Assert.IsNull(shortcut.Sample(0, 0));
        shortcut.Sample((ushort)mask, (ushort)mask);
        Assert.IsNull(shortcut.Sample(0x30, 0x30));
        Assert.IsNull(shortcut.Sample((ushort)mask, 0));
        Assert.IsNull(shortcut.Sample(0, 0));
        shortcut.Sample((ushort)mask, (ushort)mask);
        Assert.IsNull(shortcut.Sample(0x4000, 0x4000));
        Assert.IsNull(shortcut.Sample(0, 0));
        shortcut.Sample((ushort)mask, (ushort)mask);
        shortcut.Reset();
        Assert.IsNull(shortcut.Sample(0, 0));
    }

    [TestMethod]
    [DataRow(0x4000)]
    [DataRow(0x100)]
    [DataRow(0x200)]
    public void SustainedEditingChangesToWordsWithoutBurst(int mask)
    {
        var repeat=new TextEntryRepeat();
        repeat.Sample((ushort)mask,(ushort)mask,null,0);
        foreach(var time in new[]{400,490,580,670})
        { Assert.IsNotNull(repeat.Sample((ushort)mask,0,null,time));Assert.IsFalse(repeat.ByWord); }
        Assert.IsNotNull(repeat.Sample((ushort)mask,0,null,1000));Assert.IsTrue(repeat.ByWord);
        Assert.IsNull(repeat.Sample((ushort)mask,0,null,1249));
        Assert.IsNotNull(repeat.Sample((ushort)mask,0,null,1250));
        repeat.Sample(0,0,null,1260);Assert.IsFalse(repeat.ByWord);
        repeat.Sample((ushort)mask,(ushort)mask,null,1300);
        repeat.Sample((ushort)mask,0,null,1700);Assert.IsFalse(repeat.ByWord);
    }

    [TestMethod]
    public void PasswordsCharactersAndDelayedFirstSamplesDoNotAccelerate()
    {
        foreach(var mask in new ushort[]{0x4000,0x100,0x200,0x1000})
        {
            var repeat=new TextEntryRepeat();repeat.Sample(mask,mask,"q",0);
            for(var time=400;time<4000;time+=90)repeat.Sample(mask,0,"q",time,allowWords:false);
            Assert.IsFalse(repeat.ByWord);
        }
        var delayed=new TextEntryRepeat();delayed.Sample(0x4000,0x4000,null,0);
        delayed.Sample(0x4000,0,null,5000);Assert.IsFalse(delayed.ByWord);
        var character=new TextEntryRepeat();character.Sample(0x1000,0x1000,"q",0);
        for(var time=400;time<4000;time+=90)character.Sample(0x1000,0,"q",time);
        Assert.IsFalse(character.ByWord);
    }

    [TestMethod]
    public void FreshDirectionChangeRestartsCharacterDelay()
    {
        var repeat=new TextEntryRepeat();repeat.Sample(0x100,0x100,null,0);
        foreach(var time in new[]{400,490,580,670,1000})repeat.Sample(0x100,0,null,time);
        Assert.IsTrue(repeat.ByWord);
        Assert.IsNull(repeat.Sample(0x200,0x200,null,1100));Assert.IsFalse(repeat.ByWord);
        Assert.IsNull(repeat.Sample(0x200,0,null,1499));
        Assert.AreEqual(ControllerButton.RightBumper,repeat.Sample(0x200,0,null,1500));Assert.IsFalse(repeat.ByWord);
    }

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
        repeat.Reset();
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
