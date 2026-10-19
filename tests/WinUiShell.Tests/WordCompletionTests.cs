using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class WordCompletionTests
{
    [TestMethod]
    public void CompletionReplacesOnlyCurrentWordAndPreservesFrequencyAndCase()
    {
        var dictionary=new WordCompletion(["hello","help","helicopter","other"]);
        CollectionAssert.AreEqual(new[]{"Hello","Help"},dictionary.Complete("Hel",2,default).Items.Select(item=>item.Value).ToArray());
        var context=WordCompletionContext.Capture("before helter after",10,0)!;
        Assert.AreEqual("hel",context.Prefix);
        Assert.AreEqual(new TextEntryEditing.Edit("before hello after",12),context.Accept("hello",100));
        Assert.IsNull(context.Accept("other",100));Assert.IsNull(context.Accept("hello",8));
        Assert.IsNull(WordCompletionContext.Capture("hello",3,2));
        Assert.IsNull(WordCompletionContext.Capture("code_hel",8,0));
        Assert.IsNull(WordCompletionContext.Capture("12hel",5,0));
        Assert.IsNull(WordCompletionContext.Capture("can't",3,0));
    }

    [TestMethod]
    public void UtilityShortcutsRequireReleaseAndNeverFireForVisibilityChord()
    {
        foreach(var mask in new ushort[]{0x10,0x20})
        {
            var shortcut=new TextEntryUtilityShortcuts();
            Assert.IsNull(shortcut.Sample(mask,mask));Assert.IsNull(shortcut.Sample(mask,0));
            Assert.AreEqual(mask==0x10?ControllerButton.Menu:ControllerButton.View,shortcut.Sample(0,0));
            Assert.IsNull(shortcut.Sample(0,0));
            shortcut.Sample(mask,mask);shortcut.Sample(0x30,(ushort)(0x30^mask));shortcut.Sample(mask,0);
            Assert.IsNull(shortcut.Sample(0,0));
            shortcut.Sample(mask,mask);shortcut.Reset();Assert.IsNull(shortcut.Sample(0,0));
            Assert.IsNull(shortcut.Sample(mask,0));Assert.IsNull(shortcut.Sample(0,0));
        }
    }
}
