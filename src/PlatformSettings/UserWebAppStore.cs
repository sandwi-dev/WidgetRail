using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.PlatformSettings;

public sealed record UserWebApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Url { get; init; }
    public bool Enabled { get; init; } = true;
    public long Revision { get; init; } = 1;
}

internal sealed record UserWebAppDocument
{
    public int SchemaVersion { get; init; } = 1;
    public UserWebApp[] Apps { get; init; } = [];
}

/// <summary>Local website definitions, independent of installed executable packages and browser profiles.</summary>
public sealed class UserWebAppStore
{
    public const string IdPrefix = "widgetrail.webapp.";
    public const int MaximumApps = 128;
    public const int MaximumIconBytes = 512 * 1024;
    private const int MaximumDocumentBytes = 512 * 1024;
    private readonly string root, path, iconRoot, lockPath;
    public UserWebAppStore(PlatformSettingsPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        root=paths.RootDirectory; path=Path.Combine(root,"webapps.json");
        iconRoot=Path.Combine(root,"webapp-icons");lockPath=Path.Combine(root,".webapps.lock");
    }
    public string CatalogPath => path;
    public static bool IsId(string? id) => id is not null && id.StartsWith(IdPrefix,StringComparison.Ordinal) &&
        id.Length == IdPrefix.Length+32 && Guid.TryParseExact(id[IdPrefix.Length..],"N",out _) && id==id.ToLowerInvariant();

    public static string NormalizeUrl(string value)
    {
        value=value.Trim();
        if(value.Length==0 || value.Length>2048 || value.Any(char.IsControl))
            throw Invalid("Enter a website address.");
        if(!value.Contains("://",StringComparison.Ordinal))
        {
            if(value.StartsWith('/'))throw Invalid("Use an http or https website address.");
            value="https://"+value;
        }
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length>0 || string.IsNullOrWhiteSpace(uri.Host) || uri.AbsoluteUri.Length>2048)
            throw Invalid("Use an http or https website address without embedded sign-in details.");
        return uri.AbsoluteUri;
    }
    public static string SuggestedName(string url) => new Uri(NormalizeUrl(url)).IdnHost;
    private static string ValidateName(string name)
    {
        name=name.Trim();
        if(name.Length is <1 or >80 || name.Any(char.IsControl))throw Invalid("Use a name between 1 and 80 characters.");
        return name;
    }
    private static PlatformSettingsException Invalid(string message)=>new("invalid_webapp",message);

    public async Task<IReadOnlyList<UserWebApp>> ReadAsync(CancellationToken token=default)
    {
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(root);
        if(!File.Exists(path))return [];
        FileSystemGuard.RejectReparsePoint(path);
        var bytes=await ReadBoundedAsync(path,MaximumDocumentBytes,token).ConfigureAwait(false);
        try
        {
            StrictJson.RejectDuplicateProperties(bytes);
            var document=JsonSerializer.Deserialize(bytes,UserWebAppJsonContext.Default.UserWebAppDocument);
            if(document is null || document.SchemaVersion!=1 || document.Apps is null || document.Apps.Length>MaximumApps)
                throw Invalid("The saved webapp catalog is invalid.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var app in document.Apps)
                if(app is null || app.Name is null || app.Url is null || !IsId(app.Id) || !ids.Add(app.Id) || app.Name!=ValidateName(app.Name) ||
                    app.Url!=NormalizeUrl(app.Url) || app.Revision<1)throw Invalid("The saved webapp catalog is invalid.");
            return document.Apps;
        }
        catch(JsonException error){throw new PlatformSettingsException("invalid_webapp_catalog","The saved webapp catalog could not be read.",error);}
    }

    public async Task<UserWebApp> CreateAsync(string name,string url,CancellationToken token=default)
    {
        var app=new UserWebApp {Id=IdPrefix+Guid.NewGuid().ToString("N"),Name=ValidateName(name),Url=NormalizeUrl(url)};
        await using var lease=await LockAsync(token).ConfigureAwait(false);
        var apps=await ReadAsync(token).ConfigureAwait(false);
        if(apps.Count>=MaximumApps)throw Invalid("Remove a webapp before adding another.");
        await SaveAsync([..apps,app],token).ConfigureAwait(false);return app;
    }
    public async Task<UserWebApp> UpdateAsync(string id,long expectedRevision,string name,string url,bool enabled,CancellationToken token=default)
    {
        name=ValidateName(name);url=NormalizeUrl(url);
        await using var lease=await LockAsync(token).ConfigureAwait(false);
        var apps=await ReadAsync(token).ConfigureAwait(false);
        var current=Current(apps,id,expectedRevision);
        var next=current with {Name=name,Url=url,Enabled=enabled,Revision=checked(current.Revision+1)};
        if(current.Url!=url)DeleteIcon(id);
        await SaveAsync(apps.Select(a=>a.Id==id?next:a).ToArray(),token).ConfigureAwait(false);
        return next;
    }
    public async Task RemoveAsync(string id,long expectedRevision,CancellationToken token=default)
    {
        await using var lease=await LockAsync(token).ConfigureAwait(false);
        var apps=await ReadAsync(token).ConfigureAwait(false);
        _=Current(apps,id,expectedRevision);
        DeleteIcon(id);
        await SaveAsync(apps.Where(a=>a.Id!=id).ToArray(),token).ConfigureAwait(false);
    }
    private static UserWebApp Current(IReadOnlyList<UserWebApp> apps,string id,long revision)
    {
        var current=apps.SingleOrDefault(a=>a.Id==id);
        if(current is null || current.Revision!=revision)throw new PlatformSettingsException("webapp_changed","This webapp changed. Reopen it and try again.");
        return current;
    }

    // expectedUrl rejects late favicon callbacks from an earlier address or a removed app.
    public async Task<bool> SaveIconAsync(string id,string expectedUrl,ReadOnlyMemory<byte> png,CancellationToken token=default)
    {
        if(!ValidIcon(png.Span))throw Invalid("The website icon is not a supported PNG.");
        await using var lease=await LockAsync(token).ConfigureAwait(false);
        if(!(await ReadAsync(token).ConfigureAwait(false)).Any(a=>a.Id==id&&a.Url==expectedUrl))return false;
        var old=await ReadIconAsync(id,token).ConfigureAwait(false);
        if(old is not null && png.Span.SequenceEqual(old))return false;
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(iconRoot);Directory.CreateDirectory(iconRoot);
        await WriteAtomicAsync(IconPath(id),png,token).ConfigureAwait(false);return true;
    }
    public async Task<byte[]?> ReadIconAsync(string id,CancellationToken token=default)
    {
        var file=IconPath(id);
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(iconRoot);
        if(!File.Exists(file))return null;
        FileSystemGuard.RejectReparsePoint(file);
        try{var bytes=await ReadBoundedAsync(file,MaximumIconBytes,token).ConfigureAwait(false);return ValidIcon(bytes)?bytes:null;}
        catch(FileNotFoundException){return null;}
        catch(PlatformSettingsException error)when(error.Code=="invalid_webapp"){return null;}
    }
    private string IconPath(string id)
    {if(!IsId(id))throw Invalid("Unknown webapp.");return Path.Combine(iconRoot,id[IdPrefix.Length..]+".png");}
    private void DeleteIcon(string id)
    {
        var file=IconPath(id);FileSystemGuard.EnsureExistingPathHasNoReparsePoints(iconRoot);
        FileSystemGuard.RejectReparsePoint(file);if(File.Exists(file))File.Delete(file);
    }
    private static bool ValidIcon(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length is <33 or >MaximumIconBytes || !bytes[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8,4))!=13 || !bytes.Slice(12,4).SequenceEqual("IHDR"u8))return false;
        var width=BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16,4));var height=BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20,4));
        return width is >0 and <=256 && height is >0 and <=256;
    }
    private async Task SaveAsync(UserWebApp[] apps,CancellationToken token)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new UserWebAppDocument{Apps=apps},UserWebAppJsonContext.Default.UserWebAppDocument);
        if(bytes.Length>MaximumDocumentBytes)throw Invalid("The saved webapp catalog is too large.");
        await WriteAtomicAsync(path,bytes,token).ConfigureAwait(false);
    }
    private static async Task WriteAtomicAsync(string destination,ReadOnlyMemory<byte> bytes,CancellationToken token)
    {
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(destination);FileSystemGuard.RejectReparsePoint(destination);
        var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,16384,FileOptions.Asynchronous|FileOptions.WriteThrough))
            {await file.WriteAsync(bytes,token).ConfigureAwait(false);await file.FlushAsync(token).ConfigureAwait(false);file.Flush(true);}
            token.ThrowIfCancellationRequested();File.Move(temporary,destination,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private async Task<FileStream> LockAsync(CancellationToken token)
    {
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(root);Directory.CreateDirectory(root);
        var timer=Stopwatch.StartNew();
        while(true)
        {
            token.ThrowIfCancellationRequested();FileSystemGuard.RejectReparsePoint(lockPath);
            try{return new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException)when(timer.Elapsed<TimeSpan.FromSeconds(5)){await Task.Delay(40,token).ConfigureAwait(false);}
        }
    }
    private static async Task<byte[]> ReadBoundedAsync(string file,int maximum,CancellationToken token)
    {
        await using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,16384,true);
        if(input.Length>maximum)throw Invalid("Saved webapp data exceeds its size limit.");
        using var output=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0)
        {if(output.Length+count>maximum)throw Invalid("Saved webapp data exceeds its size limit.");output.Write(buffer,0,count);}
        return output.ToArray();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(UserWebAppDocument))]
internal sealed partial class UserWebAppJsonContext:JsonSerializerContext;
