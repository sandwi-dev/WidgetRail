using System.Buffers.Binary;
using Windows.Media.MediaProperties;

namespace WidgetRail.WidgetBridge;

/// <summary>Bounded AVC sample-description admission; unknown color/track semantics fail closed instead of being discarded.</summary>
internal static class WindowsCaptureEditVideoAdmission
{
    // Attribute IDs from the Windows SDK mfapi.h; the ordinary encoding profile omits these source attributes.
    internal static readonly Guid SampleDescription = new("261e9d83-9529-4b8f-a111-8b9c950a81a9");
    internal static readonly Guid Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    internal static readonly Guid Rotation = new("c380465d-2271-428c-9b83-ecea3b4a85c1");
    internal static readonly Guid Transfer = new("5fb0fce9-be5c-4935-a811-ec838f8eed93");
    internal static readonly Guid Primaries = new("dbfbe4d7-0740-4ee0-8192-850ab0e21935");
    internal static void Demand(VideoEncodingProperties video)
    {
        if (video.Subtype != MediaEncodingSubtypes.H264 || !video.Properties.TryGetValue(Profile, out var profile) || Convert.ToUInt32(profile) is not (66 or 77 or 100) ||
            video.Properties.TryGetValue(Rotation, out var rotation) && Convert.ToUInt32(rotation) != 0 ||
            video.Properties.TryGetValue(Transfer, out var transfer) && Convert.ToUInt32(transfer) is not (0 or 5) ||
            video.Properties.TryGetValue(Primaries, out var primaries) && Convert.ToUInt32(primaries) is not (0 or 2) ||
            !video.Properties.TryGetValue(SampleDescription, out var description) || description is not byte[] bytes)
            throw WindowsCaptureEditCodec.Unsupported("video");
        DemandDescription(bytes, video.Width, video.Height);
    }
    internal static void DemandDescription(ReadOnlySpan<byte> bytes, uint width, uint height)
    {
        void Reject() => throw WindowsCaptureEditCodec.Unsupported("video");
        if (bytes.Length is < 102 or > 65536 || U32(bytes) != bytes.Length || !bytes.Slice(4, 4).SequenceEqual("stsd"u8) || U32(bytes[12..]) != 1) Reject();
        var entry = bytes[16..]; if (U32(entry) != entry.Length || !entry.Slice(4, 4).SequenceEqual("avc1"u8)) Reject();
        var position = 86; var configuration = false;
        while (position < entry.Length)
        {
            if (entry.Length - position < 8) Reject(); var box = entry[position..]; var size = U32(box);
            if (size < 8 || size > box.Length) Reject(); var data = box.Slice(8, checked((int)size - 8)); var type = System.Text.Encoding.ASCII.GetString(box.Slice(4, 4));
            switch (type)
            {
                case "avcC":
                    if (configuration || data.Length < 8 || data[0] != 1 || data[1] is not (66 or 77 or 100) || (data[5] & 31) != 1) Reject();
                    var count = BinaryPrimitives.ReadUInt16BigEndian(data[6..]); if (count < 4 || count > data.Length - 8) Reject();
                    DemandSequence(data.Slice(8, count), width, height); configuration = true; break;
                case "colr":
                    if (data.Length is not (10 or 11) || !(data[..4].SequenceEqual("nclx"u8) || data[..4].SequenceEqual("nclc"u8)) ||
                        !Color(BinaryPrimitives.ReadUInt16BigEndian(data[4..])) || !Color(BinaryPrimitives.ReadUInt16BigEndian(data[6..])) ||
                        !Color(BinaryPrimitives.ReadUInt16BigEndian(data[8..])) || data.Length == 11 && data[10] != 0) Reject();
                    break;
                case "pasp": if (data.Length != 8 || U32(data) != U32(data[4..])) Reject(); break;
                case "btrt": if (data.Length != 12) Reject(); break;
                case "fiel": if (data.Length != 2 || data[0] != 1) Reject(); break;
                default: Reject(); break;
            }
            position += checked((int)size);
        }
        if (!configuration) Reject();
    }
    private static uint U32(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32BigEndian(bytes);
    private static bool Color(uint code) => code is 1 or 2;
    private static void DemandSequence(ReadOnlySpan<byte> nal, uint width, uint height)
    {
        if (nal.Length is < 4 or > 8192 || (nal[0] & 31) != 7) throw WindowsCaptureEditCodec.Unsupported("video");
        var rbsp = new List<byte>(nal.Length); var zeros = 0;
        foreach (var value in nal[1..])
        {
            if (zeros == 2 && value == 3) { zeros = 0; continue; }
            rbsp.Add(value); zeros = value == 0 ? zeros + 1 : 0;
        }
        var bits = new Bits(rbsp.ToArray()); var profile = bits.Read(8); bits.Read(8); bits.Read(8); bits.Exp();
        if (profile is not (66 or 77 or 100)) throw WindowsCaptureEditCodec.Unsupported("video");
        if (profile == 100 && (bits.Exp() != 1 || bits.Exp() != 0 || bits.Exp() != 0 || bits.Read(1) != 0 || bits.Read(1) != 0))
            throw WindowsCaptureEditCodec.Unsupported("video");
        bits.Exp(); var order = bits.Exp();
        if (order == 0) bits.Exp();
        else if (order == 1) { bits.Read(1); bits.Exp(); bits.Exp(); var cycle = bits.Exp(); if (cycle > 255) throw WindowsCaptureEditCodec.Unsupported("video"); for (var i = 0; i < cycle; ++i) bits.Exp(); }
        else if (order != 2) throw WindowsCaptureEditCodec.Unsupported("video");
        bits.Exp(); bits.Read(1); var macroWidth = bits.Exp() + 1L; var macroHeight = bits.Exp() + 1L;
        if (bits.Read(1) != 1) throw WindowsCaptureEditCodec.Unsupported("video"); bits.Read(1);
        long left = 0, right = 0, top = 0, bottom = 0;
        if (bits.Read(1) != 0) { left = bits.Exp(); right = bits.Exp(); top = bits.Exp(); bottom = bits.Exp(); }
        if (macroWidth * 16 - (left + right) * 2 != width || macroHeight * 16 - (top + bottom) * 2 != height) throw WindowsCaptureEditCodec.Unsupported("video");
        if (bits.Read(1) == 0) return;
        if (bits.Read(1) != 0)
        { var aspect = bits.Read(8); if (aspect == 255) { if (bits.Read(16) != bits.Read(16)) throw WindowsCaptureEditCodec.Unsupported("video"); } else if (aspect is not (0 or 1)) throw WindowsCaptureEditCodec.Unsupported("video"); }
        if (bits.Read(1) != 0) bits.Read(1);
        if (bits.Read(1) != 0)
        {
            bits.Read(3); if (bits.Read(1) != 0) throw WindowsCaptureEditCodec.Unsupported("video");
            if (bits.Read(1) != 0 && (!Color(bits.Read(8)) || !Color(bits.Read(8)) || !Color(bits.Read(8)))) throw WindowsCaptureEditCodec.Unsupported("video");
        }
    }
    private sealed class Bits(byte[] bytes)
    {
        private int position;
        internal uint Read(int count)
        {
            if (count is < 0 or > 32 || position + count > bytes.Length * 8) throw WindowsCaptureEditCodec.Unsupported("video");
            uint value = 0; for (var i = 0; i < count; ++i, ++position) value = (value << 1) | (uint)((bytes[position / 8] >> (7 - position % 8)) & 1); return value;
        }
        internal uint Exp()
        {
            var zeros = 0; while (Read(1) == 0) if (++zeros > 24) throw WindowsCaptureEditCodec.Unsupported("video");
            return ((1u << zeros) - 1) + Read(zeros);
        }
    }
}
