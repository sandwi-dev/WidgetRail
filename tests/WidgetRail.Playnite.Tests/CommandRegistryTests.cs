using WidgetRail.Playnite;

namespace WidgetRail.Playnite.Tests;

[TestClass]
public sealed class CommandRegistryTests
{
    [TestMethod]
    public async Task ReplaySharesTheEffectAndDifferentPayloadIsRejected()
    {
        var registry = new CommandRegistry<int>();
        var work = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid().ToString("D"); var calls = 0; var signature = new string('a', 64);
        Task<int> Run() { calls++; return work.Task; }
        var first = registry.Execute(id, signature, Run);
        var replay = registry.Execute(id, signature, Run);
        Assert.AreSame(first, replay); Assert.AreEqual(1, calls);
        Assert.Throws<CommandConflictException>(() => registry.Execute(id, new string('b', 64), Run));
        work.SetResult(7); Assert.AreEqual(7, await first); Assert.AreEqual(7, await registry.Execute(id, signature, Run));
        Assert.AreEqual(1, calls);
    }
    [TestMethod]
    public async Task PendingEffectAdmissionIsBounded()
    {
        var registry = new CommandRegistry<int>();
        var work = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Enumerable.Range(0, 4).Select(_ => registry.Execute(Guid.NewGuid().ToString("D"), new string('a', 64), () => work.Task)).ToArray();
        Assert.Throws<CommandCapacityException>(() => registry.Execute(Guid.NewGuid().ToString("D"), new string('a', 64), () => work.Task));
        work.SetResult(1); await Task.WhenAll(running);
    }
}
