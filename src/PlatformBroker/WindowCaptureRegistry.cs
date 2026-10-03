using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

/// <summary>Trusted foreground capture request. Output path never crosses widget IPC.</summary>
public sealed record HostWindowCapture(string RequestId, BrokerWidgetIdentity Widget,
    WindowCaptureKind Kind, string OutputPath);
public sealed record HostCaptureCompletion(string RequestId, int Width, int Height, double DurationSeconds, string? ErrorCode = null, NativeWindowPreviewTarget? Target = null, CaptureApplicationContext? SourceApplication = null);
public sealed record HostCaptureAttachment(CaptureAttachment Attachment, string Path);

/// <summary>Single-process capability owner. Every operation remains scoped to the original broker object.</summary>
internal sealed class WindowCaptureRegistry(TimeProvider? time = null) : IDisposable
{
    internal static WindowCaptureRegistry Shared { get; } = new();
    private readonly object gate = new();
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly string root = Path.Combine(Path.GetTempPath(), "WidgetRail", "captures", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingDeletes = new(StringComparer.Ordinal);
    private bool disposed;
    private ITimer? sweeper;
    private FileStream? directoryLease;
    private DateTimeOffset nextOrphanSweep;
    private sealed class Entry(object owner, HostWindowCapture request, DateTimeOffset expires)
    {
        internal readonly object Owner = owner;
        internal readonly HostWindowCapture Request = request;
        internal DateTimeOffset Expires = expires;
        internal WindowCapturePhase Phase;
        internal CaptureAttachment? Attachment;
        internal string? Error;
    }
    internal WindowCaptureTicket Enqueue(object owner, BrokerWidgetIdentity widget, WindowCaptureKind kind)
    {
        lock (gate)
        {
            ThrowIfDisposed(); Sweep();
            sweeper ??= clock.CreateTimer(_ => { lock (gate) { if (!disposed) Sweep(); } }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            foreach (var old in entries.Where(pair => ReferenceEquals(pair.Value.Owner, owner) && pair.Value.Phase is WindowCapturePhase.Cancelled or WindowCapturePhase.Failed).ToArray())
            { entries.Remove(old.Key); Delete(old.Value.Request.OutputPath); }
            if (!Enum.IsDefined(kind)) throw Error("invalid_payload");
            if (entries.Values.Any(entry => entry.Phase is WindowCapturePhase.Queued or WindowCapturePhase.Preparing or WindowCapturePhase.Recording)) throw Error("capture_busy");
            if (pendingDeletes.Count + entries.Count >= CaptureLimits.MaximumTotal || entries.Values.Count(entry => ReferenceEquals(entry.Owner, owner)) >= CaptureLimits.MaximumPerWidget) throw Error("capture_limit");
            var id = Guid.NewGuid().ToString("N");
            var request = new HostWindowCapture(id, widget, kind,
                Path.Combine(root, id + (kind == WindowCaptureKind.Screenshot ? ".png" : ".mp4")));
            entries.Add(id, new(owner, request, clock.GetUtcNow().AddMinutes(2)));
            return new(id);
        }
    }
    internal HostWindowCapture? Take()
    {
        lock (gate)
        {
            ThrowIfDisposed(); Sweep();
            var entry = entries.Values.FirstOrDefault(item => item.Phase is WindowCapturePhase.Queued or WindowCapturePhase.Preparing or WindowCapturePhase.Recording);
            if (entry is null) return null;
            Directory.CreateDirectory(root);
            directoryLease ??= new FileStream(Path.Combine(root, ".owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            // Repeated takes return the same request until a terminal report: a
            // slow/destructively consumed IPC response cannot lose the capture.
            if (entry.Phase == WindowCapturePhase.Queued) entry.Phase = WindowCapturePhase.Preparing;
            return entry.Request;
        }
    }
    internal bool IsCurrent(string requestId)
    {
        lock (gate) { Sweep(); return !disposed && entries.TryGetValue(requestId, out var entry) && entry.Phase is WindowCapturePhase.Preparing or WindowCapturePhase.Recording; }
    }
    internal void Recording(string requestId)
    {
        lock (gate)
        {
            var entry = Demand(requestId);
            if (entry.Phase != WindowCapturePhase.Preparing) throw Error("capture_stale");
            entry.Phase = WindowCapturePhase.Recording;
        }
    }
    internal void Complete(HostCaptureCompletion result)
    {
        lock (gate)
        {
            var entry = Demand(result.RequestId);
            if (entry.Phase is not (WindowCapturePhase.Preparing or WindowCapturePhase.Recording)) throw Error("capture_stale");
            if (result.ErrorCode is { } error)
            {
                entry.Phase = error == "cancelled" ? WindowCapturePhase.Cancelled : WindowCapturePhase.Failed;
                entry.Error = ValidError(error) ? error : "capture_failed";
                Delete(entry.Request.OutputPath); return;
            }
            var file = new FileInfo(entry.Request.OutputPath);
            var expires = clock.GetUtcNow().AddMinutes(30);
            var attachment = new CaptureAttachment(Guid.NewGuid().ToString("N"),
                entry.Request.Kind == WindowCaptureKind.Screenshot ? "image/png" : "video/mp4", result.Width, result.Height,
                file.Exists ? file.Length : 0, result.DurationSeconds, expires.ToUnixTimeMilliseconds())
                { SourceApplication = result.SourceApplication };
            if (!attachment.IsWellFormed() || entries.Values.Sum(item => item.Attachment?.ByteLength ?? 0) + pendingDeletes.Count * (long)CaptureLimits.MaximumAttachmentBytes + attachment.ByteLength > CaptureLimits.MaximumTotalBytes || !MatchesHeader(entry.Request.OutputPath, entry.Request.Kind))
            { entry.Phase = WindowCapturePhase.Failed; entry.Error = "capture_invalid_media"; Delete(entry.Request.OutputPath); return; }
            entry.Attachment = attachment; entry.Phase = WindowCapturePhase.Ready; entry.Expires = expires;
        }
    }
    internal WindowCaptureStatus Status(object owner, string id)
    {
        lock (gate) { var entry = Owned(owner, id); return new(id, entry.Phase, entry.Attachment, entry.Error); }
    }
    internal void Cancel(object owner, string id)
    {
        lock (gate)
        {
            var entry = Owned(owner, id);
            entry.Phase = WindowCapturePhase.Cancelled; entry.Attachment = null; entry.Error = "cancelled";
            Delete(entry.Request.OutputPath);
        }
    }
    internal void Discard(object owner, string attachmentId)
    {
        lock (gate)
        {
            var entry = Attachment(owner, attachmentId);
            entries.Remove(entry.Request.RequestId); Delete(entry.Request.OutputPath);
        }
    }
    internal HostCaptureAttachment ResolveOwned(object owner, CaptureAttachment expected)
    {
        lock (gate)
        {
            var entry = Attachment(owner, expected.Id);
            if (entry.Attachment != expected) throw Error("capture_unavailable");
            return new(expected, entry.Request.OutputPath);
        }
    }
    internal HostCaptureAttachment Resolve(BrokerWidgetIdentity widget, string id)
    {
        lock (gate)
        {
            Sweep();
            var entry = entries.Values.SingleOrDefault(item => item.Request.Widget == widget && item.Phase == WindowCapturePhase.Ready && item.Attachment?.Id == id)
                ?? throw Error("capture_unavailable");
            return new(entry.Attachment!, entry.Request.OutputPath);
        }
    }
    internal CaptureReadChunk Read(object owner, CaptureReadRequest request)
    {
        lock (gate)
        {
            var entry = Attachment(owner, request.AttachmentId);
            var length = entry.Attachment!.ByteLength;
            if (request.Offset < 0 || request.Offset >= length) throw Error("invalid_payload");
            using var input = new FileStream(entry.Request.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length != length) throw Error("capture_unavailable");
            input.Position = request.Offset;
            var buffer = new byte[(int)Math.Min(CaptureLimits.ChunkBytes, length - request.Offset)];
            input.ReadExactly(buffer);
            return new(request.Offset, buffer, input.Position == length);
        }
    }
    internal void Retire(object owner)
    {
        lock (gate)
            foreach (var pair in entries.Where(pair => ReferenceEquals(pair.Value.Owner, owner)).ToArray())
            { entries.Remove(pair.Key); Delete(pair.Value.Request.OutputPath); }
    }
    private Entry Owned(object owner, string id)
    {
        var entry = Demand(id);
        if (!ReferenceEquals(entry.Owner, owner)) throw Error("capture_unavailable");
        return entry;
    }
    private Entry Attachment(object owner, string id)
    {
        Sweep();
        return entries.Values.SingleOrDefault(item => ReferenceEquals(item.Owner, owner) && item.Phase == WindowCapturePhase.Ready && item.Attachment?.Id == id)
            ?? throw Error("capture_unavailable");
    }
    private Entry Demand(string id)
    {
        ThrowIfDisposed(); Sweep();
        return CaptureLimits.ValidToken(id) && entries.TryGetValue(id, out var entry) ? entry : throw Error("capture_unavailable");
    }
    private void Sweep()
    {
        if (clock.GetUtcNow() >= nextOrphanSweep)
        {
            nextOrphanSweep = clock.GetUtcNow().AddMinutes(1);
            DeleteExpiredSessions();
        }
        foreach (var path in pendingDeletes.ToArray()) Delete(path);
        var now = clock.GetUtcNow();
        foreach (var pair in entries.Where(pair => pair.Value.Expires <= now).ToArray())
        { entries.Remove(pair.Key); Delete(pair.Value.Request.OutputPath); }
    }
    // A crashed host cannot run Dispose. Delete only expired, unlocked capture
    // sessions and known generated files; never traverse links or recurse.
    private void DeleteExpiredSessions()
    {
        var parent = Path.GetDirectoryName(root)!;
        try
        {
            if (!Directory.Exists(parent)) return;
            foreach (var directory in Directory.EnumerateDirectories(parent).Take(64))
            {
                if (directory == root || !CaptureLimits.ValidToken(Path.GetFileName(directory)) ||
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                    Directory.GetLastWriteTimeUtc(directory) > clock.GetUtcNow().UtcDateTime.AddMinutes(-35)) continue;
                try
                {
                    var owner = Path.Combine(directory, ".owner");
                    if (File.Exists(owner)) { using var lease = new FileStream(owner, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                    foreach (var path in Directory.EnumerateFiles(directory).Take(CaptureLimits.MaximumTotal + 1))
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 &&
                            (Path.GetFileName(path) == ".owner" || CaptureLimits.ValidToken(Path.GetFileNameWithoutExtension(path)) && Path.GetExtension(path) is ".png" or ".mp4")) File.Delete(path);
                    Directory.Delete(directory);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static bool MatchesHeader(string path, WindowCaptureKind kind)
    {
        using var input = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        if (input.Read(header) < header.Length) return false;
        return kind == WindowCaptureKind.Screenshot ? header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) : header.Slice(4, 4).SequenceEqual("ftyp"u8);
    }
    private static bool ValidError(string value) => value.Length is > 0 and <= 80 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    private static BrokerException Error(string code) => new(code, "Capture is unavailable or the request is invalid.");
    private void Delete(string path)
    {
        try { File.Delete(path); pendingDeletes.Remove(path); }
        catch (IOException) { pendingDeletes.Add(path); }
        catch (UnauthorizedAccessException) { pendingDeletes.Add(path); }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true; sweeper?.Dispose();
            foreach (var entry in entries.Values) Delete(entry.Request.OutputPath);
            entries.Clear();
            foreach (var path in pendingDeletes.ToArray()) Delete(path);
            directoryLease?.Dispose(); directoryLease = null;
            Delete(Path.Combine(root, ".owner"));
            try { if (Directory.Exists(root)) Directory.Delete(root); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
