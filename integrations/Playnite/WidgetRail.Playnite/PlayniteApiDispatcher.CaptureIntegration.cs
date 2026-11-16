using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetRail.Playnite;

internal sealed partial class PlayniteApiDispatcher
{
    private CaptureCompanionCoordinator captureCompanions=null!;
    private void StartCaptureCompanions()
    {
        var settings=Path.Combine(api.Paths.ExtensionsDataPath,IntegrationIdentity.PluginId,"capture-context-roots.json");
        captureCompanions=new CaptureCompanionCoordinator(settings,connection.ProfileId,ReadCaptureCompanionSnapshotAsync);
        achievements.CaptureStateChanged+=captureCompanions.Invalidate;
    }
    private object CaptureIntegration()=>new{profileId=connection.ProfileId,roots=captureCompanions.Roots,providerRoots=achievements.CaptureConfiguration().Roots.Take(256).ToArray()};
    private object ConfigureCaptureIntegration(Dictionary<string,object> body)
    {
        if(body.Count!=2||!body.TryGetValue("path",out var path)||!(path is string value)||!body.TryGetValue("enabled",out var enabled)||!(enabled is bool active))
            throw new ApiException(400,"invalid_capture_integration");
        var canonical=active?CaptureCompanionCoordinator.Canonical(value):captureCompanions.Roots.FirstOrDefault(root=>string.Equals(root,value,StringComparison.OrdinalIgnoreCase));
        if(canonical==null){if(!active)return CaptureIntegration();throw new ApiException(400,"invalid_capture_directory");}
        if(active&&!achievements.CaptureConfiguration().Roots.Contains(canonical,StringComparer.OrdinalIgnoreCase))throw new ApiException(409,"capture_root_not_configured");
        captureCompanions.Configure(canonical,active);
        return CaptureIntegration();
    }
    private async Task<CaptureCompanionSnapshot> ReadCaptureCompanionSnapshotAsync(string[] selected,CancellationToken token)
    {
        var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var root in selected)
        {
            token.ThrowIfCancellationRequested();
            if(CaptureCompanionCoordinator.Canonical(root)==null)continue;
            folders.Add(Path.GetFileName(root));
            foreach(var directory in Directory.EnumerateDirectories(root).Take(10000))folders.Add(Path.GetFileName(directory));
        }
        var configuration=await OnUiAsync(()=>achievements.CaptureConfiguration(),token,System.Windows.Threading.DispatcherPriority.Background).ConfigureAwait(false);
        var candidates=await OnUiAsync(()=>achievements.CaptureCandidateGames(selected,folders,configuration),token,System.Windows.Threading.DispatcherPriority.Background).ConfigureAwait(false);
        var games=new List<CaptureCompanionGame>();var rows=0;
        foreach(var id in candidates)
        {
            var game=await OnUiAsync(()=>achievements.CaptureGame(id,configuration),token,System.Windows.Threading.DispatcherPriority.Background).ConfigureAwait(false);
            if(game==null)continue;
            rows+=game.Achievements.Length;if(rows>100000)break;
            games.Add(game);
        }
        return new CaptureCompanionSnapshot{Roots=configuration.Roots,Suffixes=configuration.Suffixes,Folders=folders.ToArray(),Games=games.ToArray()};
    }
}
