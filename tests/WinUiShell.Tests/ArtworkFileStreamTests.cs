using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class ArtworkFileStreamTests
{
    [TestMethod]
    public async Task CancelingAnOpenRetiresImmediatelyAndClosesItsLateStream()
    {
        var path = Path.Combine(Path.GetTempPath(), "wrail-file-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(path, [1]);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var canceled = new CancellationTokenSource();
        FileStream? late = null;
        try
        {
            var pending = ArtworkFileStream.OpenAsync(() =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return late = File.OpenRead(path);
            }, canceled.Token);
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(1)));
            release.Set();
            Assert.IsTrue(SpinWait.SpinUntil(() => late is { CanRead: false }, TimeSpan.FromSeconds(3)));
        }
        finally { release.Set(); late?.Dispose(); File.Delete(path); }
    }

    [TestMethod]
    public void FileStreamIsReadOnlyBoundedAndRetainsOpenedFileAcrossReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-artwork-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "image.png");
        var next = Path.Combine(root, "next.png");
        var previous = Path.Combine(root, "previous.png");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            using (var file = ArtworkFileStream.Open(new(path, "one")))
            {
                Assert.IsFalse(file.CanWrite);
                Assert.IsTrue(file.CanSeek);
                File.WriteAllBytes(next, [4, 5, 6]);
                File.Move(path, previous);
                File.Move(next, path);
                Assert.AreEqual(1, file.ReadByte(), "An open stream must retain the original file after replacement.");
            }
            using (var file = ArtworkFileStream.Open(new(path, "two") { AllowedRoot = root }))
                Assert.AreEqual(4, file.ReadByte());
            Assert.Throws<UnauthorizedAccessException>(() =>
                ArtworkFileStream.Open(new(path, "three") { AllowedRoot = Path.Combine(root, "other") }));
            using (var file = File.OpenWrite(path)) file.SetLength(ProtocolConstants.MaximumEncodedArtworkBytes + 1L);
            Assert.Throws<InvalidDataException>(() => ArtworkFileStream.Open(new(path, "oversized")));
            File.Delete(path);
            Assert.Throws<FileNotFoundException>(() => ArtworkFileStream.Open(new(path, "missing")));
        }
        finally
        {
            File.Delete(path);
            File.Delete(next);
            File.Delete(previous);
            Directory.Delete(root);
        }
    }

    [TestMethod]
    public void PackageReferenceCannotBeOpenedBeforeBridgeAdmission()
    {
        Assert.Throws<InvalidDataException>(() => ArtworkFileStream.Open(new("assets/image.png", "one", PackageRelative: true)));
    }
}
