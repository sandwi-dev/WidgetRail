using System.Runtime.InteropServices;

namespace WidgetRail.OverlayPlatformClient;

public static class PlatformAbi
{
    public const uint Version = 5;
}

public enum PlatformStatus : uint
{
    Ok, InvalidArgument, InvalidVersion, NotInitialized, ShutDown, AllocationFailed, ControllerIsolationUnavailable
}
public enum PlatformEventKind : uint { None, GuideToggleRequested, LegacyGuidePollingChanged, VisibilityChanged, FocusChanged }
public enum GuideSource : uint { None, GameInput, LegacyCompatibility, DualSenseHid }
public enum NavigationDirection : uint { None, Left, Right, Up, Down }
public enum NavigationPhase : uint { None, Pressed, Repeated }
public enum ControllerReadPath : uint { None, GameInputVisibleLease, XInputCompatibility, ControllerIsolation, DualSenseHid, DualSenseIsolation }
public enum ControllerFamily : uint { Unknown, Xbox, PlayStation }
public enum NativeShortcutSource : uint { Unavailable, Shared, Isolated }

[StructLayout(LayoutKind.Sequential)]
public struct PlatformEvent
{
    public uint StructSize;
    public uint AbiVersion;
    public PlatformEventKind Kind;
    public GuideSource GuideSource;
    public ulong TimestampMilliseconds;
    public uint Value;
    public static PlatformEvent Create() => new() { StructSize = 32, AbiVersion = PlatformAbi.Version };
}

[StructLayout(LayoutKind.Sequential)]
public struct RawControllerState
{
    public ushort Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short LeftThumbX;
    public short LeftThumbY;
    public short RightThumbX;
    public short RightThumbY;
}

[StructLayout(LayoutKind.Sequential)]
public struct NavigationEvent
{
    public NavigationDirection Direction;
    public NavigationPhase Phase;
}

[StructLayout(LayoutKind.Sequential)]
public struct ControllerFrame
{
    public uint StructSize;
    public uint AbiVersion;
    public uint Connected;
    public uint ForegroundExclusive;
    public ControllerReadPath ReadPath;
    public RawControllerState State;
    public ushort PressedButtons;
    public ushort ReleasedButtons;
    public uint LeftTriggerPressed;
    public uint LeftTriggerReleased;
    public uint RightTriggerPressed;
    public uint RightTriggerReleased;
    public uint RecoveryChordPressed;
    public uint Primed;
    public NavigationEvent StickNavigation;
    public NavigationEvent DpadNavigation;
    public uint RemainingFrames;
    public ControllerFamily LastInputFamily;
    public static ControllerFrame Create() => new() { StructSize = 84, AbiVersion = PlatformAbi.Version };
}

[StructLayout(LayoutKind.Sequential)]
public struct PlacementInput
{
    public uint StructSize;
    public uint AbiVersion;
    public int WorkLeft;
    public int WorkTop;
    public int WorkRight;
    public int WorkBottom;
    public uint Dpi;
    public float DesiredWidthDip;
    public float DesiredHeightDip;
    public float SideMarginDip;
    public float TopMarginDip;
    public float BottomMarginDip;
    public static PlacementInput Create() => new()
    {
        StructSize = 48, AbiVersion = PlatformAbi.Version, Dpi = 96,
        SideMarginDip = 24, TopMarginDip = 24, BottomMarginDip = 32
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct Placement
{
    public uint StructSize;
    public uint AbiVersion;
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public static Placement Create() => new() { StructSize = 24, AbiVersion = PlatformAbi.Version };
}

[StructLayout(LayoutKind.Sequential)]
public struct PlatformCreateOptions
{
    public uint StructSize;
    public uint AbiVersion;
    public nint CallbackContext;
    public nint EventAvailable;
    public nint Diagnostic;
    public static PlatformCreateOptions Create() => new()
    {
        StructSize = checked((uint)Marshal.SizeOf<PlatformCreateOptions>()), AbiVersion = PlatformAbi.Version
    };
}

/// <summary>Explicit native failure; a successful call is not physical input proof.</summary>
public sealed class PlatformException(string operation, PlatformStatus status)
    : Exception($"{operation} failed with {status}.")
{
    public PlatformStatus Status { get; } = status;
}
