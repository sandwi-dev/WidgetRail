using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod]
    public async Task CapturedGameActionsDoNotResolveAnotherGameFromTheControlId()
    {
        var host = new FakeHost(100);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var selected = PlayniteLibraryItem.From(Item(80));
        var target = new PlayniteLibraryGameTarget(selected, () => true);
        var misleadingControl = PlayniteLibraryIdentity.FocusId("grid", PlayniteLibraryItem.From(Item(0)).Key);
        Assert.IsFalse(widget.HomeCollection.Items.Any(item => item.Key == selected.Key));
        await widget.HandleCapturedGameActionAsync(target, new(PlayniteLibraryActions.Favorite, misleadingControl));
        CollectionAssert.Contains(host.Authority.FavoriteGameIds.ToArray(), selected.Value.SavedId);
        CollectionAssert.DoesNotContain(host.Authority.FavoriteGameIds.ToArray(), Item(0).SavedId);
        await widget.HandleCapturedGameActionAsync(target, new(PlayniteLibraryActions.Launch, misleadingControl));
        CollectionAssert.AreEqual(new[] { selected.Value.AppId }, host.Launches);
        CollectionAssert.AreEqual(new[] { selected.Value.SavedId }, host.ResolveRequests[^1].ToArray());
        await Background(widget);
    }

    [TestMethod]
    public async Task CapturedModalUsesLogicalOwnerForAvailabilityAndCompletionChanges()
    {
        var host = new FakeHost(100);
        var widget = Create(host, out var application);
        await Interactive(widget);
        await Ready(widget, host);
        var current = true;
        var selected = PlayniteLibraryItem.From(Item(80));
        var target = new PlayniteLibraryGameTarget(selected, () => current);
        await widget.HandleCapturedGameActionAsync(target, new(PlayniteLibraryActions.DetailsOpen, "unrealized-control"));
        await widget.WhenDetailsIdleAsync();
        Assert.AreEqual(selected.Key, widget.RenderState.Value.DetailsItem!.Key);
        Assert.AreSame(target, widget.RenderState.Value.DetailsCapturedTarget);
        var generation = widget.RenderState.Value.DetailsGeneration;
        await widget.OnActionAsync(new(PlayniteLibraryDetailsPresentation.CompletionAction(generation, "Completed"),
            PlayniteLibraryDetailsPresentation.OptionsId));
        Assert.AreEqual((selected.Value.SavedId, "Completed"), application.CompletionRequests.Single());
        Assert.AreEqual("Completed", widget.RenderState.Value.DetailsExtras.ConfirmedCompletionStatus);
        current = false;
        await widget.OnActionAsync(new(PlayniteLibraryDetailsPresentation.CompletionAction(generation, "Playing"),
            PlayniteLibraryDetailsPresentation.OptionsId));
        Assert.AreEqual(1, application.CompletionRequests.Count, "Retired logical owner must reject a subsequent mutation.");
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, PlayniteLibraryDetailsPresentation.OptionsId));
        Assert.IsNull(widget.RenderState.Value.DetailsCapturedTarget);
        await Background(widget);
    }

    [TestMethod]
    public async Task CapturedLaunchRevalidatesExactOwnerBeforeProviderLaunch()
    {
        var host = new FakeHost(100);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var selected = PlayniteLibraryItem.From(Item(80));
        var current = true;
        var target = new PlayniteLibraryGameTarget(selected, () => current);
        host.ResolveHandler = _ => { current = false; return [selected.Value]; };
        await widget.HandleCapturedGameActionAsync(target, new(PlayniteLibraryActions.Launch, "unrealized-control"));
        Assert.AreEqual(0, host.Launches.Count);
        Assert.IsNull(widget.RenderState.Value.LaunchingSavedId);
        var resolveCount = host.ResolveRequests.Count;
        await widget.HandleCapturedGameActionAsync(target, new(PlayniteLibraryActions.Favorite, "unrealized-control"));
        await widget.HandleCapturedGameActionAsync(target, new(PlayniteLibraryActions.Launch, "unrealized-control"));
        Assert.AreEqual(resolveCount, host.ResolveRequests.Count);
        Assert.AreEqual(0, host.Authority.FavoriteGameIds.Count);
        await Background(widget);
    }
}
