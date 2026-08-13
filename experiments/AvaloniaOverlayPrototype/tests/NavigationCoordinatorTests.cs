using GameBarAlternative.AvaloniaPrototype.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public sealed class NavigationCoordinatorTests
{
    [TestMethod]
    [Timeout(5_000)]
    public async Task Loading_retains_admitted_page_until_destination_is_ready()
    {
        using var coordinator = new NavigationCoordinator<object>();
        var firstPage = new object();
        var secondPage = new object();
        await coordinator.NavigateAsync(PrototypeRoute.Settings, (_, _) => Task.FromResult(firstPage));
        var readiness = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = coordinator.NavigateAsync(
            PrototypeRoute.AudioMixer,
            (_, cancellationToken) => readiness.Task.WaitAsync(cancellationToken));

        Assert.IsTrue(coordinator.IsLoading);
        Assert.AreSame(firstPage, coordinator.CurrentPage);
        Assert.AreEqual(PrototypeRoute.Settings, coordinator.CurrentRoute);
        readiness.SetResult(secondPage);

        var result = await pending;
        Assert.AreEqual(NavigationOutcome.Committed, result.Outcome);
        Assert.AreSame(secondPage, coordinator.CurrentPage);
    }

    [TestMethod]
    [Timeout(5_000)]
    public async Task Latest_navigation_supersedes_an_unready_destination()
    {
        using var coordinator = new NavigationCoordinator<object>();
        var neverReady = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var superseded = coordinator.NavigateAsync(
            PrototypeRoute.SpotifyPlayer,
            (_, cancellationToken) => neverReady.Task.WaitAsync(cancellationToken));

        var latestPage = new object();
        var latest = await coordinator.NavigateAsync(
            PrototypeRoute.GameLauncher,
            (_, _) => Task.FromResult(latestPage));

        Assert.AreEqual(NavigationOutcome.Committed, latest.Outcome);
        Assert.AreSame(latestPage, coordinator.CurrentPage);
        Assert.AreEqual(NavigationOutcome.Superseded, (await superseded).Outcome);
        Assert.AreEqual(PrototypeRoute.GameLauncher, coordinator.CurrentRoute);
    }

    [TestMethod]
    public async Task Back_history_returns_routes_in_last_admitted_order()
    {
        using var coordinator = new NavigationCoordinator<object>();
        await coordinator.NavigateAsync(PrototypeRoute.Settings, (_, _) => Task.FromResult(new object()));
        await coordinator.NavigateAsync(PrototypeRoute.AudioMixer, (_, _) => Task.FromResult(new object()));
        await coordinator.NavigateAsync(PrototypeRoute.SpotifyPlayer, (_, _) => Task.FromResult(new object()));

        Assert.AreEqual(PrototypeRoute.AudioMixer, coordinator.PopHistory());
        Assert.AreEqual(PrototypeRoute.Settings, coordinator.PopHistory());
        Assert.IsNull(coordinator.PopHistory());
    }
}
