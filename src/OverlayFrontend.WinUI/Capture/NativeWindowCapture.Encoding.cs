using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Previews;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

internal sealed record CaptureEncodingDevice(ulong Luid, CaptureEncodingAdapter Adapter);
internal sealed record CaptureEncodingEnvironment(string Id, ulong DefaultAdapter, IReadOnlyList<CaptureEncodingDevice> Devices);

internal static partial class NativeWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct EncodingAdapterNative
    {
        internal ulong Luid, DriverVersion;
        internal uint VendorId, DeviceId, Flags, Reserved;
        internal fixed char Name[128];
        internal string ReadName() { fixed (char* value = Name) return new string(value,0,128).TrimEnd('\0'); }
    }
    [InlineArray(8)] private struct EncodingAdapterArray { private EncodingAdapterNative element; }
    [StructLayout(LayoutKind.Sequential)]
    private struct EncodingEnvironmentNative
    {
        internal uint Size, Version, Count, Reserved;
        internal ulong DefaultAdapter;
        internal EncodingAdapterArray Adapters;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct EncodingSelectionNative
    {
        internal uint Size, Version, Codec, PixelFormat, RateControl, Quality;
        internal ulong AdapterLuid;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct EncodingProbeNative
    {
        internal uint Size, Version, Supported, Hardware;
        internal int Error;
        internal uint Stage;
        internal double ElapsedMilliseconds;
        internal uint EncodedFrames, DecodedFrames;
        internal ulong AdapterLuid;
        internal uint Codec, PixelFormat, RateControl, Quality, ReplayGop, ReplayNoBFrames, Reserved0, Reserved1;
    }
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint="WrailGetCaptureEncodingEnvironment")]
    private static partial int ReadEncodingEnvironmentNative(ref EncodingEnvironmentNative value);
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint="WrailReadCaptureSourceSize")]
    private static partial int ReadEncodingSourceSizeNative(in NativePreviewTarget target,out uint width,out uint height);
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint="WrailProbeCaptureEncodingV2")]
    private static partial int ProbeExplicitEncodingNative(in Options options,in EncodingSelectionNative selection,ref EncodingProbeNative result);
    [LibraryImport("WinUiWindowPreviewNative.dll",EntryPoint="WrailCaptureWindowConfiguredV4",StringMarshalling=StringMarshalling.Utf16)]
    private static partial int CaptureConfiguredEncoding(in NativePreviewTarget target,string path,in Options options,in EncodingSelectionNative selection,
        uint sourceWidth,uint sourceHeight,nint authority,nint context,ref ConfiguredResult result);

    internal static unsafe CaptureEncodingEnvironment ReadEncodingEnvironment()
    {
        if (sizeof(EncodingEnvironmentNative)!=2328 || sizeof(EncodingAdapterNative)!=288 || sizeof(EncodingSelectionNative)!=32 || sizeof(EncodingProbeNative)!=80)
            throw new InvalidOperationException("Native encoding contract does not match its verified ABI.");
        var native=new EncodingEnvironmentNative {Size=2328,Version=1};
        Marshal.ThrowExceptionForHR(ReadEncodingEnvironmentNative(ref native));
        if(native.Size!=2328 || native.Version!=1 || native.Count is 0 or >8 || native.Reserved!=0)
            throw new InvalidDataException("Invalid encoding adapter inventory.");
        var values=new List<(ulong Luid,ulong Driver,uint Vendor,uint Device,uint Flags,string Name)>();
        for(var index=0;index<native.Count;++index)
        {
            var item=native.Adapters[index];var name=item.ReadName();
            if(item.Luid==0 || item.Reserved!=0 || name.Length is 0 or >160 || name.Any(char.IsControl) || values.Any(value=>value.Luid==item.Luid))
                throw new InvalidDataException("Invalid encoding adapter identity.");
            values.Add((item.Luid,item.DriverVersion,item.VendorId,item.DeviceId,item.Flags,name));
        }
        if(!values.Any(value=>value.Luid==native.DefaultAdapter)) throw new InvalidDataException("No hardware default encoding adapter.");
        var fingerprint=Token(native.DefaultAdapter.ToString("x16",CultureInfo.InvariantCulture)+":"+
            string.Join(";",values.OrderBy(value=>value.Luid).Select(value=>FormattableString.Invariant($"{value.Luid:x16}:{value.Driver:x16}:{value.Vendor:x}:{value.Device:x}:{value.Flags:x}"))));
        discovery.ObserveEnvironment(fingerprint);
        return new(fingerprint,native.DefaultAdapter,values.Select(value=>new CaptureEncodingDevice(value.Luid,
            new(Token(FormattableString.Invariant($"adapter:{value.Luid:x16}:{value.Vendor:x}:{value.Device:x}")),value.Name,fingerprint))).ToArray());
    }

    internal static unsafe (int Width,int Height) ReadEncodingSourceSize(NativeWindowPreviewTarget target)
    {
        if(!ulong.TryParse(target.Handle,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var window) || window==0 ||
            !ulong.TryParse(target.ProcessCreated,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var created) || created==0 ||
            target.ProcessId==0 || target.ClassName.Length is 0 or >=256)
            throw new ReplayBackendException(CaptureReplayIssue.TargetUnavailable);
        var native=new NativePreviewTarget {Size=(uint)sizeof(NativePreviewTarget),Version=1,Window=window,ProcessCreated=created,ProcessId=target.ProcessId};
        for(var i=0;i<target.ClassName.Length;++i)native.ClassName[i]=target.ClassName[i];
        var result=ReadEncodingSourceSizeNative(in native,out var width,out var height);
        if(result<0 || width is 0 or >CaptureLimits.MaximumWidth || height is 0 or >CaptureLimits.MaximumHeight ||
            (ulong)width*height>CaptureLimits.MaximumPixels)throw new ReplayBackendException(CaptureReplayIssue.TargetUnavailable,result);
        return ((int)width,(int)height);
    }

    internal static Task<bool> VerifyEncodingAsync(CaptureRecordingProfile profile,CaptureEncodingDevice device,bool audio) =>
        discovery.VerifyEncodingAsync(profile,audio,(value,bitrate,withAudio)=>ProbeExplicitEncoding(value,device,bitrate,withAudio));
    internal static Task<bool> EncodingApplicationAudioAsync()=>discovery.ApplicationAudioAsync();
    internal static Task<bool> EncodingCaptureAvailableAsync()=>discovery.AvailableAsync();

    private static unsafe bool ProbeExplicitEncoding(CaptureRecordingProfile profile,CaptureEncodingDevice device,int bitrate,bool audio)
    {
        if(profile.Adapter!=device.Adapter || profile.Encoding is not {} encoding) return false;
        var options=new Options {Size=(uint)sizeof(Options),Version=3,Kind=2,MaximumWidth=(uint)profile.MaximumWidth,
            MaximumHeight=(uint)profile.MaximumHeight,FramesPerSecond=(uint)profile.FramesPerSecond,Bitrate=(uint)bitrate,
            DurationSeconds=1,Encoder=(uint)CaptureEncoderMode.Hardware,MaximumBytes=CaptureLimits.MaximumAttachmentBytes};
        var selection=new EncodingSelectionNative {Size=32,Version=1,Codec=(uint)encoding.Codec,PixelFormat=(uint)encoding.PixelFormat,
            RateControl=(uint)encoding.RateControl,Quality=(uint)(encoding.Quality??75),AdapterLuid=device.Luid};
        var result=new EncodingProbeNative {Size=80,Version=2};
        return ProbeExplicitEncodingNative(in options,in selection,ref result)>=0 && result.Size==80 && result.Version==2 &&
            result.Supported==1 && result.Hardware==1 && result.Error>=0 && result.Stage==6 && result.EncodedFrames==4 &&
            result.DecodedFrames is >0 and <=4 && result.AdapterLuid==device.Luid && result.Codec==selection.Codec &&
            result.PixelFormat==selection.PixelFormat && result.RateControl==selection.RateControl && result.Quality==selection.Quality &&
            // CodecAPI setter acknowledgements are diagnostics, not encoded
            // packet-order evidence. The probe's real replay sink and recorder
            // enforce keyframe/order safety even when a setter is unsupported.
            result.ReplayGop<=1 && result.ReplayNoBFrames<=1 && result.Reserved0==0 && result.Reserved1==0;
    }
    private static string Token(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0,16));
}
