using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

/// <summary>Trusted foreground capture request. Output path never crosses widget IPC.</summary>
public sealed record HostWindowCapture(string RequestId, BrokerWidgetIdentity Widget,
    WindowCaptureKind Kind, string OutputPath, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] CaptureImageResolution Resolution = CaptureImageResolution.Context)
{
    public CaptureSessionRequest? Options { get; init; }
}
public sealed record HostCaptureCompletion(string RequestId, int Width, int Height, double DurationSeconds, string? ErrorCode = null, NativeWindowPreviewTarget? Target = null, CaptureApplicationContext? SourceApplication = null)
{
    public CaptureMediaProperties? Properties { get; init; }
}
public sealed record HostCaptureAttachment(CaptureAttachment Attachment, string Path, [property: JsonIgnore] string RequiredCapability = PlatformCapabilities.WindowCaptureV1);

/// <summary>Single-process capability owner. Every operation remains scoped to the original broker object.</summary>
internal sealed class WindowCaptureRegistry(TimeProvider? time = null, long maximumBytes = CaptureLimits.MaximumTotalBytes) : IDisposable
{
    internal const int CapabilityDiscoveryTimeoutSeconds = 15;
    internal static WindowCaptureRegistry Shared { get; } = new();
    private readonly object gate = new();
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly string root = Path.Combine(Path.GetTempPath(), "WidgetRail", "captures", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> pendingDeletes = new(StringComparer.Ordinal);
    private bool disposed;
    private CaptureCapabilities capabilities = CaptureCapabilities.Unavailable;
    private readonly TaskCompletionSource<CaptureCapabilities> capabilityReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<CaptureCapabilities> encodingReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int capabilityWaiters, encodingWaiters;
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
        internal string RequiredCapability = PlatformCapabilities.WindowCaptureV1;
        internal bool StopRequested;
    }
    internal CaptureCapabilities Capabilities { get { lock (gate) return capabilities; } }
    internal bool NeedsCapabilities { get { lock (gate) return capabilityWaiters + encodingWaiters > 0 && !capabilityReady.Task.IsCompleted; } }
    internal bool NeedsEncodingCapabilities { get { lock (gate) return encodingWaiters > 0 && !encodingReady.Task.IsCompleted; } }
    internal async Task<CaptureCapabilities> GetCapabilitiesAsync(CancellationToken token, bool includeEncoding = true)
    {
        Task<CaptureCapabilities> ready;
        lock (gate)
        {
            ThrowIfDisposed();
            ready = includeEncoding ? encodingReady.Task : capabilityReady.Task;
            if (ready.IsCompleted) return capabilities;
            if (includeEncoding) ++encodingWaiters; else ++capabilityWaiters;
        }
        try { await ready.WaitAsync(TimeSpan.FromSeconds(CapabilityDiscoveryTimeoutSeconds), token).ConfigureAwait(false); lock (gate) return capabilities; }
        catch (TimeoutException) { throw Error("capture_capabilities_unavailable"); }
        finally { lock (gate) { if (includeEncoding) --encodingWaiters; else --capabilityWaiters; } }
    }
    internal void SetCapabilities(CaptureCapabilities value, bool encodingProbed = true)
    {
        if (value is null || !value.IsWellFormed()) throw Error("invalid_capture_capabilities");
        lock (gate)
        {
            ThrowIfDisposed();
            if (!encodingProbed && encodingReady.Task.IsCompleted) return;
            capabilities = value with { ImageFormats = value.ImageFormats.ToArray(), RecordingProfiles = value.RecordingProfiles.ToArray(), AudioModes = value.AudioModes.ToArray() };
            capabilityReady.TrySetResult(capabilities);
            if (encodingProbed) encodingReady.TrySetResult(capabilities);
        }
    }
    internal WindowCaptureTicket EnqueueConfigured(object owner, BrokerWidgetIdentity widget, CaptureSessionRequest options)
    {
        lock (gate)
        {
            var validation = capabilities.Validate(options);
            if (!validation.Supported) throw Error(validation.ErrorCode!);
            return Enqueue(owner, widget, options.Recording is null ? WindowCaptureKind.Screenshot : WindowCaptureKind.Video,
                options.Screenshot?.MaximumWidth is null && options.Recording is null ? CaptureImageResolution.Original : CaptureImageResolution.Context, options);
        }
    }
    internal WindowCaptureTicket Enqueue(object owner, BrokerWidgetIdentity widget, WindowCaptureKind kind, CaptureImageResolution resolution = CaptureImageResolution.Context,
        CaptureSessionRequest? options = null)
    {
        lock (gate)
        {
            ThrowIfDisposed(); Sweep();
            sweeper ??= clock.CreateTimer(_ => { lock (gate) { if (!disposed) Sweep(); } }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            foreach (var old in entries.Where(pair => ReferenceEquals(pair.Value.Owner, owner) && pair.Value.Phase is WindowCapturePhase.Cancelled or WindowCapturePhase.Failed).ToArray())
            { entries.Remove(old.Key); Delete(old.Value.Request.OutputPath); }
            if (!Enum.IsDefined(kind) || !Enum.IsDefined(resolution) || kind == WindowCaptureKind.Video && resolution != CaptureImageResolution.Context) throw Error("invalid_payload");
            if (entries.Values.Any(entry => entry.Phase is WindowCapturePhase.Queued or WindowCapturePhase.Preparing or WindowCapturePhase.Recording)) throw Error("capture_busy");
            if (pendingDeletes.Count + entries.Count >= CaptureLimits.MaximumTotal || entries.Values.Count(entry => ReferenceEquals(entry.Owner, owner)) >= CaptureLimits.MaximumPerWidget) throw Error("capture_limit");
            var reservation = options?.MaximumBytes ?? (resolution == CaptureImageResolution.Original ? CaptureLimits.MaximumAttachmentBytes : CaptureLimits.MaximumContextBytes);
            if (UsageBytes() + reservation > maximumBytes) throw Error("capture_limit");
            var id = Guid.NewGuid().ToString("N");
            var request = new HostWindowCapture(id, widget, kind,
                Path.Combine(root, id + (kind == WindowCaptureKind.Screenshot ? ".png" : ".mp4")), resolution) { Options = options };
            entries.Add(id, new(owner, request, clock.GetUtcNow().AddSeconds(120 + (options?.Preparation.DelaySeconds ?? 0) + (options?.Recording?.MaximumDurationSeconds ?? 0))));
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
    internal bool IsStopRequested(string id)
    { lock (gate) return entries.TryGetValue(id, out var entry) && entry.StopRequested; }
    internal void Stop(object owner, string id)
    {
        lock (gate)
        {
            var entry = Owned(owner, id);
            if (entry.Request.Options?.Recording is null || entry.Phase is not (WindowCapturePhase.Recording or WindowCapturePhase.Ready))
                throw Error("capture_not_recording");
            if (entry.Phase == WindowCapturePhase.Recording) entry.StopRequested = true;
        }
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
                { SourceApplication = result.SourceApplication, Properties = entry.Request.Options is null ? null : result.Properties };
            var options = entry.Request.Options;
            if (!attachment.IsWellFormed() || options is not null && !MatchesOptions(options, attachment) ||
                options is null && entry.Request.Resolution == CaptureImageResolution.Context && entry.Request.Kind == WindowCaptureKind.Screenshot &&
                (attachment.Width > 1920 || attachment.Height > 1080 || attachment.ByteLength > CaptureLimits.MaximumContextBytes) || UsageBytes(entry) + attachment.ByteLength > maximumBytes || !MatchesHeader(entry.Request.OutputPath, entry.Request.Kind))
            { entry.Phase = WindowCapturePhase.Failed; entry.Error = "capture_invalid_media"; Delete(entry.Request.OutputPath); return; }
            entry.Attachment = attachment; entry.Phase = WindowCapturePhase.Ready; entry.Expires = expires;
        }
    }
    internal WindowCaptureStatus Status(object owner, string id)
    {
        lock (gate) { var entry = Owned(owner, id); return new(id, entry.Phase, entry.Attachment, entry.Error) { StopRequested = entry.StopRequested }; }
    }
    private static bool MatchesOptions(CaptureSessionRequest options, CaptureAttachment attachment)
    {
        if (attachment.Properties is not { } actual || attachment.ByteLength > options.MaximumBytes) return false;
        if (options.Screenshot is { } image)
            return image.Format == CaptureImageFormat.Png && attachment.ContentType == "image/png" &&
                ExpectedSize(image.MaximumWidth ?? actual.SourceWidth, image.MaximumHeight ?? actual.SourceHeight, false);
        var video = options.Recording!;
        return attachment.ContentType == "video/mp4" && actual.RequestedFramesPerSecond == video.FramesPerSecond &&
            actual.Encoder == video.Encoder && actual.Audio == video.Audio && attachment.DurationSeconds <= video.MaximumDurationSeconds + .25 &&
            actual.SourceWidth >= 2 && actual.SourceHeight >= 2 && ExpectedSize(video.MaximumWidth, video.MaximumHeight, true);

        bool ExpectedSize(int width, int height, bool video)
        {
            var scale = Math.Min(1, Math.Min((double)width / actual.SourceWidth, (double)height / actual.SourceHeight));
            var expectedWidth = Math.Max(1, (int)(actual.SourceWidth * scale));
            var expectedHeight = Math.Max(1, (int)(actual.SourceHeight * scale));
            if (video) { expectedWidth = Math.Max(2, expectedWidth & ~1); expectedHeight = Math.Max(2, expectedHeight & ~1); }
            return attachment.Width == expectedWidth && attachment.Height == expectedHeight;
        }
    }
    internal void Cancel(object owner, string id)
    {
        lock (gate)
        {
            var entry = Owned(owner, id);
            var reserved = ReservedBytes(entry);
            entry.Phase = WindowCapturePhase.Cancelled; entry.Attachment = null; entry.Error = "cancelled";
            Delete(entry.Request.OutputPath, reserved);
        }
    }
    internal void Discard(object owner, string attachmentId, string requiredCapability = PlatformCapabilities.WindowCaptureV1)
    {
        lock (gate)
        {
            var entry = Attachment(owner, attachmentId);
            if (entry.RequiredCapability != requiredCapability) throw Error("capture_unavailable");
            entries.Remove(entry.Request.RequestId); Delete(entry.Request.OutputPath, entry.Attachment?.ByteLength);
        }
    }
    internal HostCaptureAttachment ResolveOwned(object owner, CaptureAttachment expected)
    {
        lock (gate)
        {
            var entry = Attachment(owner, expected.Id);
            if (entry.Attachment != expected) throw Error("capture_unavailable");
            return new(expected, entry.Request.OutputPath, entry.RequiredCapability);
        }
    }
    internal HostCaptureAttachment Resolve(BrokerWidgetIdentity widget, string id)
    {
        lock (gate)
        {
            Sweep();
            var entry = entries.Values.SingleOrDefault(item => item.Request.Widget == widget && item.Phase == WindowCapturePhase.Ready && item.Attachment?.Id == id)
                ?? throw Error("capture_unavailable");
            return new(entry.Attachment!, entry.Request.OutputPath, entry.RequiredCapability);
        }
    }
    internal CaptureReadChunk Read(object owner, CaptureReadRequest request, string requiredCapability = PlatformCapabilities.WindowCaptureV1)
    {
        lock (gate)
        {
            var entry = Attachment(owner, request.AttachmentId);
            if (entry.RequiredCapability != requiredCapability) throw Error("capture_unavailable");
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
    internal async Task<CaptureAttachment> ImportAsync(object owner, BrokerWidgetIdentity identity, CaptureLibraryReadLease source, CancellationToken token)
    {
        string id, path; CaptureAttachment media;
        lock (gate)
        {
            ThrowIfDisposed(); Sweep();
            if (pendingDeletes.Count + entries.Count >= CaptureLimits.MaximumTotal || entries.Values.Count(entry => ReferenceEquals(entry.Owner, owner)) >= CaptureLimits.MaximumPerWidget ||
                UsageBytes() + source.Media.ByteLength > maximumBytes)
                throw Error("capture_limit");
            Directory.CreateDirectory(root);
            directoryLease ??= new FileStream(Path.Combine(root, ".owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            sweeper ??= clock.CreateTimer(_ => { lock (gate) { if (!disposed) Sweep(); } }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            id = Guid.NewGuid().ToString("N");
            var kind = source.Media.ContentType == "image/png" ? WindowCaptureKind.Screenshot : WindowCaptureKind.Video;
            path = Path.Combine(root, id + (kind == WindowCaptureKind.Screenshot ? ".png" : ".mp4"));
            var expires = clock.GetUtcNow().AddMinutes(30);
            media = source.Media with { Origin = CaptureAttachmentOrigin.SavedLibrary, Id = Guid.NewGuid().ToString("N"), ExpiresAtUnixMilliseconds = expires.ToUnixTimeMilliseconds() };
            entries.Add(id, new(owner, new(id, identity, kind, path), expires)
            { Phase = WindowCapturePhase.Ready, Attachment = media, RequiredCapability = PlatformCapabilities.CaptureLibraryReadV1 });
        }
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            { await source.Stream.CopyToAsync(output, token).ConfigureAwait(false); await output.FlushAsync(token).ConfigureAwait(false); }
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                _ = Owned(owner, id); // Cancellation/revocation cannot resurrect a retired import.
                return media;
            }
        }
        catch
        {
            lock (gate) { entries.Remove(id); Delete(path, source.Media.ByteLength); }
            throw;
        }
    }
    internal void Retire(object owner, string? requiredCapability = null)
    {
        lock (gate)
            foreach (var pair in entries.Where(pair => ReferenceEquals(pair.Value.Owner, owner) && (requiredCapability is null || pair.Value.RequiredCapability == requiredCapability)).ToArray())
            { entries.Remove(pair.Key); Delete(pair.Value.Request.OutputPath, pair.Value.Attachment?.ByteLength); }
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
        foreach (var path in pendingDeletes.Keys.ToArray()) Delete(path);
        var now = clock.GetUtcNow();
        foreach (var pair in entries.Where(pair => pair.Value.Expires <= now).ToArray())
        { entries.Remove(pair.Key); Delete(pair.Value.Request.OutputPath, pair.Value.Attachment?.ByteLength); }
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
    private static bool MatchesHeader(string path, WindowCaptureKind kind, CaptureImageResolution resolution = CaptureImageResolution.Context)
    {
        using var input = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        if (input.Read(header) < header.Length) return false;
        return kind == WindowCaptureKind.Screenshot ? header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) : header.Slice(4, 4).SequenceEqual("ftyp"u8);
    }
    private static bool ValidError(string value) => value.Length is > 0 and <= 80 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    private static long ReservedBytes(Entry entry) => entry.Attachment?.ByteLength ??
        (entry.Phase is WindowCapturePhase.Queued or WindowCapturePhase.Preparing or WindowCapturePhase.Recording
            ? entry.Request.Options?.MaximumBytes ?? (entry.Request.Resolution == CaptureImageResolution.Original ? CaptureLimits.MaximumAttachmentBytes : CaptureLimits.MaximumContextBytes) : 0);
    private long UsageBytes(Entry? excluded = null) => entries.Values.Where(entry => !ReferenceEquals(entry, excluded)).Sum(ReservedBytes) + pendingDeletes.Values.Sum();
    private static BrokerException Error(string code) => new(code, "Capture is unavailable or the request is invalid.");
    private void Delete(string path, long? retainedBytes = null)
    {
        try { File.Delete(path); pendingDeletes.Remove(path); }
        catch (IOException) { pendingDeletes[path] = retainedBytes ?? pendingDeletes.GetValueOrDefault(path, CaptureLimits.MaximumAttachmentBytes); }
        catch (UnauthorizedAccessException) { pendingDeletes[path] = retainedBytes ?? pendingDeletes.GetValueOrDefault(path, CaptureLimits.MaximumAttachmentBytes); }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true; sweeper?.Dispose();
            capabilityReady.TrySetCanceled(); encodingReady.TrySetCanceled();
            foreach (var entry in entries.Values) Delete(entry.Request.OutputPath);
            entries.Clear();
            foreach (var path in pendingDeletes.Keys.ToArray()) Delete(path);
            directoryLease?.Dispose(); directoryLease = null;
            Delete(Path.Combine(root, ".owner"));
            try { if (Directory.Exists(root)) Directory.Delete(root); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
