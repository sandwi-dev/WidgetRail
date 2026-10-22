using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class IntroductionProgressTests
{
    [TestMethod]
    public async Task MissingFileStartsWelcomeAndAllStepsRoundTrip()
    {
        await InProfile(async store =>
        {
            Assert.AreEqual(IntroductionProgress.New, await store.LoadAsync());
            foreach (var step in IntroductionProgress.Steps)
                foreach (var paused in new[] { false, true })
                {
                    var state = new IntroductionProgress(1, step, paused);
                    Assert.IsTrue(await store.SaveAsync(state));
                    Assert.AreEqual(state, await store.LoadAsync());
                }
            Assert.AreEqual("complete", new IntroductionProgress(1, "complete").Next().Step);
        });
    }

    [TestMethod]
    [DataRow("")][DataRow("null")][DataRow("[]")][DataRow("{}")][DataRow("{")]
    [DataRow("{\"version\":1,\"step\":null}")]
    [DataRow("{\"version\":1,\"step\":\"unknown\"}")]
    [DataRow("{\"version\":999,\"step\":\"welcome\"}")]
    [DataRow("{\"version\":-1,\"step\":\"welcome\"}")]
    [DataRow("{\"version\":\"1\",\"step\":\"welcome\"}")]
    [DataRow("{\"version\":1,\"step\":{},\"paused\":false}")]
    [DataRow("{\"version\":1,\"step\":\"guide\",\"paused\":\"yes\"}")]
    public async Task InvalidProgressPausesSafelyWithoutOverwritingTheFile(string content)
    {
        await InProfile(async store =>
        {
            await File.WriteAllTextAsync(store.Path, content);
            Assert.AreEqual(IntroductionProgress.SafeFallback, await store.LoadAsync());
            Assert.AreEqual(content, await File.ReadAllTextAsync(store.Path));
        });
    }

    [TestMethod]
    public async Task SizeDepthLockedAndDirectoryCasesRemainNonFatal()
    {
        await InProfile(async store =>
        {
            foreach (var value in new[] { new string(' ', IntroductionProgressStore.MaximumBytes + 1),
                new string('[', 40) + "0" + new string(']', 40) })
            {
                await File.WriteAllTextAsync(store.Path, value);
                Assert.AreEqual(IntroductionProgress.SafeFallback, await store.LoadAsync());
            }
            using (File.Open(store.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.AreEqual(IntroductionProgress.SafeFallback, await store.LoadAsync());
                Assert.IsFalse(await store.SaveAsync(IntroductionProgress.New));
            }
            File.Delete(store.Path); Directory.CreateDirectory(store.Path);
            Assert.AreEqual(IntroductionProgress.SafeFallback, await store.LoadAsync());
            Assert.IsFalse(await store.SaveAsync(IntroductionProgress.New));
            Directory.Delete(store.Path);
        });
    }

    [TestMethod]
    public async Task HumanEditsAndRapidWritesRemainValid()
    {
        await InProfile(async store =>
        {
            await File.WriteAllTextAsync(store.Path, "{ /* test a later step */ \"version\":1,\"step\":\"appearance\",\"paused\":false, }");
            Assert.AreEqual(new IntroductionProgress(1, "appearance"), await store.LoadAsync());
            var saves = IntroductionProgress.Steps.Select(step => store.SaveAsync(new(1, step))).ToArray();
            await Task.WhenAll(saves);
            Assert.AreEqual("complete", (await store.LoadAsync()).Step);
            Assert.IsFalse(await store.SaveAsync(new(1, "nonsense")));
            Assert.AreEqual("complete", (await store.LoadAsync()).Step);
            Assert.AreEqual(1, Directory.GetFiles(System.IO.Path.GetDirectoryName(store.Path)!).Length);
        });
    }

    private static async Task InProfile(Func<IntroductionProgressStore, Task> test)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "widgetrail-introduction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { await test(new(root)); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
