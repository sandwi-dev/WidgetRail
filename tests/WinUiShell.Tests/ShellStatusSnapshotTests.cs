using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ShellStatusSnapshotTests
{
    [TestMethod]
    public void LocalOrCaptiveConnectivityDoesNotClaimInternetAccess()
    {
        Assert.AreEqual(ShellInternetStatus.Offline, ShellStatusSnapshot.InternetLevel(0));
        Assert.AreEqual(ShellInternetStatus.Limited, ShellStatusSnapshot.InternetLevel(1));
        Assert.AreEqual(ShellInternetStatus.Limited, ShellStatusSnapshot.InternetLevel(2));
        Assert.AreEqual(ShellInternetStatus.Online, ShellStatusSnapshot.InternetLevel(3));
        Assert.AreEqual(ShellInternetStatus.Unknown, ShellStatusSnapshot.InternetLevel(null));
        Assert.AreEqual(ShellInternetStatus.Unknown, ShellStatusSnapshot.InternetLevel(99));
    }

    [TestMethod]
    public void MultipleBluetoothRadiosPreferAnyEnabledAdapter()
    {
        Assert.AreEqual(ShellBluetoothStatus.On, ShellStatusSnapshot.BluetoothRadios([0, 2, 1]));
        Assert.AreEqual(ShellBluetoothStatus.On, ShellStatusSnapshot.BluetoothRadios([1, 2, 0]));
        Assert.AreEqual(ShellBluetoothStatus.Off, ShellStatusSnapshot.BluetoothRadios([2, 3]));
    }

    [TestMethod]
    public void UnavailableAndUnknownAreNotReportedAsOff()
    {
        Assert.AreEqual(ShellBluetoothStatus.Unavailable, ShellStatusSnapshot.BluetoothRadios([]));
        Assert.AreEqual(ShellBluetoothStatus.Unknown, ShellStatusSnapshot.BluetoothRadios([2, 0]));
        Assert.AreEqual(ShellBluetoothStatus.Unknown, ShellStatusSnapshot.BluetoothRadios([3, 99]));
        Assert.AreNotEqual(new ShellStatusSnapshot(ShellInternetStatus.Offline, ShellBluetoothStatus.Off).Describe(DateTime.Today, CultureInfo.InvariantCulture),
            ShellStatusSnapshot.Unknown.Describe(DateTime.Today, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void AccessibleDescriptionIncludesClockAndBothStatusMeanings()
    {
        var now = new DateTime(2026, 9, 28, 17, 42, 53);
        var state = new ShellStatusSnapshot(ShellInternetStatus.Limited, ShellBluetoothStatus.On);
        var culture = CultureInfo.GetCultureInfo("en-US");
        var text = state.Describe(now, culture);
        StringAssert.Contains(text, now.ToString("t", culture));
        StringAssert.Contains(text, now.ToString("d", culture));
        StringAssert.Contains(text, "Limited network access or sign-in required");
        StringAssert.Contains(text, "Bluetooth on");
    }
}
