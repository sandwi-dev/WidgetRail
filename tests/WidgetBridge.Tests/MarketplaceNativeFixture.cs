using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

// Used only by the test executable. Core workers, package admission, catalogue
// monitoring, consent, intents and installation are real; HTTP and clock are local.
internal static class MarketplaceNativeFixture
{
    private sealed record Configuration(string ReleaseRoot,string ProfileRoot,string PackagesDirectory,string ControlPath,string RequestLog);
    private sealed record Control(string Version="1.0.0",string? FailPackage=null,int AdvanceSeconds=0,int DownloadDelayMs=0);
    private sealed record Package(string Path,WidgetManifest Manifest);
    private static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};

    internal static async Task ServeAsync(string pipe,string root)
    {
        var config=JsonSerializer.Deserialize<Configuration>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"catalogue-native-fixture.json")),Json)!;
        root=Path.GetFullPath(root);
        if(root!=Path.GetFullPath(config.ProfileRoot)||root==PlatformSettingsPaths.CreateDefault().RootDirectory)
            throw new InvalidOperationException("Native catalogue fixture requires its exact isolated profile.");
        var packages=new[]{"widgetrail.firstparty.performance-monitor","widgetrail.firstparty.audio-mixer"}.Select(id=>
        {
            var path=Path.Combine(config.PackagesDirectory,id+".wrwidget");
            using var archive=ZipFile.OpenRead(path);
            using var stream=archive.GetEntry("manifest.json")!.Open();using var bytes=new MemoryStream();stream.CopyTo(bytes);
            var manifest=ManifestJson.Deserialize(bytes.ToArray());
            if(manifest.Id!=id||manifest.Publisher!="widgetrail.firstparty")throw new InvalidDataException("Unexpected local acceptance package.");
            return new Package(path,manifest);
        }).ToArray();
        var catalogPath=Path.Combine(config.ReleaseRoot,"widget-catalog.json");
        var worker=Path.Combine(config.ReleaseRoot,"runtime","WidgetWorkerHost","WidgetWorkerHost.exe");
        var installed=Path.Combine(root,"widgets");
        var paths=new PlatformSettingsPaths(root);var store=new PlatformSettingsStore(paths);
        var initial=await BridgeCatalog.LoadWithInstalledObservedAsync(catalogPath,installed,worker,default);
        if(initial.Warnings.Count!=0)throw new InvalidDataException(string.Join("; ",initial.Warnings));
        await using var monitor=new BridgeCatalogMonitor(catalogPath,installed,worker,initial.Catalog,initial.Warnings,
            installedCatalogPending:false,loadCatalog:async token=>
            {
                var load=await BridgeCatalog.LoadWithInstalledObservedAsync(catalogPath,installed,worker,token);
                return load with{Catalog=load.Catalog.WithWidgetSettings((await store.LoadAsync(token)).BuiltInWidgets)
                    .WithStoppedWidgets(await new WidgetStopStateStore(paths).LoadAsync(token))};
            },settingsFilePath:paths.SettingsFile);
        await using var appearance=new PlatformAppearanceService(paths,new ThemeManager(store,new ThemeCatalog(paths)));
        await appearance.StartAsync();
        var consent=new ConsentStore(Path.Combine(root,"consent"));
        await initial.Catalog.EnsureBundledPermissionDefaultsAsync(consent,default);
        var clock=new Clock(config.ControlPath);
        using var http=new Transport(config,packages);
        var sources=new BridgeMarketplaceSources(packages.Select(p=>new CuratedWidget(p.Manifest.Id,p.Manifest.Publisher,
            p.Manifest.Name,"Local acceptance package. Uses the normal review, permissions and update workflow.",[],
            ["This fixture serves verified local widget archives; it makes no GitHub requests."])).ToArray(),http,clock);
        await using var server=new WidgetBridgeServer(pipe,initial.Catalog,256*1024,appearance,consent,
            new SimulatedPlatformBrokerBackend(),monitor,residencyBudget:null,capabilityDiagnosticSink:null,
            settingsStore:store,marketplaceSources:sources);
        monitor.Start();
        await server.RunAsync(TimeSpan.FromSeconds(30),default);
    }

    private static Control ReadControl(string path)
    {
        var value=File.Exists(path)?JsonSerializer.Deserialize<Control>(File.ReadAllText(path),Json)??new():new();
        if(value.Version is not ("1.0.0" or "2.0.0")||value.AdvanceSeconds is <0 or >86400||value.DownloadDelayMs is <0 or >10000)
            throw new InvalidDataException("Invalid native acceptance control.");
        return value;
    }
    private sealed class Clock(string path):TimeProvider
    {public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow.AddSeconds(ReadControl(path).AdvanceSeconds);}
    private sealed class Transport(Configuration config,Package[] packages):HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string,byte[]> archives=new();
        private readonly object logGate=new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var state=ReadControl(config.ControlPath);var uri=request.RequestUri!;
            lock(logGate)File.AppendAllText(config.RequestLog,JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,path=uri.AbsolutePath,version=state.Version})+Environment.NewLine);
            if(uri.Host=="api.github.com")
            {
                if(!uri.AbsolutePath.Contains("/releases",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected metadata request.");
                return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new[]{new{
                    draft=false,prerelease=false,tag_name="native-fixture-"+state.Version,
                    assets=packages.Select(p=>new{name=p.Manifest.Id+"-"+state.Version+".wrwidget",size=Archive(p,state.Version).Length,
                        digest="sha256:"+Convert.ToHexString(SHA256.HashData(Archive(p,state.Version)))}).ToArray()}}))};
            }
            if(uri.Host!="github.com")throw new InvalidOperationException("The native fixture never falls back to external HTTP.");
            var package=packages.Single(p=>uri.AbsolutePath.EndsWith(p.Manifest.Id+"-"+state.Version+".wrwidget",StringComparison.Ordinal));
            await Task.Delay(state.DownloadDelayMs,token);
            return state.FailPackage==package.Manifest.Id?new(HttpStatusCode.ServiceUnavailable):new(HttpStatusCode.OK){Content=new ByteArrayContent(Archive(package,state.Version))};
        }
        private byte[] Archive(Package package,string version)=>archives.GetOrAdd(package.Manifest.Id+":"+version,_=>
        {
            using var original=ZipFile.OpenRead(package.Path);using var buffer=new MemoryStream();
            using(var output=new ZipArchive(buffer,ZipArchiveMode.Create,leaveOpen:true))
            foreach(var entry in original.Entries)
            {
                var copy=output.CreateEntry(entry.FullName,CompressionLevel.Fastest);copy.LastWriteTime=entry.LastWriteTime;
                using var destination=copy.Open();
                if(entry.FullName=="manifest.json")destination.Write(ManifestJson.Serialize(package.Manifest with{Version=version}));
                else{using var source=entry.Open();source.CopyTo(destination);}
            }
            return buffer.ToArray();
        });
    }
}
