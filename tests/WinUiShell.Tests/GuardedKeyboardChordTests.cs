using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WinUiShell.Tests;

[TestClass]
public sealed class GuardedKeyboardChordTests
{
    private static ReadOnlySpan<ushort> ThreeKeyChord => [0x5B, 0x12, 0x47];

    [TestMethod]
    public void ThreeKeysSubmitOneBalancedChordWithoutSendingNativeInput()
    {
        var calls = new List<GuardedKeyboardChord.NativeInput[]>();
        var result = GuardedKeyboardChord.Request(ThreeKeyChord, _ => false,
            events => { calls.Add(events); return (uint)events.Length; });
        Assert.AreEqual(KeyboardChordResult.Requested, result);
        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new ushort[] { 0x5B, 0x12, 0x47, 0x47, 0x12, 0x5B },
            calls[0].Select(x => x.Data.Keyboard.VirtualKey).ToArray());
        CollectionAssert.AreEqual(new uint[] { 1, 0, 0, 2, 2, 3 },
            calls[0].Select(x => x.Data.Keyboard.Flags).ToArray());
        Assert.IsTrue(calls[0].All(x => x.Type == 1));
    }

    [TestMethod]
    public void HeldModifiersAndShortcutKeyPreventAnySubmission()
    {
        foreach (var held in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x47 })
            Assert.AreEqual(KeyboardChordResult.KeyboardBusy,
                GuardedKeyboardChord.Request(ThreeKeyChord, key => key == held,
                    _ => throw new AssertFailedException("Held user keys must not be injected or released.")));
    }

    [TestMethod]
    public void EveryPartialPrefixReleasesOnlyOutstandingInjectedKeysWithoutRetrying()
    {
        ushort[][] releases = [[], [0x5B], [0x12, 0x5B], [0x47, 0x12, 0x5B], [0x12, 0x5B], [0x5B]];
        for (uint accepted = 0; accepted < 6; accepted++)
        {
            var calls = new List<GuardedKeyboardChord.NativeInput[]>();
            var result = GuardedKeyboardChord.Request(ThreeKeyChord, _ => false,
                events => { calls.Add(events); return calls.Count == 1 ? accepted : 0; });
            Assert.AreEqual(KeyboardChordResult.InputRejected, result);
            Assert.HasCount(accepted == 0 ? 1 : 2, calls);
            if (accepted == 0) continue;
            CollectionAssert.AreEqual(releases[accepted], calls[1].Select(x => x.Data.Keyboard.VirtualKey).ToArray());
            Assert.IsTrue(calls[1].All(x => (x.Data.Keyboard.Flags & 2) != 0));
        }
    }
}
