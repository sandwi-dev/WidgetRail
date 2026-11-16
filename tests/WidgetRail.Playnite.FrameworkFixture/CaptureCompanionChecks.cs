using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WidgetRail.Playnite;

internal static class CaptureCompanionChecks
{
    internal static async Task RunAsync(Action<bool,string> check,string fixtureRoot)
    {
        var root=Path.Combine(fixtureRoot,"captures");var folder=Path.Combine(root,"Game");Directory.CreateDirectory(folder);
        var profile=Guid.NewGuid().ToString("D");var game=Guid.NewGuid();var reads=0;
        var snapshot=new CaptureCompanionSnapshot{Roots=new[]{root},Folders=new[]{"Game"},Suffixes=new[]{"clean","notification","framed"},Games=new[]{
            new CaptureCompanionGame{Id=game,Name="Game",Folder="Game",Provider="steam",Achievements=new[]{
                new CaptureCompanionAchievement{ApiName="ACH",Name="Achievement",Stem="Achievement",UnlockedAt=1000}}}}};
        var file=Path.Combine(folder,"001_Achievement_clean.png");File.WriteAllBytes(file,new byte[]{1,2,3});
        var settings=Path.Combine(fixtureRoot,"companion-roots.json");
        using(var coordinator=new CaptureCompanionCoordinator(settings,profile,(_,_)=>{Interlocked.Increment(ref reads);return Task.FromResult(snapshot);}))
        {
            await Task.Delay(900);check(!File.Exists(file+ProviderCaptureCompanions.Suffix),"Companions are opt-in per root");
            coordinator.Configure(root,true);await Wait(()=>File.Exists(file+ProviderCaptureCompanions.Suffix));
            check(File.ReadAllBytes(file).SequenceEqual(new byte[]{1,2,3}),"Companion scan keeps original bytes");
            var captured=IntegrationFiles.Read<ProviderCaptureContext>(file+ProviderCaptureCompanions.Suffix)!;
            check(captured.References[0].Id==CaptureRelatedIdentity.Game(profile,game),"Companion binds the local profile and game");
            check(captured.References[1].Id==CaptureRelatedIdentity.Achievement(profile,game,"steam","ACH"),"Companion binds exact provider achievement");
            var before=reads;var next=Path.Combine(folder,"002_Achievement_notification.mp4");File.WriteAllBytes(next,new byte[]{4,5,6});
            await Wait(()=>File.Exists(next+ProviderCaptureCompanions.Suffix));
            check(reads==before,"A file notification reuses cached provider mappings");
            var second=IntegrationFiles.Read<ProviderCaptureContext>(next+ProviderCaptureCompanions.Suffix)!;
            check(second.IdempotencyKey!=captured.IdempotencyKey,"Variants have separate stable artifact keys");
            coordinator.Configure(root,false);var disabled=Path.Combine(folder,"003_Achievement_clean.png");File.WriteAllBytes(disabled,new byte[]{7});
            await Task.Delay(1000);check(!File.Exists(disabled+ProviderCaptureCompanions.Suffix),"Disconnected root stops companion delivery");
        }
        var entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource<CaptureCompanionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var delayed=new CaptureCompanionCoordinator(settings,profile,(_,_)=>{entered.TrySetResult(true);return release.Task;}))
        {
            delayed.Configure(root,true);await entered.Task;
            delayed.Configure(root,false);release.TrySetResult(snapshot);await Task.Delay(300);
            check(!File.Exists(Path.Combine(folder,"003_Achievement_clean.png")+ProviderCaptureCompanions.Suffix),"Retired root rejects late uncooperative provider snapshot");
        }
        async Task Wait(Func<bool> ready)
        {var until=Environment.TickCount+6000;while(!ready()){if(Environment.TickCount>until)throw new TimeoutException("Companion fixture timed out.");await Task.Delay(20);}}
    }
}
