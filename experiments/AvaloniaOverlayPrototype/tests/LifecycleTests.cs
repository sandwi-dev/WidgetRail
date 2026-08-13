using GameBarAlternative.AvaloniaPrototype.Lifecycle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public sealed class LifecycleTests
{
    [TestMethod]
    public void Hidden_lifecycle_cancels_visible_work_and_rejects_new_rendering()
    {
        var lifecycle = new PrototypeLifecycle();
        Assert.AreEqual(PrototypeVisibility.Hidden, lifecycle.Visibility);
        Assert.IsTrue(lifecycle.VisibleLifetime.IsCancellationRequested);

        lifecycle.Show();
        var visibleToken = lifecycle.VisibleLifetime;
        Assert.IsFalse(visibleToken.IsCancellationRequested);

        lifecycle.Hide();
        Assert.IsTrue(visibleToken.IsCancellationRequested);
    }
}
