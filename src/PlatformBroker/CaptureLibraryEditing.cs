using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

// Trusted host contract only. No path, recipe or authority is exposed over widget IPC yet.
internal sealed record CaptureImageEdit(int QuarterTurns, int X, int Y, int Width, int Height);
internal sealed record CaptureVideoEdit(long StartTicks, long EndTicks);
internal sealed record CaptureEditRecipe(CaptureImageEdit? Image = null, CaptureVideoEdit? Video = null)
{
    internal bool IsValid(CaptureAttachment media)
    {
        if (Image is { } image && Video is null && media.ContentType == "image/png")
        {
            var width = image.QuarterTurns % 2 == 0 ? media.Width : media.Height;
            var height = image.QuarterTurns % 2 == 0 ? media.Height : media.Width;
            return image.QuarterTurns is >= 0 and <= 3 && image.X >= 0 && image.Y >= 0 && image.Width > 0 && image.Height > 0 &&
                (long)image.X + image.Width <= width && (long)image.Y + image.Height <= height &&
                (image.QuarterTurns != 0 || image.X != 0 || image.Y != 0 || image.Width != width || image.Height != height);
        }
        return Image is null && Video is { } video && media.ContentType == "video/mp4" && video.StartTicks >= 0 && video.EndTicks > video.StartTicks &&
            video.EndTicks <= Math.Round(media.DurationSeconds * TimeSpan.TicksPerSecond) &&
            video.EndTicks - video.StartTicks <= CaptureEditLimits.MaximumVideoTicks &&
            (video.StartTicks != 0 || video.EndTicks != Math.Round(media.DurationSeconds * TimeSpan.TicksPerSecond));
    }
}
internal sealed record CaptureEditDerivation(string SourceId, long SourceContentRevision, long SourceMetadataRevision, CaptureEditRecipe Recipe,
    bool SourceIsTemporary = false, string? SourceFingerprint = null);
internal sealed record CaptureEditInput(CaptureAttachment Media, string Path, Stream Stream);
internal delegate Task CaptureEditCodec(CaptureEditInput source, CaptureEditRecipe recipe, Stream output, IProgress<double>? progress, CancellationToken token);
internal static class CaptureEditLimits
{
    internal const long MaximumVideoBytes = 2L * 1024 * 1024 * 1024;
    internal const long MaximumVideoTicks = 10L * 60 * TimeSpan.TicksPerSecond;
    internal const int MaximumAssociations = 256;
    internal static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);
}

/// <summary>Disk-backed encoder output. A seek or resize cannot bypass its byte budget.</summary>
internal sealed class CaptureEditOutput(Stream output, long maximum) : Stream
{
    public override bool CanRead => output.CanRead;
    public override bool CanSeek => output.CanSeek;
    public override bool CanWrite => output.CanWrite;
    public override long Length => output.Length;
    public override long Position { get => output.Position; set { Bound(value); output.Position = value; } }
    private void Bound(long value) { if (value < 0 || value > maximum) throw new BrokerException("capture_library_edit_output_limit", "The edited copy exceeds its output budget."); }
    public override void Flush() => output.Flush();
    public override Task FlushAsync(CancellationToken token) => output.FlushAsync(token);
    public override int Read(byte[] buffer, int offset, int count) => output.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => output.ReadAsync(buffer, token);
    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = checked((origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => Position, SeekOrigin.End => Length, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }) + offset);
        Bound(target); return output.Seek(offset, origin);
    }
    public override void SetLength(long value) { Bound(value); output.SetLength(value); }
    public override void Write(byte[] buffer, int offset, int count) { Bound(checked(Position + count)); output.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Bound(checked(Position + buffer.Length)); output.Write(buffer); }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    { Bound(checked(Position + buffer.Length)); return output.WriteAsync(buffer, token); }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
    { Bound(checked(Position + count)); return output.WriteAsync(buffer, offset, count, token); }
    // The store owns and flushes the file; WinRT stream adapters can dispose this wrapper independently.
}
