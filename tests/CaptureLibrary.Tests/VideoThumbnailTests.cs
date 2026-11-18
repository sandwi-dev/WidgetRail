using System.Security.Cryptography;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

internal static class VideoThumbnailTests
{
    internal static async Task CachePolicy()
    {
        using var fixture=new Fixture();var calls=0;
        var bytes=new byte[24];fixture.Bytes.AsSpan(0,8).CopyTo(bytes);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16,4),100);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20,4),100);
        using var store=new CaptureLibraryStore(fixture.Library,fixture.Exports,null,async(_,path,token)=>
        {calls++;await File.WriteAllBytesAsync(path,bytes,token);return new(100,100);});
        var item=await store.SaveAsync(fixture.Source with{Attachment=fixture.Source.Attachment with{ContentType="video/mp4",DurationSeconds=1}},default);
        var old=Path.Combine(fixture.Library,item.Id+".thumbnail");var current=Path.Combine(fixture.Library,item.Id+".video-thumbnail-v2");
        await File.WriteAllBytesAsync(old,bytes);
        await store.ThumbnailAsync(item.Id,default);
        if(calls!=1||File.Exists(old)||!File.Exists(current))throw new InvalidOperationException("Legacy video policy cache was reused.");
        await store.ThumbnailAsync(item.Id,default);
        if(calls!=1)throw new InvalidOperationException("Current video policy cache was not reused.");
        await File.WriteAllBytesAsync(old,bytes);
        await store.DeleteAsync(item.Id,default);
        if(File.Exists(old)||File.Exists(current))throw new InvalidOperationException("Removed media retained thumbnail derivatives.");
        var screenshot=await store.SaveAsync(fixture.Source,default);
        var screenshotCache=Path.Combine(fixture.Library,screenshot.Id+".thumbnail");
        await File.WriteAllBytesAsync(screenshotCache,bytes);
        await store.ThumbnailAsync(screenshot.Id,default);
        if(calls!=1)throw new InvalidOperationException("Unchanged screenshot cache was unnecessarily regenerated.");
    }
    internal static async Task EdgeProof(string directory)
    {
        directory=Path.GetFullPath(directory);
        if(Directory.Exists(directory))throw new ArgumentException("Use a fresh thumbnail evidence directory.");
        Directory.CreateDirectory(directory);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token=deadline.Token;
        const int width=1280,height=480;
        var image=Path.Combine(directory,"edge-source.png");
        await WriteImage(image,width,height,token);
        var folder=await StorageFolder.GetFolderFromPathAsync(directory).AsTask(token);
        var video=await folder.CreateFileAsync("edge-source.mp4",CreationCollisionOption.FailIfExists).AsTask(token);
        var composition=new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(await StorageFile.GetFileFromPathAsync(image).AsTask(token),TimeSpan.FromSeconds(1)).AsTask(token));
        var profile=MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width=width;profile.Video.Height=height;profile.Video.Bitrate=4_000_000;
        profile.Video.FrameRate.Numerator=30;profile.Video.FrameRate.Denominator=1;
        var encoded=await composition.RenderToFileAsync(video,MediaTrimmingPreference.Precise,profile).AsTask(token);
        if(encoded!=TranscodeFailureReason.None)throw new InvalidOperationException("Owned fixture encode failed: "+encoded);
        var originalHash=await Hash(video.Path);
        var legacyPath=Path.Combine(directory,"windows-videos-view.png");
        using(var legacy=await video.GetThumbnailAsync(ThumbnailMode.VideosView,256,ThumbnailOptions.UseCurrentScale).AsTask(token))
        {
            if(legacy is null||legacy.Type!=ThumbnailType.Image)throw new InvalidOperationException("Owned video did not produce a real thumbnail.");
            await Reencode(legacy,legacyPath,token);
        }
        var currentPath=Path.Combine(directory,"library-thumbnail.png");
        await WindowsCaptureImageCodec.CreateThumbnailAsync(video.Path,currentPath,token);
        var legacyFrame=await Inspect(legacyPath,token);
        var currentFrame=await Inspect(currentPath,token);
        var unchanged=originalHash.SequenceEqual(await Hash(video.Path));
        var result=new{legacy=legacyFrame,current=currentFrame,originalUnchanged=unchanged};
        await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),token);
        Console.WriteLine(JsonSerializer.Serialize(result));
        if(!currentFrame.EdgesPresent||Math.Abs(currentFrame.Width/(double)currentFrame.Height-width/(double)height)>.03||!unchanged)
            throw new InvalidOperationException("Video thumbnail cropped an edge or changed source aspect/original bytes.");
        var generations=0;var library=Path.Combine(directory,"library");
        using var store=new CaptureLibraryStore(library,Path.Combine(directory,"captures"),null,
            async (source,target,cancel)=>{generations++;return await WindowsCaptureImageCodec.CreateThumbnailAsync(source,target,cancel);});
        var media=new CaptureAttachment(Guid.NewGuid().ToString("N"),"video/mp4",width,height,new FileInfo(video.Path).Length,1,
            DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeMilliseconds());
        var saved=await store.SaveAsync(new(media,video.Path),token);
        var oldCache=Path.Combine(library,saved.Id+".thumbnail");
        var newCache=Path.Combine(library,saved.Id+".video-thumbnail-v2");
        File.Copy(legacyPath,oldCache);
        var oldBytes=await File.ReadAllBytesAsync(oldCache,token);
        if(!new CaptureLibraryThumbnail(legacyFrame.Width,legacyFrame.Height,oldBytes).IsWellFormed())
            throw new InvalidOperationException("The legacy cache negative control must be structurally valid.");
        var regenerated=await store.ThumbnailAsync(saved.Id,token);
        if(generations!=1||File.Exists(oldCache)||!File.Exists(newCache)||(await Inspect(newCache,token)).EdgesPresent!=true)
            throw new InvalidOperationException("Legacy video thumbnail was not replaced lazily.");
        if(!(await store.ThumbnailAsync(saved.Id,token)).Bytes.SequenceEqual(regenerated.Bytes)||generations!=1)
            throw new InvalidOperationException("Current video thumbnail cache was regenerated.");
        await File.WriteAllBytesAsync(newCache,[1,2,3],token);
        await store.ThumbnailAsync(saved.Id,token);
        if(generations!=2)throw new InvalidOperationException("Corrupt versioned thumbnail was not regenerated.");
        File.Copy(legacyPath,oldCache);
        await store.DeleteAsync(saved.Id,token);
        if(File.Exists(oldCache)||File.Exists(newCache))throw new InvalidOperationException("Thumbnail versions survived original removal.");
        if(!originalHash.SequenceEqual(await Hash(video.Path)))throw new InvalidOperationException("Cache recovery changed the source video.");
        await File.WriteAllTextAsync(Path.Combine(directory,"cache-result.json"),JsonSerializer.Serialize(new{passed=true,generations,
            legacyRegenerated=true,warmReused=true,corruptRegenerated=true,versionsRemoved=true,originalUnchanged=true}),token);
        Console.WriteLine("PASS video edge preservation, lazy legacy cache repair, warm reuse, corrupt cache repair, and removal");
    }

    private sealed record Frame(int Width,int Height,int[] Left,int[] Right,bool EdgesPresent);
    private static async Task<Frame> Inspect(string path,CancellationToken token)
    {
        // Decode owned bytes so a WinRT decoder's lazy mapped-section lifetime
        // cannot interfere with the subsequent corrupt-cache recovery test.
        using var stream=new InMemoryRandomAccessStream();
        await stream.WriteAsync((await File.ReadAllBytesAsync(path,token)).AsBuffer()).AsTask(token);stream.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(token);
        var bytes=(await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,new(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage).AsTask(token)).DetachPixelData();
        int[] Pixel(uint x){var i=checked((int)((decoder.PixelHeight/2*decoder.PixelWidth+x)*4));return [bytes[i+2],bytes[i+1],bytes[i]];}
        var left=Pixel(decoder.PixelWidth/20);var right=Pixel(decoder.PixelWidth*19/20);
        return new((int)decoder.PixelWidth,(int)decoder.PixelHeight,left,right,left[0]>160&&left[1]<80&&left[2]<80&&right[2]>160&&right[0]<80&&right[1]<80);
    }
    private static async Task WriteImage(string path,int width,int height,CancellationToken token)
    {
        using var encoded=new InMemoryRandomAccessStream();
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,encoded).AsTask(token);
        var pixels=new byte[width*height*4];
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {var i=(y*width+x)*4;pixels[i]=(byte)(x>=width*4/5?240:20);pixels[i+1]=(byte)(x>=width/5&&x<width*4/5?180:20);pixels[i+2]=(byte)(x<width/5?240:20);pixels[i+3]=255;}
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,(uint)width,(uint)height,96,96,pixels);
        await encoder.FlushAsync().AsTask(token);encoded.Seek(0);
        await using var output=File.Create(path);await encoded.AsStreamForRead().CopyToAsync(output,token);
    }
    private static async Task Reencode(IRandomAccessStream input,string outputPath,CancellationToken token)
    {
        var decoder=await BitmapDecoder.CreateAsync(input).AsTask(token);
        var scale=Math.Min(1,Math.Min(256d/decoder.PixelWidth,144d/decoder.PixelHeight));
        using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,
            new(){ScaledWidth=Math.Max(1,(uint)(decoder.PixelWidth*scale)),ScaledHeight=Math.Max(1,(uint)(decoder.PixelHeight*scale))},
            ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage).AsTask(token);
        using var memory=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,memory).AsTask(token);
        encoder.SetSoftwareBitmap(bitmap);await encoder.FlushAsync().AsTask(token);memory.Seek(0);
        await using var output=File.Create(outputPath);await memory.AsStreamForRead().CopyToAsync(output,token);
    }
    private static async Task<byte[]> Hash(string path){await using var stream=File.OpenRead(path);return await SHA256.HashDataAsync(stream);}
}
