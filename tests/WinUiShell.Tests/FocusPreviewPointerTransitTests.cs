using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
namespace WinUiShell.Tests;
[TestClass]
public sealed class FocusPreviewPointerTransitTests
{
    [TestMethod]
    public void DiagonalTravelCrossesOtherRowsWithoutAbandoningTheCurrentPopup()
    {
        var policy=new FocusPreviewPointerTransit();
        policy.SetBounds(new(0,40,100,30),new(140,0,200,180));
        Assert.IsTrue(policy.Retain((95,55)));
        Assert.IsTrue(policy.Retain((105,75)));
        Assert.IsTrue(policy.Retain((120,110)));
        Assert.IsTrue(policy.Retain((145,120)));
        Assert.IsTrue(policy.Retain((200,150)));
    }
    [TestMethod]
    public void MovementAwayAndUnrelatedNeighborsReleaseTransit()
    {
        var policy=new FocusPreviewPointerTransit();policy.SetBounds(new(0,40,100,30),new(140,0,200,180));
        Assert.IsTrue(policy.Retain((95,55)));
        Assert.IsFalse(policy.Retain((60,90)), "Moving down the list must admit another row.");
        Assert.IsFalse(policy.Retain((120,100)), "An abandoned corridor cannot recapture the pointer.");
        policy.Retain((95,55));policy.Retain((120,55));
        Assert.IsFalse(policy.Retain((110,55)), "Moving backwards outside both surfaces cancels intent.");
    }
    [TestMethod]
    public void TravelWorksInEveryDirectionAndBackIntoSource()
    {
        foreach(var tuple in new[] {
            (new AnchorRectangle(0,40,100,30),new AnchorRectangle(140,0,200,180),(95d,55d),(120d,55d),(145d,55d)),
            (new AnchorRectangle(250,40,100,30),new AnchorRectangle(0,0,210,180),(255d,55d),(230d,55d),(205d,55d)),
            (new AnchorRectangle(40,0,50,30),new AnchorRectangle(0,70,180,150),(65d,25d),(65d,50d),(65d,75d)),
            (new AnchorRectangle(40,200,50,30),new AnchorRectangle(0,0,180,160),(65d,205d),(65d,180d),(65d,155d)) })
        {
            var policy=new FocusPreviewPointerTransit();policy.SetBounds(tuple.Item1,tuple.Item2);
            Assert.IsTrue(policy.Retain(tuple.Item3));Assert.IsTrue(policy.Retain(tuple.Item4));Assert.IsTrue(policy.Retain(tuple.Item5));
            Assert.IsTrue(policy.Retain(tuple.Item4));Assert.IsTrue(policy.Retain(tuple.Item3));
        }
    }
    [TestMethod]
    public void LayoutChangesAndInvalidGeometryRetireOldCorridors()
    {
        var policy=new FocusPreviewPointerTransit();policy.SetBounds(new(0,40,100,30),new(140,0,200,180));policy.Retain((95,55));
        policy.SetBounds(new(0,140,100,30),new(140,0,200,180));Assert.IsFalse(policy.Retain((120,55)));
        policy.SetBounds(default,default);Assert.IsFalse(policy.Retain((double.NaN,0)));
    }
}
