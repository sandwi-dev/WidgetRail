using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

// Seekable view over a bounded, immutable split archive. ZipArchive can read it
// without reconstructing the complete archive in memory or on disk.
internal sealed class EngineArchiveStream : Stream
{
    private readonly FileStream[] parts;
    private readonly long[] starts;
    private readonly long length;
    private long position;
    private EngineArchiveStream(FileStream[] files)
    {
        parts=files;starts=new long[files.Length];
        for(var i=0;i<files.Length;i++){starts[i]=length;length+=files[i].Length;}
    }
    internal static Stream Open(string location)
    {
        if(File.Exists(location+".zip"))return File.OpenRead(location+".zip");
        var descriptor=new FileInfo(location+".zip.parts.json");
        if(!descriptor.Exists||descriptor.Length>4096)throw new IOException("The engine archive is missing or incomplete.");
        using var json=JsonDocument.Parse(File.ReadAllText(descriptor.FullName));
        var count=json.RootElement.GetProperty("parts").GetInt32();var total=json.RootElement.GetProperty("length").GetInt64();
        if(count is <1 or >32||total is <1 or >768L*1024*1024)throw new IOException("Invalid engine archive bounds.");
        var files=new List<FileStream>();
        try
        {
            for(var i=0;i<count;i++)
            {
                var path=location+".zip.part"+i;
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Engine archive parts cannot be redirected.");
                var file=File.OpenRead(path);files.Add(file);
                if(file.Length is <1 or >32L*1024*1024)throw new IOException("Invalid engine archive part size.");
            }
            if(files.Sum(f=>f.Length)!=total)throw new IOException("The engine archive has incomplete parts.");
            return new EngineArchiveStream(files.ToArray());
        }
        catch{foreach(var file in files)file.Dispose();throw;}
    }
    public override bool CanRead=>true;
    public override bool CanSeek=>true;
    public override bool CanWrite=>false;
    public override long Length=>length;
    public override long Position{get=>position;set=>Seek(value,SeekOrigin.Begin);}
    public override long Seek(long offset,SeekOrigin origin)
    {
        var target=checked(offset+(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:origin==SeekOrigin.End?length:throw new ArgumentOutOfRangeException(nameof(origin))));
        if(target<0||target>length)throw new IOException("Archive seek exceeds its bounds.");return position=target;
    }
    public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
    public override int Read(Span<byte> buffer)
    {
        if(position==length||buffer.Length==0)return 0;
        var index=Array.BinarySearch(starts,position);if(index<0)index=~index-1;
        parts[index].Position=position-starts[index];var count=parts[index].Read(buffer);
        if(count==0)throw new IOException("The engine archive changed while reading.");position+=count;return count;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();if(position==length||buffer.Length==0)return 0;
        var index=Array.BinarySearch(starts,position);if(index<0)index=~index-1;
        parts[index].Position=position-starts[index];var count=await parts[index].ReadAsync(buffer,token);
        if(count==0)throw new IOException("The engine archive changed while reading.");position+=count;return count;
    }
    protected override void Dispose(bool disposing){if(disposing)foreach(var part in parts)part.Dispose();base.Dispose(disposing);}
    public override void Flush(){}
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
