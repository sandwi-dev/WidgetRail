using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetRail.Playnite;

[DataContract]
internal sealed class CaptureCompanionRootSettings
{
    [DataMember(Name="roots")] public string[] Roots { get; set; } = Array.Empty<string>();
}
internal sealed class CaptureCompanionGame
{
    internal Guid Id;
    internal string Name="", Folder="", Provider="";
    internal string? CustomDirectory;
    internal CaptureCompanionAchievement[] Achievements=Array.Empty<CaptureCompanionAchievement>();
}
internal sealed class CaptureCompanionAchievement
{
    internal string ApiName="", Name="", Stem="";
    internal long? UnlockedAt;
}
internal sealed class CaptureCompanionSnapshot
{
    internal string[] Roots=Array.Empty<string>(), Suffixes=Array.Empty<string>(), Folders=Array.Empty<string>();
    internal CaptureCompanionGame[] Games=Array.Empty<CaptureCompanionGame>();
}

/// <summary>Optional provider-owned companion author. It remains in Playnite, so
/// metadata delivery does not depend on a foreground or resident widget.</summary>
internal sealed class CaptureCompanionCoordinator : IDisposable
{
    private readonly object gate=new object();
    private readonly string settingsFile,profileId;
    private readonly Func<string[],CancellationToken,Task<CaptureCompanionSnapshot>> read;
    private readonly CancellationTokenSource lifetime=new CancellationTokenSource();
    private readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
    private readonly Timer timer;
    private string[] roots=Array.Empty<string>();
    private CaptureCompanionSnapshot? cached;
    private int mappingRevision;
    private int fullScan=1;
    private readonly ConcurrentDictionary<string,byte> pending=new ConcurrentDictionary<string,byte>(StringComparer.OrdinalIgnoreCase);
    private int running,dirty,disposed;
    internal CaptureCompanionCoordinator(string settingsFile,string profileId,Func<string[],CancellationToken,Task<CaptureCompanionSnapshot>> read)
    {
        this.settingsFile=settingsFile;this.profileId=profileId;this.read=read;
        timer=new Timer(_=>Start(),null,Timeout.Infinite,Timeout.Infinite);
        try
        {
            var saved=IntegrationFiles.Read<CaptureCompanionRootSettings>(settingsFile);
            roots=(saved?.Roots??Array.Empty<string>()).Take(8).Select(Normalize).Where(value=>value!=null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is SerializationException || error is ArgumentException) { }
        Watch();Schedule();
    }
    internal string[] Roots { get { lock(gate)return roots.ToArray(); } }
    internal void Configure(string path,bool enabled)
    {
        var canonical=enabled?Canonical(path):Roots.FirstOrDefault(root=>string.Equals(root,path,StringComparison.OrdinalIgnoreCase));
        if(canonical==null){if(!enabled)return;throw new ApiException(400,"invalid_capture_directory");}
        lock(gate)
        {
            var next=new HashSet<string>(roots,StringComparer.OrdinalIgnoreCase);
            if(enabled)next.Add(canonical);else next.Remove(canonical);
            if(next.Count>8)throw new ApiException(409,"capture_root_limit");
            var value=next.OrderBy(item=>item,StringComparer.OrdinalIgnoreCase).ToArray();
            IntegrationFiles.Write(settingsFile,new CaptureCompanionRootSettings{Roots=value});
            roots=value;cached=null;++mappingRevision;Interlocked.Exchange(ref fullScan,1);Watch();
        }
        Schedule();
    }
    internal static string? Canonical(string? path)
    {
        var full=Normalize(path);if(full==null)return null;
        try
        {
            if(!Directory.Exists(full))return null;
            for(var current=full;!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
                if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return null;
            return full;
        }
        catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException){return null;}
    }
    // Pure normalization is safe inside the provider's read-locked projection;
    // filesystem availability/reparse checks run later for the eight opted-in roots.
    internal static string? Normalize(string? path)
    {
        if(path==null||string.IsNullOrWhiteSpace(path)||path.Length>1024||path.StartsWith("\\\\",StringComparison.Ordinal)||!Path.IsPathRooted(path))return null;
        try{var full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);return full.Length>=3?full:null;}
        catch(Exception error)when(error is ArgumentException||error is NotSupportedException||error is PathTooLongException){return null;}
    }
    private void Watch()
    {
        foreach(var watcher in watchers)watcher.Dispose();watchers.Clear();
        foreach(var root in roots)
        {
            try
            {
                var watcher=new FileSystemWatcher(root){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.DirectoryName};
                watcher.Created+=Changed;watcher.Changed+=Changed;watcher.Renamed+=Renamed;watcher.Error+=(_,_)=>{Interlocked.Exchange(ref fullScan,1);Schedule();};
                watcher.EnableRaisingEvents=true;watchers.Add(watcher);
            }
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is ArgumentException){ }
        }
    }
    private void Changed(object sender,FileSystemEventArgs args)
    {
        var extension=Path.GetExtension(args.FullPath);
        if((string.Equals(extension,".png",StringComparison.OrdinalIgnoreCase)||string.Equals(extension,".mp4",StringComparison.OrdinalIgnoreCase))&&
            !File.Exists(args.FullPath+ProviderCaptureCompanions.Suffix))
        {
            QueueFile(args.FullPath);
            lock(gate)
                if(cached!=null&&!cached.Folders.Contains(Path.GetFileName(Path.GetDirectoryName(args.FullPath)),StringComparer.OrdinalIgnoreCase))
                {cached=null;++mappingRevision;}
            Schedule();
        }
    }
    private void Renamed(object sender,RenamedEventArgs args)=>Changed(sender,args);
    private void QueueFile(string path)
    {
        if(pending.Count<1024)pending.TryAdd(path,0);
        else Interlocked.Exchange(ref fullScan,1);
    }
    internal void Invalidate()
    {
        lock(gate){cached=null;++mappingRevision;}
        Schedule();
    }
    internal void Schedule()
    {
        if(Volatile.Read(ref disposed)!=0)return;
        if(Interlocked.Exchange(ref dirty,1)==0)
            try{timer.Change(750,Timeout.Infinite);}catch(ObjectDisposedException){ }
    }
    private void Start()
    {
        if(Volatile.Read(ref disposed)!=0||Interlocked.CompareExchange(ref running,1,0)!=0)return;
        Interlocked.Exchange(ref dirty,0);
        _=RunAsync();
    }
    private async Task RunAsync()
    {
        var scanningAll=false;
        try
        {
            string[] selected;CaptureCompanionSnapshot? snapshot;int revision;
            lock(gate){selected=roots.ToArray();snapshot=cached;revision=mappingRevision;}
            if(selected.Length==0)return;
            scanningAll=Interlocked.Exchange(ref fullScan,0)!=0;
            if(!scanningAll&&pending.IsEmpty)return;
            var queued=pending.Keys.ToArray();
            foreach(var path in queued)pending.TryRemove(path,out _);
            if(snapshot==null)
            {
                snapshot=await read(selected,lifetime.Token).ConfigureAwait(false);
                lock(gate)
                {
                    if(mappingRevision!=revision){foreach(var path in queued)QueueFile(path);if(scanningAll)Interlocked.Exchange(ref fullScan,1);return;}
                    cached=snapshot;
                }
            }
            var namesByGame=new Dictionary<Guid,Dictionary<string,CaptureCompanionAchievement?>>();
            var gamesByFolder=new Dictionary<string,CaptureCompanionGame?>(StringComparer.OrdinalIgnoreCase);
            var plainFolders=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
            foreach(var root in selected)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if(Canonical(root)==null||!snapshot.Roots.Contains(root,StringComparer.OrdinalIgnoreCase))continue;
                var files=scanningAll?EnumerateMedia(root):queued.Where(path=>path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
                var visited=0;
                foreach(var file in files)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    if(Volatile.Read(ref mappingRevision)!=revision)
                    {if(scanningAll)Interlocked.Exchange(ref fullScan,1);else foreach(var path in queued)QueueFile(path);return;}
                    if(++visited%64==0)await Task.Yield();
                    if(File.Exists(file+ProviderCaptureCompanions.Suffix))continue;
                    var folder=Path.GetDirectoryName(file);
                    if(folder==null)continue;
                    if(!plainFolders.TryGetValue(folder,out var plain))plainFolders[folder]=plain=!string.Equals(Path.GetFileName(folder),"Test",StringComparison.OrdinalIgnoreCase)&&Canonical(folder)!=null;
                    if(!plain)continue;
                    if(!gamesByFolder.TryGetValue(folder,out var candidate))gamesByFolder[folder]=candidate=ProviderCaptureCompanions.Unique(snapshot.Games.Where(game=>game.CustomDirectory!=null
                        ?string.Equals(game.CustomDirectory,folder,StringComparison.OrdinalIgnoreCase)
                        :string.Equals(Path.GetFileName(folder),game.Folder,StringComparison.OrdinalIgnoreCase)&&!string.Equals(folder,root,StringComparison.OrdinalIgnoreCase)));
                    if(candidate==null)continue;
                    if(candidate.Achievements.Length==0){QueueFile(file);continue;}
                    if(!namesByGame.TryGetValue(candidate.Id,out var names))
                        namesByGame[candidate.Id]=names=candidate.Achievements.GroupBy(item=>item.Stem,StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>ProviderCaptureCompanions.Unique(group),StringComparer.OrdinalIgnoreCase);
                        var stems=ProviderCaptureCompanions.AchievementStems(Path.GetFileName(file),snapshot.Suffixes);
                        if(stems.Any(stem=>names.TryGetValue(stem,out var match)&&match==null))continue;
                        var achievement=ProviderCaptureCompanions.Unique(stems.Where(names.ContainsKey).Select(stem=>names[stem]!).Distinct());
                        if(achievement==null)continue;
                        var relative=file.Substring(root.Length+1).Replace('\\','/');
                        var context=new ProviderCaptureContext
                        {
                            IdempotencyKey="playnite-file-"+CaptureRelatedIdentity.Hash(profileId+"\0"+root.ToUpperInvariant()+"\0"+relative.ToUpperInvariant()),
                            // A current unlock time is not a proven timestamp
                            // for an older file, duplicate capture or re-fire.
                            EventAtUnixMilliseconds=null,
                            References=new[]{
                                new ProviderCaptureReference{Role="Subject",Kind=CaptureRelatedIdentity.GameKind,Id=CaptureRelatedIdentity.Game(profileId,candidate.Id),Label=Label(candidate.Name),HasRelatedAction=true},
                                new ProviderCaptureReference{Role="Event",Kind=CaptureRelatedIdentity.AchievementKind,Id=CaptureRelatedIdentity.Achievement(profileId,candidate.Id,candidate.Provider,achievement.ApiName),Label=Label(achievement.Name),HasRelatedAction=true}},
                        };
                        try{ProviderCaptureCompanions.Write(root,file,context);}
                        catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is ArgumentException){ }
                }
            }
        }
        catch(Exception error) when(error is OperationCanceledException || error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException || error is System.Reflection.TargetInvocationException){ }
        finally
        {
            Interlocked.Exchange(ref running,0);
            if(Volatile.Read(ref dirty)!=0&&Volatile.Read(ref disposed)==0)
                try{timer.Change(750,Timeout.Infinite);}catch(ObjectDisposedException){ }
        }
    }
    private static IEnumerable<string> EnumerateMedia(string root)
    {
        foreach(var folder in new[]{root}.Concat(Directory.EnumerateDirectories(root).Take(10000)))
        {
            if(string.Equals(Path.GetFileName(folder),"Test",StringComparison.OrdinalIgnoreCase)||Canonical(folder)==null)continue;
            foreach(var path in Directory.EnumerateFiles(folder).Take(100000))
                if(ProviderCaptureCompanions.HasCaptureSignature(Path.GetFileName(path)))yield return path;
        }
    }
    private static string Label(string value)=>new string(value.Where(c=>!char.IsControl(c)).Take(160).ToArray());
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        lifetime.Cancel();timer.Dispose();lock(gate){foreach(var watcher in watchers)watcher.Dispose();watchers.Clear();}
    }
}
