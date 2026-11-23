using System.Buffers.Binary;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Bounds the GIF composition canvas and every image descriptor before native decoding.</summary>
internal static class NativeGifBounds
{
    // The codec retains composition surfaces, not one full bitmap per frame.
    // Inspect descriptors without decompressing pixels or inventing a frame-count
    // limit. Encoded size already bounds traversal and compressed data.
    internal static (int Width, int Height)? Validate(Stream stream, CancellationToken token)
    {
        Span<byte> header = stackalloc byte[13];
        if (stream.ReadAtLeast(header[..6], 6, throwOnEndOfStream: false) < 6 ||
            !(header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8))) return null;
        Read(header[6..]);
        var width = Word(header[6..]); var height = Word(header[8..]);
        Dimensions(width, height);
        if ((header[10] & 0x80) != 0) Skip(3 * (1 << ((header[10] & 7) + 1)));
        var hasImage = false;
        Span<byte> descriptor = stackalloc byte[9];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            switch (Byte())
            {
                case 0x3b:
                    if (!hasImage) throw Invalid();
                    return (width, height);
                case 0x21:
                    _ = Byte(); // Extension label; every extension uses sub-blocks.
                    Blocks();
                    break;
                case 0x2c:
                    Read(descriptor);
                    var left = Word(descriptor); var top = Word(descriptor[2..]);
                    var frameWidth = Word(descriptor[4..]); var frameHeight = Word(descriptor[6..]);
                    Dimensions(frameWidth, frameHeight);
                    if (left + frameWidth > width || top + frameHeight > height) throw Invalid();
                    if ((descriptor[8] & 0x80) != 0) Skip(3 * (1 << ((descriptor[8] & 7) + 1)));
                    _ = Byte(); // LZW minimum code size is validated by the codec.
                    Blocks(); hasImage = true;
                    break;
                default: throw Invalid();
            }
        }

        void Read(Span<byte> bytes)
        {
            token.ThrowIfCancellationRequested();
            if (stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) != bytes.Length) throw Invalid();
        }
        int Byte() { var value = stream.ReadByte(); return value < 0 ? throw Invalid() : value; }
        void Skip(int count)
        {
            if (count > stream.Length - stream.Position) throw Invalid();
            stream.Seek(count, SeekOrigin.Current);
        }
        void Blocks()
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var length = Byte(); if (length == 0) return;
                Skip(length);
            }
        }
    }

    private static int Word(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    private static InvalidDataException Invalid() => new("GIF has invalid or out-of-bounds image descriptors.");
    private static void Dimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > ProtocolConstants.MaximumEncodedArtworkDimension ||
            height > ProtocolConstants.MaximumEncodedArtworkDimension || (long)width * height > ProtocolConstants.MaximumEncodedArtworkPixels)
            throw Invalid();
    }
}
