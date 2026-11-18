using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using Windows.Media.Editing;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class RecordingCodecConsumers
{
    internal static async Task Run(string sourceDirectory,string outputDirectory)
    {
        sourceDirectory=Path.GetFullPath(sourceDirectory);outputDirectory=Path.GetFullPath(outputDirectory);
        if(Directory.Exists(outputDirectory))throw new ArgumentException("Use a fresh evidence directory.");
        Directory.CreateDirectory(outputDirectory);
        var results=new List<object>();
        foreach(var name in new[]{"codec-1-nv12-cbr.mp4","codec-2-nv12-cbr.mp4","codec-2-format-2.mp4","codec-3-nv12-cbr.mp4","codec-3-format-2.mp4"})
        {
            var source=Path.Combine(sourceDirectory,name);
            if(!File.Exists(source))continue;
            var initial=await Hash(source);
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var token=deadline.Token;
            var media=new CaptureAttachment(Guid.NewGuid().ToString("N"),"video/mp4",320,240,new FileInfo(source).Length,2,long.MaxValue)
                {Origin=CaptureAttachmentOrigin.SavedLibrary};
            string? inspectionError=null,thumbnailError=null,trimError=null,frameError=null;
            int[]? frameCenter=null;
            long thumbnailBytes=0;double trimmedDuration=0;
            try { media=await WindowsCaptureImageCodec.InspectAsync(source,media,token); }
            catch(Exception error) { inspectionError=Error(error); }
            if(inspectionError is null)
            {
                try
                {
                    var thumbnail=Path.Combine(outputDirectory,name+".png");
                    var size=await WindowsCaptureImageCodec.CreateThumbnailAsync(source,thumbnail,token);
                    var bytes=await File.ReadAllBytesAsync(thumbnail,token);
                    if(!new CaptureLibraryThumbnail(size.Width,size.Height,bytes).IsWellFormed())throw new InvalidDataException("Invalid thumbnail.");
                    thumbnailBytes=bytes.Length;
                }
                catch(Exception error) { thumbnailError=Error(error); }
                if(thumbnailError is not null)
                {
                    try
                    {
                        var composition=new MediaComposition();
                        composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(source).AsTask(token)).AsTask(token));
                        using var frame=await composition.GetThumbnailAsync(TimeSpan.FromSeconds(.25),96,54,VideoFramePrecision.NearestFrame).AsTask(token);
                        var decoder=await BitmapDecoder.CreateAsync(frame).AsTask(token);
                        var pixels=(await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,new(),
                            ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage).AsTask(token)).DetachPixelData();
                        var center=checked((int)((decoder.PixelHeight/2*decoder.PixelWidth+decoder.PixelWidth/2)*4));
                        frameCenter=[pixels[center],pixels[center+1],pixels[center+2]];
                        // The owned encoder fixture is a colored GPU texture,
                        // never a grayscale file icon or an empty black frame.
                        if(frameCenter[0]<40 || frameCenter[1]<20 || frameCenter[0]-frameCenter[1]<10)
                            throw new InvalidDataException("Decoded frame did not match the owned GPU fixture color.");
                    }
                    catch(Exception error) { frameError=Error(error); }
                }
                try
                {
                    var output=Path.Combine(outputDirectory,"trim-"+name);
                    await using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
                    await using(var destination=new FileStream(output,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
                    {
                        var start=(long)(media.DurationSeconds*TimeSpan.TicksPerSecond/4);
                        var end=(long)(media.DurationSeconds*TimeSpan.TicksPerSecond*3/4);
                        await WindowsCaptureEditCodec.EncodeAsync(new(media,source,input),new(Video:new(start,end)),destination,null,token);
                    }
                    var trimmed=await WindowsCaptureImageCodec.InspectAsync(output,media,token);
                    trimmedDuration=trimmed.DurationSeconds;
                    if(Math.Abs(trimmedDuration-media.DurationSeconds/2)>.1)throw new InvalidDataException("Trim duration changed.");
                }
                catch(Exception error) { trimError=Error(error); }
            }
            if(!initial.SequenceEqual(await Hash(source)))throw new InvalidOperationException("Consumer changed original media.");
            results.Add(new {name,media.Width,media.Height,media.DurationSeconds,inspectionError,thumbnailBytes,thumbnailError,frameCenter,frameError,trimmedDuration,trimError,originalUnchanged=true});
        }
        if(results.Count==0)throw new InvalidOperationException("No owned codec fixtures found.");
        var json=JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true});
        await File.WriteAllTextAsync(Path.Combine(outputDirectory,"result.json"),json);
        Console.WriteLine(json);
    }
    private static async Task<byte[]> Hash(string path) { await using var input=File.OpenRead(path);return await SHA256.HashDataAsync(input); }
    private static string Error(Exception error)=>error is BrokerException broker?broker.Code:$"{error.GetType().Name}:0x{error.HResult:X8}";
}
