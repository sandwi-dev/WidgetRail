using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media
{
    internal sealed partial class MediaPlayerView
    {
        internal bool CanResumeForValidation(HostMediaPlayerSource grant,long? ticks=null)
        {
            lock(inlineResumeGate)
                return inlineResume is { } entry && OwnsInlineResume(entry) &&
                    entry.Accepts(grant,ticks??Environment.TickCount64,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }
    internal sealed partial class NativePublicMediaSource
    {
        internal static Action<string>? OpenStageForValidation { get; set; }
        static partial void ObserveOpenStage(string stage) => OpenStageForValidation?.Invoke(stage);
    }
}

namespace WidgetRail.OverlayFrontend.WinUI.Validation
{
    internal sealed partial class WidgetStylesValidationPage
    {
        private sealed class HttpRequestCounter : EventListener
        {
            private int requests;
            internal int Count => Volatile.Read(ref requests);
            protected override void OnEventSourceCreated(EventSource source)
            {
                if (source.Name == "System.Net.Http") EnableEvents(source, EventLevel.Informational);
            }
            protected override void OnEventWritten(EventWrittenEventArgs args)
            {
                if (args.EventName == "RequestStart") Interlocked.Increment(ref requests);
            }
        }

        private async Task PublicInlineTimingAsync(string output)
        {
            const string url = "https://media.w3.org/2010/05/sintel/trailer.mp4";
            var player = new MediaPlayerView { Width = 480, Height = 260 };
            var panel = new StackPanel();
            panel.Children.Add(player);
            panel.Children.Add(new Border { Height = 800 });
            var viewport = new ScrollViewer { Width = 500, Height = 270, Content = panel };
            host.Children.Add(viewport);
            using var http = new HttpRequestCounter();
            var samples = new List<object>();
            var clock = new Stopwatch();
            var visit = 0;
            var initialRequests = 0;
            var integrity = new List<string>();
            void Sample(string stage)
            {
                lock (samples) samples.Add(new { visit, stage, elapsedMs = clock.ElapsedMilliseconds, requests = http.Count-initialRequests });
            }
            Exception? failure = null;
            try
            {
                NativePublicMediaSource.OpenStageForValidation = Sample;
                var definition = new MediaPlayerDefinition(MediaPlayerSource.PublicFile(url), new(Muted:true))
                    { Presentation = MediaPlayerPresentation.Inline };
                player.SetActive(true,true);
                player.Apply(_ =>
                {
                    Sample("source-resolved");
                    return Task.FromResult(new HostMediaPlayerSource(definition,url));
                },definition,()=>true,"public-inline-timing");
                await Wait(()=>player.IsLoaded && player.IsInlinePoster);
                for (visit=1; visit<=2; visit++)
                {
                    initialRequests=http.Count;
                    clock.Restart();
                    if(!player.Handle(ControllerButton.A,ControllerEventPhase.Pressed))throw new InvalidOperationException("Play was not handled.");
                    player.Handle(ControllerButton.A,ControllerEventPhase.Released);
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    while(!player.IsPlaying)await Task.Delay(15,timeout.Token);
                    Sample("playing");
                    var session=((MediaPlayerElement)player.PreviewVideoForValidation).MediaPlayer.PlaybackSession;
                    var start=session.Position;
                    while(session.Position<=start || session.NaturalVideoWidth==0)await Task.Delay(15,timeout.Token);
                    Sample("clock-advancing");
                    if(visit==2)break;
                    viewport.ChangeView(null,500,null,true);
                    await Wait(()=>viewport.VerticalOffset>=499 && !player.IsPlaying);
                    await Task.Delay(450);
                    viewport.ChangeView(null,0,null,true);
                    await Wait(()=>viewport.VerticalOffset<1);
                    await Task.Delay(250);
                    if(player.IsPlaying)throw new InvalidOperationException("Inline video resumed without Play.");
                }
                viewport.ChangeView(null,500,null,true);
                var currentGrant=new HostMediaPlayerSource(definition,url);
                await Wait(()=>player.CanResumeForValidation(currentGrant) && !player.IsPlaying && MediaPlayerView.ResidentPlayers==0);
                using var otherRow=new MediaPlayerView();
                if(otherRow.CanResumeForValidation(currentGrant) || player.CanResumeForValidation(currentGrant with { Uri=url+"?rotated" }) ||
                    player.CanResumeForValidation(currentGrant with { ExpiresAtUnixMilliseconds=1 }) ||
                    player.CanResumeForValidation(currentGrant,Environment.TickCount64+16_000))
                    throw new InvalidOperationException("Paused bytes escaped their owning view, URI, grant or deadline.");
                integrity.Add("A short-lived same-grant byte cache owns no decoder and rejects a different view, URL, expiry or elapsed deadline.");
                player.Apply(_=>Task.FromResult(currentGrant),definition,()=>true,"replacement-row-lease");
                if(player.CanResumeForValidation(currentGrant))throw new InvalidOperationException("A rebound row retained its predecessor's bytes.");
                integrity.Add("Replacing the row lease for the same URL invalidates the cached bytes.");
            }
            catch(Exception error){failure=error;}
            finally
            {
                NativePublicMediaSource.OpenStageForValidation=null;
                player.Dispose();
                await player.PlaybackRetirement;
                host.Children.Remove(viewport);
                await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new { passed=failure is null, samples,integrity, error=failure?.GetType().Name }));
            }
        }
    }
}
