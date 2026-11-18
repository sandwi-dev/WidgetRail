using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Media.Transcoding;
using Windows.Storage;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

internal static class EditingCodecTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject(string code, Func<Task> action)
    { try { await action(); } catch (BrokerException error) when (error.Code == code) { return; } throw new InvalidOperationException("Expected " + code); }
    internal static async Task ImagePixels()
    {
        using var f = new Fixture();
        var pixels = Enumerable.Range(0, 15).SelectMany(i => new byte[] { (byte)(i * 13), (byte)(240 - i * 7), (byte)(i * 5), (byte)(50 + i * 12) }).ToArray();
        var path = Path.Combine(f.Root, "alpha.png");
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 5, 3, 96, 96, pixels); await encoder.FlushAsync();
        }
        using var store = new CaptureLibraryStore(f.Library, f.Exports, null, null, WindowsCaptureImageCodec.InspectAsync);
        var original = await store.SaveAsync(new(new(Guid.NewGuid().ToString("N"), "image/png", 5, 3, new FileInfo(path).Length, 0, long.MaxValue), path), default);
        var before = File.ReadAllBytes(f.MediaPath(original.Id));
        await using (var source = new FileStream(f.MediaPath(original.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var preview = new MemoryStream())
        {
            var size = await WindowsCaptureEditCodec.CreateImagePreviewAsync(new(original.Media, source.Name, source), preview, default);
            preview.Position = 0; var decoder = await BitmapDecoder.CreateAsync(preview.AsRandomAccessStream());
            var actual = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            Check(size is { Width: 5, Height: 3 } && actual.SequenceEqual(pixels), "Editor preview lost alpha before interaction");
        }
        var edited = await store.SaveEditedCopyAsync(new(original.Id, original.Revision, original.MetadataRevision), new(new(1, 1, 1, 2, 3)),
            Fixture.Identity, () => true, WindowsCaptureEditCodec.EncodeAsync, null, default);
        await using (var file = new FileStream(f.MediaPath(edited.Id), FileMode.Open, FileAccess.Read))
        {
            var decoder = await BitmapDecoder.CreateAsync(file.AsRandomAccessStream());
            var actual = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            var expected = new List<byte>(); for (var y = 1; y < 4; ++y) for (var x = 1; x < 3; ++x) expected.AddRange(pixels.Skip((y + (2 - x) * 5) * 4).Take(4));
            Check(decoder.PixelWidth == 2 && decoder.PixelHeight == 3 && actual.SequenceEqual(expected), "Production edit changed crop pixels or alpha");
        }
        Check(File.ReadAllBytes(f.MediaPath(original.Id)).SequenceEqual(before), "PNG edit changed original");
        foreach (var turn in new[] { 0, 2, 3 })
        {
            var image = turn == 3 ? new CaptureImageEdit(3, 0, 1, 2, 3) : new CaptureImageEdit(turn, 1, 0, 3, 2);
            var next = await store.SaveEditedCopyAsync(new(original.Id, original.Revision, original.MetadataRevision), new(image), Fixture.Identity, () => true,
                WindowsCaptureEditCodec.EncodeAsync, null, default);
            await using var file = new FileStream(f.MediaPath(next.Id), FileMode.Open, FileAccess.Read);
            var decoder = await BitmapDecoder.CreateAsync(file.AsRandomAccessStream());
            var actual = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            var expected = new List<byte>();
            for (var y = image.Y; y < image.Y + image.Height; ++y) for (var x = image.X; x < image.X + image.Width; ++x)
            {
                var (sourceX, sourceY) = turn switch { 0 => (x, y), 2 => (4 - x, 2 - y), _ => (4 - y, x) };
                expected.AddRange(pixels.Skip((sourceY * 5 + sourceX) * 4).Take(4));
            }
            Check(actual.SequenceEqual(expected), "Quarter-turn crop mismatch: " + turn);
        }
        using (new FileStream(f.MediaPath(original.Id), FileMode.Open, FileAccess.ReadWrite)) { }
        var unsupported = before.ToArray(); unsupported[24] = 16;
        await Reject("capture_library_edit_unsupported_image", () => { WindowsCaptureEditCodec.DemandImage(new MemoryStream(unsupported)); return Task.CompletedTask; });
    }

    internal static async Task VideoRangeAndLargeSource()
    {
        using var f = new Fixture(); var provider = Path.Combine(f.Root, "provider"); Directory.CreateDirectory(provider);
        var wav = Path.Combine(f.Root, "tone.wav");
        using (var output = new BinaryWriter(File.Create(wav)))
        {
            var samples = 48000 * 6; output.Write("RIFF"u8); output.Write(36 + samples * 2); output.Write("WAVEfmt "u8); output.Write(16);
            output.Write((short)1); output.Write((short)1); output.Write(48000); output.Write(96000); output.Write((short)2); output.Write((short)16);
            output.Write("data"u8); output.Write(samples * 2); for (var n = 0; n < samples; ++n) output.Write((short)(Math.Sin(n * Math.PI * 2 * 440 / 48000) * 8000));
        }
        var folder = await StorageFolder.GetFolderFromPathAsync(provider); var sourceFile = await folder.CreateFileAsync("source.mp4", CreationCollisionOption.FailIfExists);
        var composition = new MediaComposition();
        foreach (var color in new[] { Windows.UI.Color.FromArgb(255,255,0,0), Windows.UI.Color.FromArgb(255,0,255,0), Windows.UI.Color.FromArgb(255,0,0,255) })
            composition.Clips.Add(MediaClip.CreateFromColor(color, TimeSpan.FromSeconds(2)));
        composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(wav)));
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga); profile.Video.Width = 320; profile.Video.Height = 240; profile.Video.Bitrate = 800000;
        profile.Video.FrameRate.Numerator = 30; profile.Video.FrameRate.Denominator = 1;
        Check(await composition.RenderToFileAsync(sourceFile, MediaTrimmingPreference.Precise, profile) == TranscodeFailureReason.None, "Could not create native video fixture");
        var smallOriginal = await File.ReadAllBytesAsync(sourceFile.Path);
        using (var media = MediaSource.CreateFromStorageFile(sourceFile))
        {
            var item = new MediaPlaybackItem(media); await media.OpenAsync(); var properties = item.VideoTracks.Single().GetEncodingProperties();
            WindowsCaptureEditVideoAdmission.Demand(properties);
            foreach (var (key, value) in new[] { (WindowsCaptureEditVideoAdmission.Transfer, 15u), (WindowsCaptureEditVideoAdmission.Transfer, 16u),
                         (WindowsCaptureEditVideoAdmission.Primaries, 9u), (WindowsCaptureEditVideoAdmission.Profile, 110u), (WindowsCaptureEditVideoAdmission.Rotation, 90u) })
            {
                var had = properties.Properties.TryGetValue(key, out var previous); properties.Properties[key] = value;
                await Reject("capture_library_edit_unsupported_video", () => { WindowsCaptureEditVideoAdmission.Demand(properties); return Task.CompletedTask; });
                if (had) properties.Properties[key] = previous!; else properties.Properties.Remove(key);
            }
        }
        var multiPath = Path.Combine(provider, "multiple-audio.mp4"); await File.WriteAllBytesAsync(multiPath, DuplicateAudio(smallOriginal));
        using (var media = MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(multiPath)))
        {
            var item = new MediaPlaybackItem(media); await media.OpenAsync(); Check(item.AudioTracks.Count == 2, "Real multi-audio fixture did not expose both tracks");
        }
        await using (var multi = new FileStream(multiPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var ignored = new MemoryStream())
            await Reject("capture_library_edit_unsupported_video", () => WindowsCaptureEditCodec.EncodeAsync(
                new(new(Guid.NewGuid().ToString("N"), "video/mp4", 320, 240, multi.Length, 6, long.MaxValue) { Origin = CaptureAttachmentOrigin.SavedLibrary }, multiPath, multi),
                new(Video: new(TimeSpan.TicksPerSecond, 5 * TimeSpan.TicksPerSecond)), ignored, null, default));
        // A valid MP4 with a sparse free box exercises input beyond temporary capture budgets without encoding hundreds of megabytes.
        await using (var append = new FileStream(sourceFile.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            append.Position = append.Length; var padding = new byte[8]; BinaryPrimitives.WriteUInt32BigEndian(padding, CaptureLimits.MaximumAttachmentBytes + 4096u); "free"u8.CopyTo(padding.AsSpan(4));
            await append.WriteAsync(padding); append.SetLength(append.Position + CaptureLimits.MaximumAttachmentBytes + 4096 - 8);
        }
        using var store = new CaptureLibraryStore(f.Library, f.Exports, null, null, WindowsCaptureImageCodec.InspectAsync);
        var grant = await store.AddSourceAsync(provider, "Video edit fixture", Fixture.Identity, default);
        var original = (await store.LinkAsync(new(grant.Id, "source.mp4"), Fixture.Identity, default)).Item!;
        byte[] originalHash;
        await using (var input = File.OpenRead(sourceFile.Path)) originalHash = await System.Security.Cryptography.SHA256.HashDataAsync(input);
        Check(original.Media.ByteLength > CaptureLimits.MaximumAttachmentBytes, "Large-source fixture did not cross temporary budget");
        var stopwatch = Stopwatch.StartNew();
        var edited = await store.SaveEditedCopyAsync(new(original.Id, original.Revision, original.MetadataRevision), new(Video: new(TimeSpan.TicksPerSecond, 5 * TimeSpan.TicksPerSecond)),
            Fixture.Identity, () => true, WindowsCaptureEditCodec.EncodeAsync, null, default);
        stopwatch.Stop(); Console.WriteLine($"Native edit: {stopwatch.ElapsedMilliseconds} ms, source={original.Media.ByteLength}, output={edited.Media.ByteLength}");
        Check(Math.Abs(edited.Media.DurationSeconds - 4) <= .05 && edited.Media.Width == 320 && edited.Media.Height == 240 && edited.SourceId is null, "Trim duration, dimensions or ownership changed");
        Check(Directory.EnumerateFiles(f.Exports, "*.mp4").Count() == 1, "Large source was imported instead of edited in place");
        var result = await StorageFile.GetFileFromPathAsync(f.MediaPath(edited.Id));
        using (var media = MediaSource.CreateFromStorageFile(result))
        {
            var item = new MediaPlaybackItem(media); await media.OpenAsync(); Check(item.AudioTracks.Count == 1 && item.VideoTracks.Count == 1, "Edited clip lost audio or video");
        }
        var timeline = new MediaComposition(); timeline.Clips.Add(await MediaClip.CreateFromFileAsync(result));
        using (var thumbnail = await timeline.GetThumbnailAsync(TimeSpan.FromSeconds(3.8), 16, 16, VideoFramePrecision.NearestFrame))
        {
            var decoder = await BitmapDecoder.CreateAsync(thumbnail); var bytes = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                new(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            // Windows fits a 4:3 frame into the requested square thumbnail. Sample content, not its black bars.
            var center = checked((int)((decoder.PixelHeight / 2 * decoder.PixelWidth + decoder.PixelWidth / 2) * 4));
            Check(bytes[center] > 220 && bytes[center + 1] < 20 && bytes[center + 2] < 20, "Trim end did not select the blue source segment");
        }
        using (new FileStream(sourceFile.Path, FileMode.Open, FileAccess.ReadWrite)) { }
        await using (var input = File.OpenRead(sourceFile.Path)) Check(originalHash.SequenceEqual(await System.Security.Cryptography.SHA256.HashDataAsync(input)), "Trim modified linked original bytes");
    }

    // Fixture-only ISO BMFF edit: duplicate the existing audio track, retaining its samples in the preceding mdat.
    private static byte[] DuplicateAudio(byte[] source)
    {
        static IEnumerable<(int Offset, int Length, string Type)> Boxes(byte[] bytes, int offset, int end)
        {
            while (offset < end)
            {
                var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset))); if (size < 8 || offset + size > end) throw new InvalidDataException("Fixture box");
                yield return (offset, size, System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4)); offset += size;
            }
        }
        var movie = Boxes(source, 0, source.Length).Single(box => box.Type == "moov");
        Check(movie.Offset + movie.Length == source.Length, "Fixture expects tail moov, so media offsets remain unchanged");
        var track = Boxes(source, movie.Offset + 8, source.Length).Where(box => box.Type == "trak").Single(track =>
        {
            var media = Boxes(source, track.Offset + 8, track.Offset + track.Length).Single(box => box.Type == "mdia");
            var handler = Boxes(source, media.Offset + 8, media.Offset + media.Length).Single(box => box.Type == "hdlr");
            return source.AsSpan(handler.Offset + 16, 4).SequenceEqual("soun"u8);
        });
        var copy = source.AsSpan(track.Offset, track.Length).ToArray(); var header = Boxes(copy, 8, copy.Length).Single(box => box.Type == "tkhd");
        Check(copy[header.Offset + 8] == 0, "Fixture expects version0 track header"); BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(header.Offset + 20), 42);
        var output = source.Concat(copy).ToArray(); BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(movie.Offset), (uint)(movie.Length + copy.Length));
        var movieHeader = Boxes(output, movie.Offset + 8, movie.Offset + movie.Length).Single(box => box.Type == "mvhd");
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(movieHeader.Offset + movieHeader.Length - 4), 43); return output;
    }
}
