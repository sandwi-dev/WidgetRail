using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

/// <summary>Private x64 ABI; exact source identity and broker output paths never enter widget IPC.</summary>
internal static partial class ReplayWorkerNative
{
    private const string Library = "ReplayWorkerHost.dll";
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Configuration
    {
        internal uint Size, Version, Flags, Reserved;
        internal ulong Epoch, Window, Created;
        internal uint ProcessId, MaximumWidth, MaximumHeight, FramesPerSecond, Bitrate, AudioMode, IncludeCursor, DurationSeconds;
        internal ulong MemoryBytes;
        internal fixed char ClassName[256];
        internal uint Codec, PixelFormat, RateControl, Quality;
        internal ulong AdapterLuid;
        internal uint ExpectedSourceWidth, ExpectedSourceHeight;
        internal void SetClassName(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length >= 256 || value.Contains('\0')) throw new ArgumentException("Invalid native window class.");
            fixed (char* destination = ClassName)
            {
                var target = new Span<char>(destination, 256); target.Clear(); value.AsSpan().CopyTo(target);
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct State
    {
        internal uint Size, Version, Phase, Issue;
        internal ulong Epoch, Revision;
        internal long BufferedTicks;
        internal ulong ResidentBytes, DroppedFrames;
        internal uint Width, Height, SourceWidth, SourceHeight, Flags;
        internal int Error;
        internal ulong SaveId;
        internal uint SaveOutcome, SavedWidth, SavedHeight, SavedSourceWidth, SavedSourceHeight, SavedFrames, SavedMissed, SavedAudio;
        internal ulong SavedBytes;
        internal long SavedDurationTicks;
        internal ulong PublishedTick;
        internal uint Codec, PixelFormat, RateControl, Quality;
        internal ulong AdapterLuid;
    }
    internal sealed class WorkerHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal WorkerHandle(nint value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() { WrailReplayWorkerRelease(handle); return true; }
    }
    [LibraryImport(Library, EntryPoint = "WrailReplayWorkerStartV2", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int WrailReplayWorkerStart(string executable, in Configuration configuration, out nint worker);
    [LibraryImport(Library, EntryPoint = "WrailReplayWorkerReadV2")]
    internal static partial int WrailReplayWorkerRead(WorkerHandle worker, out State state, uint timeoutMilliseconds);
    [LibraryImport(Library)]
    internal static partial int WrailReplayWorkerHeartbeat(WorkerHandle worker, ulong epoch);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int WrailReplayWorkerSave(WorkerHandle worker, ulong epoch, string path, uint durationSeconds, out ulong saveId);
    [LibraryImport(Library)]
    internal static partial int WrailReplayWorkerCancelSave(WorkerHandle worker, ulong epoch, ulong saveId);
    [LibraryImport(Library)]
    internal static partial int WrailReplayWorkerRequestStop(WorkerHandle worker, ulong epoch);
    [LibraryImport(Library)]
    private static partial void WrailReplayWorkerRelease(nint worker);
}
