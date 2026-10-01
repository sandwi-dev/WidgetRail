using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WinUiShell.Tests;

[TestClass]
public sealed class TextEntryEditingTests
{
    [TestMethod]
    [DataRow("😀")]
    [DataRow("e\u0301")]
    [DataRow("👩‍💻")]
    [DataRow("🇯🇵")]
    public void MovementAndDeletionPreserveWholeTextElements(string element)
    {
        var value = "a" + element + "b";
        var end = 1 + element.Length;
        Assert.AreEqual(1, TextEntryEditing.Move(value, end, 0, -1));
        Assert.AreEqual(end, TextEntryEditing.Move(value, 1, 0, 1));
        Assert.AreEqual(new TextEntryEditing.Edit("ab", 1), TextEntryEditing.Backspace(value, end, 0));
        Assert.AreEqual(new TextEntryEditing.Edit("aQb", 2), TextEntryEditing.Insert(value, 2, 1, "Q", 96));
    }

    [TestMethod]
    public void InteriorCaretAndSelectionCannotSplitSurrogates()
    {
        Assert.AreEqual(new TextEntryEditing.Edit("aQ😀b", 2), TextEntryEditing.Insert("a😀b", 2, 0, "Q", 96));
        Assert.AreEqual(new TextEntryEditing.Edit("ab", 1), TextEntryEditing.Backspace("a😀b", 2, 0));
        Assert.AreEqual(1, TextEntryEditing.Move("a😀b", 2, 0, -1));
        Assert.AreEqual(3, TextEntryEditing.Move("a😀b", 2, 0, 1));
        Assert.AreEqual(1, TextEntryEditing.Move("a😀b", 1, 2, -1));
        Assert.AreEqual(3, TextEntryEditing.Move("a😀b", 1, 2, 1));
    }

    [TestMethod]
    public void Utf16LimitIsPreservedWithoutTruncation()
    {
        Assert.IsNull(TextEntryEditing.Insert("abc", 3, 0, "😀", 4));
        Assert.AreEqual(new TextEntryEditing.Edit("ab😀", 4), TextEntryEditing.Insert("abc", 2, 1, "😀", 4));
        Assert.AreEqual(new TextEntryEditing.Edit("", 0), TextEntryEditing.Backspace("", 0, 0));
        Assert.AreEqual(0, TextEntryEditing.Move("", 0, 0, -1));
        Assert.AreEqual(0, TextEntryEditing.Move("", 0, 0, 1));
    }
}
