using System.Diagnostics.Tracing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Diagnostics;

namespace WinUiShell.Tests;

[TestClass]
public sealed class PresentationPipelineTraceTests
{
    [TestMethod]
    public void TraceIsOptInCorrelatesNumericStagesAndDoesNotReviveDisabledOperations()
    {
        var log = PresentationPipelineTrace.Log;
        Assert.AreEqual(new Guid("d32b0e30-924a-598f-9296-f318d749fa45"), log.Guid);
        var disabled = log.Begin();
        Assert.AreEqual(0L, disabled.Id);
        using (var listener = new PipelineListener())
        {
            var first = log.Begin();
            var second = log.Begin();
            Assert.IsGreaterThan(0L, first.Id);
            Assert.AreNotEqual(first.Id, second.Id);
            log.End(first.Id, PipelineStage.RangeData, first.Started, 32, PipelineOutcome.Completed);
            log.End(first.Id, PipelineStage.RangeTotal, first.Started, 8, PipelineOutcome.Cancelled);
            log.End(disabled.Id, PipelineStage.RangeData, disabled.Started, 32, PipelineOutcome.Completed);
            Assert.HasCount(2, listener.Events);
            Assert.IsTrue(listener.Events.All(value => value.EventId == 1));
            Assert.AreEqual(first.Id, listener.Events[0].Values[0]);
            Assert.AreEqual(first.Id, listener.Events[1].Values[0]);
            Assert.AreEqual((int)PipelineOutcome.Cancelled, listener.Events[1].Values[4]);
            Assert.IsTrue(listener.Events.All(value => value.Values.All(field => field is long or int or double)));
            Assert.IsTrue(listener.Events.All(value => (double)value.Values[2]! >= 0));
        }
        Assert.AreEqual(0L, log.Begin().Id);
    }

    private sealed class PipelineListener : EventListener
    {
        private List<(int EventId, object?[] Values)>? events;
        internal List<(int EventId, object?[] Values)> Events => events ??= [];
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "WidgetRail-PresentationPipeline") EnableEvents(eventSource, EventLevel.Informational);
        }
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventSource.Name == "WidgetRail-PresentationPipeline")
                Events.Add((eventData.EventId, eventData.Payload?.ToArray() ?? []));
        }
    }
}
