using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WidgetRail.Playnite;

internal sealed class CaptureRootConfiguration
{
    internal string[] Roots=Array.Empty<string>(),Suffixes=Array.Empty<string>();
    internal Dictionary<Guid,string> Custom=new Dictionary<Guid,string>();
}
internal sealed partial class AchievementAdapter
{
    internal event Action? CaptureStateChanged;
    internal CaptureRootConfiguration CaptureConfiguration()
    {
        EnsureBinding();
        var result=new CaptureRootConfiguration();
        if(plugin==null)return result;
        var settings=Value(Value(plugin,"Settings"),"Persisted");
        var screenshot=CaptureCompanionCoordinator.Normalize(Value(settings,"UnlockScreenshotDirectory") as string);
        var recording=CaptureCompanionCoordinator.Normalize(Value(settings,"UnlockRecordingDirectory") as string)??screenshot;
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(screenshot!=null)roots.Add(screenshot);if(recording!=null)roots.Add(recording);
        result.Suffixes=new[]{"UnlockScreenshotSuffixClean","UnlockScreenshotSuffixWithToast","UnlockScreenshotSuffixFramed"}.Select(name=>Value(settings,name) as string??"").ToArray();
        var store=Value(plugin,"GameCustomDataStore");
        var query=store?.GetType().GetMethods(BindingFlags.Public|BindingFlags.Instance).SingleOrDefault(method=>method.Name=="QueryAll"&&method.IsGenericMethodDefinition&&method.GetGenericArguments().Length==1&&method.GetParameters().Length==1);
        if(query!=null)
        {
            var bound=query.MakeGenericMethod(typeof(KeyValuePair<Guid,string>[]));
            var delegateType=bound.GetParameters()[0].ParameterType;
            var rowType=delegateType.GetGenericArguments()[0].GetGenericArguments()[0];
            var projection=GetType().GetMethod(nameof(ProjectCaptureRoots),BindingFlags.NonPublic|BindingFlags.Instance)!.MakeGenericMethod(rowType);
            var callback=Delegate.CreateDelegate(delegateType,this,projection);
            if(bound.Invoke(store,new object[]{callback}) is KeyValuePair<Guid,string>[] pairs)
                foreach(var pair in pairs){result.Custom[pair.Key]=pair.Value;roots.Add(pair.Value);}
        }
        result.Roots=roots.Take(10000).ToArray();return result;
    }
    private KeyValuePair<Guid,string>[] ProjectCaptureRoots<T>(IEnumerable<T> values)
    {
        var result=new List<KeyValuePair<Guid,string>>();var count=0;
        foreach(var value in values)
        {
            if(++count>10000)break;
            if(Value(value,"PlayniteGameId") is Guid id&&id!=Guid.Empty&&
                CaptureCompanionCoordinator.Normalize(Value(Value(value,"CaptureOverride"),"CaptureDirectory") as string) is string root)
                result.Add(new KeyValuePair<Guid,string>(id,root));
        }
        return result.ToArray();
    }
    internal Guid[] CaptureCandidateGames(string[] selected,HashSet<string> folders,CaptureRootConfiguration configuration)
    {
        EnsureBinding();
        if(service==null||read==null)return Array.Empty<Guid>();
        return api.Database.Games.Where(game=>!game.Hidden&&
            (configuration.Custom.TryGetValue(game.Id,out var root)?selected.Contains(root,StringComparer.OrdinalIgnoreCase):folders.Contains(ProviderCaptureCompanions.Segment(game.Name,true))))
            .Take(10000).Select(game=>game.Id).ToArray();
    }
    internal CaptureCompanionGame? CaptureGame(Guid id,CaptureRootConfiguration configuration)
    {
        EnsureBinding();if(service==null||read==null)return null;
        var game=api.Database.Games.Get(id);if(game==null||game.Hidden)return null;
        var unavailable=new CaptureCompanionGame{Id=id,Name=game.Name,Folder=ProviderCaptureCompanions.Segment(game.Name,true),
            CustomDirectory=configuration.Custom.TryGetValue(id,out var configured)?configured:null};
        var data=read.Invoke(service,new object[]{id.ToString("D")});
        if(!(Value(data,"Achievements") is IEnumerable rows))return unavailable;
        var name=Value(data,"GameName") as string??game.Name;
        var provider=Value(data,"EffectiveProviderKey") as string??Value(data,"ProviderKey") as string;
        if(string.IsNullOrWhiteSpace(provider)||provider!.Length>64)return unavailable;
        var values=new List<CaptureCompanionAchievement>();var count=0;
        foreach(var row in rows)
        {
            if(++count>4000)break;
            if(Value(row,"IsFiltered") is bool filtered&&filtered||!(Value(row,"Unlocked") is bool unlocked)||!unlocked)continue;
            var apiName=Value(row,"ApiName") as string;var display=Value(row,"DisplayName") as string;
            if(string.IsNullOrWhiteSpace(apiName)||apiName!.Length>256||string.IsNullOrWhiteSpace(display))continue;
            var stamp=Value(row,"UnlockTimeUtc") as DateTime?;
            var instant=stamp.HasValue?new DateTimeOffset(stamp.Value.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(stamp.Value,DateTimeKind.Utc):stamp.Value.ToUniversalTime()).ToUnixTimeMilliseconds():(long?)null;
            values.Add(new CaptureCompanionAchievement{ApiName=apiName,Name=display!,Stem=ProviderCaptureCompanions.Segment(display!),UnlockedAt=instant>0?instant:null});
        }
        return new CaptureCompanionGame{Id=id,Name=name,Folder=ProviderCaptureCompanions.Segment(name,true),Provider=provider,
            CustomDirectory=configuration.Custom.TryGetValue(id,out var custom)?custom:null,Achievements=values.ToArray()};
    }
}
