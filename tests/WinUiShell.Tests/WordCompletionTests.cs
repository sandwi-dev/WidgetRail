using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class WordCompletionTests
{
    [TestMethod]
    [DataRow("https://example.com/hel|?q=two#section", "https://example.com/hello?q=two#section")]
    [DataRow("https://hel|.example.com/after", "https://hello.example.com/after")]
    [DataRow("first.hel|+tag@example.com", "first.hello+tag@example.com")]
    [DataRow("first_hel|@example.com", "first_hello@example.com")]
    [DataRow("first@hel|.com", "first@hello.com")]
    [DataRow("find hel| near me", "find hello near me")]
    public void AddressCompletionPreservesEverySurroundingCharacter(string marked, string expected)
    {
        var caret = marked.IndexOf('|');
        var text = marked.Replace("|", "");
        var context = WordCompletionContext.Capture(text, caret, 0, address: true)!;
        Assert.AreEqual("hel", context.Prefix);
        var edit = context.Accept("hello", 2048)!.Value;
        Assert.AreEqual(expected, edit.Value);
        Assert.AreEqual(context.Start + 5, edit.Caret);
        Assert.IsNull(WordCompletionContext.Capture(text, caret, 1, address: true));
    }

    [TestMethod]
    public void FullBundledVocabularyRetainsFrequencyOrderAndHasBoundedCancellableLookups()
    {
        using var stream = typeof(WordCompletionTests).Assembly.GetManifestResourceStream("WidgetRail.EnglishWords")!;
        using var reader = new StreamReader(stream);
        var words = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.IsGreaterThan(80000, words.Length);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var dictionary = new WordCompletion(words);
        var constructionBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var constructionMs = timer.Elapsed.TotalMilliseconds;
        var uncommon = words.Skip(10000).First(word => word.Length >= 8 &&
            !words.Take(10000).Any(common => common.StartsWith(word[..^1], StringComparison.Ordinal)));
        Assert.IsTrue(dictionary.Complete(uncommon[..^1], 5, default).Items.Any(item => item.Value == uncommon));
        CollectionAssert.AreEqual(words.Where(word => word.Length > 2 && word.StartsWith("sa", StringComparison.Ordinal)).Take(5).ToArray(),
            dictionary.Complete("sa", 5, default).Items.Select(item => item.Value).ToArray());
        Assert.AreEqual(0, dictionary.Complete("sa", 0, default).Items.Count);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => dictionary.Complete("sa", 5, cancellation.Token));
        var prefixes = new[] { "sa", "he", "re", "un", "constitution", "characteristically", "zzzzzz" };
        timer.Restart();
        for (var i = 0; i < 10000; i++)
            Assert.IsLessThanOrEqualTo(5, dictionary.Complete(prefixes[i % prefixes.Length], 5, default).Items.Count);
        Console.WriteLine($"PERF {words.Length} words: index construction {constructionMs:F2} ms, {constructionBytes} allocated bytes; 10,000 lookups {timer.Elapsed.TotalMilliseconds:F2} ms.");
    }

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
