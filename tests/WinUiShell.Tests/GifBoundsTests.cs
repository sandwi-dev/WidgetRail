using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class GifBoundsTests
{
    private static byte[] Header(int width = 1, int height = 1) =>
        [71, 73, 70, 56, 57, 97, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 128, 0, 0, 255, 0, 0, 0, 0, 255];
    private static byte[] Frame(int width = 1, int height = 1, int left = 0) =>
        [44, (byte)left, (byte)(left >> 8), 0, 0, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0, 2, 2, 68, 1, 0];
    private static void Validate(byte[] bytes, CancellationToken token = default)
    { using var stream = new MemoryStream(bytes, writable: false); NativeGifBounds.Validate(stream, token); }

    [TestMethod]
    public void NormalAnimationAndLocalPaletteAreAccepted()
    {
        var local = Frame(); local[9] = 128;
        Validate([.. Header(2, 2), 33, 249, 4, 0, 20, 0, 0, 0, .. Frame(),
            .. local[..10], 255, 0, 0, 0, 0, 255, .. local[10..], 59]);
    }

    [TestMethod]
    public void LogicalCanvasIsBoundedEvenWithTinyFirstFrame() =>
        Assert.ThrowsExactly<InvalidDataException>(() => Validate([.. Header(65535, 65535), .. Frame(), 59]));

    [TestMethod]
    public void LaterDeltaFrameIsBounded() =>
        Assert.ThrowsExactly<InvalidDataException>(() => Validate([.. Header(), .. Frame(), .. Frame(65535, 65535), 59]));

    [TestMethod]
    public void DeltaFrameMustFitCompositionCanvas() =>
        Assert.ThrowsExactly<InvalidDataException>(() => Validate([.. Header(16, 16), .. Frame(16, 16, 1), 59]));

    [TestMethod]
    public void ExactDimensionBoundAndManySmallFramesAreAccepted()
    {
        var bytes = new List<byte>(Header(4096, 4096));
        for (var i = 0; i < 10000; i++) bytes.AddRange(Frame());
        bytes.Add(59); Validate(bytes.ToArray());
    }

    [TestMethod]
    public void TruncatedDescriptorsPalettesAndBlocksAreRejected()
    {
        byte[] valid = [.. Header(), .. Frame(), 59];
        for (var length = 6; length < valid.Length; length++)
            Assert.ThrowsExactly<InvalidDataException>(() => Validate(valid[..length]), "Length " + length);
    }

    [TestMethod]
    public void ExtensionsAreSkippedWithoutInterpretingTheirPayloadAsFrames() =>
        Validate([.. Header(), 33, 254, 3, 44, 59, 33, 0, .. Frame(), 59]);

    [TestMethod]
    public void MissingFramesAndUnknownBlockTagsAreRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Validate([.. Header(), 59]));
        Assert.ThrowsExactly<InvalidDataException>(() => Validate([.. Header(), 42, .. Frame(), 59]));
    }

    [TestMethod]
    public void CancellationRetiresDescriptorTraversal()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => Validate([.. Header(), .. Frame(), 59], cancellation.Token));
    }

    [TestMethod]
    public void OtherImageFormatsRemainOwnedByTheirNativeCodec() => Validate([137, 80, 78, 71, 13, 10, 26, 10]);

    [TestMethod]
    public void DimensionsDescribeCanvasNotFirstDeltaFrame()
    {
        using var stream = new MemoryStream([.. Header(32, 48), .. Frame(1, 1, 4), 59]);
        Assert.AreEqual((32, 48), NativeGifBounds.Validate(stream, default));
    }
}
