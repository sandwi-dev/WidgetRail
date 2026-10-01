using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayPlatformClient;

namespace OverlayPlatformClient.Tests;

[TestClass]
public sealed class ProcessLeaseTests
{
    [TestMethod]
    public void OwnerIsBoundAndReleasedOnceWhileClientsNeverConstructALease()
    {
        var native = new Fake();
        var election = OverlayProcessLease.Elect("test", true, native);
        Assert.AreEqual(ProcessElectionResult.Owner, election.Result);
        election.Lease!.BindWindow(42, 0x8001); election.Lease.Dispose(); election.Lease.Dispose();
        Assert.AreEqual(1, native.Ends); Assert.AreEqual(1, native.Binds);
        foreach (var result in new[] { ProcessElectionResult.Redirected, ProcessElectionResult.AlreadyRunning })
        { native.Result = result; Assert.IsNull(OverlayProcessLease.Elect("test", false, native).Lease); }
        Assert.AreEqual(1, native.Ends);
    }
    [TestMethod]
    public void WrongThreadCannotReleaseNativeMutexAndOwnerCanStillDispose()
    {
        var native = new Fake(); var lease = OverlayProcessLease.Elect("test", true, native).Lease!;
        Exception? failure = null;
        var worker = new Thread(() => { try { lease.Dispose(); } catch (Exception error) { failure = error; } });
        worker.Start(); worker.Join();
        Assert.IsInstanceOfType<InvalidOperationException>(failure);
        Assert.AreEqual(0, native.Ends);
        lease.Dispose(); Assert.AreEqual(1, native.Ends);
    }
    [TestMethod]
    public void ElectionFailureAndInvalidProfileCannotBecomeOwners()
    {
        var native = new Fake { Result = ProcessElectionResult.Failed };
        Assert.Throws<InvalidOperationException>(() => OverlayProcessLease.Elect("test", true, native));
        Assert.Throws<ArgumentException>(() => OverlayProcessLease.Elect("invalid/profile", true, native));
        Assert.AreEqual(1, native.Begins);
    }
    private sealed class Fake : IOverlayProcessNative
    {
        public ProcessElectionResult Result = ProcessElectionResult.Owner;
        public int Ends, Binds, Begins;
        public ProcessElectionResult Begin(string profile, bool showExisting, out nint owner, out string error)
        { ++Begins; owner = Result == ProcessElectionResult.Owner ? 17 : 0; error = "fixture failure"; return Result; }
        public ProcessLeaseStatus BindWindow(nint owner, nuint window, uint message) { ++Binds; return ProcessLeaseStatus.Ok; }
        public ProcessLeaseStatus End(nint owner) { ++Ends; return ProcessLeaseStatus.Ok; }
    }
}
