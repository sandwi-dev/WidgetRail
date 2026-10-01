using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayPlatformClient;

namespace OverlayPlatformClient.Tests;

[TestClass]
public sealed class ControllerIsolationRecoveryTests
{
    [TestMethod]
    public void AbiMismatchNeverInvokesRecovery()
    {
        var native = new FakeRecovery { Version = PlatformAbi.Version + 1 };
        var result = ControllerIsolationRecovery.Recover(native);
        Assert.AreEqual(PlatformStatus.InvalidVersion, result.Status);
        Assert.AreEqual(1, native.VersionCalls);
        Assert.AreEqual(0, native.RecoveryCalls);
    }

    [TestMethod]
    [DataRow(PlatformStatus.Ok, "No local controller isolation recovery is pending.")]
    [DataRow(PlatformStatus.ControllerIsolationUnavailable, "Controller isolation owner busy error=170")]
    public void NativeStatusAndOwnershipDiagnosticArePreserved(PlatformStatus status, string message)
    {
        var native = new FakeRecovery { Status = status, Message = message };
        var result = ControllerIsolationRecovery.Recover(native);
        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(message, result.Message);
        Assert.AreEqual(status == PlatformStatus.Ok, result.Succeeded);
        Assert.AreEqual(1, native.RecoveryCalls);
        Assert.AreEqual(512, native.Capacity);
        Assert.IsTrue(native.BufferWasClear);
    }

    [TestMethod]
    public void DiagnosticBufferIsBoundedEvenWhenNativeOmitsTermination()
    {
        var result = ControllerIsolationRecovery.Recover(new FakeRecovery { Message = new string('x', 600) });
        Assert.AreEqual(new string('x', 512), result.Message);
        result = ControllerIsolationRecovery.Recover(new FakeRecovery { Status = PlatformStatus.ControllerIsolationUnavailable });
        StringAssert.Contains(result.Message, "could not complete");
    }

    [TestMethod]
    public void RecoveryDeclarationMatchesTheRetainedNativeAbiWithoutCallingIt()
    {
        var method = typeof(OverlayPlatformNative).GetMethod("NativeRecoverControllerIsolation", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.AreEqual("WidgetRailOverlayPlatformRecoverControllerIsolation", method.GetCustomAttribute<LibraryImportAttribute>()!.EntryPoint);
        Assert.AreEqual("OverlayPlatformInterop.dll", method.GetCustomAttribute<LibraryImportAttribute>()!.LibraryName);
        Assert.AreEqual(typeof(PlatformStatus), method.ReturnType);
        Assert.IsTrue(method.GetParameters()[0].ParameterType.IsPointer);
        Assert.AreEqual(typeof(char), method.GetParameters()[0].ParameterType.GetElementType());
        Assert.AreEqual(typeof(uint), method.GetParameters()[1].ParameterType);
        CollectionAssert.Contains(method.GetCustomAttribute<UnmanagedCallConvAttribute>()!.CallConvs!, typeof(CallConvStdcall));
    }

    [TestMethod]
    public void OrdinaryStartupDoesNotRecoverOrPresent()
    {
        foreach (var arguments in new string[][] { [], ["--hidden"], ["--settings-root=C:\\profile"] })
        {
            Assert.IsFalse(ControllerRecoveryCommand.TryRun(arguments,
                () => throw new AssertFailedException("Normal startup invoked recovery."),
                _ => throw new AssertFailedException("Normal startup displayed recovery."), out var exitCode));
            Assert.AreEqual(0, exitCode);
        }
    }

    [TestMethod]
    [DataRow(PlatformStatus.Ok, 0)]
    [DataRow(PlatformStatus.ControllerIsolationUnavailable, 1)]
    public void StandaloneFlagDispatchesExactlyOnceAndPresentsItsResult(PlatformStatus status, int expectedExit)
    {
        var calls = 0;
        var displayed = new List<ControllerIsolationRecoveryResult>();
        var result = new ControllerIsolationRecoveryResult(status, "native outcome");
        Assert.IsTrue(ControllerRecoveryCommand.TryRun([ControllerRecoveryCommand.Flag.ToUpperInvariant()],
            () => { calls++; return result; }, displayed.Add, out var exitCode));
        Assert.AreEqual(1, calls);
        Assert.AreSame(result, displayed.Single());
        Assert.AreEqual(expectedExit, exitCode);
    }

    [TestMethod]
    public void MixedDuplicateOrValuedFlagsRejectWithoutRecovery()
    {
        foreach (var arguments in new[]
        {
            new[] { ControllerRecoveryCommand.Flag, "--hidden" },
            ["--settings-root=C:\\profile", ControllerRecoveryCommand.Flag],
            [ControllerRecoveryCommand.Flag, ControllerRecoveryCommand.Flag],
            [ControllerRecoveryCommand.Flag + "=true"],
            [ControllerRecoveryCommand.Flag, "--development-probe-only"],
        })
        {
            var calls = 0;
            var displayed = new List<ControllerIsolationRecoveryResult>();
            Assert.IsTrue(ControllerRecoveryCommand.TryRun(arguments,
                () => { calls++; return new(PlatformStatus.Ok, "must not run"); }, displayed.Add, out var exitCode));
            Assert.AreEqual(0, calls);
            Assert.AreEqual(1, exitCode);
            Assert.AreEqual(PlatformStatus.InvalidArgument, displayed.Single().Status);
            StringAssert.Contains(displayed[0].Message, "must be used alone");
        }
    }

    [TestMethod]
    public void MirroredActivationVectorsCountOnceButDuplicateTokensRemainInvalid()
    {
        var calls = 0;
        var displayed = new List<ControllerIsolationRecoveryResult>();
        ControllerIsolationRecoveryResult Recover() { calls++; return new(PlatformStatus.Ok, "fake recovered"); }
        Assert.IsTrue(ControllerRecoveryCommand.TryRun([ControllerRecoveryCommand.Flag], [ControllerRecoveryCommand.Flag],
            Recover, displayed.Add, out var exitCode));
        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(1, calls);
        displayed.Clear();
        foreach (var (command, activation) in new (string[], string[])[]
        {
            ([ControllerRecoveryCommand.Flag, ControllerRecoveryCommand.Flag], []),
            ([], [ControllerRecoveryCommand.Flag, ControllerRecoveryCommand.Flag]),
            ([ControllerRecoveryCommand.Flag, ControllerRecoveryCommand.Flag], [ControllerRecoveryCommand.Flag, ControllerRecoveryCommand.Flag]),
            (["--hidden"], [ControllerRecoveryCommand.Flag]),
        })
        {
            Assert.IsTrue(ControllerRecoveryCommand.TryRun(command, activation, Recover, displayed.Add, out exitCode));
            Assert.AreEqual(1, exitCode);
            Assert.AreEqual(1, calls, "An invalid raw argument channel must never invoke recovery.");
        }
    }

    [TestMethod]
    public void MissingRecoveryLibraryIsReportedAsFailureWithoutNormalStartup()
    {
        var displayed = new List<ControllerIsolationRecoveryResult>();
        Assert.IsTrue(ControllerRecoveryCommand.TryRun([ControllerRecoveryCommand.Flag],
            () => throw new DllNotFoundException(new string('x', 700)), displayed.Add, out var exitCode));
        Assert.AreEqual(1, exitCode);
        Assert.IsFalse(displayed.Single().Succeeded);
        Assert.IsLessThan(600, displayed[0].Message.Length);
    }

    private sealed class FakeRecovery : IControllerIsolationRecoveryNative
    {
        internal uint Version = PlatformAbi.Version;
        internal PlatformStatus Status;
        internal string Message = "";
        internal int VersionCalls, RecoveryCalls, Capacity;
        internal bool BufferWasClear;
        public uint GetAbiVersion() { VersionCalls++; return Version; }
        public PlatformStatus RecoverControllerIsolation(Span<char> message)
        {
            RecoveryCalls++; Capacity = message.Length; BufferWasClear = message.IndexOfAnyExcept('\0') < 0;
            Message.AsSpan(0, Math.Min(Message.Length, message.Length)).CopyTo(message);
            return Status;
        }
    }
}
