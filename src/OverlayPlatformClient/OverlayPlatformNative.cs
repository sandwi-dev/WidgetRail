using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WidgetRail.OverlayPlatformClient;

/// <summary>Loads only the application-local ABI on the currently supported platform.</summary>
public sealed partial class OverlayPlatformNative : IOverlayPlatformNative
{
    public OverlayPlatformNative()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The native overlay platform currently supports Windows x64.");
    }

    public uint GetAbiVersion() => NativeGetAbiVersion();
    public PlatformStatus Create(in PlatformCreateOptions options, out nint handle) => NativeCreate(in options, out handle);
    public PlatformStatus Initialize(nint handle) => NativeInitialize(handle);
    public void Shutdown(nint handle) => NativeShutdown(handle);
    public void Destroy(nint handle) => NativeDestroy(handle);
    public uint HasGameInput(nint handle) => NativeHasGameInput(handle);
    public uint RequiresLegacyGuidePolling(nint handle) => NativeRequiresLegacyGuidePolling(handle);
    public uint ControllerPrerequisites() => NativeControllerPrerequisites();
    public uint ControllerControlState(nint handle) => NativeControllerControlState(handle);
    public PlatformStatus SetExclusiveControl(nint handle, uint enabled) => NativeSetExclusiveControl(handle, enabled);
    public PlatformStatus SetWindowState(nint handle, uint visible, uint focused) => NativeSetWindowState(handle, visible, focused);
    public PlatformStatus PrepareVisible(nint handle) => NativePrepareVisible(handle);
    public PlatformStatus DrainEvent(nint handle, ulong nowMilliseconds, ref PlatformEvent value, out uint hasEvent) => NativeDrainEvent(handle, nowMilliseconds, ref value, out hasEvent);
    public PlatformStatus PollLegacyGuide(nint handle, ulong nowMilliseconds, ref PlatformEvent value, out uint hasEvent) => NativePollLegacyGuide(handle, nowMilliseconds, ref value, out hasEvent);
    public PlatformStatus PrimeController(nint handle, uint foregroundConfirmed, ulong nowMilliseconds) => NativePrimeController(handle, foregroundConfirmed, nowMilliseconds);
    public PlatformStatus ReadController(nint handle, uint foregroundConfirmed, ulong nowMilliseconds, ref ControllerFrame frame) => NativeReadController(handle, foregroundConfirmed, nowMilliseconds, ref frame);
    public PlatformStatus SetOwnedWindows(nint handle, nuint overlay, nuint backdrop) => NativeSetOwnedWindows(handle, overlay, backdrop);
    public PlatformStatus AcquireForeground(nint handle, out uint confirmed) => NativeAcquireForeground(handle, out confirmed);
    public uint ObserveForegroundTarget(nint handle, nuint candidate, uint candidateIsValid) => NativeObserveForegroundTarget(handle, candidate, candidateIsValid);
    public nuint RememberedForegroundTarget(nint handle) => NativeRememberedForegroundTarget(handle);
    public nuint ResolveForegroundTarget(nint handle, nuint fallback, uint rememberedTargetIsValid) => NativeResolveForegroundTarget(handle, fallback, rememberedTargetIsValid);
    public PlatformStatus ComputePlacement(in PlacementInput input, ref Placement output, out uint hasPlacement) => NativeComputePlacement(in input, ref output, out hasPlacement);
    public NativeShortcutSource NativeShortcutButtons(nint handle, out ushort buttons) => NativeNativeShortcutButtons(handle, out buttons);
    public PlatformStatus SetViewMenuShortcut(nint handle, uint enabled) => NativeSetViewMenuShortcut(handle, enabled);
    public PlatformStatus PollViewMenuShortcut(nint handle, out uint pressed, out uint consumed) => NativePollViewMenuShortcut(handle, out pressed, out consumed);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformControllerPrerequisites")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial uint NativeControllerPrerequisites();

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformControllerControlState")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial uint NativeControllerControlState(nint handle);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformSetExclusiveControl")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativeSetExclusiveControl(nint handle, uint enabled);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformSetViewMenuShortcut")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativeSetViewMenuShortcut(nint handle, uint enabled);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformPollViewMenuShortcut")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativePollViewMenuShortcut(nint handle, out uint pressed, out uint consumed);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformGetAbiVersion")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial uint NativeGetAbiVersion();

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformCreate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativeCreate(in PlatformCreateOptions options, out nint handle);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformInitialize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativeInitialize(nint handle);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformShutdown")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial void NativeShutdown(nint handle);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformDestroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial void NativeDestroy(nint handle);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformHasGameInput")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial uint NativeHasGameInput(nint handle);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformRequiresLegacyGuidePolling")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial uint NativeRequiresLegacyGuidePolling(nint handle);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformSetWindowState")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativeSetWindowState(nint handle, uint visible, uint focused);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformPrepareVisible")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativePrepareVisible(nint handle);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformDrainEvent")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativeDrainEvent(nint handle, ulong nowMilliseconds, ref PlatformEvent value, out uint hasEvent);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformPollLegacyGuide")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativePollLegacyGuide(nint handle, ulong nowMilliseconds, ref PlatformEvent value, out uint hasEvent);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformPrimeController")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativePrimeController(nint handle, uint foregroundConfirmed, ulong nowMilliseconds);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformReadController")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativeReadController(nint handle, uint foregroundConfirmed, ulong nowMilliseconds, ref ControllerFrame frame);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformSetOwnedWindows")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativeSetOwnedWindows(nint handle, nuint overlay, nuint backdrop);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformAcquireForeground")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativeAcquireForeground(nint handle, out uint confirmed);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformObserveForegroundTarget")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial uint NativeObserveForegroundTarget(nint handle, nuint candidate, uint candidateIsValid);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformRememberedForegroundTarget")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial nuint NativeRememberedForegroundTarget(nint handle);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformResolveForegroundTarget")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial nuint NativeResolveForegroundTarget(nint handle, nuint fallback, uint rememberedTargetIsValid);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformComputePlacement")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial PlatformStatus NativeComputePlacement(in PlacementInput input, ref Placement output, out uint hasPlacement);

    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformNativeShortcutButtons")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial NativeShortcutSource NativeNativeShortcutButtons(nint handle, out ushort buttons);
}
