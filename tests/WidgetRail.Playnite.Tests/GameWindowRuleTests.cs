using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Playnite;

namespace WidgetRail.Tests.Playnite;

[TestClass]
public sealed class GameWindowRuleTests
{
    [TestMethod]
    public void ConfirmedWindowCanRecoverEarlierCapturesWithinThisRunAndPreservesOlderIntervals()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var sessions = new GameCaptureSessions(directory.FullName);
            var game = Guid.NewGuid();
            var start = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds();
            var created = (ulong)DateTimeOffset.FromUnixTimeMilliseconds(start - 100).UtcDateTime.ToFileTimeUtc();
            Assert.IsTrue(sessions.BindWindow(game, 42, created, start, start + 3000));
            Assert.AreEqual(start, sessions.Snapshot(game, start + 5000).Single().From);
            Assert.IsFalse(sessions.BindWindow(Guid.NewGuid(), 42, created, start, start + 3500));
            sessions.Stop(game, start + 5000);
            Assert.IsTrue(sessions.BindWindow(game, 42, created, start + 8000, start + 9000));
            var saved = new GameCaptureSessions(directory.FullName).Snapshot(game, start + 10000);
            Assert.AreEqual(2, saved.Length);
            Assert.AreEqual(start + 5000, saved[0].Through);
            Assert.AreEqual(start + 8000, saved[1].From);
            Assert.AreEqual(start + 9000, saved[1].Through);
        }
        finally { directory.Delete(true); }
    }

    [TestMethod]
    public void ExplicitWindowBelongsToOneRunAndRetiresOldHandoffTickets()
    {
        var tracker = new GameLaunchTracker();
        var id = Guid.NewGuid();
        tracker.Starting(id, "Avowed");
        tracker.Started(id, 21, 100);
        var session = tracker.RunningSessions()[id].SessionId;
        var ticket = tracker.Begin(id, true, false);
        Assert.IsTrue(tracker.TrySetWindow(id, session, 42, 200, 77));
        Assert.IsNull(tracker.Read(id, ticket));
        var chosen = tracker.Read(id, tracker.Begin(id, true, false))!;
        Assert.AreEqual(42, chosen.ProcessId);
        Assert.AreEqual(77L, chosen.WindowHandle);
        Assert.AreEqual("Avowed", chosen.Name);

        var other = Guid.NewGuid();
        tracker.Starting(other);
        tracker.Started(other, 84, 400);
        Assert.IsFalse(tracker.TrySetWindow(id, session, 84, 400, 88));
        tracker.Stopped(id);
        tracker.Starting(id);
        tracker.Started(id, 21, 500);
        Assert.IsFalse(tracker.TrySetWindow(id, session, 42, 200, 77));
        Assert.AreEqual(0L, tracker.RunningSessions()[id].WindowHandle);
    }

    [TestMethod]
    public void StoreUpdateKeepsTheFamilyAndRelativeImageButNeverTheLauncherOrAnotherPackage()
    {
        const string family = "Microsoft.Avowed_8wekyb3d8bbwe";
        const string oldRoot = @"C:\WindowsApps\Microsoft.Avowed_1_x64";
        const string newRoot = @"D:\XboxGames\Microsoft.Avowed_2_x64";
        const string image = @"Alabama\Binaries\WinGDK\Avowed-WinGDK-Shipping.exe";
        var rule = GameWindowRule.FromImage(Path.Combine(oldRoot, image), family, oldRoot)!;
        Assert.AreEqual(image, rule.Image);
        Assert.IsTrue(rule.Matches(GameWindowRule.FromImage(Path.Combine(newRoot, image), family, newRoot)));
        Assert.IsFalse(rule.Matches(GameWindowRule.FromImage(Path.Combine(newRoot, "GameLaunchHelper.exe"), family, newRoot)));
        Assert.IsFalse(rule.Matches(GameWindowRule.FromImage(Path.Combine(newRoot, image), "Another_family", newRoot)));
        Assert.IsFalse(rule.Matches(GameWindowRule.FromImage(Path.Combine(newRoot, image))));
        Assert.IsNull(GameWindowRule.FromImage(Path.Combine(newRoot + "-other", image), family, newRoot));
    }

    [TestMethod]
    public void DesktopRuleDistinguishesIdenticallyNamedExecutablesAndSurvivesAProfileReload()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var choice = GameWindowRule.FromImage(@"D:\Games\Prey\Prey.exe")!;
            var path = Path.Combine(directory.FullName, "window.json");
            IntegrationFiles.Write(path, choice);
            var saved = IntegrationFiles.Read<GameWindowRule>(path)!;
            Assert.IsTrue(saved.Matches(GameWindowRule.FromImage(@"d:\games\prey\PREY.EXE")));
            Assert.IsFalse(saved.Matches(GameWindowRule.FromImage(@"C:\Other\Prey.exe")));
            Assert.IsFalse(saved.Matches(null));
        }
        finally { directory.Delete(true); }
    }
}
