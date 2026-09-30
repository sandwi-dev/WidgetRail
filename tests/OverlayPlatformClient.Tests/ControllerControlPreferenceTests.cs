using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;

namespace OverlayPlatformClient.Tests;

[TestClass]
public sealed class ControllerControlPreferenceTests
{
    [TestMethod]
    public void InitialOffChecksRecoveryAndShortcutChangesDoNotReapply()
    {
        var helper = new ControllerControlPreference();
        var calls = new List<bool>();
        bool Apply(bool enabled) { calls.Add(enabled); return true; }
        var off = new ControllerSettings();
        Assert.AreEqual(true, helper.Apply(off, Apply));
        Assert.IsNull(helper.Apply(off with { OpenShortcut = ControllerOpenShortcut.Guide }, Apply));
        CollectionAssert.AreEqual(new[] { false }, calls);
        var on = off with { ExclusiveControl = true, Revision = 1 };
        Assert.AreEqual(true, helper.Apply(on, Apply));
        Assert.IsNull(helper.Apply(on, Apply));
        Assert.IsNull(helper.Apply(on with { OpenShortcut = ControllerOpenShortcut.Guide }, Apply));
        CollectionAssert.AreEqual(new[] { false, true }, calls);
    }

    [TestMethod]
    public void FailedRequestIsNotRetriedUntilAnExplicitRevision()
    {
        var helper = new ControllerControlPreference();
        var calls = 0;
        bool Apply(bool enabled) { calls++; return false; }
        var requested = new ControllerSettings { ExclusiveControl = true, Revision = 8 };
        Assert.AreEqual(false, helper.Apply(requested, Apply));
        Assert.IsNull(helper.Apply(requested, Apply));
        Assert.AreEqual(1, calls);
        Assert.AreEqual(false, helper.Apply(requested with { Revision = 9 }, Apply));
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public void RecoveryAndResetToEarlierRevisionStillApplyOff()
    {
        var helper = new ControllerControlPreference();
        var calls = new List<bool>();
        bool Apply(bool enabled) { calls.Add(enabled); return true; }
        Assert.AreEqual(true, helper.Apply(new() { Revision = 4 }, Apply));
        Assert.AreEqual(true, helper.Apply(new(), Apply));
        Assert.IsNull(helper.Apply(new(), Apply));
        CollectionAssert.AreEqual(new[] { false, false }, calls);
    }

    [TestMethod]
    public void UnexpectedFailureIsNotReplayedOnNextExchange()
    {
        var helper = new ControllerControlPreference();
        var calls = 0;
        bool Apply(bool enabled) { calls++; throw new InvalidOperationException("native failure"); }
        var requested = new ControllerSettings { ExclusiveControl = true, Revision = 1 };
        Assert.Throws<InvalidOperationException>(() => helper.Apply(requested, Apply));
        Assert.IsNull(helper.Apply(requested, Apply));
        Assert.AreEqual(1, calls);
    }
}
