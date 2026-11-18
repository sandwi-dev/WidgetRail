using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class CaptureRecordingSummaryTests
{
    [TestMethod]
    public void AutoApprovalDoesNotPresentLegacyFallbackAsResolved()
    {
        var options=new RecordingOptions(Encoder:CaptureEncoderMode.Hardware){Selection=new()};
        var text=CaptureRecordingSummary.Video(options);
        StringAssert.Contains(text,"Auto size");StringAssert.Contains(text,"Auto fps");StringAssert.Contains(text,"Auto codec");
        Assert.IsFalse(text.Contains("1280",StringComparison.Ordinal));
        var source=CaptureRecordingSummary.Video(options with{Selection=new(){Resolution=CaptureRecordingResolution.Source,FramesPerSecond=60,Codec=CaptureVideoCodec.Hevc}});
        StringAssert.Contains(source,"Source size");StringAssert.Contains(source,"60 fps");StringAssert.Contains(source,"HEVC");
    }

    [TestMethod]
    public void LegacyExplicitRequestRetainsItsActualEncoderAndAudioMeaning()
    {
        var options=new RecordingOptions(1280,720,15,Encoder:CaptureEncoderMode.Software,Audio:CaptureAudioMode.None);
        var text=CaptureRecordingSummary.Video(options);
        StringAssert.Contains(text,"1280 × 720");StringAssert.Contains(text,"15 fps");StringAssert.Contains(text,"Software");
        Assert.IsFalse(text.Contains("Auto",StringComparison.Ordinal));
        Assert.AreEqual("Audio: Off",CaptureRecordingSummary.Audio(options));
        StringAssert.Contains(CaptureRecordingSummary.Audio(options with{Audio=CaptureAudioMode.Application}),"Microphone off");
    }
}
